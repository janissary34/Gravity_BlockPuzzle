using GravityPuzzle.Infrastructure.Pooling;
using UnityEngine;

namespace GravityPuzzle
{
    public static class ShredderCatchZonePool
    {
        private static GameObjectPool<ShredderCatchZone> pool;

        public static void Configure(ShredderCatchZone prefab, Transform parent, int capacity)
        {
            if (prefab == null)
                return;

            pool = new GameObjectPool<ShredderCatchZone>(prefab, parent, capacity);
            pool.Prewarm();
        }

        public static bool TryRent(out ShredderCatchZone zone)
        {
            if (pool != null)
                return pool.TryRent(out zone);

            zone = null;
            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPool() => pool = null;
    }

    /// <summary>
    /// Authored visual clipping mask for a shredder feed. It is rented once and
    /// configured by board coordinates; no runtime GameObject or component is
    /// manufactured during gameplay.
    /// </summary>
    [RequireComponent(typeof(SpriteMask))]
    public sealed class ShredderFeedMask : MonoBehaviour, IPoolable
    {
        private SpriteMask spriteMask;

        private void Awake()
        {
            spriteMask = GetComponent<SpriteMask>();
        }

        public void Configure(float shredderY, float verticalOffset, Vector2 scale)
        {
            transform.position = new Vector3(0f, shredderY + verticalOffset, 0f);
            transform.localScale = new Vector3(scale.x, scale.y, 1f);
            spriteMask.enabled = true;
        }

        public void OnSpawn()
        {
            spriteMask.enabled = true;
        }

        public void OnDespawn()
        {
            spriteMask.enabled = false;
        }
    }

    public static class ShredderFeedMaskPool
    {
        private static GameObjectPool<ShredderFeedMask> pool;

        public static void Configure(ShredderFeedMask prefab, Transform parent)
        {
            if (prefab == null)
                return;

            pool = new GameObjectPool<ShredderFeedMask>(prefab, parent, 1);
            pool.Prewarm();
        }

        public static bool TryRent(out ShredderFeedMask mask)
        {
            if (pool != null)
                return pool.TryRent(out mask);

            mask = null;
            return false;
        }

        public static void Return(ShredderFeedMask mask)
        {
            if (pool != null)
                pool.Return(mask);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPool() => pool = null;
    }
}
