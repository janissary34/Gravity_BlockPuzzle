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
        private readonly Dictionary<float, int> activeFeedsByShredderLine =
            new Dictionary<float, int>();

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
        /// catch zone is both the selected mouth and the authoritative
        /// proximity check: a piece cannot join a feed merely because another
        /// piece in the same lane is already being shredded.
        /// </summary>
        public bool TryCapturePiece(PuzzlePiece piece, ShredderCatchZone zone)
        {
            if (piece == null || zone == null ||
                !zone.ContainsCaptureFootprint(piece))
                return false;

            float shredderY = zone.ShredY;
            if (!TryAcquireFeedLane(shredderY, out int feedDepth))
                return false;

            if (!piece.TryBeginShredderHandoff())
            {
                ReleaseFeedLane(shredderY);
                return false;
            }

            StartCoroutine(FeedPieceIntoShredder(piece, shredderY, feedDepth));
            return true;
        }

        private bool TryAcquireFeedLane(float shredderY, out int feedDepth)
        {
            activeFeedsByShredderLine.TryGetValue(shredderY, out int activeInLane);
            feedDepth = activeInLane;
            int laneCapacity = shredderConfig != null
                ? shredderConfig.FeedQueueCapacity
                : 16;
            if (activeInLane >= laneCapacity)
                return false;

            activeFeedsByShredderLine[shredderY] = activeInLane + 1;
            return true;
        }

        private void ReleaseFeedLane(float shredderY)
        {
            if (!activeFeedsByShredderLine.TryGetValue(shredderY, out int activeInLane))
                return;

            if (activeInLane <= 1)
                activeFeedsByShredderLine.Remove(shredderY);
            else
                activeFeedsByShredderLine[shredderY] = activeInLane - 1;
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

        private System.Collections.IEnumerator FeedPieceIntoShredder(
            PuzzlePiece piece,
            float shredderY,
            int feedDepth)
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
            // The lead piece stays in front. Followers preserve their vertical
            // spacing and use one shared rear layer, so a deep valid queue
            // cannot disappear behind the board background.
            piece.SetShredderPresentationDepth(feedDepth > 0 ? -10 : 0);
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

            // Emit directly beneath the rotating shredder wheels. The grains then
            // emerge naturally from underneath the mechanism into the open space.
            float offsetBelow = shredderConfig != null ? shredderConfig.ExitSeamOffsetBelowShredder : 0.65f;
            float grinderExitSeamY = shredderY - offsetBelow;

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

                int targetPieceParticles = Mathf.Max(1, (int)(Mathf.Max(1f, totalProgress) * (shredderConfig != null ? shredderConfig.ParticlesPerShreddedCell : 8)));
                int emissionStride = Mathf.Max(1, shardList.Count / targetPieceParticles);

                // A) Shred voxel shards crossing the cutter line and exit at the gear seam.
                for (int i = 0; i < shardList.Count; i++)
                {
                    VoxelShard shard = shardList[i];
                    if (shard == null || processedShards.Contains(shard)) continue;

                    if (shard.transform.position.y <= shredderY)
                    {
                        processedShards.Add(shard);

                        Vector2 contactWorldPos = new Vector2(
                            shard.transform.position.x,
                            shredderY);
                        Color shardColor = tileColor;

                        float shardProgress = progressPerGrain * LevelProgressManager.SandGrainsPerRenderedVoxel;
                        scheduledProgress += shardProgress;

                        bool shouldEmitParticle = (processedShards.Count % emissionStride == 0);
                        if (shouldEmitParticle)
                        {
                            shard.BeginProgressHandoff(
                                contactWorldPos,
                                shardColor,
                                shardProgress,
                                1);
                        }
                        else
                        {
                            LevelProgressManager.Instance?.AddProgress(shardProgress);
                            shard.Recycle();
                        }
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

                        Vector2 contactWorldPos = new Vector2(
                            r.transform.position.x,
                            shredderY);

                        if (shardList.Count == 0)
                        {
                            LevelProgressManager progressManager = LevelProgressManager.Instance;
                            if (progressManager != null)
                            {
                                float cellProgress = totalProgress / Mathf.Max(1, pieceRenderers.Length);
                                scheduledProgress += cellProgress;
                                int burstCount = shredderConfig != null ? shredderConfig.ParticlesPerShreddedCell : 24;
                                progressManager.SpawnFlyingVoxelBurst(
                                    contactWorldPos,
                                    Opaque(tileColor),
                                    cellProgress,
                                    burstCount);
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
            ReleaseFeedLane(shredderY);
        }

        private static Color Opaque(Color color) => new Color(color.r, color.g, color.b, 1f);
    }
}
