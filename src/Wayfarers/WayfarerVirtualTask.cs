using UnityEngine;
using VikingAI.Profiles;
using VikingAI.Simulation;
using VikingFactions.Configuration;
using VikingFactions.Core;

namespace VikingFactions.Wayfarers
{
    /// <summary>
    /// Simulates unloaded Wayfarer ZDOs: marches approaching Wayfarers to the hearth, sends neglected
    /// visitors home after a long unattended stay, and destroys departing Wayfarers at their origin.
    /// </summary>
    public sealed class WayfarerVirtualTask : IVirtualAgentTask
    {
        private const float UnattendedStaySeconds = 1200f;

        public string Id => "VikingFactions.WayfarerVirtual";
        public NpcRoleFlags RequiredRole => FactionRoles.Wayfarer;

        public void TickUnloadedZdo(ZDO zdo, NpcRoleFlags roles, float dt)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableWayfarers.Value) return;
            if (!zdo.IsValid() || !zdo.IsOwner()) return;

            int state = zdo.GetInt(FactionKeys.WfState, 0);
            double now = ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0.0;

            if (state == 0)
            {
                Vector3 target = zdo.GetVec3(FactionKeys.WfTargetPos, zdo.GetPosition());
                if (VirtualAgentDirector.MoveZdoAlongTerrain(zdo, target, 3.4f, dt, 8f))
                {
                    zdo.Set(FactionKeys.WfState, 1);
                    zdo.Set(FactionKeys.WfSince, (float)now);
                }
            }
            else if (state == 1)
            {
                if (now - zdo.GetFloat(FactionKeys.WfSince, (float)now) > UnattendedStaySeconds)
                {
                    zdo.Set(FactionKeys.WfState, 2);
                }
            }
            else
            {
                Vector3 origin = zdo.GetVec3(FactionKeys.WfOriginPos, zdo.GetPosition());
                if (VirtualAgentDirector.MoveZdoAlongTerrain(zdo, origin, 3.6f, dt, 8f) && ZDOMan.instance != null)
                {
                    ZDOMan.instance.DestroyZDO(zdo);
                }
            }
        }

        public void ApplyCatchUpOnLoad(Character agent, AgentBehaviorProfile profile, double elapsedOfflineSeconds)
        {
        }
    }
}
