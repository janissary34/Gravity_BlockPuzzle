using System;
using System.Collections.Generic;
using GravityPuzzle.Config;
using GravityPuzzle.Presentation.VFX;
using UnityEngine;

namespace GravityPuzzle
{
    /// <summary>
    /// Handles block shredding triggers, pre-fractured composite block voxelization,
    /// and pooled shard progress handoff to the level progress manager.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public class BlockShredder : MonoBehaviour
    {
        private static readonly SpriteRenderer[] EmptyRenderers = new SpriteRenderer[0];

        [Header("Configuration")]
        [Tooltip("Authoring asset used for both this feed behaviour and runtime-created shredder wheels.")]
        [SerializeField] private ShredderConfig shredderConfig;

        [Header("Grinding Presentation")]
        [Tooltip("One shared world-space particle system that emits square debris along active cutter edges.")]
        [SerializeField] private ShredderDebrisParticleSystem debrisParticleSystem;

        [Header("Audio")]
        [Tooltip("One-shot played when a shredded voxel is handed to the flying particle presentation.")]
        [SerializeField] private AudioClip particleShredClip;
        [Tooltip("Cached 2D source used exclusively for shredded-particle feedback.")]
        [SerializeField] private AudioSource particleShredAudioSource;
        [Tooltip("Minimum time between particle shred one-shots so a large piece cannot flood the audio mix.")]
        [Min(0f)] [SerializeField] private float particleShredSoundMinInterval = .08f;

        public static BlockShredder Instance { get; private set; }
        public ShredderConfig Config => shredderConfig;

        private ShredderFeedMask activeFeedMask;
        private int activeFeedCount;
        private readonly Dictionary<float, int> activeFeedsByShredderLine =
            new Dictionary<float, int>();
        private static readonly Comparison<BoxCollider2D> CompareCellsByCenterX = (a, b) =>
        {
            if (a == null || b == null) return 0;
            return a.bounds.center.x.CompareTo(b.bounds.center.x);
        };
        private float nextParticleShredSoundTime;

        private readonly struct CutterContactSegment
        {
            public readonly float MinX;
            public readonly float MaxX;
            public readonly float Width;
            public readonly float CenterX;

            public CutterContactSegment(float minX, float maxX)
            {
                MinX = minX;
                MaxX = maxX;
                Width = Mathf.Max(0.05f, maxX - minX);
                CenterX = (minX + maxX) * 0.5f;
            }
        }

        private readonly CutterContactSegment[] activeSegmentsBuffer = new CutterContactSegment[16];
        private readonly Vector2[] rawContactBoundsBuffer = new Vector2[32];
        private readonly List<Bounds> cachedCellBoundsBuffer = new List<Bounds>(32);

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[Shredder] Duplicate BlockShredder component was disabled. Assign exactly one authored shredder controller.", this);
                enabled = false;
                return;
            }
            Instance = this;
            if (particleShredAudioSource == null)
                particleShredAudioSource = GetComponent<AudioSource>();
            if (debrisParticleSystem != null && shredderConfig != null &&
                shredderConfig.EnableContactDebrisPresentation)
                debrisParticleSystem.Configure(shredderConfig);
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
            float mouthInset = shredderConfig != null
                ? shredderConfig.FeedMaskMouthInset
                : 0f;
            activeFeedMask.Configure(
                shredderY,
                (shredderConfig != null ? shredderConfig.FeedMaskVerticalOffset : -15f) - mouthInset,
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

            // Artist-atlas pieces do not contain the legacy per-voxel objects
            // that used to trigger this sound. The feed itself is the common
            // logical shred event, so always provide its first audible beat.
            PlayParticleShredSound();

            // 2. Capture the current presentation before the feed mask begins
            // clipping the piece beneath the cutter.
            // An intact atlas is a single large SpriteRenderer and can only
            // ever read as a rectangular mask. Switch to the authored modular
            // cells before caching renderers so each row becomes a tangible
            // cutter surface with its own irregular edge.
            piece.PrepareModularShredderSurface();
            SpriteRenderer[] pieceRenderers = piece.ConfiguredShredderRenderers ?? EmptyRenderers;
            piece.BeginShredderPresentation(pieceRenderers, shredderY, shredderConfig);
            piece.ApplyShredderPresentationClipping();
            // The lead piece stays in front. Followers preserve their vertical
            // spacing and use one shared rear layer, so a deep valid queue
            // cannot disappear behind the board background.
            piece.SetShredderPresentationDepth(feedDepth > 0 ? -10 : 0);
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

            HashSet<VoxelShard> processedShards = new HashSet<VoxelShard>();
            float totalProgress = Mathf.Max(0f, piece.RemainingProgressUnits);
            piece.GetActiveCellBounds(cachedCellBoundsBuffer);
            int blockCount = Mathf.Max(1, piece.ProgressUnits);
            if (cachedCellBoundsBuffer.Count > 0)
                blockCount = Mathf.Max(blockCount, cachedCellBoundsBuffer.Count);

            // Total flights are strictly and directly proportional to the piece's block count!
            // 1-block piece -> 28 flights
            // 2-block piece -> 56 flights
            // 3-block piece -> 84 flights
            // 4-block piece -> 112 flights
            const int BaseFlightsPerBlock = 28;
            int multiplier = (shredderConfig != null && shredderConfig.ProgressVoxelMultiplier > 0)
                ? shredderConfig.ProgressVoxelMultiplier
                : 1;
            int flightsPerBlock = Mathf.Clamp(BaseFlightsPerBlock * multiplier, 20, 60);
            int progressFlightCount = blockCount * flightsPerBlock;

            float maxTime = 6.0f;
            float elapsed = 0f;
            float previousShakeOffsetX = 0f;
            float feedSpeed = shredderConfig != null ? shredderConfig.FeedSpeed : 3.8f;
            float tremorIntensity = shredderConfig != null ? shredderConfig.TremorIntensity : .045f;
            float tremorFrequency = shredderConfig != null ? shredderConfig.TremorFrequency : 55f;
            float shakeAmplitude = shredderConfig != null ? shredderConfig.FeedShakeAmplitude : 2.5f;
            float maxTiltAngle = shredderConfig != null ? shredderConfig.MaxFeedTiltAngle : 5f;
            float debrisAccumulator = 0f;
            // The mask's top edge is lowered into the wheel mouth. Wheel teeth
            // render above this edge, turning visual removal into occlusion.
            float visualCutterY = shredderY - (shredderConfig != null
                ? shredderConfig.FeedMaskMouthInset
                : 0f);
            // Fragments start at the visible mouth/top tooth tangent, before
            // the mask hides material farther inside the wheel mechanism.
            float debrisContactY = shredderY;
            int totalCollisionCells = piece.CollisionCellCount;

            // Total area based on true cell blocks (1 unit per block)
            float totalRendererArea = Mathf.Max(0.5f, blockCount * 1.0f);
            float remainingProgressToEmit = totalProgress;
            int remainingFlightsToEmit = progressFlightCount;
            float progressVoxelAccumulator = 0f;

            List<BoxCollider2D> releasedCellsBuffer = new List<BoxCollider2D>(Mathf.Max(4, totalCollisionCells));
            bool contactPresentationStarted = false;
            Vector2 lastContactCenter = Vector2.zero;
            float lastContactWidth = 0f;

            // 3. The kinematic feed owns the descent while this coroutine watches
            // the crossing cells and converts them to shred effects.
            while (piece != null && elapsed < maxTime)
            {
                elapsed += Time.deltaTime;

                if (rb != null)
                {
                    // Apply continuous high-frequency horizontal tremor
                    float shakeOffsetX = Mathf.Sin(Time.time * tremorFrequency) *
                                         tremorIntensity * shakeAmplitude;
                    // The feed is kinematic, so commit its position here rather
                    // than waiting for a later physics step to integrate a
                    // velocity.  The grid release immediately below must see
                    // the same cutter crossing as the presentation; otherwise
                    // pieces above keep treating already-shredded cells as solid
                    // until the whole root is returned to its pool.
                    rb.position += new Vector2(
                        shakeOffsetX - previousShakeOffsetX,
                        -feedSpeed * Time.deltaTime);
                    previousShakeOffsetX = shakeOffsetX;
                    rb.velocity = Vector2.zero;

                    // Keep the feed visually stable while it enters the cutter.
                    float currentAngle = Mathf.DeltaAngle(0f, rb.rotation);
                    if (Mathf.Abs(currentAngle) > maxTiltAngle)
                    {
                        rb.angularVelocity *= 0.5f;
                        rb.rotation = Mathf.Clamp(currentAngle, -maxTiltAngle, maxTiltAngle);
                    }
                }

                // Sample active block cells directly so concave/notched pieces (L, U, etc.)
                // never produce false contact in empty air.
                piece.GetActiveCellBounds(cachedCellBoundsBuffer);

                bool hasContactSurface = TryGetCutterContactSurface(
                    cachedCellBoundsBuffer,
                    debrisContactY,
                    out float contactWidth,
                    out Vector2 currentContactCenter);
                if (hasContactSurface)
                {
                    lastContactWidth = contactWidth;
                    lastContactCenter = currentContactCenter;
                }

                // Release cells that have reached the cutter mouth for physics clearance.
                releasedCellsBuffer.Clear();
                float cutterThresholdY = shredderY + (shredderConfig != null
                    ? shredderConfig.CaptureApproachDistance + 0.12f
                    : 0.16f);
                piece.ReleaseCollisionCellsAtOrBelow(cutterThresholdY, releasedCellsBuffer);

                // Continuous voxel stream: distribute voxels smoothly every frame while contacting cutter
                if (hasContactSurface && contactWidth > 0f && remainingFlightsToEmit > 0)
                {
                    float dy = feedSpeed * Time.deltaTime;
                    float frameArea = contactWidth * dy;
                    float frameFraction = frameArea / totalRendererArea;

                    progressVoxelAccumulator += progressFlightCount * frameFraction;

                    if (progressVoxelAccumulator >= 1f)
                    {
                        int flightsToEmit = Mathf.Min(remainingFlightsToEmit, Mathf.FloorToInt(progressVoxelAccumulator));
                        if (flightsToEmit > 0)
                        {
                            float progressToEmit = remainingFlightsToEmit > 0
                                ? Mathf.Min(remainingProgressToEmit, remainingProgressToEmit * (flightsToEmit / (float)remainingFlightsToEmit))
                                : remainingProgressToEmit;

                            progressVoxelAccumulator -= flightsToEmit;
                            remainingFlightsToEmit = Mathf.Max(0, remainingFlightsToEmit - flightsToEmit);
                            remainingProgressToEmit = Mathf.Max(0f, remainingProgressToEmit - progressToEmit);

                            LevelProgressManager progressManager = LevelProgressManager.Instance;
                            if (progressManager != null && progressToEmit > 0.00001f)
                            {
                                int segCount = GetActiveCutterSegments(cachedCellBoundsBuffer, debrisContactY);
                                if (segCount <= 1)
                                {
                                    float emitCenterX = segCount == 1 ? activeSegmentsBuffer[0].CenterX : currentContactCenter.x;
                                    float emitWidth = segCount == 1 ? activeSegmentsBuffer[0].Width : contactWidth;
                                    progressManager.SpawnFlyingVoxelBurst(
                                        new Vector3(emitCenterX, debrisContactY, 0f),
                                        tileColor,
                                        progressToEmit,
                                        flightsToEmit,
                                        emitWidth);
                                }
                                else
                                {
                                    float totalSegWidth = 0f;
                                    for (int s = 0; s < segCount; s++)
                                        totalSegWidth += activeSegmentsBuffer[s].Width;

                                    if (totalSegWidth <= 0.001f)
                                        totalSegWidth = 1f;

                                    int flightsLeftInBatch = flightsToEmit;
                                    float progressLeftInBatch = progressToEmit;

                                    for (int s = 0; s < segCount; s++)
                                    {
                                        CutterContactSegment seg = activeSegmentsBuffer[s];
                                        bool isLast = s == segCount - 1;
                                        int segFlights = isLast
                                            ? flightsLeftInBatch
                                            : Mathf.Min(flightsLeftInBatch, Mathf.Max(1, Mathf.RoundToInt(flightsToEmit * (seg.Width / totalSegWidth))));
                                        float segProgress = isLast
                                            ? progressLeftInBatch
                                            : Mathf.Min(progressLeftInBatch, progressToEmit * (seg.Width / totalSegWidth));

                                        flightsLeftInBatch = Mathf.Max(0, flightsLeftInBatch - segFlights);
                                        progressLeftInBatch = Mathf.Max(0f, progressLeftInBatch - segProgress);

                                        if (segFlights > 0 && segProgress > 0.00001f)
                                        {
                                            progressManager.SpawnFlyingVoxelBurst(
                                                new Vector3(seg.CenterX, debrisContactY, 0f),
                                                tileColor,
                                                segProgress,
                                                segFlights,
                                                seg.Width);
                                        }
                                    }
                                }
                            }

                            PlayParticleShredSound();
                        }
                    }
                }

                // This is deliberately time-based rather than a final burst.
                // Do not begin until visible material reaches the hidden cutter
                // edge; before that moment no crumbs should appear at the mouth.
                if (hasContactSurface && debrisParticleSystem != null && shredderConfig != null &&
                    shredderConfig.EnableContactDebrisPresentation)
                {
                    if (!contactPresentationStarted)
                    {
                        int entryBurstCount = Mathf.Min(
                            shredderConfig.MaxDebrisEntryBurstPerFrame,
                            Mathf.CeilToInt(contactWidth * shredderConfig.DebrisEntryBurstPerWorldUnit));
                        EmitEntryBurstAtContactSurface(
                            cachedCellBoundsBuffer,
                            debrisContactY,
                            contactWidth,
                            tileColor,
                            entryBurstCount);
                    }

                    debrisAccumulator += Time.deltaTime *
                                       shredderConfig.DebrisPerSecondPerWorldUnit * contactWidth;
                    int debrisCount = Mathf.Min(
                        shredderConfig.MaxDebrisPerFrame,
                        Mathf.FloorToInt(debrisAccumulator));
                    if (debrisCount > 0)
                    {
                        debrisAccumulator -= debrisCount;
                        EmitDebrisAtContactSurface(
                            cachedCellBoundsBuffer,
                            debrisContactY,
                            contactWidth,
                            tileColor,
                            debrisCount);
                    }
                }

                if (hasContactSurface)
                    contactPresentationStarted = true;

                // A) Shred voxel shards crossing the cutter line. Their visual
                // material conversion is handled by the local cutter particles;
                // the single pooled HUD acknowledgement was already scheduled
                // from the first cutter-contact frame above.
                for (int i = 0; i < shardList.Count; i++)
                {
                    VoxelShard shard = shardList[i];
                    if (shard == null || processedShards.Contains(shard)) continue;

                    if (shard.transform.position.y <= shredderY)
                    {
                        processedShards.Add(shard);

                        shard.Recycle();
                    }
                }

                // B) Erase generic renderers crossing the cutter line and use the same seam.
                int activeCount = 0;
                float topY = float.NegativeInfinity;

                for (int rendererIndex = 0; rendererIndex < pieceRenderers.Length; rendererIndex++)
                {
                    SpriteRenderer r = pieceRenderers[rendererIndex];
                    if (r == null || !r.enabled || r.gameObject.name.StartsWith("Selected Fill") || r.gameObject.name.StartsWith("White Selection"))
                        continue;

                    // Voxel shards are handled by the dedicated pooled-shard
                    // pass above. Do not perform GetComponent in this feed loop.
                    if (shardTransforms.Contains(r.transform))
                        continue;

                    // Keep the atlas renderer alive until its *top* is inside
                    // the occluded wheel mouth. Hiding at its transform centre
                    // was the visible pop that made a block read as deleted.
                    bool rendererFullyCrossed = r.bounds.max.y <= visualCutterY;
                    if (rendererFullyCrossed)
                    {
                        piece.HideShredderRenderer(r);

                        Vector2 contactWorldPos = new Vector2(
                            r.transform.position.x,
                            visualCutterY);
                        if (debrisParticleSystem != null && shredderConfig != null &&
                            shredderConfig.EnableContactDebrisPresentation)
                        {
                            debrisParticleSystem.EmitAtCutter(
                                contactWorldPos,
                                Mathf.Max(.05f, r.bounds.size.x),
                                tileColor,
                                shredderConfig.DebrisBurstPerCrossedCell);
                        }

                        PlayParticleShredSound();
                    }
                    else
                    {
                        activeCount++;
                        topY = Mathf.Max(topY, r.bounds.max.y);
                    }
                }

                // The last visible pixel is now safely behind the wheel art.
                bool topBelowShredder = topY != float.NegativeInfinity &&
                                        topY <= visualCutterY;
                bool allShardsDone = shardList.Count == 0 || processedShards.Count >= shardList.Count;

                if (topBelowShredder || (allShardsDone && activeCount == 0))
                {
                    // Flush any remaining progress or flights when the piece finishes feeding
                    if (remainingProgressToEmit > 0.0001f || remainingFlightsToEmit > 0)
                    {
                        LevelProgressManager progressManager = LevelProgressManager.Instance;
                        if (progressManager != null)
                        {
                            int flushFlights = Mathf.Min(remainingFlightsToEmit, 4);
                            if (flushFlights > 0)
                            {
                                progressManager.SpawnFlyingVoxelBurst(
                                    new Vector3(lastContactCenter.x, debrisContactY, 0f),
                                    tileColor,
                                    remainingProgressToEmit,
                                    flushFlights,
                                    lastContactWidth);
                            }
                            else if (remainingProgressToEmit > 0.0001f)
                            {
                                progressManager.AddProgress(remainingProgressToEmit);
                            }
                        }

                        remainingProgressToEmit = 0f;
                        remainingFlightsToEmit = 0;
                    }

                    EmitFinalDebrisBurst(lastContactCenter, lastContactWidth, tileColor);
                    break;
                }

                yield return null;
            }

            if (piece != null)
            {
                // A malformed or fully-occluded renderer can occasionally
                // miss the cutter sample. Preserve any remaining uncommitted
                // progress in this safe fallback.
                if (remainingProgressToEmit > 0.0001f)
                {
                    LevelProgressManager progressManager = LevelProgressManager.Instance;
                    if (progressManager != null)
                    {
                        // The shredder owns only the world-space conversion.
                        // The established prewarmed HUD-flight system owns the
                        // Bézier route and adds progress on arrival.
                        int flushFlights = Mathf.Min(remainingFlightsToEmit, 4);
                        if (flushFlights > 0)
                        {
                            progressManager.SpawnFlyingVoxelBurst(
                                new Vector3(lastContactCenter.x, debrisContactY, 0f),
                                tileColor,
                                remainingProgressToEmit,
                                flushFlights,
                                lastContactWidth);
                        }
                        else
                        {
                            progressManager.AddProgress(remainingProgressToEmit);
                        }
                    }

                    remainingProgressToEmit = 0f;
                    remainingFlightsToEmit = 0;
                }

                piece.ReleaseInstance();
            }

            ReleaseFeedMask();
            ReleaseFeedLane(shredderY);
        }

        private static Color Opaque(Color color) => new Color(color.r, color.g, color.b, 1f);

        /// <summary>
        /// Finds the width of real material currently intersecting the cutter.
        /// We deliberately keep this separate from emission: a concave piece can
        /// have several disconnected contact cells, and emitting once over their
        /// enclosing bounds would put debris in the empty gaps.
        /// </summary>
        private static bool TryGetCutterContactSurface(
            List<Bounds> cellBounds,
            float cutterY,
            out float totalWidth,
            out Vector2 weightedCenter)
        {
            totalWidth = 0f;
            weightedCenter = new Vector2(0f, cutterY);
            if (cellBounds == null || cellBounds.Count == 0)
                return false;

            for (int index = 0; index < cellBounds.Count; index++)
            {
                Bounds bounds = cellBounds[index];
                if (bounds.min.y > cutterY + 0.08f || bounds.max.y < cutterY - 0.08f)
                    continue;

                float width = Mathf.Max(.05f, bounds.size.x);
                totalWidth += width;
                weightedCenter.x += bounds.center.x * width;
            }

            if (totalWidth <= 0f)
                return false;

            weightedCenter.x /= totalWidth;
            return true;
        }

        private int GetActiveCutterSegments(
            List<Bounds> cellBounds,
            float cutterY)
        {
            if (cellBounds == null || cellBounds.Count == 0)
                return 0;

            int touchingCount = 0;
            for (int index = 0; index < cellBounds.Count; index++)
            {
                Bounds bounds = cellBounds[index];
                if (bounds.min.y > cutterY + 0.08f || bounds.max.y < cutterY - 0.08f)
                    continue;

                if (touchingCount < rawContactBoundsBuffer.Length)
                {
                    rawContactBoundsBuffer[touchingCount] = new Vector2(bounds.min.x, bounds.max.x);
                    touchingCount++;
                }
            }

            if (touchingCount == 0)
                return 0;

            // In-place sort by min.x (insertion sort for tiny arrays - 0 allocations)
            for (int i = 1; i < touchingCount; i++)
            {
                Vector2 key = rawContactBoundsBuffer[i];
                int j = i - 1;
                while (j >= 0 && rawContactBoundsBuffer[j].x > key.x)
                {
                    rawContactBoundsBuffer[j + 1] = rawContactBoundsBuffer[j];
                    j--;
                }
                rawContactBoundsBuffer[j + 1] = key;
            }

            // Merge contiguous segments
            int segmentCount = 0;
            float currentMinX = rawContactBoundsBuffer[0].x;
            float currentMaxX = rawContactBoundsBuffer[0].y;

            for (int i = 1; i < touchingCount; i++)
            {
                if (rawContactBoundsBuffer[i].x - currentMaxX <= 0.35f)
                {
                    currentMaxX = Mathf.Max(currentMaxX, rawContactBoundsBuffer[i].y);
                }
                else
                {
                    if (segmentCount < activeSegmentsBuffer.Length)
                        activeSegmentsBuffer[segmentCount++] = new CutterContactSegment(currentMinX, currentMaxX);
                    currentMinX = rawContactBoundsBuffer[i].x;
                    currentMaxX = rawContactBoundsBuffer[i].y;
                }
            }

            if (segmentCount < activeSegmentsBuffer.Length)
                activeSegmentsBuffer[segmentCount++] = new CutterContactSegment(currentMinX, currentMaxX);

            return segmentCount;
        }

        /// <summary>
        /// Emits independently from every active cell touching the cutter. This
        /// keeps voxel sources glued to the teeth/material intersections even
        /// for stepped, split, and irregular piece silhouettes.
        /// </summary>
        private void EmitDebrisAtContactSurface(
            List<Bounds> cellBounds,
            float cutterY,
            float totalWidth,
            Color color,
            int totalCount)
        {
            if (totalCount <= 0 || totalWidth <= 0f || cellBounds == null)
                return;

            int emittedCount = 0;
            float remainingWidth = totalWidth;
            for (int index = 0; index < cellBounds.Count; index++)
            {
                Bounds bounds = cellBounds[index];
                if (bounds.min.y > cutterY + 0.08f || bounds.max.y < cutterY - 0.08f)
                    continue;

                float width = Mathf.Max(.05f, bounds.size.x);
                int remaining = totalCount - emittedCount;
                int count = remainingWidth <= width
                    ? remaining
                    : Mathf.Min(remaining, Mathf.RoundToInt(remaining * width / remainingWidth));
                if (count <= 0)
                {
                    remainingWidth -= width;
                    continue;
                }

                debrisParticleSystem.EmitAtCutter(
                    new Vector2(bounds.center.x, cutterY),
                    width,
                    color,
                    count);
                emittedCount += count;
                remainingWidth -= width;
            }
        }

        private void EmitEntryBurstAtContactSurface(
            List<Bounds> cellBounds,
            float cutterY,
            float totalWidth,
            Color color,
            int totalCount)
        {
            if (totalCount <= 0 || totalWidth <= 0f || cellBounds == null)
                return;

            int emittedCount = 0;
            float remainingWidth = totalWidth;
            for (int index = 0; index < cellBounds.Count; index++)
            {
                Bounds bounds = cellBounds[index];
                if (bounds.min.y > cutterY + 0.08f || bounds.max.y < cutterY - 0.08f)
                    continue;

                float width = Mathf.Max(.05f, bounds.size.x);
                int remaining = totalCount - emittedCount;
                int count = remainingWidth <= width
                    ? remaining
                    : Mathf.Min(remaining, Mathf.RoundToInt(remaining * width / remainingWidth));
                if (count > 0)
                {
                    debrisParticleSystem.EmitEntryBurstAtCutter(
                        new Vector2(bounds.center.x, cutterY), width, color, count);
                    emittedCount += count;
                }

                remainingWidth -= width;
            }
        }

        private void EmitFinalDebrisBurst(Vector2 contactCenter, float contactWidth, Color color)
        {
            if (debrisParticleSystem == null || shredderConfig == null ||
                !shredderConfig.EnableContactDebrisPresentation || contactWidth <= 0f)
                return;

            int count = Mathf.Min(
                shredderConfig.MaxDebrisPerFrame,
                Mathf.CeilToInt(contactWidth * shredderConfig.DebrisFinalBurstPerWorldUnit));
            debrisParticleSystem.EmitAtCutter(contactCenter, contactWidth, color, count);
        }

        private void PlayParticleShredSound()
        {
            if (particleShredClip == null || particleShredAudioSource == null ||
                Time.time < nextParticleShredSoundTime)
                return;

            particleShredAudioSource.PlayOneShot(particleShredClip);
            nextParticleShredSoundTime = Time.time + particleShredSoundMinInterval;
        }
    }
}
