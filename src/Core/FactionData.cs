using System;
using UnityEngine;
using VikingAI.Profiles;

namespace VikingFactions.Core
{
    /// <summary>
    /// Custom NpcRoleFlags bits owned by VikingFactions. VikingAI reserves bits 0-6; VikingFactions uses 16+.
    /// Roles are persisted in the agent ZDO so VirtualAgentDirector routes unloaded ticks to our tasks.
    /// </summary>
    public static class FactionRoles
    {
        public const NpcRoleFlags Wayfarer = (NpcRoleFlags)(1u << 16);
        public const NpcRoleFlags Marauder = (NpcRoleFlags)(1u << 17);
    }

    public static class FactionKeys
    {
        // Wayfarer
        public const string WfState = "VF_WF_State"; // 0=Approaching, 1=Visiting, 2=Departing
        public const string WfTargetPos = "VF_WF_TargetPos";
        public const string WfOriginPos = "VF_WF_OriginPos";
        public const string WfLastMeal = "VF_WF_LastMeal";
        public const string WfMood = "VF_WF_Mood"; // -1 neglected, 0 content, 1 well fed
        public const string WfSince = "VF_WF_Since";

        // Raider
        public const string CampGuid = "VF_CampGuid";
        public const string CampState = "VF_CampState"; // 0=Active, 1=Abandoned, 2=Reclaimed
        public const string CampLastRaidTime = "VF_CampLastRaid";
        public const string IsSpoilsChest = "VF_IsSpoilsChest";
        public const string RaiderState = "VF_Raider_State"; // 0=March, 1=Raiding, 2=Fleeing, 3=CampGuard
        public const string RaiderTargetPos = "VF_Raider_TargetPos";
        public const string RaiderCampPos = "VF_Raider_CampPos";
        public const string RaiderFaction = "VF_Raider_Faction";
        public const string RaiderStolen = "VF_Raider_Stolen";
        public const string RaiderLoot = "VF_Raider_Loot";
        public const string RaiderSince = "VF_Raider_Since";
    }

    /// <summary>Shared inventory serialization helpers for raider loot and camp chests.</summary>
    public static class LootStore
    {
        public const int Width = 6;
        public const int Height = 3;

        public static Inventory NewInventory(string name = "VF_Loot")
        {
            return new Inventory(name, null, Width, Height);
        }

        public static Inventory LoadBase64(ZDO zdo, string key)
        {
            Inventory inv = NewInventory();
            string b64 = zdo.GetString(key, "");
            if (!string.IsNullOrEmpty(b64))
            {
                try { inv.Load(new ZPackage(b64)); }
                catch (Exception ex) { VikingFactionsPlugin.Log.LogWarning($"[LootStore] Failed to load '{key}': {ex.Message}"); }
            }
            return inv;
        }

        public static void SaveBase64(ZDO zdo, string key, Inventory inv)
        {
            if (inv.NrOfItems() == 0)
            {
                zdo.Set(key, "");
                return;
            }
            ZPackage pkg = new ZPackage();
            inv.Save(pkg);
            zdo.Set(key, pkg.GetBase64());
        }

        public static Inventory LoadChest(ZDO chestZdo)
        {
            Inventory inv = NewInventory("SpoilsChest");
            byte[] data = chestZdo.GetByteArray(ZDOVars.s_items);
            if (data != null && data.Length > 0)
            {
                try { inv.Load(new ZPackage(data)); }
                catch (Exception ex) { VikingFactionsPlugin.Log.LogWarning($"[LootStore] Failed to load chest: {ex.Message}"); }
            }
            return inv;
        }

        public static void SaveChest(ZDO chestZdo, Inventory inv)
        {
            ZPackage pkg = new ZPackage();
            inv.Save(pkg);
            chestZdo.Set(ZDOVars.s_items, pkg.GetArray());
        }

        public static bool AddPrefab(Inventory inv, string prefabName, int stack)
        {
            if (ObjectDB.instance == null) return false;
            GameObject prefab = ObjectDB.instance.GetItemPrefab(prefabName);
            if (prefab == null || prefab.GetComponent<ItemDrop>() == null) return false;
            return inv.AddItem(prefab, stack);
        }

        /// <summary>Moves all items from <paramref name="source"/> into <paramref name="dest"/>; returns the number of stacks that did not fit.</summary>
        public static int MoveAll(Inventory source, Inventory dest)
        {
            int overflow = 0;
            var items = new System.Collections.Generic.List<ItemDrop.ItemData>(source.GetAllItems());
            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData copy = items[i].Clone();
                if (!dest.AddItem(copy)) overflow++;
            }
            return overflow;
        }
    }
}
