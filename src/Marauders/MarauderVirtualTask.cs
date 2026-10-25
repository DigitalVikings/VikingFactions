using UnityEngine;
using VikingAI.Profiles;
using VikingAI.Simulation;
using VikingFactions.Configuration;
using VikingFactions.Core;

namespace VikingFactions.Marauders
{
    /// <summary>
    /// Simulates unloaded raider ZDOs: warbands march on the settlement, loot-laden raiders march back to camp
    /// and deposit into the Spoils Chest, and warbands that arrive but are never loaded give up after a timeout.
    /// </summary>
    public sealed class MarauderVirtualTask : IVirtualAgentTask
    {
        private const float GiveUpSeconds = 180f;

        public string Id => "VikingFactions.MarauderVirtual";
        public NpcRoleFlags RequiredRole => FactionRoles.Marauder;

        public void TickUnloadedZdo(ZDO zdo, NpcRoleFlags roles, float dt)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableMarauderHeists.Value) return;
            if (!zdo.IsValid() || !zdo.IsOwner()) return;

            int state = zdo.GetInt(FactionKeys.RaiderState, 3);
            if (state == 3) return;

            double now = ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0.0;
            if (state == 0)
            {
                Vector3 target = zdo.GetVec3(FactionKeys.RaiderTargetPos, zdo.GetPosition());
                if (VirtualAgentDirector.MoveZdoAlongTerrain(zdo, target, 4.5f, dt, 14f))
                {
                    RaiderCampDirector.SetState(zdo, 1);
                }
            }
            else if (state == 1)
            {
                // Arrived while the settlement is unloaded: nothing to siphon from, so withdraw.
                if (now - zdo.GetFloat(FactionKeys.RaiderSince, (float)now) > GiveUpSeconds)
                {
                    RaiderCampDirector.SetState(zdo, 2);
                }
            }
            else if (state == 2)
            {
                bool carrying = zdo.GetInt(FactionKeys.RaiderStolen, 0) > 0;
                float speed = 4.6f * (carrying ? ModConfig.RaiderCarrySpeedMultiplier.Value : 1f);
                Vector3 camp = zdo.GetVec3(FactionKeys.RaiderCampPos, zdo.GetPosition());
                if (VirtualAgentDirector.MoveZdoAlongTerrain(zdo, camp, speed, dt, 6f))
                {
                    if (carrying) RaiderCampDirector.DepositLoot(zdo);
                    RaiderCampDirector.SetState(zdo, 3);
                }
            }
        }

        public void ApplyCatchUpOnLoad(Character agent, AgentBehaviorProfile profile, double elapsedOfflineSeconds)
        {
        }
    }
}
