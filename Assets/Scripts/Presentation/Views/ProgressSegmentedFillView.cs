using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

namespace GravityPuzzle.Presentation.Views
{
    /// <summary>
    /// Renders per-segment voxel colours inside the existing UI Slider fill capsule
    /// via one preallocated 1D ramp texture and custom UI shader (single draw call).
    /// Presentation-only: gameplay progress remains owned by LevelProgressManager.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Image))]
    public sealed class ProgressSegmentedFillView : MonoBehaviour
    {
        public const int RampResolution = 512;
        private const string FallbackShaderName = "Gravity Puzzle/UI Progress Segmented Fill";

        private static readonly int SegmentTexId = Shader.PropertyToID("_SegmentTex");
        private static readonly int SpriteUvRectId = Shader.PropertyToID("_SpriteUvRect");

        [SerializeField] private Shader segmentedFillShader;
        [SerializeField] private Color defaultFillColor = new Color(1f, .72f, .15f, 1f);
        [Tooltip("When enabled, all incoming progress segments render with uniformFillColor instead of per-voxel colours.")]
        [SerializeField] private bool useUniformColor;
        [SerializeField] private Color uniformFillColor = new Color(.22f, .84f, .24f, 1f);

        private Image fillImage;
        private Material runtimeMaterial;
        private Texture2D segmentTexture;
        private readonly Color32[] segmentPixels = new Color32[RampResolution];
        private bool isTextureDirty;
        private Sprite cachedSprite;

        public Color DefaultFillColor => defaultFillColor;
        public bool UseUniformColor => useUniformColor;
        public Color UniformFillColor => uniformFillColor;

        private void Awake()
        {
            EnsureInitialized();
        }

        private void OnEnable()
        {
            EnsureInitialized();
            RefreshSpriteUvBounds();
        }

        private void LateUpdate()
        {
            if (isTextureDirty)
                FlushSegmentTexture();
        }

        private void OnDestroy()
        {
            if (fillImage != null && fillImage.material == runtimeMaterial)
                fillImage.material = null;

            if (runtimeMaterial != null)
            {
                Destroy(runtimeMaterial);
                runtimeMaterial = null;
            }

            if (segmentTexture != null)
            {
                Destroy(segmentTexture);
                segmentTexture = null;
            }
        }

        /// <summary>
        /// Clears all painted segments back to the authored fallback (or active uniform) colour.
        /// </summary>
        public void ResetSegments()
        {
            EnsureInitialized();
            FillAllPixels(ToOpaqueColor32(GetActiveDefaultColor()));
            FlushSegmentTexture();
        }

        /// <summary>
        /// Switches between per-voxel segmented colouring and a single uniform fill colour.
        /// </summary>
        public void SetUniformColor(bool enabled, Color color)
        {
            useUniformColor = enabled;
            uniformFillColor = color;
            ResetSegments();
        }

        /// <summary>
        /// Paints the normalized progress span [startNormalized, endNormalized] with
        /// the arriving voxel's colour, or the uniform fill colour when enabled.
        /// Flushes to the GPU once per frame in LateUpdate.
        /// </summary>
        public void RecordSegment(float startNormalized, float endNormalized, Color voxelColor)
        {
            EnsureInitialized();

            float clampedStart = Mathf.Clamp01(Mathf.Min(startNormalized, endNormalized));
            float clampedEnd = Mathf.Clamp01(Mathf.Max(startNormalized, endNormalized));
            int startPixel = Mathf.Clamp(Mathf.FloorToInt(clampedStart * RampResolution), 0, RampResolution - 1);
            int endPixel = Mathf.Clamp(Mathf.CeilToInt(clampedEnd * RampResolution) - 1, startPixel, RampResolution - 1);

            Color activeColor = useUniformColor ? uniformFillColor : voxelColor;
            Color32 color32 = ToOpaqueColor32(activeColor);
            for (int index = startPixel; index <= endPixel; index++)
                segmentPixels[index] = color32;

            isTextureDirty = true;
        }

        /// <summary>
        /// Returns the colour stored at a normalized bar position [0..1], useful for verification.
        /// </summary>
        public Color32 SampleSegmentPixel(float normalizedPosition)
        {
            int pixelIndex = Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(normalizedPosition) * (RampResolution - 1)), 0, RampResolution - 1);
            return segmentPixels[pixelIndex];
        }

        private void EnsureInitialized()
        {
            if (fillImage == null)
                fillImage = GetComponent<Image>();

            if (segmentTexture == null)
            {
                segmentTexture = new Texture2D(RampResolution, 1, TextureFormat.RGBA32, false, false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.HideAndDontSave
                };

                FillAllPixels(ToOpaqueColor32(GetActiveDefaultColor()));
                FlushSegmentTexture();
            }

            if (runtimeMaterial == null)
            {
                Shader shader = segmentedFillShader != null ? segmentedFillShader : Shader.Find(FallbackShaderName);
                if (shader != null && shader.isSupported)
                {
                    runtimeMaterial = new Material(shader)
                    {
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    runtimeMaterial.SetTexture(SegmentTexId, segmentTexture);
                }
            }

            if (fillImage != null && runtimeMaterial != null && fillImage.material != runtimeMaterial)
                fillImage.material = runtimeMaterial;

            RefreshSpriteUvBounds();
        }

        private void RefreshSpriteUvBounds()
        {
            if (fillImage == null || runtimeMaterial == null)
                return;

            Sprite activeSprite = fillImage.overrideSprite != null ? fillImage.overrideSprite : fillImage.sprite;
            if (activeSprite == null || activeSprite == cachedSprite)
                return;

            cachedSprite = activeSprite;
            Vector4 outerUv = DataUtility.GetOuterUV(activeSprite);
            float uWidth = Mathf.Max(0.0001f, outerUv.z - outerUv.x);
            float vHeight = Mathf.Max(0.0001f, outerUv.w - outerUv.y);
            runtimeMaterial.SetVector(SpriteUvRectId, new Vector4(outerUv.x, outerUv.y, uWidth, vHeight));
        }

        private Color GetActiveDefaultColor()
        {
            return useUniformColor ? uniformFillColor : defaultFillColor;
        }

        private void FillAllPixels(Color32 color32)
        {
            for (int index = 0; index < RampResolution; index++)
                segmentPixels[index] = color32;
        }

        private void FlushSegmentTexture()
        {
            isTextureDirty = false;
            if (segmentTexture == null)
                return;

            segmentTexture.SetPixels32(segmentPixels);
            segmentTexture.Apply(false, false);
        }

        private static Color32 ToOpaqueColor32(Color color)
        {
            return new Color(color.r, color.g, color.b, 1f);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (Application.isPlaying && segmentTexture != null)
                ResetSegments();
        }
#endif
    }
}
