using VikingAI.NPC.Prefabs;
using VikingAI.Profiles;
using VikingFactions.Core;
using VikingFactions.Marauders;
using VikingFactions.Wayfarers;

namespace VikingFactions
{
    /// <summary>Registers the zero-asset humanoid prefabs VikingFactions needs (Wayfarer, Marauder, Warlord).</summary>
    public static class FactionPrefabs
    {
        public static void RegisterAll()
        {
            HumanoidPrefabBuilder.CreateHumanoidNpcPrefab(new HumanoidPrefabConfig
            {
                PrefabName = HospitalityScanner.WayfarerPrefabName,
                DisplayName = "Wayfarer",
                Faction = Character.Faction.Players,
                Group = "VF_Wayfarers",
                MaxHealth = 90f,
                IsAggravatable = false,
                DefaultItemNames = new[] { "ArmorRagsChest", "ArmorRagsLegs", "CapeDeerHide" },
                BehaviorProfile = AgentBehaviorProfile.CreateVillagerDefault(HospitalityScanner.WayfarerPrefabName, FactionRoles.Wayfarer)
            });

            if (RaiderFactionRegistry.TryGetFaction("marauders", out IRaiderFactionDefinition marauders))
            {
                HumanoidPrefabBuilder.CreateHumanoidNpcPrefab(new HumanoidPrefabConfig
                {
                    PrefabName = RaiderFactionRegistry.MarauderPrefabName,
                    DisplayName = "Exile Marauder",
                    Faction = Character.Faction.PlainsMonsters,
                    Group = "VF_Marauders",
                    MaxHealth = 160f,
                    IsAggravatable = true,
                    DefaultItemNames = marauders.RaiderDefaultEquipment,
                    BehaviorProfile = AgentBehaviorProfile.CreateVillagerDefault(RaiderFactionRegistry.MarauderPrefabName, FactionRoles.Marauder)
                });

                HumanoidPrefabBuilder.CreateHumanoidNpcPrefab(new HumanoidPrefabConfig
                {
                    PrefabName = RaiderFactionRegistry.WarlordPrefabName,
                    DisplayName = "Marauder Warlord",
                    Faction = Character.Faction.PlainsMonsters,
                    Group = "VF_Marauders",
                    MaxHealth = 700f,
                    IsAggravatable = true,
                    DefaultItemNames = marauders.WarlordDefaultEquipment,
                    BehaviorProfile = AgentBehaviorProfile.CreateVillagerDefault(RaiderFactionRegistry.WarlordPrefabName, FactionRoles.Marauder)
                });
            }
        }
    }
}
