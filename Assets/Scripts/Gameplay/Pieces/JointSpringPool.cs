using System.Collections.Generic;
using GravityPuzzle.Infrastructure.Pooling;
using GravityPuzzle.Presentation.Views;
using UnityEngine;

namespace GravityPuzzle.Gameplay.Pieces
{
    public static class JointSpringPool
    {
        private static GameObjectPool<JointSpringView> pool;
        private static readonly List<JointSpringView> activeSprings = new List<JointSpringView>();

        public static void Configure(JointSpringView prefab, Transform parent, int capacity)
        {
            ResetAllActive();

            if (prefab != null && capacity > 0)
            {
                pool = new GameObjectPool<JointSpringView>(prefab, parent, capacity);
                pool.Prewarm();
            }
            else
            {
                pool = null;
            }
        }

        public static bool TryRent(out JointSpringView spring)
        {
            if (pool != null && pool.TryRent(out spring))
            {
                activeSprings.Add(spring);
                return true;
            }

            spring = null;
            return false;
        }

        public static void ResetAllActive()
        {
            for (int i = activeSprings.Count - 1; i >= 0; i--)
            {
                if (activeSprings[i] != null)
                    activeSprings[i].Despawn();
            }
            activeSprings.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPool()
        {
            pool = null;
            activeSprings.Clear();
        }
    }
}
