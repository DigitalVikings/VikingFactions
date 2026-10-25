using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using VikingAI.Core;
using VikingAI.NPC.Affordances;
using VikingAI.Profiles;
using VikingAI.Tactics;
using VikingFactions.Configuration;
using VikingFactions.Core;

namespace VikingFactions.Wayfarers
{
    /// <summary>
    /// Drives loaded Wayfarers. Wins the tick while approaching/departing or when a meal or sleep is due;
    /// otherwise yields (score 0) so VikingAI's SmartAffordanceBrainModule handles warming and sitting.
    /// Beds are reserved through AffordanceRegistry only; bed ownership (ZDOVars.s_owner) is never touched.
    /// </summary>
    public sealed class WayfarerBrainModule : IAIBrainModule
    {
        public string Id => "VikingFactions.WayfarerHospitality";

        private const float MovingScore = 60f;
        private const float ServiceScore = 50f;

        private static readonly string[] _happy =
        {
            "A warm fire and good company!", "May Odin bless this hall.",
            "Fine shelter from the wilds.", "Nothing beats a hot hearth after a long march."
        };
        private static readonly string[] _hungry =
        {
            "The larders in this hall are bare...", "My stomach rumbles. Is there no food to spare?",
            "A warm fire, yet an empty table."
        };
        private static readonly string[] _noBed =
        {
            "Not a spare bunk in the hall tonight...", "Sleeping on the floorboards again, it seems."
        };
        private static readonly string[] _depart =
        {
            "The hearth grows cold. Time to move on.", "Safe travels, friend. I must return to the road."
        };

        private sealed class LocalState
        {
            public float NextChat = Time.time + Random.Range(15f, 35f);
            public float NextBedCheck;
            public float Neglect;
            public Component? Bed;
        }

        private static readonly ConditionalWeakTable<Character, LocalState> _states = new ConditionalWeakTable<Character, LocalState>();

        private static LocalState GetState(Character c) => _states.GetValue(c, _ => new LocalState());

        private static bool IsWayfarer(Character c, out ZDO zdo)
        {
            zdo = null!;
            ZNetView? nview = AgentProfileRegistry.GetNView(c);
            if (nview == null || !nview.IsValid() || !nview.IsOwner()) return false;
            zdo = nview.GetZDO();
            return zdo != null && zdo.GetPrefab() == HospitalityScanner.WayfarerPrefabName.GetStableHashCode();
        }

        public float EvaluateUtility(BaseAI ai, Character character, AgentBehaviorProfile profile)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableWayfarers.Value) return 0f;
            if (!IsWayfarer(character, out ZDO zdo)) return 0f;
            if (ai is MonsterAI mai && mai.GetTargetCreature() != null) return 0f; // self-defence / fleeing is vanilla

            int state = zdo.GetInt(FactionKeys.WfState, 0);
            if (state != 1) return MovingScore;

            LocalState local = GetState(character);
            double now = ZNet.instance.GetTimeSeconds();
            bool needsMeal = now - zdo.GetFloat(FactionKeys.WfLastMeal, (float)now) >= ModConfig.WayfarerMealIntervalSeconds.Value;
            if (needsMeal || EnvMan.IsNight() || local.Neglect >= 120f)
            {
                return ServiceScore;
            }

            CheckHearthUpkeep(character, local, Time.deltaTime);
            AmbientChatter(character, zdo, local);
            return 0f;
        }

        public bool Tick(BaseAI ai, Character character, AgentBehaviorProfile profile, float dt)
        {
            if (!IsWayfarer(character, out ZDO zdo)) return false;
            LocalState local = GetState(character);
            int state = zdo.GetInt(FactionKeys.WfState, 0);
            Vector3 pos = character.transform.position;

            if (state == 0)
            {
                Vector3 target = zdo.GetVec3(FactionKeys.WfTargetPos, pos);
                if (Utils.DistanceXZ(pos, target) <= 8f)
                {
                    zdo.Set(FactionKeys.WfState, 1);
                    zdo.Set(FactionKeys.WfSince, (float)ZNet.instance.GetTimeSeconds());
                    ai.StopMoving();
                    AiHelpers.Say(character, "Hail! May a weary traveler share your hearth?");
                    return true;
                }
                AiHelpers.MoveTo(ai, dt, target, 3f, false);
                return true;
            }

            if (state == 2)
            {
                ReleaseBed(character, local);
                Vector3 origin = zdo.GetVec3(FactionKeys.WfOriginPos, pos);
                if (Utils.DistanceXZ(pos, origin) <= 10f)
                {
                    if (ZDOMan.instance != null) ZDOMan.instance.DestroyZDO(zdo);
                    return true;
                }
                AiHelpers.MoveTo(ai, dt, origin, 3f, false);
                return true;
            }

            // Visiting
            CheckHearthUpkeep(character, local, dt);
            if (local.Neglect >= 120f)
            {
                AiHelpers.Say(character, _depart[Random.Range(0, _depart.Length)]);
                zdo.Set(FactionKeys.WfState, 2);
                return true;
            }

            double now = ZNet.instance.GetTimeSeconds();
            if (now - zdo.GetFloat(FactionKeys.WfLastMeal, (float)now) >= ModConfig.WayfarerMealIntervalSeconds.Value)
            {
                if (TryMeal(ai, character, zdo, local, now, dt)) return true;
            }

            if (EnvMan.IsNight())
            {
                if (TrySleep(ai, character, local, dt)) return true;
            }
            else
            {
                ReleaseBed(character, local);
            }

            AmbientChatter(character, zdo, local);
            return false;
        }

        private static bool TryMeal(BaseAI ai, Character character, ZDO zdo, LocalState local, double now, float dt)
        {
            Container? chest = null;
            ItemDrop.ItemData? food = null;
            float bestSq = float.MaxValue;
            Vector3 pos = character.transform.position;

            foreach (Container c in PieceRegistry.Containers)
            {
                if (c == null) continue;
                float dSq = (c.transform.position - pos).sqrMagnitude;
                if (dSq > 22f * 22f || dSq >= bestSq || c.IsInUse()) continue;

                Inventory? inv = c.GetInventory();
                if (inv == null) continue;
                List<ItemDrop.ItemData> items = inv.GetAllItems();
                for (int i = 0; i < items.Count; i++)
                {
                    ItemDrop.ItemData it = items[i];
                    if (it?.m_shared != null && it.m_shared.m_food > 0f)
                    {
                        bestSq = dSq;
                        chest = c;
                        food = it;
                        break;
                    }
                }
            }

            if (chest == null || food == null)
            {
                zdo.Set(FactionKeys.WfMood, -1);
                local.Neglect += dt * 1.5f;
                if (Time.time >= local.NextChat)
                {
                    local.NextChat = Time.time + Random.Range(35f, 60f);
                    AiHelpers.Say(character, _hungry[Random.Range(0, _hungry.Length)]);
                }
                return false;
            }

            if (Vector3.Distance(pos, chest.transform.position) > 2.2f)
            {
                AiHelpers.MoveTo(ai, dt, chest.transform.position, 1.8f, false);
                return true;
            }

            ai.StopMoving();
            AiHelpers.LookAt(ai, chest.transform.position);

            ZNetView? cview = chest.GetComponent<ZNetView>();
            if (cview != null && cview.IsValid()) cview.ClaimOwnership();

            string foodName = food.m_shared.m_name;
            chest.GetInventory().RemoveOneItem(food);

            zdo.Set(FactionKeys.WfLastMeal, (float)now);
            zdo.Set(FactionKeys.WfMood, 1);
            local.Neglect = Mathf.Max(0f, local.Neglect - 45f);
            character.Heal(25f, true);
            AiHelpers.Say(character, $"Grateful for the {Localization.instance.Localize(foodName)}!");
            local.NextChat = Time.time + Random.Range(45f, 90f);
            return true;
        }

        private static bool TrySleep(BaseAI ai, Character character, LocalState local, float dt)
        {
            Vector3 pos = character.transform.position;

            if (local.Bed == null)
            {
                if (Time.time < local.NextBedCheck) return false;
                local.NextBedCheck = Time.time + 8f;

                Bed? chosen = null;
                float bestSq = 24f * 24f;
                foreach (Bed b in PieceRegistry.Beds)
                {
                    if (b == null || AffordanceRegistry.IsReservedByOther(b, character) || PlayerInBed(b)) continue;
                    float dSq = (b.transform.position - pos).sqrMagnitude;
                    if (dSq < bestSq) { bestSq = dSq; chosen = b; }
                }

                if (chosen != null && AffordanceRegistry.TryReserve(chosen, character, 60f))
                {
                    local.Bed = chosen;
                }
                else
                {
                    if (Time.time >= local.NextChat)
                    {
                        local.NextChat = Time.time + Random.Range(45f, 80f);
                        AiHelpers.Say(character, _noBed[Random.Range(0, _noBed.Length)]);
                    }
                    return false;
                }
            }

            AffordanceRegistry.TryReserve(local.Bed!, character, 30f);
            Vector3 bedPos = local.Bed!.transform.position;
            if (Vector3.Distance(pos, bedPos) > 2f)
            {
                AiHelpers.MoveTo(ai, dt, bedPos, 1.6f, false);
                return true;
            }

            ai.StopMoving();
            character.GetComponent<ZSyncAnimation>()?.SetBool("attach_bed", true);
            return true;
        }

        private static void ReleaseBed(Character character, LocalState local)
        {
            if (local.Bed == null) return;
            AffordanceRegistry.ReleaseReservation(local.Bed, character);
            local.Bed = null;
            character.GetComponent<ZSyncAnimation>()?.SetBool("attach_bed", false);
        }

        private static bool PlayerInBed(Bed bed)
        {
            List<Player> players = Player.GetAllPlayers();
            for (int i = 0; i < players.Count; i++)
            {
                Player p = players[i];
                if (p != null && p.InBed() && Vector3.Distance(p.transform.position, bed.transform.position) < 2.5f) return true;
            }
            return false;
        }

        private static void CheckHearthUpkeep(Character character, LocalState local, float dt)
        {
            bool lit = false;
            float rSq = 22f * 22f;
            Vector3 pos = character.transform.position;
            foreach (Fireplace f in PieceRegistry.Fireplaces)
            {
                if (f != null && f.IsBurning() && (f.transform.position - pos).sqrMagnitude <= rSq)
                {
                    lit = true;
                    break;
                }
            }
            local.Neglect = lit ? Mathf.Max(0f, local.Neglect - dt * 0.5f) : local.Neglect + dt;
        }

        private static void AmbientChatter(Character character, ZDO zdo, LocalState local)
        {
            if (Time.time < local.NextChat) return;
            local.NextChat = Time.time + Random.Range(45f, 95f);
            if (!AiHelpers.PlayerNear(character.transform.position, 12f)) return;

            string[] pool = zdo.GetInt(FactionKeys.WfMood, 0) >= 0 ? _happy : _hungry;
            AiHelpers.Say(character, pool[Random.Range(0, pool.Length)]);
        }
    }
}
