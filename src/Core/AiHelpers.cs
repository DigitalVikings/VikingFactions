using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace VikingFactions.Core
{
    /// <summary>Reflection shims for protected BaseAI members and shared NPC speech helpers.</summary>
    public static class AiHelpers
    {
        private static readonly MethodInfo _moveTo = AccessTools.Method(typeof(BaseAI), "MoveTo");
        private static readonly MethodInfo _lookAt = AccessTools.Method(typeof(BaseAI), "LookAt");
        private static readonly object[] _moveArgs = new object[4];
        private static readonly object[] _lookArgs = new object[1];

        public static bool MoveTo(BaseAI ai, float dt, Vector3 point, float dist, bool run)
        {
            _moveArgs[0] = dt;
            _moveArgs[1] = point;
            _moveArgs[2] = dist;
            _moveArgs[3] = run;
            return _moveTo != null && (bool)_moveTo.Invoke(ai, _moveArgs);
        }

        public static void LookAt(BaseAI ai, Vector3 point)
        {
            _lookArgs[0] = point;
            _lookAt?.Invoke(ai, _lookArgs);
        }

        public static void Say(Character speaker, string text)
        {
            if (Chat.instance == null || speaker == null) return;
            Chat.instance.SetNpcText(speaker.gameObject, Vector3.up * 1.8f, 20f, 6f, speaker.m_name, text, false);
        }

        public static bool PlayerNear(Vector3 pos, float radius)
        {
            List<Player> players = Player.GetAllPlayers();
            float rSq = radius * radius;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] != null && (players[i].transform.position - pos).sqrMagnitude <= rSq) return true;
            }
            return false;
        }
    }
}
