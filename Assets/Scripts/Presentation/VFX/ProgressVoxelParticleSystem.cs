using UnityEngine;

namespace GravityPuzzle.Presentation.VFX
{
    public enum BezierEaseType
    {
        InOutQuad,
        InQuad,
        OutQuad,
        SmoothStep,
        Linear
    }

    /// <summary>
    /// High-performance flying voxel particle system with an authentic 3-stage Quadratic Bezier arc:
    /// P0 (Contact World Point) -> P1 (Drop/Scatter Control Point) -> P2 (Slider Handle Target).
    /// Operates on a pre-allocated zero-GC struct buffer and renders via a single ParticleSystem draw call in World Simulation Space.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class ProgressVoxelParticleSystem : MonoBehaviour
    {
        private const int MaxActiveFlights = 3000;

        [Header("Components")]
        [SerializeField] private ParticleSystem particleSys;
        [SerializeField] private ParticleSystemRenderer particleRenderer;

        [Header("Sorting")]
        [SerializeField] private int sortingOrder = 20;
        [SerializeField] private string sortingLayerName = "Default";

        [Header("Particle Visual Settings")]
        [Tooltip("Global multiplier for emitted particle count. 1 = normal, 2 = 2x particles, 3 = 3x particles, etc.")]
        [Range(1, 10)] [SerializeField] private int particleMultiplier = 1;
        [Tooltip("Base size of each flying particle voxel.")]
        [Range(0.05f, 1.5f)] [SerializeField] private float baseParticleSize = 0.28f;

        [Header("Bezier Arc Settings (P0 -> P1 -> P2)")]
        [Tooltip("Minimum downward drop for control point P1 (shredder blade fall effect).")]
        [Range(0.2f, 8f)] [SerializeField] private float minDropDistance = 1.5f;
        [Tooltip("Maximum downward drop for control point P1 (shredder blade fall effect).")]
        [Range(0.5f, 10f)] [SerializeField] private float maxDropDistance = 2.5f;
        [Tooltip("Horizontal random scatter (+-X) for control point P1.")]
        [Range(0.05f, 2f)] [SerializeField] private float horizontalScatterRadius = 0.45f;
        [Tooltip("Base flight duration from P0 to P2 in seconds.")]
        [Range(0.2f, 2f)] [SerializeField] private float flightDuration = 0.65f;
        [Tooltip("Easing curve applied to Bezier parameter t.")]
        [SerializeField] private BezierEaseType easeType = BezierEaseType.InQuad;

        private struct FlightData
        {
            public Vector3 p0;
            public Vector3 p1;
            public Vector3 p2;
            public float elapsed;
            public float duration;
            public int groupId;
            public bool arrived;
            public Color32 color;
            public float size;
        }

        private readonly FlightData[] activeFlights = new FlightData[MaxActiveFlights];
        private readonly ParticleSystem.Particle[] particleBuffer = new ParticleSystem.Particle[MaxActiveFlights];
        private int activeCount;
        private int nextFlightGroupId = 1;

        private Vector3 targetWorldPosition = Vector3.up * 4f;
        private bool hasTargetPosition;

        public ParticleSystem ParticleSystemComponent => particleSys;

        /// <summary>
        /// Returns the latest possible arrival time for a requested flight.
        /// Progress state uses this to remain behind the visual flight rather
        /// than advancing the slider when particles are emitted.
        /// </summary>
        public float MaximumFlightDuration(float requestedDuration)
        {
            return Mathf.Max(.01f, requestedDuration) + .04f;
        }

#if UNITY_EDITOR
        private void Reset()
        {
            if (particleSys == null) particleSys = GetComponent<ParticleSystem>();
            if (particleRenderer == null) particleRenderer = GetComponent<ParticleSystemRenderer>();
        }
#endif

        private void Awake()
        {
            transform.SetParent(null, false);
            transform.position = Vector3.zero;
            transform.localScale = Vector3.one;
            transform.rotation = Quaternion.identity;
            gameObject.layer = 0;

            if (particleSys == null) particleSys = GetComponent<ParticleSystem>();
            if (particleRenderer == null) particleRenderer = GetComponent<ParticleSystemRenderer>();

            ConfigureRendererSorting();

            if (particleRenderer != null && particleRenderer.sharedMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader != null)
                    particleRenderer.sharedMaterial = new Material(shader);
            }

            if (particleSys != null)
            {
                var main = particleSys.main;
                main.playOnAwake = false;
                main.loop = false;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = MaxActiveFlights;
                main.gravityModifier = 0f;
                main.startSpeed = 0f;
                main.simulationSpeed = 1f;

                var emission = particleSys.emission;
                emission.enabled = false;
            }
        }

        public void ConfigureRendererSorting()
        {
            if (particleRenderer != null)
            {
                if (!string.IsNullOrEmpty(sortingLayerName))
                    particleRenderer.sortingLayerName = sortingLayerName;
                particleRenderer.sortingOrder = sortingOrder;
            }
        }

        /// <summary>
        /// Places flight particles above the authored progress UI so their
        /// arrival remains visible at the target pin.
        /// </summary>
        public void SetRendererSortingOrder(int order)
        {
            sortingOrder = order;
            ConfigureRendererSorting();
        }

        /// <summary>
        /// Updates the world position of the Slider Handle (P2).
        /// </summary>
        public void SetTargetPosition(Vector3 worldTarget)
        {
            targetWorldPosition = worldTarget;
            hasTargetPosition = true;

            for (int index = 0; index < activeCount; index++)
            {
                if (!activeFlights[index].arrived)
                    activeFlights[index].p2 = worldTarget;
            }
        }

        /// <summary>
        /// Emits flying particles that follow P0 (Contact) -> P1 (Drop/Scatter) -> P2 (Slider Handle Target).
        /// </summary>
        public int EmitVoxel(Vector3 contactWorldPos, Color color, float flightDuration = 0.55f, int count = 1)
        {
            if (particleSys != null && !particleSys.isPlaying)
                particleSys.Play();

            float duration = flightDuration > 0.01f ? flightDuration : this.flightDuration;
            Vector3 p2 = targetWorldPosition;
            Color32 color32 = color;
            int groupId = nextFlightGroupId++;
            if (nextFlightGroupId == int.MaxValue)
                nextFlightGroupId = 1;

            int totalCount = count * Mathf.Max(1, particleMultiplier);

            for (int i = 0; i < totalCount; i++)
            {
                if (activeCount >= MaxActiveFlights)
                    break;

                // P0: Instantaneous contact world position with minor microscopic jitter
                Vector3 p0 = contactWorldPos + new Vector3(
                    Random.Range(-0.06f, 0.06f),
                    Random.Range(-0.02f, 0.02f),
                    0f);

                // P1: Drop control point (0.5 - 1.0 units below P0, with +-X horizontal scatter)
                float drop = Random.Range(minDropDistance, maxDropDistance);
                float scatterX = Random.Range(-horizontalScatterRadius, horizontalScatterRadius);
                Vector3 p1 = new Vector3(p0.x + scatterX, p0.y - drop, 0f);

                activeFlights[activeCount] = new FlightData
                {
                    p0 = p0,
                    p1 = p1,
                    p2 = p2,
                    elapsed = 0f,
                    duration = duration + Random.Range(-0.04f, 0.04f),
                    groupId = groupId,
                    color = color32,
                    size = Random.Range(baseParticleSize * 0.85f, baseParticleSize * 1.15f)
                };

                activeCount++;
            }

            return groupId;
        }

        /// <summary>
        /// Emits a burst of particles for booster impacts (Hammer, Rocket).
        /// </summary>
        public int EmitVoxelBurst(Vector3 contactWorldPos, Color color, int particleCount, float flightDuration = 0.55f)
        {
            int clampedCount = Mathf.Clamp(particleCount, 1, 128);
            return EmitVoxel(contactWorldPos, color, flightDuration, clampedCount);
        }

        /// <summary>Returns true while any particle in the emitted group is still visible.</summary>
        public bool IsFlightGroupActive(int groupId)
        {
            for (int index = 0; index < activeCount; index++)
            {
                if (activeFlights[index].groupId == groupId)
                    return true;
            }

            return false;
        }

        private void LateUpdate()
        {
            if (activeCount == 0 || particleSys == null) return;

            float dt = Time.deltaTime;
            int aliveCount = 0;

            for (int i = 0; i < activeCount; i++)
            {
                activeFlights[i].elapsed += dt;
                FlightData flight = activeFlights[i];
                if (flight.arrived)
                    continue;

                float normalizedTime = flight.elapsed / flight.duration;
                bool reachedTarget = normalizedTime >= 1f;

                // Apply easing to normalized time parameter
                float t = ApplyEasing(Mathf.Clamp01(normalizedTime), easeType);

                // Authentic 3-stage Quadratic Bezier equation: B(t) = (1-t)^2*P0 + 2*(1-t)*t*P1 + t^2*P2
                float inv = 1f - t;
                Vector3 pos = inv * inv * flight.p0 +
                              2f * inv * t * flight.p1 +
                              t * t * flight.p2;
                pos.z = 0f;

                particleBuffer[aliveCount].position = pos;
                particleBuffer[aliveCount].velocity = Vector3.zero;
                particleBuffer[aliveCount].startColor = flight.color;
                particleBuffer[aliveCount].startSize = flight.size > 0.05f ? flight.size : 0.3f;
                particleBuffer[aliveCount].remainingLifetime = 10f;
                particleBuffer[aliveCount].startLifetime = 10f;

                // Keep the particle for one rendered frame at P2. Without this,
                // the flight is removed just before it visually reaches the pin.
                flight.arrived = reachedTarget;

                activeFlights[aliveCount] = flight;

                aliveCount++;
            }

            int prevCount = activeCount;
            activeCount = aliveCount;
            if (activeCount > 0 || prevCount > 0)
                particleSys.SetParticles(particleBuffer, activeCount);
        }

        private static float ApplyEasing(float t, BezierEaseType type)
        {
            switch (type)
            {
                case BezierEaseType.InQuad:
                    return t * t;

                case BezierEaseType.OutQuad:
                    return t * (2f - t);

                case BezierEaseType.InOutQuad:
                    return t < 0.5f ? 2f * t * t : -1f + (4f - 2f * t) * t;

                case BezierEaseType.SmoothStep:
                    return t * t * (3f - 2f * t);

                case BezierEaseType.Linear:
                default:
                    return t;
            }
        }
    }
}
