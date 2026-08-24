using System.Collections.Generic;
using GravityPuzzle.Config;
using UnityEngine;
using UnityEngine.UI;

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

        [Header("UI Slider Attraction Target")]
        [SerializeField, Tooltip("Top UI Slider reference to fill when Gems arrive.")]
        private Slider targetUISlider;

        [SerializeField, Tooltip("Specific RectTransform target for Gem attraction (defaults to slider handle/rect if null).")]
        private RectTransform targetUIRectTransform;

        [SerializeField, Tooltip("Camera used for Screen/World conversion (defaults to Camera.main).")]
        private Camera targetCamera;

        public static BlockShredder Instance { get; private set; }
        public static int ActiveGemFlightCount { get; private set; }
        public static bool HasActiveGemFlights => ActiveGemFlightCount > 0;
        public ShredderConfig Config => shredderConfig;

        private ShredderFeedMask activeFeedMask;
        private int activeFeedCount;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetGemFlightCount()
        {
            ActiveGemFlightCount = 0;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[Shredder] Duplicate BlockShredder component was disabled. Assign exactly one authored shredder controller.", this);
                enabled = false;
                return;
            }
            Instance = this;
            if (shredderConfig != null && shredderConfig.WheelPrefab != null)
                ShredderWheelPool.Configure(shredderConfig.WheelPrefab, transform, shredderConfig.WheelPoolCapacity);
            if (shredderConfig != null && shredderConfig.CatchZonePrefab != null)
                ShredderCatchZonePool.Configure(
                    shredderConfig.CatchZonePrefab,
                    transform,
                    shredderConfig.CatchZonePoolCapacity);
            if (shredderConfig != null && shredderConfig.FeedMaskPrefab != null)
                ShredderFeedMaskPool.Configure(shredderConfig.FeedMaskPrefab, transform);

            if (targetCamera == null)
                targetCamera = Camera.main;

            if (targetUIRectTransform == null && targetUISlider != null)
                targetUIRectTransform = targetUISlider.GetComponent<RectTransform>();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// Starts the coordinate-authorized handoff into the shredder. The
        /// caller has already established that the piece footprint reached a
        /// configured catch zone; physics triggers never decide this transition.
        /// </summary>
        public bool TryCapturePiece(PuzzlePiece piece, float shredderY)
        {
            if (piece == null || !piece.TryBeginShredderHandoff())
                return false;

            StartCoroutine(FeedPieceIntoShredder(piece, shredderY));
            return true;
        }

        private void AcquireFeedMask(float shredderY)
        {
            if (activeFeedMask == null && !ShredderFeedMaskPool.TryRent(out activeFeedMask))
            {
                Debug.LogWarning("[Shredder] Feed-mask pool is not configured; feed continues without visual clipping.", this);
                return;
            }

            activeFeedCount++;
            activeFeedMask.Configure(
                shredderY,
                shredderConfig != null ? shredderConfig.FeedMaskVerticalOffset : -15f,
                shredderConfig != null ? shredderConfig.FeedMaskScale : new Vector2(60f, 30f));
        }

        private void ReleaseFeedMask()
        {
            if (activeFeedCount > 0)
                activeFeedCount--;

            if (activeFeedCount != 0 || activeFeedMask == null)
                return;

            ShredderFeedMaskPool.Return(activeFeedMask);
            activeFeedMask = null;
        }

        private System.Collections.IEnumerator FeedPieceIntoShredder(PuzzlePiece piece, float shredderY)
        {
            if (piece == null) yield break;

            AcquireFeedMask(shredderY);

            // 1. Release the piece into physics with its full collision geometry
            // intact. Individual lower cells are removed only when they reach
            // the cutter line below, preventing a feed from passing through an
            // obstacle or another falling piece.
            piece.SetSelected(false);
            piece.EnterShredderPhysics(shredderConfig);
            piece.ApplyShredderCollisionMaterial(shredderConfig != null
                ? shredderConfig.FeedPhysicsMaterial
                : null);
            Rigidbody2D rb = piece.Body;

            // 2. Sprite Masking / Visual Clipping: mask out any portion moving below shredderY
            SpriteRenderer[] pieceRenderers = piece.GetComponentsInChildren<SpriteRenderer>(true);
            piece.BeginShredderPresentation(pieceRenderers);
            piece.ApplyShredderPresentationClipping();
            // Always use the authored PuzzlePiece colour for debris and UI voxels.
            // Renderer colours can be temporarily changed by selection or masking.
            Color tileColor = Opaque(piece.VisualColor);

            // Get all voxel shards if present
            VoxelShard[] shards = piece.GetComponentsInChildren<VoxelShard>(true);
            List<VoxelShard> shardList = new List<VoxelShard>(shards);
            shardList.Sort((a, b) => a.transform.position.y.CompareTo(b.transform.position.y));

            // Setup gems
            int gemCount = Mathf.RoundToInt(shardList.Count * shredderConfig.GemVoxelRatio);
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
            float feedSpeed = shredderConfig != null ? shredderConfig.FeedSpeed : .7f;
            float tremorIntensity = shredderConfig != null ? shredderConfig.TremorIntensity : .045f;
            float tremorFrequency = shredderConfig != null ? shredderConfig.TremorFrequency : 55f;
            float shakeAmplitude = shredderConfig != null ? shredderConfig.FeedShakeAmplitude : 2.5f;
            float maxTiltAngle = shredderConfig != null ? shredderConfig.MaxFeedTiltAngle : 5f;

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
                    float shakeOffsetX = Mathf.Sin(Time.time * tremorFrequency) *
                                         tremorIntensity * shakeAmplitude;
                    rb.position += new Vector2(shakeOffsetX - previousShakeOffsetX, 0f);
                    previousShakeOffsetX = shakeOffsetX;
                    rb.velocity = new Vector2(0f, -feedSpeed);

                    // Keep the feed visually stable while it enters the cutter.
                    float currentAngle = Mathf.DeltaAngle(0f, rb.rotation);
                    if (Mathf.Abs(currentAngle) > maxTiltAngle)
                    {
                        rb.angularVelocity *= 0.5f;
                        rb.rotation = Mathf.Clamp(currentAngle, -maxTiltAngle, maxTiltAngle);
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
                        piece.HideShredderRenderer(r);

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

            ReleaseFeedMask();
        }

        private static Color Opaque(Color color) => new Color(color.r, color.g, color.b, 1f);

        /// <summary>
        /// Allows dynamically assigning the target UI Slider at runtime.
        /// </summary>
        public void SetTargetUISlider(Slider slider, RectTransform targetRect = null)
        {
            targetUISlider = slider;
            targetUIRectTransform = targetRect != null ? targetRect : (slider != null ? slider.GetComponent<RectTransform>() : null);
        }
    }

}
