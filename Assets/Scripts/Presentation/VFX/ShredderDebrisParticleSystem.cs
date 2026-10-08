using GravityPuzzle.Config;
using UnityEngine;

namespace GravityPuzzle.Presentation.VFX
{
    /// <summary>
    /// One shared, world-space particle system for all active cutter mouths.
    /// It is presentation-only: the grid and shredder feed remain the sole
    /// owners of when a piece is actually released.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem), typeof(ParticleSystemRenderer))]
    public sealed class ShredderDebrisParticleSystem : MonoBehaviour
    {
        private ParticleSystem particleSystemComponent;
        private ParticleSystemRenderer particleRenderer;
        private ShredderConfig config;

        private void Awake()
        {
            particleSystemComponent = GetComponent<ParticleSystem>();
            particleRenderer = GetComponent<ParticleSystemRenderer>();
            // The component is authored on the scene controller, but the
            // advanced presentation is deliberately opt-in. Do not let Unity's
            // default ParticleSystem settings emit before BlockShredder enables
            // it through the config.
            ParticleSystem.MainModule main = particleSystemComponent.main;
            main.playOnAwake = false;
            ParticleSystem.EmissionModule emission = particleSystemComponent.emission;
            emission.enabled = false;
            particleSystemComponent.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        public void Configure(ShredderConfig shredderConfig)
        {
            config = shredderConfig;
            if (particleSystemComponent == null || particleRenderer == null || config == null)
                return;

            ParticleSystem.MainModule main = particleSystemComponent.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 512;
            main.gravityModifier = config.DebrisGravityModifier;

            ParticleSystem.EmissionModule emission = particleSystemComponent.emission;
            emission.enabled = false;

            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = particleSystemComponent.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(
                1f,
                AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));

            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particleSystemComponent.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient fade = new Gradient();
            fade.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(.8f, .55f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = fade;

            ParticleSystem.TextureSheetAnimationModule textureSheet = particleSystemComponent.textureSheetAnimation;
            textureSheet.enabled = true;
            textureSheet.mode = ParticleSystemAnimationMode.Sprites;
            if (textureSheet.spriteCount == 0)
                textureSheet.AddSprite(PrototypeBootstrap.GetSquareSprite());

            particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            particleRenderer.sortingOrder = config.DebrisSortingOrder;
        }

        public void EmitAtCutter(Vector2 cutterPosition, float width, Color color, int count)
        {
            if (particleSystemComponent == null || config == null || count <= 0)
                return;

            if (!particleSystemComponent.isPlaying)
                particleSystemComponent.Play();

            Vector2 sizeRange = config.DebrisSizeRange;
            float minSize = Mathf.Min(sizeRange.x, sizeRange.y);
            float maxSize = Mathf.Max(sizeRange.x, sizeRange.y);
            Color opaqueColor = new Color(color.r, color.g, color.b, 1f);
            for (int index = 0; index < count; index++)
            {
                ParticleSystem.EmitParams parameters = new ParticleSystem.EmitParams
                {
                    position = cutterPosition + new Vector2(
                        Random.Range(-width * .5f, width * .5f),
                        Random.Range(-.035f, .035f)),
                    velocity = new Vector3(
                        Random.Range(-config.DebrisHorizontalSpeed, config.DebrisHorizontalSpeed),
                        -config.DebrisDownwardSpeed * Random.Range(.7f, 1.15f),
                        0f),
                    startColor = opaqueColor,
                    startSize = Random.Range(minSize, maxSize),
                    startLifetime = config.DebrisLifetime * Random.Range(.78f, 1.18f),
                    rotation = Random.Range(0f, 360f),
                    angularVelocity = Random.Range(-config.DebrisAngularVelocity, config.DebrisAngularVelocity)
                };
                particleSystemComponent.Emit(parameters, 1);
            }
        }
    }
}
