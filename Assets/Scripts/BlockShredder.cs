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

        [SerializeField, Tooltip("Speed (units/sec) at which blocks physically descend into the shredder.")]
        private float blockFeedSpeed = 1.6f;

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
                globalShredderMaskObject.transform.position = new Vector3(0f, shredderY - 10f, 0f);
                globalShredderMaskObject.transform.localScale = new Vector3(40f, 20f, 1f);

                SpriteMask mask = globalShredderMaskObject.AddComponent<SpriteMask>();
                mask.sprite = PrototypeBootstrap.GetSquareSprite();
            }
        }

        private System.Collections.IEnumerator FeedPieceIntoShredder(PuzzlePiece piece, float shredderY)
        {
            if (piece == null) yield break;

            EnsureSpriteMask(shredderY);

            // 1. Disable dynamic physics / falling motion & user dragging immediately
            piece.SetSelected(false);
            Rigidbody2D rb = piece.Body;
            if (rb != null)
            {
                rb.bodyType = RigidbodyType2D.Kinematic;
                rb.velocity = Vector2.zero;
                rb.angularVelocity = 0f;
            }

            // Disable all physical colliders to eliminate hard gear-boundary jamming
            Collider2D[] cols = piece.GetComponentsInChildren<Collider2D>();
            foreach (var c in cols) c.enabled = false;

            // 2. Enable Sprite Masking on base block renderers so solid background pixels below shredderY are masked out
            SpriteRenderer[] pieceRenderers = piece.GetComponentsInChildren<SpriteRenderer>(true);
            Color tileColor = Color.white;
            foreach (var r in pieceRenderers)
            {
                if (r != null)
                {
                    if (r.GetComponent<VoxelShard>() == null)
                    {
                        r.maskInteraction = SpriteMaskInteraction.VisibleOutsideMask;
                    }
                    else
                    {
                        r.maskInteraction = SpriteMaskInteraction.None;
                    }

                    if (r.enabled && !r.gameObject.name.StartsWith("Selected Fill") && !r.gameObject.name.StartsWith("White Selection"))
                    {
                        tileColor = r.color;
                    }
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

            // Bottom exit mouth where shredded voxels, sparks, and mini-cubes emerge (below shredder wheels)
            float bottomMouthY = shredderY - 0.65f;

            // Snap X position to nearest clean grid column so side-by-side pieces never overlap
            Vector3 basePosition = piece.transform.position;
            basePosition.x = Mathf.Round(basePosition.x * 2f) / 2f;

            // Assign distinct sorting order per feeding piece to prevent visual overlap/Z-fighting
            int shredderSortingOrder = 10 + (Mathf.Abs(piece.GetInstanceID()) % 30);
            foreach (var r in pieceRenderers)
            {
                if (r != null)
                {
                    r.sortingOrder = shredderSortingOrder;
                }
            }

            HashSet<VoxelShard> processedShards = new HashSet<VoxelShard>();

            // 3. Move block downward at constant slow "feed rate" with vertical mechanical gear teeth jitter
            float maxTime = 4.0f;
            float elapsed = 0f;

            while (piece != null && elapsed < maxTime)
            {
                elapsed += Time.deltaTime;

                // Move base position downward at constant feed rate
                basePosition.y -= blockFeedSpeed * Time.deltaTime;

                // High-frequency mechanical teeth vibration on Y-axis (vertical bite vibration)
                float microJitterY = Mathf.Sin(Time.time * 75f) * 0.025f;
                piece.transform.position = new Vector3(basePosition.x, basePosition.y + microJitterY, basePosition.z);

                bool spawnedParticleThisFrame = false;

                // A) Shred voxel shards crossing shredderY line and EMERGE FROM BOTTOM MOUTH
                for (int i = 0; i < shardList.Count; i++)
                {
                    VoxelShard shard = shardList[i];
                    if (shard == null || processedShards.Contains(shard)) continue;

                    if (shard.transform.position.y <= shredderY + 0.15f)
                    {
                        processedShards.Add(shard);

                        bool isGem = gemShards.Contains(shard);
                        if (isGem) ActiveGemFlightCount++;

                        // Ejection vector directing DOWNWARDS from bottom mouth of shredder wheels
                        Vector2 ejectionForce = new Vector2(UnityEngine.Random.Range(-0.5f, 0.5f), UnityEngine.Random.Range(-2.2f, -1.2f)) * shredExplosionForce * (isGem ? 0.4f : 0.8f);
                        Vector2 bottomPos = new Vector2(shard.transform.position.x, bottomMouthY);

                        // Emit pixelated mini-cube burst, orange sparks, and micro-dust emerging from bottom mouth
                        if (!spawnedParticleThisFrame)
                        {
                            ShredderParticleEffects.SpawnBurst(bottomPos, tileColor, 5, 3, 2);
                            spawnedParticleThisFrame = true;
                        }

                        shard.TriggerShred(
                            bottomPos,
                            ejectionForce,
                            isGem,
                            RecycleGemVoxel,
                            targetUIRectTransform,
                            targetUISlider,
                            targetCamera,
                            gemFlyDuration,
                            gemFlyEase
                        );
                    }
                }

                // B) Continuously emit particles from bottom mouth and erase renderers as they pass shredderY
                SpriteRenderer[] activeRenderers = piece.GetComponentsInChildren<SpriteRenderer>(true);
                int activeCount = 0;
                float topY = float.NegativeInfinity;

                foreach (var r in activeRenderers)
                {
                    if (r == null || !r.enabled || r.gameObject.name.StartsWith("Selected Fill") || r.gameObject.name.StartsWith("White Selection"))
                        continue;

                    // Exclude detached shards
                    if (r.transform.parent != piece.transform && r.GetComponent<VoxelShard>() != null)
                        continue;

                    if (r.transform.position.y <= shredderY + 0.05f)
                    {
                        r.enabled = false;
                        if (!spawnedParticleThisFrame)
                        {
                            Vector2 bottomPos = new Vector2(r.transform.position.x, bottomMouthY);
                            ShredderParticleEffects.SpawnBurst(bottomPos, tileColor, 5, 3, 2);
                            spawnedParticleThisFrame = true;
                        }
                    }
                    else
                    {
                        activeCount++;
                        topY = Mathf.Max(topY, r.bounds.max.y);
                    }
                }

                // Check cleanup condition: top edge of all active renderers passes below shredderY
                bool topBelowShredder = topY != float.NegativeInfinity && topY <= shredderY - 0.1f;
                bool allShardsDone = shardList.Count == 0 || processedShards.Count >= shardList.Count;

                if (topBelowShredder || (allShardsDone && activeCount == 0))
                {
                    break;
                }

                yield return null;
            }

            yield return new WaitForSeconds(0.02f);
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
        public static void SpawnBurst(Vector2 contactPosition, Color tileColor, int miniCubeCount = 4, int sparkCount = 2, int dustCount = 1)
        {
            // 1. High-density solid-colored square mini-cubes matching tile color
            for (int i = 0; i < miniCubeCount; i++)
            {
                float size = UnityEngine.Random.Range(0.045f, 0.085f);
                Color cubeColor = Color.Lerp(tileColor, Color.white, UnityEngine.Random.Range(0f, 0.15f));
                Vector2 spawnPos = contactPosition + new Vector2(UnityEngine.Random.Range(-0.25f, 0.25f), UnityEngine.Random.Range(-0.06f, 0.06f));

                GameObject cube = PrototypeBootstrap.CreateVisualBlock("MiniCubeParticle", spawnPos, Vector2.one * size, cubeColor);
                cube.GetComponent<SpriteRenderer>().sortingOrder = 22;

                Rigidbody2D rb = cube.AddComponent<Rigidbody2D>();
                rb.gravityScale = 0.35f;
                rb.velocity = new Vector2(UnityEngine.Random.Range(-1.2f, 1.2f), UnityEngine.Random.Range(-2.6f, -0.8f));
                rb.angularVelocity = UnityEngine.Random.Range(-540f, 540f);

                FadingParticle particle = cube.AddComponent<FadingParticle>();
                particle.Init(UnityEngine.Random.Range(0.45f, 0.85f), true);
            }

            // 2. Subtle orange spark particles
            for (int i = 0; i < sparkCount; i++)
            {
                float size = UnityEngine.Random.Range(0.035f, 0.06f);
                Color sparkColor = new Color(1.0f, UnityEngine.Random.Range(0.5f, 0.75f), 0.12f, 1.0f); // Vibrant orange/amber
                Vector2 spawnPos = contactPosition + new Vector2(UnityEngine.Random.Range(-0.3f, 0.3f), UnityEngine.Random.Range(-0.05f, 0.05f));

                GameObject spark = PrototypeBootstrap.CreateVisualBlock("OrangeSpark", spawnPos, Vector2.one * size, sparkColor);
                spark.GetComponent<SpriteRenderer>().sortingOrder = 25;

                Rigidbody2D rb = spark.AddComponent<Rigidbody2D>();
                rb.gravityScale = 0.2f;
                rb.velocity = new Vector2(UnityEngine.Random.Range(-2.0f, 2.0f), UnityEngine.Random.Range(-1.6f, 0.8f));
                rb.angularVelocity = UnityEngine.Random.Range(-900f, 900f);

                FadingParticle particle = spark.AddComponent<FadingParticle>();
                particle.Init(UnityEngine.Random.Range(0.18f, 0.32f), true);
            }

            // 3. Micro-dust particles
            for (int i = 0; i < dustCount; i++)
            {
                float size = UnityEngine.Random.Range(0.07f, 0.13f);
                Color dustColor = new Color(0.85f, 0.85f, 0.9f, 0.35f); // Soft greyish-white micro dust
                Vector2 spawnPos = contactPosition + new Vector2(UnityEngine.Random.Range(-0.25f, 0.25f), UnityEngine.Random.Range(-0.08f, 0.08f));

                GameObject dust = PrototypeBootstrap.CreateVisualBlock("MicroDust", spawnPos, Vector2.one * size, dustColor);
                dust.GetComponent<SpriteRenderer>().sortingOrder = 18;

                Rigidbody2D rb = dust.AddComponent<Rigidbody2D>();
                rb.gravityScale = -0.05f; // Gentle upward drift
                rb.velocity = new Vector2(UnityEngine.Random.Range(-0.4f, 0.4f), UnityEngine.Random.Range(-0.2f, 0.4f));

                FadingParticle particle = dust.AddComponent<FadingParticle>();
                particle.Init(UnityEngine.Random.Range(0.4f, 0.7f), false, true);
            }
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
