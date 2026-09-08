using GravityPuzzle.Infrastructure.Pooling;
using UnityEngine;

namespace GravityPuzzle
{
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
            // The authored prefab must not use decorative artwork as a mask:
            // transparent regions in that artwork punch holes through the
            // feeding piece. The shared square sprite produces one solid
            // rectangular clipping area below the cutter line.
            spriteMask.sprite = PrototypeBootstrap.GetSquareSprite();
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
}
