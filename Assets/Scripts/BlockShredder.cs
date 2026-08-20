using System.Collections.Generic;
using GravityPuzzle.Config;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace GravityPuzzle
{
    /// <summary>
    /// Handles block shredding triggers, pre-fractured composite block voxelization,
    /// object pooling for mobile optimization, and spawning Stone debris voxels & Gem fly voxels.
    /// </summary>
    [DisallowMultipleComponent]
    public class BlockShredder : MonoBehaviour
    {
        [Header("Configuration")]
        [Tooltip("Authoring asset used for both this feed behaviour and runtime-created shredder wheels.")]
        [SerializeField] private ShredderConfig shredderConfig;

        [Header("Shredder Explosion Forces")]
        [SerializeField, Tooltip("Base explosion impulse force applied to shattered voxels.")]
        private float shredExplosionForce = 1.4f;

        [SerializeField, Tooltip("Primary direction vector for shredder blade ejection force.")]
        private Vector2 shredForceDirection = new Vector2(0f, -1.2f);

        [SerializeField, Tooltip("Random spread angle for voxel ejection (degrees).")]
        private float ejectionSpreadAngle = 40f;

        [Header("Voxel Visuals")]
        [SerializeField, Tooltip("Fraction of voxels that are Gems (0.0 to 1.0).")]
        [Range(0.0f, 1.0f)]
        private float gemRatio = 0.25f;

        [Header("UI Slider Attraction Target")]
        [SerializeField, Tooltip("Top UI Slider reference to fill when Gems arrive.")]
        private Slider targetUISlider;

        [SerializeField, Tooltip("Specific RectTransform target for Gem attraction (defaults to slider handle/rect if null).")]
        private RectTransform targetUIRectTransform;

        [SerializeField, Tooltip("Camera used for Screen/World conversion (defaults to Camera.main).")]
        private Camera targetCamera;

        [Header("Gem Fly Settings")]
        [SerializeField, Tooltip("Duration of Gem flight from shredder to UI Target.")]
        private float gemFlyDuration = 1.1f;

        [SerializeField, Tooltip("DOTween Ease curve for Gem flight to UI.")]
        private Ease gemFlyEase = Ease.InBack;

        public static BlockShredder Instance { get; private set; }
        public static int ActiveGemFlightCount { get; private set; }
        public static bool HasActiveGemFlights => ActiveGemFlightCount > 0;
        public ShredderConfig Config => shredderConfig;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetGemFlightCount()
        {
            ActiveGemFlightCount = 0;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            ApplyConfig();
            if (shredderConfig != null && shredderConfig.WheelPrefab != null)
                ShredderWheelPool.Configure(shredderConfig.WheelPrefab, transform, shredderConfig.WheelPoolCapacity);
            if (shredderConfig != null && shredderConfig.CatchZonePrefab != null)
                ShredderCatchZonePool.Configure(
                    shredderConfig.CatchZonePrefab,
                    transform,
                    shredderConfig.CatchZonePoolCapacity);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Start()
        {
            if (targetCamera == null)
                targetCamera = Camera.main;

            if (targetUIRectTransform == null && targetUISlider != null)
                targetUIRectTransform = targetUISlider.GetComponent<RectTransform>();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            TryShredBlock(other, transform.position);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            // Covers a fast body that is already overlapping the gear trigger when
            // the simulation advances; the handoff guard makes this idempotent.
            TryShredBlock(other, transform.position);
        }

        [Header("Öğütme Ayarları (Tuning)")]
        [SerializeField, Tooltip("Shredlenme Hızı: Bloğun öğütücüye çekilme ve inme hızı (birim/saniye). Varsayılan: 0.7")]
        private float shredlenmeHizi = 0.7f;

        [SerializeField, Tooltip("Titreme Miktarı: Öğütülme esnasındaki mekanik titreme/sarsıntı genliği. Varsayılan: 0.045")]
        private float titremeMiktari = 0.045f;

        [SerializeField, Tooltip("Frequency (Hz) of mechanical grinding tremor oscillation.")]
        private float shredderTremorFrequency = 55f;

        [SerializeField, Tooltip("Brief angular impulse applied when a piece first bites into the gears.")]
        private float shredderTumbleTorque = 1.25f;

        private float maxFeedTiltAngle = 5f;
        private float feedShakeAmplitude = 2.5f;

        private static PhysicsMaterial2D shredderFeedMaterial;

        public void Configure(float feedSpeed, float tremorIntensity)
        {
            if (feedSpeed > 0f) shredlenmeHizi = feedSpeed;
            if (tremorIntensity >= 0f) titremeMiktari = tremorIntensity;
        }

        private void ApplyConfig()
        {
            if (shredderConfig == null)
                return;

            shredlenmeHizi = shredderConfig.FeedSpeed;
            titremeMiktari = shredderConfig.TremorIntensity;
            shredderTremorFrequency = shredderConfig.TremorFrequency;
            feedShakeAmplitude = shredderConfig.FeedShakeAmplitude;
            shredderTumbleTorque = shredderConfig.TumbleTorque;
            maxFeedTiltAngle = shredderConfig.MaxFeedTiltAngle;
        }

        /// <summary>
        /// Attempts to shred an entering PuzzlePiece into Stone and Gem voxels slowly/progressively with high-density particle effects.
        /// </summary>
        public void TryShredBlock(Collider2D targetCollider, Vector2 shredderCenter)
        {
            PuzzlePiece piece = targetCollider.GetComponentInParent<PuzzlePiece>();
            if (piece == null || !piece.TryBeginShredderHandoff())
                return;

            StartCoroutine(FeedPieceIntoShredder(piece, shredderCenter.y));
        }

        private static GameObject globalShredderMaskObject;

        private void EnsureSpriteMask(float shredderY)
        {
            if (globalShredderMaskObject == null)
            {
                globalShredderMaskObject = new GameObject("Shredder Sprite Mask Root");
                globalShredderMaskObject.transform.position = new Vector3(0f, shredderY - 15f, 0f);
                globalShredderMaskObject.transform.localScale = new Vector3(60f, 30f, 1f);

                SpriteMask mask = globalShredderMaskObject.AddComponent<SpriteMask>();
                mask.sprite = PrototypeBootstrap.GetSquareSprite();
                mask.frontSortingLayerID = SortingLayer.NameToID("Default");
                mask.frontSortingOrder = 32767;
                mask.backSortingLayerID = SortingLayer.NameToID("Default");
                mask.backSortingOrder = -32768;
            }
            else
            {
                globalShredderMaskObject.transform.position = new Vector3(0f, shredderY - 15f, 0f);
            }
        }

        private System.Collections.IEnumerator FeedPieceIntoShredder(PuzzlePiece piece, float shredderY)
        {
            if (piece == null) yield break;

            EnsureSpriteMask(shredderY);

            // 1. Release the piece into physics with its full collision geometry
            // intact. Individual lower cells are removed only when they reach
            // the cutter line below, preventing a feed from passing through an
            // obstacle or another falling piece.
            piece.SetSelected(false);
            piece.EnterShredderPhysics(shredderConfig);
            ApplyShredderFeedMaterial(piece);
            Rigidbody2D rb = piece.Body;

            // 2. Sprite Masking / Visual Clipping: mask out any portion moving below shredderY
            SpriteRenderer[] pieceRenderers = piece.GetComponentsInChildren<SpriteRenderer>(true);
            // Always use the authored PuzzlePiece colour for debris and UI voxels.
            // Renderer colours can be temporarily changed by selection or masking.
            Color tileColor = Opaque(piece.VisualColor);
            foreach (var r in pieceRenderers)
            {
                if (r != null)
                {
                    r.maskInteraction = SpriteMaskInteraction.VisibleOutsideMask;
                }
            }

            // Get all voxel shards if present
            VoxelShard[] shards = piece.GetComponentsInChildren<VoxelShard>(true);
            List<VoxelShard> shardList = new List<VoxelShard>(shards);
            shardList.Sort((a, b) => a.transform.position.y.CompareTo(b.transform.position.y));

            // Setup gems
            int gemCount = Mathf.RoundToInt(shardList.Count * gemRatio);
            HashSet<VoxelShard> gemShards = new HashSet<VoxelShard>();
            if (gemCount > 0 && shardList.Count > 0)
            {
                int step = Mathf.Max(1, shardList.Count / gemCount);
                for (int i = 0; i < shardList.Count && gemShards.Count < gemCount; i += step)
                {
                    gemShards.Add(shardList[i]);
                }
            }

            // Emit at the narrow seam directly beneath the rotating teeth. The
            // grains then fall naturally from the place where the mesh is cut,
            // instead of materialising lower down in open space.
            float grinderExitSeamY = shredderY - .06f;

            HashSet<VoxelShard> processedShards = new HashSet<VoxelShard>();
            float progressPerGrain = piece.RemainingProgressUnits /
                                     (float)Mathf.Max(1, shardList.Count * LevelProgressManager.SandGrainsPerRenderedVoxel);
            float maxTime = 4.0f;
            float elapsed = 0f;
            float previousShakeOffsetX = 0f;

            // 3. The kinematic feed owns the descent while this coroutine watches
            // the crossing cells and converts them to shred effects.
            while (piece != null && elapsed < maxTime)
            {
                elapsed += Time.deltaTime;

                // As the piece crosses the cutter line, release only the lower
                // collision cells. The part still above the shredder stays solid,
                // so large pieces cannot merge into one another while being fed.
                piece.ReleaseCollisionCellsAtOrBelow(shredderY);

                if (rb != null)
                {
                    // Apply continuous high-frequency horizontal tremor
                    float shakeOffsetX = Mathf.Sin(Time.time * shredderTremorFrequency) *
                                         titremeMiktari * feedShakeAmplitude;
                    rb.position += new Vector2(shakeOffsetX - previousShakeOffsetX, 0f);
                    previousShakeOffsetX = shakeOffsetX;
                    rb.velocity = new Vector2(0f, -shredlenmeHizi);

                    // Keep the feed visually stable while it enters the cutter.
                    float currentAngle = Mathf.DeltaAngle(0f, rb.rotation);
                    if (Mathf.Abs(currentAngle) > maxFeedTiltAngle)
                    {
                        rb.angularVelocity *= 0.5f;
                        rb.rotation = Mathf.Clamp(currentAngle, -maxFeedTiltAngle, maxFeedTiltAngle);
                    }
                }

                // A) Shred voxel shards crossing the cutter line and exit at the gear seam.
                for (int i = 0; i < shardList.Count; i++)
                {
                    VoxelShard shard = shardList[i];
                    if (shard == null || processedShards.Contains(shard)) continue;

                    if (shard.transform.position.y <= shredderY)
                    {
                        processedShards.Add(shard);

                        Vector2 exitSeamPosition = new Vector2(
                            shard.transform.position.x,
                            grinderExitSeamY);
                        Color shardColor = tileColor;

                        // The already pooled shard owns its post-grinder presentation
                        // and returns itself to VoxelBlockBuilder when its UI flight ends.
                        shard.BeginProgressHandoff(
                            exitSeamPosition,
                            shardColor,
                            progressPerGrain * LevelProgressManager.SandGrainsPerRenderedVoxel);
                    }
                }

                // B) Erase generic renderers crossing the cutter line and use the same seam.
                int activeCount = 0;
                float topY = float.NegativeInfinity;

                foreach (var r in pieceRenderers)
                {
                    if (r == null || !r.enabled || r.gameObject.name.StartsWith("Selected Fill") || r.gameObject.name.StartsWith("White Selection"))
                        continue;

                    // Exclude detached shards
                    if (r.transform.parent != piece.transform && r.GetComponent<VoxelShard>() != null)
                        continue;

                    if (r.transform.position.y <= shredderY)
                    {
                        r.enabled = false;

                        Vector2 exitSeamPosition = new Vector2(
                            r.transform.position.x,
                            grinderExitSeamY);

                        // Fallback for legacy pieces which have no generated VoxelShards.
                        if (LevelProgressManager.Instance != null && shardList.Count == 0)
                        {
                            Debug.Log($"[Shredder] PuzzlePiece {piece.GetInstanceID()} fallback color=#{ColorUtility.ToHtmlStringRGBA(Opaque(tileColor))}");
                            // Legacy render-only pieces have no VoxelShard to animate.
                            // Preserve their progress without manufacturing a temporary
                            // runtime grain; the regular UI-flight presenter owns it.
                            LevelProgressManager.Instance.SpawnFlyingVoxel(
                                exitSeamPosition,
                                Opaque(tileColor),
                                piece.RemainingProgressUnits,
                                null);
                        }
                    }
                    else
                    {
                        activeCount++;
                        topY = Mathf.Max(topY, r.bounds.max.y);
                    }
                }

                // 4. Clean Object Destruction: destroy immediately as top edge drops below shredderY
                bool topBelowShredder = topY != float.NegativeInfinity && topY <= shredderY;
                bool allShardsDone = shardList.Count == 0 || processedShards.Count >= shardList.Count;

                if (topBelowShredder || (allShardsDone && activeCount == 0))
                {
                    break;
                }

                yield return null;
            }

            if (piece != null)
            {
                piece.ReleaseInstance();
            }
        }

        private Vector2 CalculateEjectionVector()
        {
            float randomAngle = Random.Range(-ejectionSpreadAngle * 0.5f, ejectionSpreadAngle * 0.5f);
            Quaternion rotation = Quaternion.Euler(0, 0, randomAngle);
            Vector2 baseDir = shredForceDirection.sqrMagnitude > 0.01f ? shredForceDirection.normalized : Vector2.down;
            return rotation * baseDir;
        }

        private static Color Opaque(Color color) => new Color(color.r, color.g, color.b, 1f);

        private static void ApplyShredderFeedMaterial(PuzzlePiece piece)
        {
            if (shredderFeedMaterial == null)
            {
                shredderFeedMaterial = new PhysicsMaterial2D("Shredder Feed")
                {
                    friction = 0f,
                    bounciness = 0f
                };
            }

            Collider2D[] colliders = piece.GetComponentsInChildren<Collider2D>();
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                    colliders[i].sharedMaterial = shredderFeedMaterial;
            }
        }

        private void RecycleGemVoxel(GemFlyToUI gem)
        {
            ActiveGemFlightCount = Mathf.Max(0, ActiveGemFlightCount - 1);
        }

        /// <summary>
        /// Allows dynamically assigning the target UI Slider at runtime.
        /// </summary>
        public void SetTargetUISlider(Slider slider, RectTransform targetRect = null)
        {
            targetUISlider = slider;
            targetUIRectTransform = targetRect != null ? targetRect : (slider != null ? slider.GetComponent<RectTransform>() : null);
        }
    }

    /// <summary>
    /// Spawns high-density mini-cubes, subtle orange spark particles, and micro-dust along shredder contact line.
    /// </summary>
    public static class ShredderParticleEffects
    {
        private static Material unlitParticleMaterial;

        public static void SpawnBurst(Vector2 contactPosition, Color tileColor, int miniCubeCount = 4, int sparkCount = 3, int dustCount = 2)
        {
            // 1. High-density solid-colored square mini-cubes matching tile color
            for (int i = 0; i < miniCubeCount; i++)
            {
                // Match the visual 3x3 voxels used by every board block. These are
                // deliberately chunky so they remain readable below the gears.
                float size = UnityEngine.Random.Range(0.22f, 0.30f);
                Color cubeColor = new Color(tileColor.r, tileColor.g, tileColor.b, 1f);
                Vector2 spawnPos = contactPosition + new Vector2(UnityEngine.Random.Range(-0.25f, 0.25f), UnityEngine.Random.Range(-0.06f, 0.06f));

                GameObject cube = PrototypeBootstrap.CreateVisualBlock("MiniCubeParticle", spawnPos, Vector2.one * size, cubeColor);
                ConfigureParticleRenderer(cube.GetComponent<SpriteRenderer>(), cubeColor, 22);

                Rigidbody2D rb = cube.AddComponent<Rigidbody2D>();
                rb.gravityScale = 0.9f;
                rb.velocity = new Vector2(UnityEngine.Random.Range(-1.5f, 1.5f), UnityEngine.Random.Range(-3.4f, -1.5f));
                rb.angularVelocity = UnityEngine.Random.Range(-540f, 540f);

                FadingParticle particle = cube.AddComponent<FadingParticle>();
                particle.Init(UnityEngine.Random.Range(0.55f, 0.95f), true);
            }

            // 2. Smaller same-colour voxel chips. Never introduce an unrelated
            // orange/grey tint: every emitted debris voxel inherits the piece colour.
            for (int i = 0; i < sparkCount; i++)
            {
                float size = UnityEngine.Random.Range(0.075f, 0.14f);
                Color sparkColor = new Color(tileColor.r, tileColor.g, tileColor.b, 1f);
                Vector2 spawnPos = contactPosition + new Vector2(UnityEngine.Random.Range(-0.3f, 0.3f), UnityEngine.Random.Range(-0.05f, 0.05f));

                GameObject spark = PrototypeBootstrap.CreateVisualBlock("VoxelChip", spawnPos, Vector2.one * size, sparkColor);
                ConfigureParticleRenderer(spark.GetComponent<SpriteRenderer>(), sparkColor, 23);

                Rigidbody2D rb = spark.AddComponent<Rigidbody2D>();
                rb.gravityScale = 1.15f;
                rb.velocity = new Vector2(UnityEngine.Random.Range(-2.8f, 2.8f), UnityEngine.Random.Range(-2.5f, .15f));
                rb.angularVelocity = UnityEngine.Random.Range(-900f, 900f);

                FadingParticle particle = spark.AddComponent<FadingParticle>();
                particle.Init(UnityEngine.Random.Range(0.36f, 0.62f), true);
            }

            // 3. Fine same-colour voxel sand for a denser mechanical grind.
            for (int i = 0; i < dustCount; i++)
            {
                float size = UnityEngine.Random.Range(0.035f, 0.075f);
                Color dustColor = new Color(tileColor.r, tileColor.g, tileColor.b, 1f);
                Vector2 spawnPos = contactPosition + new Vector2(UnityEngine.Random.Range(-0.25f, 0.25f), UnityEngine.Random.Range(-0.08f, 0.08f));

                GameObject dust = PrototypeBootstrap.CreateVisualBlock("VoxelSand", spawnPos, Vector2.one * size, dustColor);
                ConfigureParticleRenderer(dust.GetComponent<SpriteRenderer>(), dustColor, 21);

                Rigidbody2D rb = dust.AddComponent<Rigidbody2D>();
                rb.gravityScale = 1.35f;
                rb.velocity = new Vector2(UnityEngine.Random.Range(-3.2f, 3.2f), UnityEngine.Random.Range(-2.2f, .35f));

                FadingParticle particle = dust.AddComponent<FadingParticle>();
                particle.Init(UnityEngine.Random.Range(0.32f, 0.58f), true);
            }
        }

        private static void ConfigureParticleRenderer(SpriteRenderer renderer, Color color, int sortingOrder)
        {
            if (renderer == null)
                return;

            renderer.color = new Color(color.r, color.g, color.b, 1f);
            renderer.maskInteraction = SpriteMaskInteraction.None;
            renderer.sortingLayerID = SortingLayer.NameToID("Default");
            renderer.sortingOrder = sortingOrder;

            if (unlitParticleMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader != null)
                    unlitParticleMaterial = new Material(shader) { name = "Shredder Unlit VFX" };
            }

            if (unlitParticleMaterial != null)
                renderer.sharedMaterial = unlitParticleMaterial;
        }
    }

    /// <summary>
    /// Fades out alpha and optionally shrinks or expands particles over time.
    /// </summary>
    public class FadingParticle : MonoBehaviour
    {
        private float lifetime;
        private float elapsed;
        private SpriteRenderer sr;
        private bool shrink;
        private bool expand;
        private Vector3 initialScale;

        public void Init(float duration, bool shrinkOnFade = false, bool expandOnFade = false)
        {
            lifetime = duration;
            shrink = shrinkOnFade;
            expand = expandOnFade;
            sr = GetComponent<SpriteRenderer>();
            initialScale = transform.localScale;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float progress = elapsed / lifetime;

            if (progress >= 1f)
            {
                Destroy(gameObject);
                return;
            }

            if (sr != null)
            {
                Color c = sr.color;
                c.a = Mathf.Lerp(1f, 0f, progress);
                sr.color = c;
            }

            if (shrink)
            {
                transform.localScale = Vector3.Lerp(initialScale, Vector3.zero, progress);
            }
            else if (expand)
            {
                transform.localScale = Vector3.Lerp(initialScale, initialScale * 1.8f, progress);
            }
        }
    }
}
