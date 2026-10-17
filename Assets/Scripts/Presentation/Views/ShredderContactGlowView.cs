using GravityPuzzle.Infrastructure.Pooling;
using UnityEngine;

namespace GravityPuzzle.Presentation.Views
{
    /// <summary>
    /// Presentation-only additive friction glow bar positioned over the cutter contact seam.
    /// Uses an additive material to saturate edge contrast and disguise the horizontal mask cutoff.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class ShredderContactGlowView : MonoBehaviour, IPoolable
    {
        [SerializeField] private SpriteRenderer spriteRenderer;
        [Tooltip("Target world-space vertical height of the friction glow bar.")]
        [SerializeField] private float heightMultiplier = 0.35f;
        [Tooltip("Multiplier applied to the contacting block width.")]
        [SerializeField] private float widthMargin = 1.20f;
        [SerializeField] private int presentationSortingOrder = 29;

        private Vector2 nativeSpriteSize = Vector2.one;

        private void Awake()
        {
            if (spriteRenderer == null)
                spriteRenderer = GetComponent<SpriteRenderer>();

            if (spriteRenderer != null)
            {
                spriteRenderer.sortingOrder = presentationSortingOrder;
                CacheNativeSpriteSize();
            }
        }

        private void CacheNativeSpriteSize()
        {
            if (spriteRenderer != null && spriteRenderer.sprite != null)
            {
                Bounds b = spriteRenderer.sprite.bounds;
                nativeSpriteSize = new Vector2(
                    Mathf.Max(0.01f, b.size.x),
                    Mathf.Max(0.01f, b.size.y));
            }
            else
            {
                nativeSpriteSize = Vector2.one;
            }
        }

        public void UpdateGlow(Vector2 center, float width, Color blockColor, float intensityMultiplier = 1f)
        {
            if (spriteRenderer == null)
                return;

            if (!spriteRenderer.enabled)
                spriteRenderer.enabled = true;

            if (nativeSpriteSize.x <= 0.01f || nativeSpriteSize.y <= 0.01f)
                CacheNativeSpriteSize();

            transform.position = new Vector3(center.x, center.y, 0f);

            // High-frequency friction pulse for intense grinding mechanical glare
            float time = Time.time;
            float pulse = 0.90f + 0.10f * Mathf.Sin(time * 52f) + 0.05f * Mathf.Cos(time * 89f);
            float targetWorldWidth = Mathf.Max(0.35f, width * widthMargin) * pulse;
            float targetWorldHeight = Mathf.Max(0.1f, heightMultiplier) * pulse;

            float scaleX = targetWorldWidth / nativeSpriteSize.x;
            float scaleY = targetWorldHeight / nativeSpriteSize.y;
            transform.localScale = new Vector3(scaleX, scaleY, 1f);

            // Additive blowout: hot core blended with block tint
            Color hotGlow = Color.Lerp(blockColor, Color.white, 0.45f);
            hotGlow.a = Mathf.Clamp01(0.95f * intensityMultiplier);
            spriteRenderer.color = hotGlow;
        }

        public void Hide()
        {
            if (spriteRenderer != null)
                spriteRenderer.enabled = false;
        }

        public void OnSpawn()
        {
            Hide();
        }

        public void OnDespawn()
        {
            Hide();
            transform.position = Vector3.zero;
            transform.localScale = Vector3.one;
        }
    }
}
