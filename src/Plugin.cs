using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Managers;
using VikingAI.Simulation;
using VikingAI.Tactics;
using VikingFactions.Configuration;
using VikingFactions.Core;
using VikingFactions.Marauders;
using VikingFactions.Telemetry;
using VikingFactions.Wayfarers;

namespace VikingFactions
{
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    [BepInDependency("com.jotunn.jotunn")]
    [BepInDependency("com.digitalvikings.vikingai")]
    public class VikingFactionsPlugin : BaseUnityPlugin
    {
        public const string ModGuid = "com.digitalvikings.vikingfactions";
        public const string ModName = "VikingFactions";
        public const string ModVersion = "0.1.0";

        internal static ManualLogSource Log = null!;
        private Harmony _harmony = null!;

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo($"{ModName} v{ModVersion} initializing...");

            ModConfig.Init(Config);
            RaiderFactionRegistry.RegisterBuiltInFactions();

            BrainPipeline.RegisterModule(new WayfarerBrainModule());
            BrainPipeline.RegisterModule(new MarauderHeistBrainModule());
            VirtualAgentDirector.RegisterTask(new WayfarerVirtualTask());
            VirtualAgentDirector.RegisterTask(new MarauderVirtualTask());
            VirtualAgentDirector.OnAgentRestored += OnAgentRestoredAfterWorldLoad;

            PrefabManager.OnVanillaPrefabsAvailable += FactionPrefabs.RegisterAll;
            VikingFactionsConsoleCommand.Register();

            _harmony = new Harmony(ModGuid);
            _harmony.PatchAll();

            Log.LogInfo($"{ModName} v{ModVersion} loaded successfully.");
        }

        private void Update()
        {
            if (!ModConfig.ModEnabled.Value) return;
            HospitalityScanner.Tick();
        }

        private void OnDestroy()
        {
            VirtualAgentDirector.OnAgentRestored -= OnAgentRestoredAfterWorldLoad;
            PrefabManager.OnVanillaPrefabsAvailable -= FactionPrefabs.RegisterAll;
            _harmony?.UnpatchSelf();
        }

        private static void OnAgentRestoredAfterWorldLoad(ZDO zdo)
        {
            if (zdo == null || !zdo.IsValid()) return;

            if (zdo.GetPrefab() == HospitalityScanner.WayfarerPrefabName.GetStableHashCode())
            {
                HospitalityScanner.RegisterWayfarer(zdo);
            }
            else if (zdo.GetInt(FactionKeys.RaiderState, -1) >= 0)
            {
                RaiderCampDirector.RegisterCampPiece(zdo);
            }
        }
    }
}
