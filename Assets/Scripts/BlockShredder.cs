using System.Collections.Generic;
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

        public static int ActiveGemFlightCount { get; private set; }
        public static bool HasActiveGemFlights => ActiveGemFlightCount > 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetGemFlightCount()
        {
            ActiveGemFlightCount = 0;
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
            // the simulation advances; TryBeginShredding makes this idempotent.
            TryShredBlock(other, transform.position);
        }

        [SerializeField, Tooltip("Speed (units/sec) at which blocks physically descend into the shredder.")]
        private float blockFeedSpeed = 1.6f;

        [SerializeField, Tooltip("Brief angular impulse applied when a piece first bites into the gears.")]
        private float shredderTumbleTorque = 1.25f;

        private static PhysicsMaterial2D shredderFeedMaterial;

        // The trigger has already accepted this piece for shredding. Keep it from
        // catching on the shredder frame or another piece while it is fed down.
        // Normal board and obstacle collisions are untouched until that point.
        private static void DisableCapturedPieceCollisions(PuzzlePiece piece)
        {
            Collider2D[] colliders = piece.GetComponentsInChildren<Collider2D>(true);
            foreach (Collider2D collider in colliders)
            {
                if (collider != null)
                    collider.enabled = false;
            }

            Physics2D.SyncTransforms();
        }

        /// <summary>
        /// Attempts to shred an entering PuzzlePiece into Stone and Gem voxels slowly/progressively with high-density particle effects.
        /// </summary>
        public void TryShredBlock(Collider2D targetCollider, Vector2 shredderCenter)
        {
            PuzzlePiece piece = targetCollider.GetComponentInParent<PuzzlePiece>();
            if (piece == null || !piece.TryBeginShredding())
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

            // 1. Release the piece into physics after removing its solid collision
            // geometry. A large shape can otherwise wedge against the shredder
            // frame before the cutter has a chance to consume it.
            piece.SetSelected(false);
            piece.PrepareForShredderPhysics();
            DisableCapturedPieceCollisions(piece);
            ApplyShredderFeedMaterial(piece);
            Rigidbody2D rb = piece.Body;
            if (rb != null)
            {
                rb.bodyType = RigidbodyType2D.Dynamic;
                rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                rb.constraints &= ~RigidbodyConstraints2D.FreezeRotation;
                rb.velocity = Vector2.down * blockFeedSpeed;
                rb.angularVelocity = 0f;
                rb.angularDrag = Mathf.Max(rb.angularDrag, 8f);
                rb.AddTorque(Random.Range(-shredderTumbleTorque, shredderTumbleTorque), ForceMode2D.Impulse);
            }

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
            float progressPerGrain = piece.ProgressUnits /
                                     (float)Mathf.Max(1, shardList.Count * LevelProgressManager.SandGrainsPerRenderedVoxel);
            float maxTime = 4.0f;
            float elapsed = 0f;

            // 3. The Rigidbody2D now owns position and rotation. The coroutine
            // only watches the falling voxels and converts them to shred effects.
            while (piece != null && elapsed < maxTime)
            {
                elapsed += Time.deltaTime;

                // As the piece crosses the cutter line, release only the lower
                // collision cells. The part still above the shredder stays solid,
                // so large pieces cannot merge into one another while being fed.
                piece.ReleaseCollisionCellsAtOrBelow(shredderY);

                if (rb != null)
                {
                    // Keep the feed moving and allow a gentle wobble without the
                    // violent free-spin caused by repeated grinder contacts.
                    if (rb.velocity.y > -blockFeedSpeed)
                        rb.velocity = new Vector2(rb.velocity.x, -blockFeedSpeed);
                    rb.angularVelocity = Mathf.Clamp(rb.angularVelocity, -55f, 55f);
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

                        // One lightweight grain per rendered voxel. It only simulates
                        // in the small space below the grinder, then hands off to UI.
                        ShredderVoxelHandoff.SpawnStream(
                            exitSeamPosition,
                            shardColor,
                            LevelProgressManager.SandGrainsPerRenderedVoxel,
                            progressPerGrain);

                        shard.Recycle();
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
                            ShredderVoxelHandoff.SpawnStream(
                                exitSeamPosition,
                                Opaque(tileColor),
                                1,
                                piece.ProgressUnits);
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
                Destroy(piece.gameObject);
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
    /// A deliberately small and short-lived world-physics phase for a shred voxel.
    /// It has no collider and exists only below the grinder before its UI hand-off.
    /// </summary>
    public sealed class ShredderVoxelHandoff : MonoBehaviour
    {
        private static Material unlitMaterial;

        private Rigidbody2D body;
        private SpriteRenderer spriteRenderer;
        private float handoffY;
        private float elapsed;
        private bool handedOff;
        private Color voxelColor;
        private float progressAmount;

        public static void SpawnStream(Vector2 position, Color color, int grainCount, float progressPerGrain)
        {
            for (int i = 0; i < Mathf.Max(1, grainCount); i++)
            {
                // Keep the spawn point on the grinder seam. A small horizontal
                // variation spreads grains across the tooth that cut the block,
                // but never creates a second artificial vertical spawn line.
                Vector2 offset = new Vector2(UnityEngine.Random.Range(-.13f, .13f), 0f);
                GameObject grain = PrototypeBootstrap.CreateVisualBlock(
                    "Shredder Sand Grain", position + offset,
                    Vector2.one * UnityEngine.Random.Range(.08f, .12f),
                    new Color(color.r, color.g, color.b, 1f));
                // Each grain falls to its own depth before transitioning to the
                // UI flight. This removes the old invisible flat landing line.
                float dropDepth = UnityEngine.Random.Range(-1.2f, -.5f);
                float individualHandoffY = position.y + dropDepth;
                grain.AddComponent<ShredderVoxelHandoff>().Initialize(
                    color,
                    individualHandoffY,
                    UnityEngine.Random.Range(0f, .12f),
                    progressPerGrain);
            }
        }

        private float launchDelay;
        private float physicsElapsed;
        private bool physicsStarted;

        private void Initialize(Color color, float targetHandoffY, float delay, float grainProgressAmount)
        {
            voxelColor = new Color(color.r, color.g, color.b, 1f);
            handoffY = targetHandoffY;
            launchDelay = delay;
            progressAmount = grainProgressAmount;
            spriteRenderer = GetComponent<SpriteRenderer>();
            spriteRenderer.color = voxelColor;
            spriteRenderer.maskInteraction = SpriteMaskInteraction.None;
            // Shredder disc/teeth render at 25–27. Keep the falling voxel
            // stream below that layer so it visibly travels behind the gears.
            spriteRenderer.sortingOrder = 4;
            if (unlitMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader != null)
                    unlitMaterial = new Material(shader) { name = "Shredder Voxel Unlit" };
            }
            if (unlitMaterial != null)
                spriteRenderer.sharedMaterial = unlitMaterial;

            body = gameObject.AddComponent<Rigidbody2D>();
            body.simulated = false;
            body.gravityScale = .42f;
            body.velocity = new Vector2(UnityEngine.Random.Range(-.22f, .22f), UnityEngine.Random.Range(-1.35f, -1.0f));
            body.angularVelocity = UnityEngine.Random.Range(-220f, 220f);
        }

        private void Update()
        {
            if (handedOff)
                return;

            elapsed += Time.deltaTime;
            if (!physicsStarted)
            {
                if (elapsed < launchDelay)
                    return;

                physicsStarted = true;
                body.simulated = true;
            }

            physicsElapsed += Time.deltaTime;
            // The timeout is only a safety net; the randomized handoff depth is
            // what determines where each voxel begins its upward UI curve.
            if (transform.position.y <= handoffY || physicsElapsed >= 1.25f)
                HandOffToUi();
        }

        private void HandOffToUi()
        {
            if (handedOff)
                return;

            handedOff = true;
            if (body != null)
                body.simulated = false;
            if (spriteRenderer != null)
                spriteRenderer.enabled = false;

            LevelProgressManager manager = LevelProgressManager.Instance;
            if (manager != null)
            {
                manager.SpawnFlyingVoxel(transform.position, voxelColor, progressAmount, () => Destroy(gameObject));
            }
            else
            {
                Destroy(gameObject);
            }
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
