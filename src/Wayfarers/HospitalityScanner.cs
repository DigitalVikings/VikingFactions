using System.Collections.Generic;
using UnityEngine;
using VikingAI.Core;
using VikingAI.Simulation;
using VikingFactions.Configuration;
using VikingFactions.Core;

namespace VikingFactions.Wayfarers
{
    /// <summary>
    /// Evaluates active hearths for Hospitality Aura (lit Fireplace + roof cover + Comfort >= MinHospitalityComfort)
    /// and dispatches friendly Wayfarers from the unloaded simulation layer.
    /// </summary>
    public static class HospitalityScanner
    {
        public const string WayfarerPrefabName = "VF_Wayfarer";

        private static float _nextScanTime;
        private static float _lastSpawnTime = -9999f;
        private static readonly HashSet<ZDO> _wayfarerZdos = new HashSet<ZDO>();

        public static int LastEvaluatedComfort { get; private set; }
        public static Vector3 LastQualifyingHearthPos { get; private set; }
        public static int ActiveWayfarerCount { get; private set; }

        public static void RegisterWayfarer(ZDO zdo)
        {
            if (zdo != null) _wayfarerZdos.Add(zdo);
        }

        public static void Tick()
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableWayfarers.Value) return;
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            if (Player.m_localPlayer == null) return;

            float now = Time.time;
            if (now < _nextScanTime) return;
            _nextScanTime = now + 15f;

            EvaluateAndSchedule(Player.m_localPlayer.transform.position, now);
        }

        public static bool TryFindQualifyingHearth(Vector3 origin, out Vector3 hearthPos, out int comfort)
        {
            hearthPos = Vector3.zero;
            comfort = 0;
            int best = 0;
            float rSq = 60f * 60f;

            foreach (Fireplace fp in PieceRegistry.Fireplaces)
            {
                if (fp == null || (fp.transform.position - origin).sqrMagnitude > rSq) continue;
                if (!fp.IsBurning()) continue;

                Vector3 check = fp.transform.position + Vector3.up * 0.8f;
                Cover.GetCoverForPoint(check, out float cover, out bool underRoof, 0.5f);
                if (!underRoof && cover < 0.7f) continue;

                int c = SE_Rested.CalculateComfortLevel(true, check);
                if (c > best)
                {
                    best = c;
                    hearthPos = fp.transform.position;
                }
            }

            if (best < ModConfig.MinHospitalityComfort.Value) return false;
            comfort = best;
            return true;
        }

        private static void EvaluateAndSchedule(Vector3 playerPos, float now)
        {
            if (!TryFindQualifyingHearth(playerPos, out Vector3 hearth, out int comfort))
            {
                LastEvaluatedComfort = 0;
                return;
            }

            LastEvaluatedComfort = comfort;
            LastQualifyingHearthPos = hearth;

            int cap = Mathf.Max(1, comfort / Mathf.Max(1, ModConfig.ComfortPerWayfarerCap.Value));
            int current = CountActiveWayfarers();
            ActiveWayfarerCount = current;

            if (current >= cap) return;
            if (now - _lastSpawnTime < ModConfig.WayfarerSpawnIntervalSeconds.Value) return;

            if (SpawnWayfarerTowards(hearth))
            {
                _lastSpawnTime = now;
                ActiveWayfarerCount = current + 1;
            }
        }

        public static int CountActiveWayfarers()
        {
            int count = 0;
            _wayfarerZdos.RemoveWhere(z => z == null || !z.IsValid());
            foreach (ZDO z in _wayfarerZdos)
            {
                if (z.GetInt(FactionKeys.WfState, 0) != 2) count++;
            }
            return count;
        }

        public static bool SpawnWayfarerTowards(Vector3 hearthPos, float minDist = 150f, float maxDist = 280f)
        {
            if (!FindValidLandPoint(hearthPos, minDist, maxDist, out Vector3 spawnPos)) return false;

            ZDO? zdo = VirtualAgentDirector.SpawnVirtualAgentZdo(
                WayfarerPrefabName,
                spawnPos,
                Quaternion.LookRotation((hearthPos - spawnPos).normalized),
                FactionRoles.Wayfarer,
                isCustomNpc: true);
            if (zdo == null) return false;

            double now = ZNet.instance.GetTimeSeconds();
            zdo.Set(FactionKeys.WfState, 0);
            zdo.Set(FactionKeys.WfTargetPos, hearthPos);
            zdo.Set(FactionKeys.WfOriginPos, spawnPos);
            zdo.Set(FactionKeys.WfLastMeal, (float)now);
            zdo.Set(FactionKeys.WfMood, 0);
            zdo.Set(FactionKeys.WfSince, (float)now);
            RegisterWayfarer(zdo);

            VikingFactionsPlugin.Log.LogInfo($"[Hospitality] Wayfarer dispatched from {spawnPos:F0} toward hearth {hearthPos:F0} (comfort {LastEvaluatedComfort}).");
            return true;
        }

        public static bool FindValidLandPoint(Vector3 center, float minDist, float maxDist, out Vector3 pos)
        {
            pos = Vector3.zero;
            if (WorldGenerator.instance == null) return false;

            float water = ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : 30f;
            for (int i = 0; i < 24; i++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                float d = Random.Range(minDist, maxDist);
                Vector3 c = center + new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);

                Heightmap.Biome biome = WorldGenerator.instance.GetBiome(c.x, c.z);
                if (biome == Heightmap.Biome.Ocean || biome == Heightmap.Biome.None) continue;

                float h = WorldGenerator.instance.GetHeight(c.x, c.z);
                if (h <= water + 1.2f) continue;

                c.y = h;
                pos = c;
                return true;
            }
            return false;
        }
    }
}
