using HarmonyLib;
using UnityEngine;
using VikingAI.Simulation;
using VikingFactions.Configuration;
using VikingFactions.Core;
using VikingFactions.Marauders;
using VikingFactions.Wayfarers;

namespace VikingFactions.Patches
{
    [HarmonyPatch]
    public static class FactionPatches
    {
        /// <summary>Re-registers persisted faction agents with the virtual simulation after a world reload.</summary>
        [HarmonyPatch(typeof(Character), "Start")]
        [HarmonyPostfix]
        private static void Character_Start_Postfix(Character __instance)
        {
            if (!ModConfig.ModEnabled.Value || __instance == null || __instance.IsPlayer()) return;

            ZNetView? nview = VikingAI.Profiles.AgentProfileRegistry.GetNView(__instance);
            if (nview == null || !nview.IsValid()) return;
            ZDO zdo = nview.GetZDO();
            if (zdo == null) return;

            if (zdo.GetPrefab() == HospitalityScanner.WayfarerPrefabName.GetStableHashCode())
            {
                HospitalityScanner.RegisterWayfarer(zdo);
                VirtualAgentDirector.TrackZdoDirectly(zdo);
            }
            else if (zdo.GetInt(FactionKeys.RaiderState, -1) >= 0)
            {
                VirtualAgentDirector.TrackZdoDirectly(zdo);
            }
        }

        /// <summary>Converts eligible vanilla base-raid events into physical VikingFactions camps and warbands.</summary>
        [HarmonyPatch(typeof(RandEventSystem), "SetRandomEvent")]
        [HarmonyPrefix]
        private static bool RandEventSystem_SetRandomEvent_Prefix(RandomEvent ev, Vector3 pos, bool __runOriginal)
        {
            if (!__runOriginal) return true;
            if (ev == null) return true;
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableMarauderHeists.Value || !ModConfig.InterceptVanillaRaids.Value) return true;
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return true;
            if (!RaiderFactionRegistry.TryGetFactionForVanillaEvent(ev.m_name, out IRaiderFactionDefinition faction))
            {
                if (!RaiderFactionRegistry.TrySelectProgressiveFaction(out faction))
                {
                    return true;
                }
            }
            else if (!RaiderFactionRegistry.IsFactionAvailable(faction))
            {
                // If this specific vanilla event maps to a faction not yet unlocked,
                // fall back to the highest available progressive faction rather than overwhelming the player
                if (!RaiderFactionRegistry.TrySelectProgressiveFaction(out faction))
                {
                    return true;
                }
            }

            if (RaiderCampDirector.TriggerFactionRaid(faction.FactionId, pos, out _))
            {
                return false;
            }
            return true; // could not place a camp; fall back to the vanilla raid
        }

        [HarmonyPatch(typeof(Character), "GetJogSpeedFactor")]
        [HarmonyPostfix]
        private static void Character_GetJogSpeedFactor_Postfix(Character __instance, ref float __result)
        {
            if (IsCarrying(__instance)) __result *= ModConfig.RaiderCarrySpeedMultiplier.Value;
        }

        [HarmonyPatch(typeof(Character), "GetRunSpeedFactor")]
        [HarmonyPostfix]
        private static void Character_GetRunSpeedFactor_Postfix(Character __instance, ref float __result)
        {
            if (IsCarrying(__instance)) __result *= ModConfig.RaiderCarrySpeedMultiplier.Value;
        }

        private static bool IsCarrying(Character c)
        {
            return c != null && MarauderHeistBrainModule.CarryStates.TryGetValue(c, out CarryBox box) && box.Carrying;
        }

        /// <summary>Raiders killed on the road drop all stolen loot at their corpse.</summary>
        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        [HarmonyPostfix]
        private static void Character_OnDeath_Postfix(Character __instance)
        {
            if (__instance == null || __instance.IsPlayer()) return;
            ZNetView? nview = VikingAI.Profiles.AgentProfileRegistry.GetNView(__instance);
            if (nview == null || !nview.IsValid() || !nview.IsOwner()) return;

            ZDO zdo = nview.GetZDO();
            if (zdo == null || zdo.GetInt(FactionKeys.RaiderState, -1) < 0) return;

            Inventory loot = LootStore.LoadBase64(zdo, FactionKeys.RaiderLoot);
            var items = loot.GetAllItems();
            for (int i = 0; i < items.Count; i++)
            {
                Vector3 p = __instance.transform.position + Vector3.up * 0.6f + Random.insideUnitSphere * 0.4f;
                ItemDrop.DropItem(items[i], 0, p, Quaternion.Euler(0f, Random.Range(0, 360), 0f));
            }

            LootStore.SaveBase64(zdo, FactionKeys.RaiderLoot, LootStore.NewInventory());
            zdo.Set(FactionKeys.RaiderStolen, 0);
            MarauderHeistBrainModule.CarryStates.Remove(__instance);
        }
    }
}
