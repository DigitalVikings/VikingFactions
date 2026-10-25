using System.Collections.Generic;
using UnityEngine;
using VikingAI.Simulation;
using VikingFactions.Configuration;
using VikingFactions.Core;
using VikingFactions.Wayfarers;

namespace VikingFactions.Marauders
{
    /// <summary>
    /// Spawns procedural raider camps directly as ZDOs (valid even in unloaded zones), dispatches marching
    /// warbands, and moves stolen loot into the camp's Spoils Chest.
    /// </summary>
    public static class RaiderCampDirector
    {
        public const int SpoilsChestWidth = LootStore.Width;
        public const int SpoilsChestHeight = LootStore.Height;

        public sealed class ActiveCampRecord
        {
            public int CampGuid;
            public string FactionId = "";
            public string FactionDisplayName = "";
            public Vector3 CampCenter;
            public Vector3 RaidTargetPos;
            public bool MinimapPinned;
            public readonly List<ZDO> Zdos = new List<ZDO>();
        }

        private static readonly Dictionary<int, ActiveCampRecord> _camps = new Dictionary<int, ActiveCampRecord>();

        public static IReadOnlyDictionary<int, ActiveCampRecord> ActiveCamps => _camps;

        public static void RegisterCampPiece(ZDO zdo)
        {
            if (zdo == null || !zdo.IsValid()) return;
            int campGuid = zdo.GetInt(FactionKeys.CampGuid, 0);
            if (campGuid == 0) return;

            if (!_camps.TryGetValue(campGuid, out ActiveCampRecord rec))
            {
                string factionId = zdo.GetString(FactionKeys.RaiderFaction, "marauders");
                string displayName = "Raider";
                if (RaiderFactionRegistry.TryGetFaction(factionId, out var def))
                {
                    displayName = def.DisplayName;
                }

                rec = new ActiveCampRecord
                {
                    CampGuid = campGuid,
                    FactionId = factionId,
                    FactionDisplayName = displayName,
                    CampCenter = zdo.GetVec3(FactionKeys.RaiderCampPos, zdo.GetPosition()),
                    RaidTargetPos = zdo.GetVec3(FactionKeys.RaiderTargetPos, zdo.GetPosition())
                };
                _camps[campGuid] = rec;
            }

            if (!rec.Zdos.Contains(zdo))
            {
                rec.Zdos.Add(zdo);
            }
        }

        public static void SetState(ZDO zdo, int state)
        {
            zdo.Set(FactionKeys.RaiderState, state);
            zdo.Set(FactionKeys.RaiderSince, ZNet.instance != null ? (float)ZNet.instance.GetTimeSeconds() : 0f);
        }

        public static bool TriggerFactionRaid(string factionId, Vector3 settlementPos, out ActiveCampRecord? record)
        {
            record = null;
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableMarauderHeists.Value) return false;
            if (ZDOMan.instance == null || ZNet.instance == null || !ZNet.instance.IsServer()) return false;

            // 1. Resolve faction taking progression into account
            if (!RaiderFactionRegistry.TryGetAvailableFaction(factionId, out IRaiderFactionDefinition faction))
            {
                if (!RaiderFactionRegistry.TrySelectProgressiveFaction(out faction))
                {
                    return false;
                }
            }

            float minDist = ModConfig.CampSpawnMinDistance.Value;
            float maxDist = Mathf.Max(minDist + 50f, ModConfig.CampSpawnMaxDistance.Value);

            // 2. Check if an existing camp can be re-used/reoccupied instead of placing a brand-new camp
            if (TryFindReusableCampNearby(faction.FactionId, settlementPos, minDist * 0.8f, maxDist * 1.2f, out ActiveCampRecord reusable))
            {
                if (ReoccupyCamp(reusable, faction, settlementPos))
                {
                    record = reusable;
                    return true;
                }
            }

            // 3. Otherwise find a valid new location
            if (!HospitalityScanner.FindValidLandPoint(settlementPos, minDist, maxDist, out Vector3 campPos)) return false;

            int campGuid = Random.Range(100000, int.MaxValue);
            Vector3 toward = settlementPos - campPos;
            toward.y = 0f;
            Quaternion campRot = Quaternion.LookRotation(toward.normalized, Vector3.up);

            ActiveCampRecord rec = new ActiveCampRecord
            {
                CampGuid = campGuid,
                FactionId = faction.FactionId,
                FactionDisplayName = faction.DisplayName,
                CampCenter = campPos,
                RaidTargetPos = settlementPos
            };

            // 1. Camp pieces
            for (int i = 0; i < faction.CampPieces.Count; i++)
            {
                CampPieceEntry piece = faction.CampPieces[i];
                int hash = piece.PrefabName.GetStableHashCode();
                Vector3 wp = GroundAt(campPos + campRot * piece.LocalOffset);

                ZDO zdo = ZDOMan.instance.CreateNewZDO(wp, hash);
                zdo.Persistent = true;
                zdo.SetPrefab(hash);
                zdo.SetRotation(campRot * Quaternion.Euler(0f, piece.LocalYawDegrees, 0f));
                zdo.Set(FactionKeys.CampGuid, campGuid);
                zdo.Set(FactionKeys.CampState, 0); // Active
                zdo.Set(FactionKeys.CampLastRaidTime, (float)ZNet.instance.GetTimeSeconds());

                if (piece.IsSpoilsChest)
                {
                    zdo.Set(FactionKeys.IsSpoilsChest, true);
                    zdo.Set(ZDOVars.s_addedDefaultItems, true);
                    SeedBounty(zdo, faction.WarlordBountyTable);
                }
                rec.Zdos.Add(zdo);
            }

            // 2. Warlord + guards (CampGuard state 3)
            SpawnRaider(faction, faction.WarlordPrefabName, GroundAt(campPos + campRot * new Vector3(0f, 0f, 1.5f)), campRot,
                campGuid, campPos, settlementPos, 3, rec);
            for (int g = 0; g < faction.CampGuardCount; g++)
            {
                float a = g / (float)Mathf.Max(1, faction.CampGuardCount) * Mathf.PI * 2f;
                Vector3 gp = GroundAt(campPos + new Vector3(Mathf.Cos(a) * 3.5f, 0f, Mathf.Sin(a) * 3.5f));
                SpawnRaider(faction, faction.RaiderPrefabName, gp, campRot, campGuid, campPos, settlementPos, 3, rec);
            }

            // 3. Raid warband (state 0)
            int raidSize = Random.Range(faction.MinRaidSize, faction.MaxRaidSize + 1);
            for (int r = 0; r < raidSize; r++)
            {
                Vector3 rp = GroundAt(campPos + campRot * new Vector3((r - raidSize * 0.5f) * 2f, 0f, -2.5f));
                SpawnRaider(faction, faction.RaiderPrefabName, rp, campRot, campGuid, campPos, settlementPos, 0, rec);
            }

            _camps[campGuid] = rec;
            record = rec;

            if (MessageHud.instance != null)
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center,
                    $"{faction.DisplayName} are marching on your stores!");
            }

            VikingFactionsPlugin.Log.LogInfo($"[Camps] '{faction.DisplayName}' camp {campGuid} at {campPos:F0}; {raidSize} raiders -> {settlementPos:F0}.");
            return true;
        }

        private static bool TryFindReusableCampNearby(
            string factionId,
            Vector3 center,
            float minDist,
            float maxDist,
            out ActiveCampRecord reusable)
        {
            reusable = null!;
            float minDistSq = minDist * minDist;
            float maxDistSq = maxDist * maxDist;

            foreach (var kvp in _camps)
            {
                ActiveCampRecord candidate = kvp.Value;
                if (!string.Equals(candidate.FactionId, factionId, System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                float distSq = Utils.DistanceXZ(center, candidate.CampCenter);
                distSq = distSq * distSq;
                if (distSq < minDistSq || distSq > maxDistSq) continue;

                // Verify the camp's chest or central structures are still alive in ZDOMan
                ZDO? chest = FindSpoilsChest(candidate.CampGuid);
                if (chest != null)
                {
                    reusable = candidate;
                    return true;
                }
            }

            return false;
        }

        private static bool ReoccupyCamp(
            ActiveCampRecord camp,
            IRaiderFactionDefinition faction,
            Vector3 targetPos)
        {
            camp.RaidTargetPos = targetPos;
            Vector3 campPos = camp.CampCenter;
            Vector3 toward = targetPos - campPos;
            toward.y = 0f;
            Quaternion campRot = Quaternion.LookRotation(toward.normalized, Vector3.up);

            // Clean up any stale destroyed ZDOs from the record list
            camp.Zdos.RemoveAll(z => z == null || !z.IsValid());

            // 1. Repair pieces and re-seed spoils chest if empty
            ZDO? chest = FindSpoilsChest(camp.CampGuid);
            if (chest != null)
            {
                Inventory chestInv = LootStore.LoadChest(chest);
                if (chestInv.NrOfItems() == 0)
                {
                    SeedBounty(chest, faction.WarlordBountyTable);
                }
                chest.Set(FactionKeys.CampState, 0); // Re-activate
                chest.Set(FactionKeys.CampLastRaidTime, (float)ZNet.instance.GetTimeSeconds());
            }

            // 2. Count live defenders and top them up if eliminated
            int existingGuards = 0;
            for (int i = 0; i < camp.Zdos.Count; i++)
            {
                ZDO z = camp.Zdos[i];
                if (z.GetInt(FactionKeys.RaiderState, -1) == 3)
                {
                    existingGuards++;
                }
            }

            if (existingGuards < faction.CampGuardCount + 1)
            {
                // Spawn warlord if needed
                SpawnRaider(faction, faction.WarlordPrefabName, GroundAt(campPos + campRot * new Vector3(0f, 0f, 1.5f)), campRot,
                    camp.CampGuid, campPos, targetPos, 3, camp);

                int needed = Mathf.Max(0, faction.CampGuardCount - existingGuards);
                for (int g = 0; g < needed; g++)
                {
                    float a = g / (float)Mathf.Max(1, needed) * Mathf.PI * 2f;
                    Vector3 gp = GroundAt(campPos + new Vector3(Mathf.Cos(a) * 3.5f, 0f, Mathf.Sin(a) * 3.5f));
                    SpawnRaider(faction, faction.RaiderPrefabName, gp, campRot, camp.CampGuid, campPos, targetPos, 3, camp);
                }
            }

            // 3. Dispatch fresh warband from this existing camp
            int raidSize = Random.Range(faction.MinRaidSize, faction.MaxRaidSize + 1);
            for (int r = 0; r < raidSize; r++)
            {
                Vector3 rp = GroundAt(campPos + campRot * new Vector3((r - raidSize * 0.5f) * 2f, 0f, -2.5f));
                SpawnRaider(faction, faction.RaiderPrefabName, rp, campRot, camp.CampGuid, campPos, targetPos, 0, camp);
            }

            if (MessageHud.instance != null)
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center,
                    $"{faction.DisplayName} are rearming at their camp and march again!");
            }

            VikingFactionsPlugin.Log.LogInfo($"[Camps] Reoccupied '{faction.DisplayName}' camp {camp.CampGuid} at {campPos:F0}; {raidSize} raiders -> {targetPos:F0}.");
            return true;
        }

        private static void SpawnRaider(IRaiderFactionDefinition faction, string prefab, Vector3 pos, Quaternion rot,
            int campGuid, Vector3 campPos, Vector3 target, int state, ActiveCampRecord rec)
        {
            ZDO? zdo = VirtualAgentDirector.SpawnVirtualAgentZdo(prefab, pos, rot, FactionRoles.Marauder, faction.UsesCustomHumanoidRig);
            if (zdo == null) return;

            zdo.Set(FactionKeys.CampGuid, campGuid);
            zdo.Set(FactionKeys.RaiderFaction, faction.FactionId);
            zdo.Set(FactionKeys.RaiderTargetPos, target);
            zdo.Set(FactionKeys.RaiderCampPos, campPos);
            zdo.Set(FactionKeys.RaiderStolen, 0);
            zdo.Set(ZDOVars.s_spawnPoint, campPos);
            SetState(zdo, state);
            rec.Zdos.Add(zdo);
        }

        private static Vector3 GroundAt(Vector3 p)
        {
            if (WorldGenerator.instance != null) p.y = WorldGenerator.instance.GetHeight(p.x, p.z);
            return p;
        }

        private static void SeedBounty(ZDO chestZdo, IReadOnlyList<BountyLootEntry> table)
        {
            if (table == null || table.Count == 0 || ObjectDB.instance == null) return;
            Inventory inv = LootStore.NewInventory("SpoilsChest");
            for (int i = 0; i < table.Count; i++)
            {
                BountyLootEntry e = table[i];
                if (Random.value > e.DropChance) continue;
                LootStore.AddPrefab(inv, e.ItemPrefabName, Random.Range(e.MinStack, e.MaxStack + 1));
            }
            LootStore.SaveChest(chestZdo, inv);
        }

        /// <summary>Moves a raider's carried loot into the camp's Spoils Chest and pins the camp on the minimap.</summary>
        public static void DepositLoot(ZDO raiderZdo)
        {
            int campGuid = raiderZdo.GetInt(FactionKeys.CampGuid, 0);
            Vector3 campPos = raiderZdo.GetVec3(FactionKeys.RaiderCampPos, raiderZdo.GetPosition());

            Inventory loot = LootStore.LoadBase64(raiderZdo, FactionKeys.RaiderLoot);
            if (loot.NrOfItems() > 0)
            {
                ZDO? chest = FindSpoilsChest(campGuid);
                if (chest != null)
                {
                    chest.SetOwner(ZDOMan.GetSessionID());
                    Inventory chestInv = LootStore.LoadChest(chest);
                    int overflow = LootStore.MoveAll(loot, chestInv);
                    LootStore.SaveChest(chest, chestInv);
                    if (overflow > 0) VikingFactionsPlugin.Log.LogWarning($"[Camps] Spoils chest full; {overflow} stolen stack(s) lost.");
                }
                LootStore.SaveBase64(raiderZdo, FactionKeys.RaiderLoot, LootStore.NewInventory());
                raiderZdo.Set(FactionKeys.RaiderStolen, 0);
                RevealCamp(campGuid, campPos);
            }
        }

        public static ZDO? FindSpoilsChest(int campGuid)
        {
            if (ZDOMan.instance == null || campGuid == 0) return null;
            int index = 0;
            List<ZDO> buffer = new List<ZDO>(64);
            while (true)
            {
                buffer.Clear();
                bool done = ZDOMan.instance.GetAllZDOsWithPrefabIterative("piece_chest", buffer, ref index);
                for (int i = 0; i < buffer.Count; i++)
                {
                    ZDO z = buffer[i];
                    if (z.GetBool(FactionKeys.IsSpoilsChest, false) && z.GetInt(FactionKeys.CampGuid, 0) == campGuid) return z;
                }
                if (done) break;
            }
            return null;
        }

        public static void RevealCamp(int campGuid, Vector3 campPos)
        {
            if (_camps.TryGetValue(campGuid, out ActiveCampRecord rec))
            {
                if (rec.MinimapPinned) return;
                rec.MinimapPinned = true;
            }

            if (Minimap.instance != null)
            {
                string label = rec != null ? $"{rec.FactionDisplayName} Camp" : "Raider Camp";
                Minimap.instance.AddPin(campPos, Minimap.PinType.Boss, label, true, false);
            }
            if (MessageHud.instance != null)
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center,
                    "Raiders escaped with your spoils! Their camp is marked on your map.");
            }
        }

        public static int ClearAllCamps()
        {
            int n = 0;
            foreach (ActiveCampRecord rec in _camps.Values)
            {
                for (int i = 0; i < rec.Zdos.Count; i++)
                {
                    ZDO z = rec.Zdos[i];
                    if (z != null && z.IsValid() && ZDOMan.instance != null)
                    {
                        ZDOMan.instance.DestroyZDO(z);
                        n++;
                    }
                }
            }
            _camps.Clear();
            return n;
        }
    }
}
