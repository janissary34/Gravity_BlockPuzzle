using GravityPuzzle.Config;
using UnityEngine;

namespace GravityPuzzle.Presentation.Views
{
    /// <summary>
    /// Temporarily swaps the renderer material during a shredder handoff.
    /// The shader uses the world-space cutter line, so the clipped block edge
    /// and debris source always remain locked to the rotating-wheel mouth.
    /// </summary>
    public sealed class ShredderDissolvePresentation : MonoBehaviour
    {
        private static readonly int ShredLineId = Shader.PropertyToID("_ShredLine");
        private static readonly int EdgeAmplitudeId = Shader.PropertyToID("_EdgeAmplitude");
        private static readonly int EdgeSoftnessId = Shader.PropertyToID("_EdgeSoftness");
        private static readonly int NoiseFrequencyId = Shader.PropertyToID("_NoiseFrequency");
        private static readonly int ShredTintId = Shader.PropertyToID("_ShredTint");

        [SerializeField] private Material dissolveMaterial;

        private MaterialPropertyBlock propertyBlock;
        private SpriteRenderer[] activeRenderers;
        private Material[] originalMaterials;
        private ShredderConfig config;
        private float shredderY;
        private Color shredTint;

        public bool IsActive => activeRenderers != null && dissolveMaterial != null;

        private void Awake()
        {
            // Unity objects must not be allocated in a MonoBehaviour field
            // initializer. Pool instances reuse this block for their complete
            // lifetime, so this creates no shredding-time GC pressure.
            propertyBlock = new MaterialPropertyBlock();
        }

        public void Begin(
            SpriteRenderer[] renderers,
            float cutterY,
            ShredderConfig shredderConfig,
            Color presentationColor)
        {
            Restore();
            if (dissolveMaterial == null || renderers == null || renderers.Length == 0 || shredderConfig == null)
                return;

            activeRenderers = renderers;
            originalMaterials = new Material[renderers.Length];
            shredderY = cutterY;
            config = shredderConfig;
            shredTint = new Color(
                presentationColor.r,
                presentationColor.g,
                presentationColor.b,
                1f);
            for (int index = 0; index < activeRenderers.Length; index++)
            {
                SpriteRenderer renderer = activeRenderers[index];
                if (renderer == null || !renderer.transform.IsChildOf(transform))
                    continue;

                originalMaterials[index] = renderer.sharedMaterial;
                renderer.sharedMaterial = dissolveMaterial;
                ApplyCutterProperties(renderer);
            }
        }

        public void Restore()
        {
            if (activeRenderers == null)
                return;

            for (int index = 0; index < activeRenderers.Length; index++)
            {
                SpriteRenderer renderer = activeRenderers[index];
                if (renderer == null || !renderer.transform.IsChildOf(transform))
                    continue;

                renderer.sharedMaterial = originalMaterials[index];
                renderer.SetPropertyBlock(null);
            }

            activeRenderers = null;
            originalMaterials = null;
            config = null;
        }

        private void LateUpdate()
        {
            if (!IsActive)
                return;

            for (int index = 0; index < activeRenderers.Length; index++)
            {
                SpriteRenderer renderer = activeRenderers[index];
                if (renderer != null && renderer.enabled && renderer.transform.IsChildOf(transform))
                    ApplyCutterProperties(renderer);
            }
        }

        private void OnDisable()
        {
            Restore();
        }

        private void ApplyCutterProperties(SpriteRenderer renderer)
        {
            if (propertyBlock == null)
                return;

            renderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat(ShredLineId, shredderY);
            propertyBlock.SetFloat(EdgeAmplitudeId, config.CutterEdgeAmplitude);
            propertyBlock.SetFloat(EdgeSoftnessId, config.CutterEdgeSoftness);
            propertyBlock.SetFloat(NoiseFrequencyId, config.CutterNoiseFrequency);
            propertyBlock.SetColor(ShredTintId, shredTint);
            renderer.SetPropertyBlock(propertyBlock);
        }
    }
}
