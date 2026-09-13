using System;
using System.Collections;
using System.Reflection;
using DV.Teleporters;
using UnityEngine;

namespace DVSurvival.Mod
{
    internal static class NativeHomeFastTravel
    {
        private static readonly MethodInfo Travel = typeof(FastTravelController).GetMethod("FastTravel",
            BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo WithoutLoco = typeof(FastTravelController).GetMethod("FastTravelWithoutLocomotive",
            BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo MoveDirection = typeof(CustomFirstPersonController).GetField("m_MoveDir",
            BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo UnderwaterVelocity = typeof(CustomFirstPersonController).GetField("underwaterVelocity",
            BindingFlags.NonPublic | BindingFlags.Instance);

        public static void Start(FastTravelController controller, FastTravelDestination house,
            CustomFirstPersonController player, Action<Exception> finished)
        {
            if (Travel == null || WithoutLoco == null) throw new MissingMethodException("Native fast travel API changed.");
            if (player != null)
            {
                // Clear pre-death momentum once; loading and movement remain owned by the game.
                MoveDirection.SetValue(player, Vector3.zero);
                UnderwaterVelocity.SetValue(player, Vector3.zero);
                player.m_Jumping = false;
                player.prevFrameLandingVelocity = Vector3.zero;
            }
            var withoutLoco = (Func<Transform, IEnumerator>)Delegate.CreateDelegate(
                typeof(Func<Transform, IEnumerator>), controller, WithoutLoco);
            // Payment lives in OnFastTravelRequested, not this native coroutine. Bypass only that
            // purchase entry point: full loading screen, terrain/FPS wait and native notifications.
            // Duration zero avoids advancing the shared world clock for a player's death.
            var routine = (IEnumerator)Travel.Invoke(controller, new object[] { house, withoutLoco, false, 0 });
            controller.StartCoroutine(Observe(routine, finished));
        }

        private static IEnumerator Observe(IEnumerator routine, Action<Exception> finished)
        {
            while (true)
            {
                bool hasNext;
                object current = null;
                Exception failure = null;
                try { hasNext = routine.MoveNext(); if (hasNext) current = routine.Current; }
                catch (Exception error) { hasNext = false; failure = error; }
                if (!hasNext)
                {
                    var disposable = routine as IDisposable;
                    if (disposable != null) disposable.Dispose();
                    finished(failure);
                    yield break;
                }
                yield return current;
            }
        }
    }
}
