using System.Collections.Generic;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;
using VikingFactions.Marauders;
using VikingFactions.Wayfarers;

namespace VikingFactions.Telemetry
{
    /// <summary>
    /// Developer console command: `vf spawn_wayfarer | trigger_raid [factionId] | status | clear_camps`.
    /// </summary>
    public sealed class VikingFactionsConsoleCommand : ConsoleCommand
    {
        public override string Name => "vf";
        public override string Help => "VikingFactions tools (spawn_wayfarer, trigger_raid [marauders|draugr|fulings], status, clear_camps)";
        public override bool IsCheat => true;

        public static void Register()
        {
            CommandManager.Instance.AddConsoleCommand(new VikingFactionsConsoleCommand());
        }

        public override void Run(string[] args)
        {
            if (Player.m_localPlayer == null)
            {
                Console.instance.Print("[VikingFactions] No local player.");
                return;
            }

            string sub = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "status";
            Vector3 pos = Player.m_localPlayer.transform.position;

            switch (sub)
            {
                case "spawn_wayfarer":
                    bool ok = HospitalityScanner.SpawnWayfarerTowards(pos, 60f, 100f);
                    Console.instance.Print(ok ? "[VikingFactions] Wayfarer dispatched toward your position." : "[VikingFactions] No valid spawn point found.");
                    break;

                case "trigger_raid":
                    string id = args!.Length > 1 ? args[1] : "marauders";
                    ok = RaiderCampDirector.TriggerFactionRaid(id, pos, out var camp);
                    Console.instance.Print(ok && camp != null
                        ? $"[VikingFactions] {camp.FactionDisplayName} camp placed at {camp.CampCenter:F0}."
                        : "[VikingFactions] Raid failed (unknown faction, disabled, not server, or no valid land).");
                    break;

                case "clear_camps":
                    Console.instance.Print($"[VikingFactions] Destroyed {RaiderCampDirector.ClearAllCamps()} camp/raider ZDOs.");
                    break;

                case "status":
                    Console.instance.Print(
                        $"[VikingFactions] Hearth comfort: {HospitalityScanner.LastEvaluatedComfort}, " +
                        $"active wayfarers: {HospitalityScanner.CountActiveWayfarers()}, " +
                        $"active camps: {RaiderCampDirector.ActiveCamps.Count}");
                    break;

                default:
                    Console.instance.Print("Usage: vf spawn_wayfarer | trigger_raid [factionId] | status | clear_camps");
                    break;
            }
        }

        public override List<string> CommandOptionList()
        {
            return new List<string> { "spawn_wayfarer", "trigger_raid", "status", "clear_camps" };
        }
    }
}
