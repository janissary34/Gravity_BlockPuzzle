using GravityPuzzle.Infrastructure.Pooling;
using UnityEngine;

namespace GravityPuzzle
{
    public static class ShredderWheelPool
    {
        private static GameObjectPool<ShredderWheel> pool;

        public static void Configure(ShredderWheel prefab, Transform parent, int capacity)
        {
            if (prefab == null)
                return;

            // Scene reloads can destroy a previous pool parent while static state
            // remains alive (notably with domain reload disabled). Never retain
            // those destroyed wheel references into a new scene.
            pool = null;
            pool = new GameObjectPool<ShredderWheel>(prefab, parent, capacity);
            pool.Prewarm();
        }

        public static bool TryRent(out ShredderWheel wheel)
        {
            if (pool != null)
                return pool.TryRent(out wheel);

            wheel = null;
            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPool() => pool = null;
    }
}
