using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using VikingAI.Core;
using VikingAI.Profiles;
using VikingAI.Tactics;
using VikingFactions.Configuration;
using VikingFactions.Core;

namespace VikingFactions.Marauders
{
    /// <summary>Tracks per-character carried-loot state so movement patches can read it without ZDO lookups.</summary>
    public sealed class CarryBox
    {
        public bool Carrying;
    }

    /// <summary>
    /// Loaded-raider heist behavior: march on the settlement, siphon chests one stack at a time, flee to camp.
    /// Defers to vanilla combat when a player is close or the raider is hurt.
    /// </summary>
    public sealed class MarauderHeistBrainModule : IAIBrainModule
    {
        public string Id => "VikingFactions.MarauderHeist";

        private const float HeistScore = 60f;

        public static readonly ConditionalWeakTable<Character, CarryBox> CarryStates = new ConditionalWeakTable<Character, CarryBox>();

        private sealed class RaiderState
        {
            public Inventory Loot = null!;
            public bool Initialized;
            public Container? Target;
            public float SiphonProgress;
            public float NextScan;
            public float RaidingSince;
        }

        private static readonly ConditionalWeakTable<Character, RaiderState> _states = new ConditionalWeakTable<Character, RaiderState>();

        private static bool TryGet(Character c, out ZDO zdo)
        {
            zdo = null!;
            ZNetView? nview = AgentProfileRegistry.GetNView(c);
            if (nview == null || !nview.IsValid() || !nview.IsOwner()) return false;
            zdo = nview.GetZDO();
            return zdo != null && zdo.GetInt(FactionKeys.RaiderState, -1) >= 0;
        }

        private static RaiderState Init(Character c, ZDO zdo, AgentBehaviorProfile profile)
        {
            RaiderState st = _states.GetValue(c, _ => new RaiderState());
            if (!st.Initialized)
            {
                st.Initialized = true;
                st.Loot = LootStore.LoadBase64(zdo, FactionKeys.RaiderLoot);
                CarryStates.GetValue(c, _ => new CarryBox()).Carrying = st.Loot.NrOfItems() > 0;

                // Hostile raiders must not inherit friendly-NPC yielding/formation behavior.
                AgentBehaviorProfile hostile = profile.Clone();
                hostile.YieldToPlayers = false;
                hostile.UseFormationFollowing = false;
                hostile.CanTraversePortals = false;
                AgentProfileRegistry.SetInstanceProfile(c, hostile);
            }
            return st;
        }

        public float EvaluateUtility(BaseAI ai, Character character, AgentBehaviorProfile profile)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableMarauderHeists.Value) return 0f;
            if (!TryGet(character, out ZDO zdo)) return 0f;

            RaiderState st = Init(character, zdo, profile);
            int state = zdo.GetInt(FactionKeys.RaiderState, 3);
            if (state == 3) return 0f;

            if (state == 2) return HeistScore; // flee regardless of combat

            bool hurt = character.GetHealthPercentage() < 0.99f;
            if (state == 1 && hurt && st.Loot.NrOfItems() > 0 && character.GetHealthPercentage() < 0.5f)
            {
                RaiderCampDirector.SetState(zdo, 2);
                return HeistScore;
            }

            if (hurt || AiHelpers.PlayerNear(character.transform.position, 7f)) return 0f; // vanilla combat
            return HeistScore;
        }

        public bool Tick(BaseAI ai, Character character, AgentBehaviorProfile profile, float dt)
        {
            if (!TryGet(character, out ZDO zdo)) return false;
            RaiderState st = Init(character, zdo, profile);
            int state = zdo.GetInt(FactionKeys.RaiderState, 3);
            Vector3 pos = character.transform.position;

            if (state == 0)
            {
                Vector3 target = zdo.GetVec3(FactionKeys.RaiderTargetPos, pos);
                if (Utils.DistanceXZ(pos, target) <= 14f)
                {
                    RaiderCampDirector.SetState(zdo, 1);
                    st.RaidingSince = Time.time;
                    return true;
                }
                AiHelpers.MoveTo(ai, dt, target, 6f, true);
                return true;
            }

            if (state == 1) return TickRaiding(ai, character, zdo, st, dt);
            if (state == 2) return TickFleeing(ai, character, zdo, st, dt);
            return false;
        }

        private static bool TickRaiding(BaseAI ai, Character character, ZDO zdo, RaiderState st, float dt)
        {
            Vector3 pos = character.transform.position;
            if (st.RaidingSince <= 0f) st.RaidingSince = Time.time;

            if (st.Target == null || !HasLoot(st.Target) || st.Target.IsInUse())
            {
                st.Target = null;
                st.SiphonProgress = 0f;
                if (Time.time >= st.NextScan)
                {
                    st.NextScan = Time.time + 1.5f;
                    st.Target = FindTarget(pos, zdo);
                }

                if (st.Target == null)
                {
                    // Nothing (left) to steal nearby: withdraw after a short search window.
                    if (Time.time - st.RaidingSince > 10f) RaiderCampDirector.SetState(zdo, 2);
                    return true;
                }
            }

            Vector3 chestPos = st.Target.transform.position;
            if (Utils.DistanceXZ(pos, chestPos) > 2.2f)
            {
                AiHelpers.MoveTo(ai, dt, chestPos, 1.8f, true);
                return true;
            }

            ai.StopMoving();
            AiHelpers.LookAt(ai, chestPos);
            st.SiphonProgress += dt;
            if (st.SiphonProgress < ModConfig.RaiderSiphonSecondsPerStack.Value) return true;

            st.SiphonProgress = 0f;
            StealOneStack(character, zdo, st);
            return true;
        }

        private static bool TickFleeing(BaseAI ai, Character character, ZDO zdo, RaiderState st, float dt)
        {
            Vector3 pos = character.transform.position;
            Vector3 camp = zdo.GetVec3(FactionKeys.RaiderCampPos, pos);
            if (Utils.DistanceXZ(pos, camp) <= 6f)
            {
                RaiderCampDirector.DepositLoot(zdo);
                st.Loot = LootStore.NewInventory();
                CarryStates.GetValue(character, _ => new CarryBox()).Carrying = false;
                RaiderCampDirector.SetState(zdo, 3);
                ai.StopMoving();
                return true;
            }
            AiHelpers.MoveTo(ai, dt, camp, 4f, true);
            return true;
        }

        private static Container? FindTarget(Vector3 pos, ZDO raiderZdo)
        {
            Container? best = null;
            float bestSq = 45f * 45f;
            foreach (Container c in PieceRegistry.Containers)
            {
                if (c == null) continue;
                float dSq = (c.transform.position - pos).sqrMagnitude;
                if (dSq >= bestSq || !HasLoot(c) || c.IsInUse()) continue;

                ZNetView? cv = c.GetComponent<ZNetView>();
                if (cv == null || !cv.IsValid()) continue;
                if (cv.GetZDO().GetBool(FactionKeys.IsSpoilsChest, false)) continue; // never loot raider camps
                if (ModConfig.RaidersRespectWards.Value && PrivateArea.InsideFactionArea(c.transform.position, Character.Faction.Players)) continue;

                bestSq = dSq;
                best = c;
            }
            return best;
        }

        private static bool HasLoot(Container c)
        {
            Inventory? inv = c.GetInventory();
            return inv != null && inv.NrOfItems() > 0;
        }

        private static void StealOneStack(Character character, ZDO zdo, RaiderState st)
        {
            Container chest = st.Target!;
            Inventory inv = chest.GetInventory();
            ItemDrop.ItemData? pick = null;
            float bestValue = -1f;
            List<ItemDrop.ItemData> items = inv.GetAllItems();
            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData it = items[i];
                if (it?.m_shared == null) continue;
                float value = (it.m_shared.m_value > 0 ? it.m_shared.m_value : 1) * it.m_stack;
                if (value > bestValue) { bestValue = value; pick = it; }
            }
            if (pick == null) return;

            ZNetView? cv = chest.GetComponent<ZNetView>();
            if (cv != null && cv.IsValid()) cv.ClaimOwnership();

            ItemDrop.ItemData copy = pick.Clone();
            if (!st.Loot.AddItem(copy))
            {
                RaiderCampDirector.SetState(zdo, 2);
                return;
            }

            inv.RemoveItem(pick);
            LootStore.SaveBase64(zdo, FactionKeys.RaiderLoot, st.Loot);

            int stolen = zdo.GetInt(FactionKeys.RaiderStolen, 0) + 1;
            zdo.Set(FactionKeys.RaiderStolen, stolen);
            CarryStates.GetValue(character, _ => new CarryBox()).Carrying = true;

            if (stolen >= ModConfig.MaxStacksPerRaider.Value)
            {
                RaiderCampDirector.SetState(zdo, 2);
            }
        }
    }
}
