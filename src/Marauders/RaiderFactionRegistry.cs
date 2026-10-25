using System;
using System.Collections.Generic;
using UnityEngine;

namespace VikingFactions.Marauders
{
    public readonly struct CampPieceEntry
    {
        public readonly string PrefabName;
        public readonly Vector3 LocalOffset;
        public readonly float LocalYawDegrees;
        public readonly bool IsSpoilsChest;

        public CampPieceEntry(string prefabName, Vector3 localOffset, float localYawDegrees = 0f, bool isSpoilsChest = false)
        {
            PrefabName = prefabName;
            LocalOffset = localOffset;
            LocalYawDegrees = localYawDegrees;
            IsSpoilsChest = isSpoilsChest;
        }
    }

    public readonly struct BountyLootEntry
    {
        public readonly string ItemPrefabName;
        public readonly int MinStack;
        public readonly int MaxStack;
        public readonly float DropChance;

        public BountyLootEntry(string itemPrefabName, int minStack, int maxStack, float dropChance = 1f)
        {
            ItemPrefabName = itemPrefabName;
            MinStack = minStack;
            MaxStack = maxStack;
            DropChance = dropChance;
        }
    }

    public interface IRaiderFactionDefinition
    {
        string FactionId { get; }
        string DisplayName { get; }
        string RaiderPrefabName { get; }
        string WarlordPrefabName { get; }
        bool UsesCustomHumanoidRig { get; }
        IReadOnlyList<string> RequiredGlobalKeys { get; }
        IReadOnlyList<string> ForbiddenGlobalKeys { get; }
        int MinRaidSize { get; }
        int MaxRaidSize { get; }
        int CampGuardCount { get; }
        string[] RaiderDefaultEquipment { get; }
        string[] WarlordDefaultEquipment { get; }
        IReadOnlyList<CampPieceEntry> CampPieces { get; }
        IReadOnlyList<BountyLootEntry> WarlordBountyTable { get; }
        IReadOnlyList<string> InterceptedVanillaEvents { get; }
    }

    public sealed class RaiderFactionDefinition : IRaiderFactionDefinition
    {
        public string FactionId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string RaiderPrefabName { get; set; } = "";
        public string WarlordPrefabName { get; set; } = "";
        public bool UsesCustomHumanoidRig { get; set; }
        public IReadOnlyList<string> RequiredGlobalKeys { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> ForbiddenGlobalKeys { get; set; } = Array.Empty<string>();
        public int MinRaidSize { get; set; } = 3;
        public int MaxRaidSize { get; set; } = 4;
        public int CampGuardCount { get; set; } = 2;
        public string[] RaiderDefaultEquipment { get; set; } = Array.Empty<string>();
        public string[] WarlordDefaultEquipment { get; set; } = Array.Empty<string>();
        public IReadOnlyList<CampPieceEntry> CampPieces { get; set; } = Array.Empty<CampPieceEntry>();
        public IReadOnlyList<BountyLootEntry> WarlordBountyTable { get; set; } = Array.Empty<BountyLootEntry>();
        public IReadOnlyList<string> InterceptedVanillaEvents { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// Open registry of raider factions and procedural camp blueprints.
    /// Downstream mods or expansions can register additional raider archetypes and camp layouts.
    /// </summary>
    public static class RaiderFactionRegistry
    {
        public const string MarauderPrefabName = "VF_Marauder";
        public const string WarlordPrefabName = "VF_Warlord";

        private static readonly Dictionary<string, IRaiderFactionDefinition> _factions =
            new Dictionary<string, IRaiderFactionDefinition>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, IRaiderFactionDefinition> _vanillaEventMap =
            new Dictionary<string, IRaiderFactionDefinition>(StringComparer.OrdinalIgnoreCase);

        public static void RegisterFaction(IRaiderFactionDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.FactionId)) return;

            _factions[definition.FactionId] = definition;
            if (definition.InterceptedVanillaEvents != null)
            {
                for (int i = 0; i < definition.InterceptedVanillaEvents.Count; i++)
                {
                    string evt = definition.InterceptedVanillaEvents[i];
                    if (!string.IsNullOrWhiteSpace(evt))
                    {
                        _vanillaEventMap[evt] = definition;
                    }
                }
            }
        }

        public static bool TryGetFaction(string factionId, out IRaiderFactionDefinition definition)
        {
            return _factions.TryGetValue(factionId, out definition!);
        }

        public static bool TryGetFactionForVanillaEvent(string eventName, out IRaiderFactionDefinition definition)
        {
            return _vanillaEventMap.TryGetValue(eventName, out definition!);
        }

        public static bool IsFactionAvailable(IRaiderFactionDefinition definition)
        {
            if (definition == null) return false;
            if (ZoneSystem.instance == null) return true;

            if (definition.RequiredGlobalKeys != null)
            {
                for (int i = 0; i < definition.RequiredGlobalKeys.Count; i++)
                {
                    string key = definition.RequiredGlobalKeys[i];
                    if (!string.IsNullOrWhiteSpace(key) && !ZoneSystem.instance.GetGlobalKey(key))
                    {
                        return false;
                    }
                }
            }

            if (definition.ForbiddenGlobalKeys != null)
            {
                for (int i = 0; i < definition.ForbiddenGlobalKeys.Count; i++)
                {
                    string key = definition.ForbiddenGlobalKeys[i];
                    if (!string.IsNullOrWhiteSpace(key) && ZoneSystem.instance.GetGlobalKey(key))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        public static bool TryGetAvailableFaction(string factionId, out IRaiderFactionDefinition definition)
        {
            definition = null!;
            if (!TryGetFaction(factionId, out var def)) return false;
            if (!IsFactionAvailable(def)) return false;
            definition = def;
            return true;
        }

        public static bool TrySelectProgressiveFaction(out IRaiderFactionDefinition chosen)
        {
            chosen = null!;
            List<IRaiderFactionDefinition> available = new List<IRaiderFactionDefinition>();
            foreach (var faction in _factions.Values)
            {
                if (IsFactionAvailable(faction))
                {
                    available.Add(faction);
                }
            }

            if (available.Count == 0) return false;

            available.Sort((a, b) => (b.RequiredGlobalKeys?.Count ?? 0).CompareTo(a.RequiredGlobalKeys?.Count ?? 0));

            if (available.Count > 1 && UnityEngine.Random.value < 0.3f)
            {
                chosen = available[UnityEngine.Random.Range(1, available.Count)];
            }
            else
            {
                chosen = available[0];
            }
            return true;
        }

        public static IEnumerable<IRaiderFactionDefinition> GetAllFactions() => _factions.Values;

        internal static void RegisterBuiltInFactions()
        {
            // 1. Exile Marauders (Human Bandits) - Available from game start
            RegisterFaction(new RaiderFactionDefinition
            {
                FactionId = "marauders",
                DisplayName = "Exile Marauders",
                RaiderPrefabName = MarauderPrefabName,
                WarlordPrefabName = WarlordPrefabName,
                UsesCustomHumanoidRig = true,
                RequiredGlobalKeys = Array.Empty<string>(),
                ForbiddenGlobalKeys = Array.Empty<string>(),
                MinRaidSize = 3,
                MaxRaidSize = 4,
                CampGuardCount = 2,
                RaiderDefaultEquipment = new[]
                {
                    "ArmorLeatherChest",
                    "ArmorLeatherLegs",
                    "HelmetLeather",
                    "AxeBronze",
                    "ShieldWood"
                },
                WarlordDefaultEquipment = new[]
                {
                    "ArmorIronChest",
                    "ArmorIronLegs",
                    "HelmetIron",
                    "CapeWolf",
                    "SwordIron",
                    "ShieldBanded"
                },
                CampPieces = new[]
                {
                    new CampPieceEntry("bonfire", Vector3.zero, 0f),
                    new CampPieceEntry("piece_chest", new Vector3(0f, 0f, 2.8f), 180f, isSpoilsChest: true),
                    new CampPieceEntry("piece_chair02", new Vector3(-2.4f, 0f, 0.5f), 90f),
                    new CampPieceEntry("piece_chair02", new Vector3(2.4f, 0f, 0.5f), -90f),
                    new CampPieceEntry("piece_banner01", new Vector3(-2.2f, 0f, 3.0f), 160f),
                    new CampPieceEntry("piece_banner01", new Vector3(2.2f, 0f, 3.0f), -160f),
                    new CampPieceEntry("wood_wall_roof_45", new Vector3(0f, 0f, 3.4f), 0f),
                    new CampPieceEntry("stake_wall", new Vector3(-4.5f, 0f, 4.5f), 45f),
                    new CampPieceEntry("stake_wall", new Vector3(4.5f, 0f, 4.5f), -45f),
                    new CampPieceEntry("stake_wall", new Vector3(-5.5f, 0f, 0f), 90f),
                    new CampPieceEntry("stake_wall", new Vector3(5.5f, 0f, 0f), -90f),
                    new CampPieceEntry("piece_groundtorch_wood", new Vector3(-3.0f, 0f, -3.5f), 0f),
                    new CampPieceEntry("piece_groundtorch_wood", new Vector3(3.0f, 0f, -3.5f), 0f)
                },
                WarlordBountyTable = new[]
                {
                    new BountyLootEntry("Coins", 35, 85, 1.0f),
                    new BountyLootEntry("Ruby", 2, 5, 0.85f),
                    new BountyLootEntry("AmberPearl", 2, 6, 0.85f),
                    new BountyLootEntry("SilverNecklace", 1, 2, 0.65f),
                    new BountyLootEntry("Iron", 4, 10, 0.75f)
                },
                InterceptedVanillaEvents = new[] { "army_eikthyr", "army_theelder", "skeletons" }
            });

            // 2. Draugr Plunderers (Undead Crypt Outpost) - Gated behind defeating The Elder
            RegisterFaction(new RaiderFactionDefinition
            {
                FactionId = "draugr",
                DisplayName = "Draugr Reavers",
                RaiderPrefabName = "Draugr",
                WarlordPrefabName = "Draugr_Elite",
                UsesCustomHumanoidRig = false,
                RequiredGlobalKeys = new[] { "defeated_gdking" },
                ForbiddenGlobalKeys = Array.Empty<string>(),
                MinRaidSize = 3,
                MaxRaidSize = 4,
                CampGuardCount = 2,
                CampPieces = new[]
                {
                    new CampPieceEntry("fire_pit", Vector3.zero, 0f),
                    new CampPieceEntry("piece_chest", new Vector3(0f, 0f, 2.6f), 180f, isSpoilsChest: true),
                    new CampPieceEntry("stone_wall_2x1", new Vector3(0f, 0f, 4.0f), 0f),
                    new CampPieceEntry("stone_wall_2x1", new Vector3(-3.2f, 0f, 2.5f), 60f),
                    new CampPieceEntry("stone_wall_2x1", new Vector3(3.2f, 0f, 2.5f), -60f),
                    new CampPieceEntry("piece_groundtorch_green", new Vector3(-2.6f, 0f, -2.6f), 0f),
                    new CampPieceEntry("piece_groundtorch_green", new Vector3(2.6f, 0f, -2.6f), 0f)
                },
                WarlordBountyTable = new[]
                {
                    new BountyLootEntry("Entrails", 6, 12, 1.0f),
                    new BountyLootEntry("IronScrap", 6, 14, 0.90f),
                    new BountyLootEntry("Chain", 1, 3, 0.65f),
                    new BountyLootEntry("Coins", 25, 60, 0.85f)
                },
                InterceptedVanillaEvents = new[] { "army_bonemass" }
            });

            // 3. Fuling Raid Party (Goblin Outpost) - Gated behind defeating Moder
            RegisterFaction(new RaiderFactionDefinition
            {
                FactionId = "fulings",
                DisplayName = "Fuling Plunderers",
                RaiderPrefabName = "Goblin",
                WarlordPrefabName = "GoblinBrute",
                UsesCustomHumanoidRig = false,
                RequiredGlobalKeys = new[] { "defeated_dragon" },
                ForbiddenGlobalKeys = Array.Empty<string>(),
                MinRaidSize = 4,
                MaxRaidSize = 5,
                CampGuardCount = 3,
                CampPieces = new[]
                {
                    new CampPieceEntry("bonfire", Vector3.zero, 0f),
                    new CampPieceEntry("piece_chest", new Vector3(0f, 0f, 3.0f), 180f, isSpoilsChest: true),
                    new CampPieceEntry("stake_wall", new Vector3(-4.2f, 0f, 4.2f), 45f),
                    new CampPieceEntry("stake_wall", new Vector3(4.2f, 0f, 4.2f), -45f),
                    new CampPieceEntry("stake_wall", new Vector3(-5.2f, 0f, 0f), 90f),
                    new CampPieceEntry("stake_wall", new Vector3(5.2f, 0f, 0f), -90f),
                    new CampPieceEntry("piece_groundtorch_wood", new Vector3(-3.0f, 0f, -3.2f), 0f),
                    new CampPieceEntry("piece_groundtorch_wood", new Vector3(3.0f, 0f, -3.2f), 0f)
                },
                WarlordBountyTable = new[]
                {
                    new BountyLootEntry("BlackMetalScrap", 8, 18, 1.0f),
                    new BountyLootEntry("Coins", 60, 140, 1.0f),
                    new BountyLootEntry("Needle", 5, 12, 0.75f),
                    new BountyLootEntry("LinenThread", 4, 10, 0.70f)
                },
                InterceptedVanillaEvents = new[] { "army_goblin" }
            });
        }
    }
}
