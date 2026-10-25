using BepInEx.Configuration;

namespace VikingFactions.Configuration
{
    public static class ModConfig
    {
        // 1 - General
        public static ConfigEntry<bool> ModEnabled { get; private set; } = null!;

        // 2 - Wayfarers & Mead Hall
        public static ConfigEntry<bool> EnableWayfarers { get; private set; } = null!;
        public static ConfigEntry<int> MinHospitalityComfort { get; private set; } = null!;
        public static ConfigEntry<int> ComfortPerWayfarerCap { get; private set; } = null!;
        public static ConfigEntry<float> WayfarerSpawnIntervalSeconds { get; private set; } = null!;
        public static ConfigEntry<float> WayfarerMealIntervalSeconds { get; private set; } = null!;

        // 3 - Marauders & Retaliation Camps
        public static ConfigEntry<bool> EnableMarauderHeists { get; private set; } = null!;
        public static ConfigEntry<bool> InterceptVanillaRaids { get; private set; } = null!;
        public static ConfigEntry<int> MaxStacksPerRaider { get; private set; } = null!;
        public static ConfigEntry<float> RaiderSiphonSecondsPerStack { get; private set; } = null!;
        public static ConfigEntry<float> RaiderCarrySpeedMultiplier { get; private set; } = null!;
        public static ConfigEntry<bool> RaidersRespectWards { get; private set; } = null!;
        public static ConfigEntry<float> CampSpawnMinDistance { get; private set; } = null!;
        public static ConfigEntry<float> CampSpawnMaxDistance { get; private set; } = null!;

        public static void Init(ConfigFile config)
        {
            ModEnabled = Bind(
                config,
                "1 - General",
                "Enabled",
                true,
                "Master switch to enable or disable all features of VikingFactions.",
                order: 100,
                isAdminOnly: true
            );

            // 2 - Wayfarers & Mead Hall
            EnableWayfarers = Bind(
                config,
                "2 - Wayfarers & Mead Hall",
                "EnableWayfarers",
                true,
                "Enable friendly Wayfarer travelers to visit qualifying Mead Halls and settlements.",
                order: 90,
                isAdminOnly: true
            );

            MinHospitalityComfort = Bind(
                config,
                "2 - Wayfarers & Mead Hall",
                "MinHospitalityComfort",
                4,
                "Minimum comfort level near a lit hearth with roof cover required to attract Wayfarers.",
                order: 80,
                new AcceptableValueRange<int>(1, 20),
                isAdminOnly: true
            );

            ComfortPerWayfarerCap = Bind(
                config,
                "2 - Wayfarers & Mead Hall",
                "ComfortPerWayfarerCap",
                3,
                "Comfort points required per simultaneous Wayfarer guest (e.g. Comfort 12 allows up to 4 Wayfarers).",
                order: 70,
                new AcceptableValueRange<int>(1, 10),
                isAdminOnly: true
            );

            WayfarerSpawnIntervalSeconds = Bind(
                config,
                "2 - Wayfarers & Mead Hall",
                "WayfarerSpawnIntervalSeconds",
                300f,
                "Minimum interval in seconds between new Wayfarer arrivals at a qualifying Mead Hall.",
                order: 60,
                new AcceptableValueRange<float>(30f, 3600f),
                isAdminOnly: true
            );

            WayfarerMealIntervalSeconds = Bind(
                config,
                "2 - Wayfarers & Mead Hall",
                "WayfarerMealIntervalSeconds",
                1200f,
                "Interval in seconds between Wayfarer meals consumed from nearby communal chests.",
                order: 50,
                new AcceptableValueRange<float>(120f, 7200f),
                isAdminOnly: true
            );

            // 3 - Marauders & Retaliation Camps
            EnableMarauderHeists = Bind(
                config,
                "3 - Marauders & Retaliation Camps",
                "EnableMarauderHeists",
                true,
                "Enable Marauder warbands, heist chest siphoning, and procedural retaliation camps.",
                order: 90,
                isAdminOnly: true
            );

            InterceptVanillaRaids = Bind(
                config,
                "3 - Marauders & Retaliation Camps",
                "InterceptVanillaRaids",
                true,
                "Convert eligible vanilla base raid events into physicalized VikingFactions camps and marching warbands.",
                order: 80,
                isAdminOnly: true
            );

            MaxStacksPerRaider = Bind(
                config,
                "3 - Marauders & Retaliation Camps",
                "MaxStacksPerRaider",
                3,
                "Maximum number of item stacks a single raider can siphon before retreating to camp.",
                order: 70,
                new AcceptableValueRange<int>(1, 10),
                isAdminOnly: true
            );

            RaiderSiphonSecondsPerStack = Bind(
                config,
                "3 - Marauders & Retaliation Camps",
                "RaiderSiphonSecondsPerStack",
                4.5f,
                "Seconds a raider must channel at an unlocked chest to siphon one stack of items.",
                order: 60,
                new AcceptableValueRange<float>(1f, 20f),
                isAdminOnly: true
            );

            RaiderCarrySpeedMultiplier = Bind(
                config,
                "3 - Marauders & Retaliation Camps",
                "RaiderCarrySpeedMultiplier",
                0.85f,
                "Movement speed multiplier applied to raiders carrying stolen loot.",
                order: 50,
                new AcceptableValueRange<float>(0.3f, 1.0f),
                isAdminOnly: true
            );

            RaidersRespectWards = Bind(
                config,
                "3 - Marauders & Retaliation Camps",
                "RaidersRespectWards",
                true,
                "If true, raiders will not siphon chests that lie inside an active player ward area.",
                order: 45,
                isAdminOnly: true
            );

            CampSpawnMinDistance = Bind(
                config,
                "3 - Marauders & Retaliation Camps",
                "CampSpawnMinDistance",
                250f,
                "Minimum distance in meters from the target settlement to spawn a procedural raider camp.",
                order: 40,
                new AcceptableValueRange<float>(120f, 400f),
                isAdminOnly: true
            );

            CampSpawnMaxDistance = Bind(
                config,
                "3 - Marauders & Retaliation Camps",
                "CampSpawnMaxDistance",
                450f,
                "Maximum distance in meters from the target settlement to spawn a procedural raider camp.",
                order: 30,
                new AcceptableValueRange<float>(200f, 800f),
                isAdminOnly: true
            );
        }

        private static ConfigEntry<T> Bind<T>(
            ConfigFile config,
            string section,
            string key,
            T defaultValue,
            string description,
            int order = 0,
            AcceptableValueBase? acceptableValues = null,
            bool isAdminOnly = false)
        {
            var tags = new ConfigurationManagerAttributes
            {
                Order = order,
                Category = section,
                IsAdminOnly = isAdminOnly
            };
            var configDesc = new ConfigDescription(description, acceptableValues, tags);
            return config.Bind(section, key, defaultValue, configDesc);
        }
    }
}
