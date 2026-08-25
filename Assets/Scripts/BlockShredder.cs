using System.Collections.Generic;
using GravityPuzzle.Config;
using UnityEngine;

namespace GravityPuzzle
{
    /// <summary>
    /// Handles block shredding triggers, pre-fractured composite block voxelization,
    /// and pooled shard progress handoff to the level progress manager.
    /// </summary>
    [DisallowMultipleComponent]
    public class BlockShredder : MonoBehaviour
    {
        private static readonly SpriteRenderer[] EmptyRenderers = new SpriteRenderer[0];

        [Header("Configuration")]
        [Tooltip("Authoring asset used for both this feed behaviour and runtime-created shredder wheels.")]
        [SerializeField] private ShredderConfig shredderConfig;

        public static BlockShredder Instance { get; private set; }
        public ShredderConfig Config => shredderConfig;

        private ShredderFeedMask activeFeedMask;
        private int activeFeedCount;

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
            SpriteRenderer[] pieceRenderers = piece.ConfiguredShredderRenderers ?? EmptyRenderers;
            piece.BeginShredderPresentation(pieceRenderers);
            piece.ApplyShredderPresentationClipping();
            // Always use the authored PuzzlePiece colour for debris and UI voxels.
            // Renderer colours can be temporarily changed by selection or masking.
            Color tileColor = Opaque(piece.VisualColor);

            // The factory records all pooled shards while it configures this
            // piece. Copy only the references needed by this concurrent feed;
            // hierarchy traversal is not valid in a gameplay handoff.
            IReadOnlyList<VoxelShard> configuredShards = piece.ConfiguredVoxelShards;
            List<VoxelShard> shardList = new List<VoxelShard>(configuredShards.Count);
            HashSet<Transform> shardTransforms = new HashSet<Transform>();
            for (int i = 0; i < configuredShards.Count; i++)
            {
                VoxelShard shard = configuredShards[i];
                if (shard == null)
                    continue;

                shardList.Add(shard);
                shardTransforms.Add(shard.transform);
            }
            shardList.Sort((a, b) => a.transform.position.y.CompareTo(b.transform.position.y));

            // Emit at the narrow seam directly beneath the rotating teeth. The
            // grains then fall naturally from the place where the mesh is cut,
            // instead of materialising lower down in open space.
            float grinderExitSeamY = shredderY - .06f;

            HashSet<VoxelShard> processedShards = new HashSet<VoxelShard>();
            float totalProgress = Mathf.Max(0f, piece.RemainingProgressUnits);
            float progressPerGrain = totalProgress /
                                     (float)Mathf.Max(1, shardList.Count * LevelProgressManager.SandGrainsPerRenderedVoxel);
            float scheduledProgress = 0f;
            bool legacyProgressScheduled = false;
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
                        float shardProgress = progressPerGrain * LevelProgressManager.SandGrainsPerRenderedVoxel;
                        scheduledProgress += shardProgress;
                        shard.BeginProgressHandoff(
                            exitSeamPosition,
                            shardColor,
                            shardProgress);
                    }
                }

                // B) Erase generic renderers crossing the cutter line and use the same seam.
                int activeCount = 0;
                float topY = float.NegativeInfinity;

                foreach (var r in pieceRenderers)
                {
                    if (r == null || !r.enabled || r.gameObject.name.StartsWith("Selected Fill") || r.gameObject.name.StartsWith("White Selection"))
                        continue;

                    // Voxel shards are handled by the dedicated pooled-shard
                    // pass above. Do not perform GetComponent in this feed loop.
                    if (shardTransforms.Contains(r.transform))
                        continue;

                    if (r.transform.position.y <= shredderY)
                    {
                        piece.HideShredderRenderer(r);

                        Vector2 exitSeamPosition = new Vector2(
                            r.transform.position.x,
                            grinderExitSeamY);

                        // Fallback for legacy pieces which have no generated VoxelShards.
                        if (shardList.Count == 0 && !legacyProgressScheduled)
                        {
                            // Legacy render-only pieces have no VoxelShard to animate.
                            // Preserve their progress without manufacturing a temporary
                            // runtime grain; the regular UI-flight presenter owns it.
                            LevelProgressManager progressManager = LevelProgressManager.Instance;
                            if (progressManager != null)
                            {
                                scheduledProgress = totalProgress;
                                legacyProgressScheduled = true;
                                progressManager.SpawnFlyingVoxel(
                                    exitSeamPosition,
                                    Opaque(tileColor),
                                    totalProgress,
                                    null);
                            }
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
                float outstandingProgress = Mathf.Max(0f, totalProgress - scheduledProgress);
                if (outstandingProgress > 0.0001f)
                {
                    LevelProgressManager progressManager = LevelProgressManager.Instance;
                    if (progressManager != null)
                    {
                        progressManager.SpawnFlyingVoxel(
                            new Vector2(piece.transform.position.x, grinderExitSeamY),
                            Opaque(tileColor),
                            outstandingProgress,
                            null);
                    }
                }

                piece.ReleaseInstance();
            }

            ReleaseFeedMask();
        }

        private static Color Opaque(Color color) => new Color(color.r, color.g, color.b, 1f);
    }
}
