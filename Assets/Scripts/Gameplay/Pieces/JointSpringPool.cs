using GravityPuzzle.Infrastructure.Pooling;
using GravityPuzzle.Presentation.Views;
using UnityEngine;

namespace GravityPuzzle.Gameplay.Pieces
{
    public static class JointSpringPool
    {
        private static GameObjectPool<JointSpringView> pool;

        public static void Configure(JointSpringView prefab, Transform parent, int capacity)
        {
            pool = null;

            if (prefab != null && capacity > 0)
            {
                pool = new GameObjectPool<JointSpringView>(prefab, parent, capacity);
                pool.Prewarm();
            }
        }

        public static bool TryRent(out JointSpringView spring)
        {
            if (pool != null && pool.TryRent(out spring))
                return true;

            spring = null;
            return false;
        }

        public static void ResetAllActive()
        {
            pool?.ReturnAllRented();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetPool()
        {
            pool = null;
        }
    }
}
