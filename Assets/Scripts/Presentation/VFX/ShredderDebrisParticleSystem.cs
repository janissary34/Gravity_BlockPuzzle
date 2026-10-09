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
        private int budgetFrame = -1;
        private int emittedParticleCountThisFrame;
        private int emittedEntryBurstCountThisFrame;

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
            // The shared emitter lives on the progress manager, not at every
            // cutter mouth. Never let off-screen bounds of that host suppress
            // an explicitly emitted, on-screen shredder burst.
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.maxParticles = config.DebrisParticleCapacity;
            main.gravityModifier = config.DebrisGravityModifier;

            ParticleSystem.EmissionModule emission = particleSystemComponent.emission;
            emission.enabled = false;

            ParticleSystem.ShapeModule shape = particleSystemComponent.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;

            ParticleSystem.VelocityOverLifetimeModule velocityOverLifetime = particleSystemComponent.velocityOverLifetime;
            velocityOverLifetime.enabled = true;
            velocityOverLifetime.space = ParticleSystemSimulationSpace.World;
            velocityOverLifetime.x = new ParticleSystem.MinMaxCurve(
                -config.DebrisHorizontalSpeed,
                config.DebrisHorizontalSpeed);
            velocityOverLifetime.y = new ParticleSystem.MinMaxCurve(
                -config.DebrisDownwardSpeed * .85f,
                -config.DebrisDownwardSpeed * .35f);

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

            // The assigned unlit debris material already renders a solid
            // square quad. Texture-sheet animation is deliberately disabled:
            // runtime-generated sprites are not consistently supported by
            // all mobile particle backends and could make otherwise-emitted
            // particles invisible.
            ParticleSystem.TextureSheetAnimationModule textureSheet = particleSystemComponent.textureSheetAnimation;
            textureSheet.enabled = false;

            particleRenderer.enabled = true;
            particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            particleRenderer.sortingOrder = config.DebrisSortingOrder;
            if (config.DebrisMaterial != null)
                particleRenderer.sharedMaterial = config.DebrisMaterial;
        }

        /// <summary>
        /// Emits debris from the visible material-conversion band at the mouth.
        /// The vertical distribution is deliberate: a single horizontal source
        /// reads as a mask edge, while this compact volume reads as block matter
        /// continuously breaking apart before it disappears behind the wheels.
        /// </summary>
        public void EmitAtCutter(Vector2 cutterPosition, float width, Color color, int count)
        {
            if (particleSystemComponent == null || config == null || count <= 0)
                return;

            RefreshFrameBudget();
            int remainingParticleBudget = config.MaxDebrisPerFrame - emittedParticleCountThisFrame;
            int particleCount = Mathf.Min(count, Mathf.Max(0, remainingParticleBudget));
            if (particleCount <= 0)
                return;
            emittedParticleCountThisFrame += particleCount;
            EmitParticles(cutterPosition, width, color, particleCount);
        }

        /// <summary>
        /// The first tooth contact gets its own bounded GPU-particle budget so
        /// it reads as material fracturing, not as a gradual mask reveal.
        /// </summary>
        public void EmitEntryBurstAtCutter(Vector2 cutterPosition, float width, Color color, int count)
        {
            if (particleSystemComponent == null || config == null || count <= 0)
                return;

            RefreshFrameBudget();
            int remainingBurstBudget = config.MaxDebrisEntryBurstPerFrame - emittedEntryBurstCountThisFrame;
            int particleCount = Mathf.Min(count, Mathf.Max(0, remainingBurstBudget));
            if (particleCount <= 0)
                return;

            emittedEntryBurstCountThisFrame += particleCount;
            EmitParticles(cutterPosition, width, color, particleCount);
        }

        private void EmitParticles(Vector2 cutterPosition, float width, Color color, int particleCount)
        {
            if (!particleSystemComponent.isPlaying)
                particleSystemComponent.Play();

            Vector2 sizeRange = config.DebrisSizeRange;
            float minSize = Mathf.Min(sizeRange.x, sizeRange.y);
            float maxSize = Mathf.Max(sizeRange.x, sizeRange.y);
            float bandHeight = config.DebrisContactBandHeight;
            Color opaqueColor = new Color(color.r, color.g, color.b, 1f);
            // Shape, lifetime, size and velocity modules randomise individual
            // particles on the native side. A single bulk call therefore
            // creates a true field of voxels rather than stacked copies of one
            // particle, with no managed loop per particle.
            ParticleSystem.MainModule main = particleSystemComponent.main;
            main.startColor = opaqueColor;
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startLifetime = new ParticleSystem.MinMaxCurve(
                config.DebrisLifetime * .78f,
                config.DebrisLifetime * 1.18f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            ParticleSystem.ShapeModule shape = particleSystemComponent.shape;
            float sourceHeight = bandHeight + .035f;
            shape.scale = new Vector3(width, sourceHeight, 0f);
            Vector3 sourceCenter = cutterPosition + Vector2.up * ((bandHeight - .035f) * .5f);
            shape.position = particleSystemComponent.transform.InverseTransformPoint(sourceCenter);
            particleSystemComponent.Emit(particleCount);
        }

        private void RefreshFrameBudget()
        {
            int currentFrame = Time.frameCount;
            if (budgetFrame == currentFrame)
                return;

            budgetFrame = currentFrame;
            emittedParticleCountThisFrame = 0;
            emittedEntryBurstCountThisFrame = 0;
        }
    }
}
