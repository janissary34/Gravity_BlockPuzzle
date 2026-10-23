using GravityPuzzle.Config;
using UnityEngine;

namespace GravityPuzzle.Presentation.VFX
{
    /// <summary>
    /// Shared world-space particle systems for active cutter mouths:
    /// - Layer B: High-speed stretched friction sparks.
    /// - Layer C: Chunky colored debris falling under gravity.
    /// Presentation-only: does not own gameplay or piece lifecycle.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem), typeof(ParticleSystemRenderer))]
    public sealed class ShredderDebrisParticleSystem : MonoBehaviour
    {
        [SerializeField] private ParticleSystem sparksParticleSystem;
        [SerializeField] private ParticleSystemRenderer sparksRenderer;

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

            if (sparksParticleSystem == null)
            {
                Transform child = transform.Find("ShredderSparks");
                if (child != null)
                {
                    sparksParticleSystem = child.GetComponent<ParticleSystem>();
                    sparksRenderer = child.GetComponent<ParticleSystemRenderer>();
                }
            }

            InitParticleSystem(particleSystemComponent);
            if (sparksParticleSystem != null)
                InitParticleSystem(sparksParticleSystem);
        }

        private static void InitParticleSystem(ParticleSystem ps)
        {
            if (ps == null)
                return;

            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = false;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        public void Configure(ShredderConfig shredderConfig)
        {
            config = shredderConfig;
            if (particleSystemComponent == null || particleRenderer == null || config == null)
                return;

            // 1. Configure Layer C (Chunky Debris) on the main ParticleSystem
            ParticleSystem.MainModule main = particleSystemComponent.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.maxParticles = config.DebrisParticleCapacity;
            main.gravityModifier = Mathf.Max(0f, config.DebrisGravityModifier);
            main.startSpeed = 0f;
            float debrisLifetime = config.DebrisLifetime;
            main.startLifetime = new ParticleSystem.MinMaxCurve(debrisLifetime * .85f, debrisLifetime * 1.15f);

            ParticleSystem.EmissionModule emission = particleSystemComponent.emission;
            emission.enabled = false;

            ParticleSystem.ShapeModule shape = particleSystemComponent.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;

            ParticleSystem.VelocityOverLifetimeModule velocityOverLifetime = particleSystemComponent.velocityOverLifetime;
            velocityOverLifetime.enabled = true;
            velocityOverLifetime.space = ParticleSystemSimulationSpace.World;
            float horizontalSpeed = Mathf.Max(0f, config.DebrisHorizontalSpeed);
            float downwardSpeed = config.DebrisInitialDownwardSpeed;
            velocityOverLifetime.x = new ParticleSystem.MinMaxCurve(-horizontalSpeed, horizontalSpeed);
            velocityOverLifetime.y = new ParticleSystem.MinMaxCurve(-downwardSpeed * 1.15f, -downwardSpeed * .85f);
            velocityOverLifetime.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = particleSystemComponent.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(
                1f,
                AnimationCurve.EaseInOut(0f, 1f, 1f, 0.2f));

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
                    new GradientAlphaKey(0.9f, 0.65f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = fade;

            particleRenderer.enabled = true;
            particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            particleRenderer.sortingOrder = config.DebrisSortingOrder;
            if (config.DebrisMaterial != null)
                particleRenderer.sharedMaterial = config.DebrisMaterial;

            // 2. Configure Layer B (Friction Sparks) on the sparks ParticleSystem
            if (sparksParticleSystem != null)
            {
                ParticleSystem.MainModule sparkMain = sparksParticleSystem.main;
                sparkMain.playOnAwake = false;
                sparkMain.loop = false;
                sparkMain.simulationSpace = ParticleSystemSimulationSpace.World;
                sparkMain.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                sparkMain.maxParticles = 512;
                sparkMain.gravityModifier = 0.5f;

                ParticleSystem.EmissionModule sparkEmission = sparksParticleSystem.emission;
                sparkEmission.enabled = false;

                ParticleSystem.ShapeModule sparkShape = sparksParticleSystem.shape;
                sparkShape.enabled = true;
                sparkShape.shapeType = ParticleSystemShapeType.Box;

                ParticleSystem.VelocityOverLifetimeModule sparkVel = sparksParticleSystem.velocityOverLifetime;
                sparkVel.enabled = true;
                sparkVel.space = ParticleSystemSimulationSpace.World;
                sparkVel.x = new ParticleSystem.MinMaxCurve(-4.5f, 4.5f);
                sparkVel.y = new ParticleSystem.MinMaxCurve(2.5f, 7.5f);

                ParticleSystem.LimitVelocityOverLifetimeModule limitVel = sparksParticleSystem.limitVelocityOverLifetime;
                limitVel.enabled = true;
                limitVel.drag = 3.5f;
                limitVel.multiplyDragByParticleSize = false;
                limitVel.multiplyDragByParticleVelocity = false;

                ParticleSystem.ColorOverLifetimeModule sparkColorMod = sparksParticleSystem.colorOverLifetime;
                sparkColorMod.enabled = true;
                Gradient sparkFade = new Gradient();
                sparkFade.SetKeys(
                    new[]
                    {
                        new GradientColorKey(Color.white, 0f),
                        new GradientColorKey(new Color(1f, 0.92f, 0.45f), 0.5f),
                        new GradientColorKey(new Color(1f, 0.65f, 0.15f), 1f)
                    },
                    new[]
                    {
                        new GradientAlphaKey(1f, 0f),
                        new GradientAlphaKey(0.85f, 0.6f),
                        new GradientAlphaKey(0f, 1f)
                    });
                sparkColorMod.color = sparkFade;

                if (sparksRenderer != null)
                {
                    sparksRenderer.enabled = true;
                    sparksRenderer.renderMode = ParticleSystemRenderMode.Stretch;
                    sparksRenderer.velocityScale = 0.22f;
                    sparksRenderer.lengthScale = 1.0f;
                    sparksRenderer.sortingOrder = 28;
                    if (config.DebrisMaterial != null)
                        sparksRenderer.sharedMaterial = config.DebrisMaterial;
                }
            }
        }

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

            Vector2 sizeRange = config.DebrisSize;
            float minSize = Mathf.Min(sizeRange.x, sizeRange.y);
            float maxSize = Mathf.Max(sizeRange.x, sizeRange.y);
            float bandHeight = config.DebrisContactBandHeight;
            Color opaqueColor = new Color(color.r, color.g, color.b, 1f);

            ParticleSystem.MainModule main = particleSystemComponent.main;
            main.startColor = opaqueColor;
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            ParticleSystem.ShapeModule shape = particleSystemComponent.shape;
            float sourceHeight = bandHeight + 0.035f;
            shape.scale = new Vector3(width, sourceHeight, 0f);
            Vector3 sourceCenter = cutterPosition + Vector2.up * ((bandHeight - 0.035f) * 0.5f);
            shape.position = particleSystemComponent.transform.InverseTransformPoint(sourceCenter);

            int sparkCount = particleCount > 2 ? Mathf.Max(1, Mathf.RoundToInt(particleCount * 0.35f)) : 0;
            int chipCount = Mathf.Max(1, particleCount - sparkCount);

            // Layer C: Chunky colored debris
            particleSystemComponent.Emit(chipCount);

            // Layer B: Friction sparks
            if (sparkCount > 0)
            {
                if (sparksParticleSystem != null)
                {
                    if (!sparksParticleSystem.isPlaying)
                        sparksParticleSystem.Play();

                    ParticleSystem.MainModule sparkMain = sparksParticleSystem.main;
                    sparkMain.startColor = Color.white;
                    sparkMain.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.08f);
                    sparkMain.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.22f);
                    sparkMain.startSpeed = new ParticleSystem.MinMaxCurve(6f, 10f);

                    ParticleSystem.ShapeModule sparkShape = sparksParticleSystem.shape;
                    sparkShape.scale = new Vector3(width, 0.05f, 0f);
                    sparkShape.position = sparksParticleSystem.transform.InverseTransformPoint(cutterPosition);

                    sparksParticleSystem.Emit(sparkCount);
                }
                else
                {
                    // Fallback to primary particle system if sparks system is not present
                    Color sparkColor = Color.Lerp(opaqueColor, Color.white, 0.85f);
                    main.startColor = sparkColor;
                    main.startSize = new ParticleSystem.MinMaxCurve(minSize * 0.4f, minSize * 0.8f);
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.22f);
                    particleSystemComponent.Emit(sparkCount);
                    main.startLifetime = new ParticleSystem.MinMaxCurve(config.DebrisLifetime * .85f, config.DebrisLifetime * 1.15f);
                }
            }
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
