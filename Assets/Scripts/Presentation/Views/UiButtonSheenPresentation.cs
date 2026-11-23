using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace GravityPuzzle.Presentation.Views
{
    /// <summary>
    /// Sweeps a RectMask2D-clipped soft white gradient Image from the left outside
    /// to the right outside of a UI button on a repeating unscaled-time interval.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiButtonSheenPresentation : MonoBehaviour
    {
        private static Sprite fallbackGradientSprite;

        [Header("References")]
        [Tooltip("Masked container RectTransform (with RectMask2D). Uses this object's RectTransform when empty.")]
        [SerializeField] private RectTransform maskRect;
        [Tooltip("Sliding sheen Image RectTransform inside the RectMask2D container.")]
        [SerializeField] private RectTransform sheenRect;
        [Tooltip("Soft white rectangular gradient Image component.")]
        [SerializeField] private Image sheenImage;

        [Header("Timing & Motion")]
        [Tooltip("Duration in seconds for the sheen bar to travel from left outside to right outside.")]
        [Min(0.05f)] [SerializeField] private float sweepDuration = 0.75f;
        [Tooltip("Total cycle interval in seconds between consecutive sheen sweeps.")]
        [Min(0.1f)] [SerializeField] private float repeatInterval = 2.5f;
        [Tooltip("Extra horizontal clearance outside the masked button edges.")]
        [Min(0f)] [SerializeField] private float extraEdgeClearance = 16f;
        [Tooltip("Peak opacity of the white sheen pass.")]
        [Range(0.05f, 1f)] [SerializeField] private float sheenAlpha = 0.28f;
        [SerializeField] private Ease sweepEase = Ease.InOutSine;

        private Sequence sheenSequence;
        private float restingY;
        private bool hasRestingY;

        private void Awake()
        {
            ResolveReferences();
            EnsureGradientSprite();
        }

        private void OnEnable()
        {
            ResolveReferences();
            if (sheenRect == null)
                return;

            EnsureGradientSprite();
            StartSheenLoop();
        }

        private void OnDisable()
        {
            KillSheenTween();
        }

        private void OnDestroy()
        {
            KillSheenTween();
        }

        private void ResolveReferences()
        {
            if (maskRect == null)
            {
                Transform maskChild = transform.Find("SheenMask");
                maskRect = maskChild as RectTransform ?? transform as RectTransform;
            }

            if (sheenRect == null && maskRect != null)
            {
                Transform sheenChild = maskRect.Find("Sheen");
                if (sheenChild != null)
                    sheenRect = sheenChild as RectTransform;
            }

            if (sheenImage == null && sheenRect != null)
                sheenImage = sheenRect.GetComponent<Image>();

            if (sheenRect != null && !hasRestingY)
            {
                restingY = sheenRect.anchoredPosition.y;
                hasRestingY = true;
            }
        }

        private void EnsureGradientSprite()
        {
            if (sheenImage == null)
                return;

            if (fallbackGradientSprite == null)
                fallbackGradientSprite = CreateSoftHorizontalGradientSprite();

            sheenImage.sprite = fallbackGradientSprite;
            Color c = sheenImage.color;
            c.r = 1f;
            c.g = 1f;
            c.b = 1f;
            c.a = sheenAlpha;
            sheenImage.color = c;
        }

        private void StartSheenLoop()
        {
            KillSheenTween();

            if (sheenRect != null)
                sheenRect.localRotation = Quaternion.identity;

            CalculateHorizontalEndpoints(out float startX, out float endX);
            Vector2 startPos = new Vector2(startX, restingY);
            sheenRect.anchoredPosition = startPos;

            float waitDuration = Mathf.Max(0.05f, repeatInterval - sweepDuration);
            sheenSequence = DOTween.Sequence()
                .SetUpdate(true)
                .SetLink(gameObject, LinkBehaviour.KillOnDisable)
                .SetAutoKill(true);

            sheenSequence.Append(
                sheenRect
                    .DOAnchorPosX(endX, sweepDuration)
                    .SetEase(sweepEase));
            sheenSequence.AppendInterval(waitDuration);
            sheenSequence.SetLoops(-1, LoopType.Restart);
        }

        private void CalculateHorizontalEndpoints(out float startX, out float endX)
        {
            RectTransform referenceRect = maskRect != null ? maskRect : transform as RectTransform;
            float buttonWidth = referenceRect != null && referenceRect.rect.width > 1f
                ? referenceRect.rect.width
                : 380f;
            float sheenWidth = sheenRect != null && sheenRect.rect.width > 1f
                ? sheenRect.rect.width
                : 130f;

            // Account for rotated sheen bounding width so the tilted bar is
            // completely outside the mask before entering and after exiting.
            float angleRadians = sheenRect != null
                ? Mathf.Abs(sheenRect.localEulerAngles.z) * Mathf.Deg2Rad
                : 0f;
            float sheenHeight = sheenRect != null && sheenRect.rect.height > 1f
                ? sheenRect.rect.height
                : 260f;
            float projectedHalfWidth = (Mathf.Abs(Mathf.Cos(angleRadians)) * sheenWidth +
                                        Mathf.Abs(Mathf.Sin(angleRadians)) * sheenHeight) * 0.5f;

            float halfSpan = buttonWidth * 0.5f + projectedHalfWidth + extraEdgeClearance;
            startX = -halfSpan;
            endX = halfSpan;
        }

        private void KillSheenTween()
        {
            if (sheenSequence != null)
            {
                sheenSequence.Kill();
                sheenSequence = null;
            }

            if (sheenRect != null)
            {
                sheenRect.DOKill();
                if (hasRestingY)
                {
                    CalculateHorizontalEndpoints(out float startX, out _);
                    sheenRect.anchoredPosition = new Vector2(startX, restingY);
                }
            }
        }

        private static Sprite CreateSoftHorizontalGradientSprite()
        {
            const int width = 96;
            const int height = 96;
            const float diagonalSkew = 0.42f;
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "RuntimeButtonSheenGradient",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Color32[] pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                // Normalized vertical coordinate in [-1, 1] with soft top/bottom fade
                // so the sheen stays strictly inside the rounded button face.
                float v = (y / (float)(height - 1)) * 2f - 1f;
                float vertEdge = Mathf.Clamp01((1f - Mathf.Abs(v)) / 0.28f);
                float vertSmooth = vertEdge * vertEdge * (3f - 2f * vertEdge);

                for (int x = 0; x < width; x++)
                {
                    float u = (x / (float)(width - 1)) * 2f - 1f;
                    float skewedU = (u - v * diagonalSkew) * 1.35f;
                    float t = Mathf.Clamp01(1f - Mathf.Abs(skewedU));
                    float horizSmooth = t * t * (3f - 2f * t);
                    float combined = Mathf.Pow(horizSmooth, 1.6f) * vertSmooth;
                    byte alpha = (byte)Mathf.RoundToInt(combined * 255f);
                    pixels[y * width + x] = new Color32(255, 255, 255, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(
                texture,
                new Rect(0f, 0f, width, height),
                new Vector2(0.5f, 0.5f),
                100f);
        }
    }
}
