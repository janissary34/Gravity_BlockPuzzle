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
            // Progress units describe board ownership, not the visual grain
            // density.  Use the prebuilt voxel presentation count so a single
            // logical board unit can still send a readable cluster of small
            // cubes along the slider path.
            int progressFlightCount = piece.ActiveVoxelPresentationCount *
                                      (shredderConfig != null ? shredderConfig.ProgressVoxelMultiplier : 1);
            float maxTime = 4.0f;
            float elapsed = 0f;
            float previousShakeOffsetX = 0f;
            float feedSpeed = shredderConfig != null ? shredderConfig.FeedSpeed : .7f;
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
            int remainingCellsToEmit = totalCollisionCells;
            float remainingProgressToEmit = totalProgress;
            int remainingFlightsToEmit = progressFlightCount;
            List<BoxCollider2D> releasedCellsBuffer = new List<BoxCollider2D>(Mathf.Max(4, totalCollisionCells));
            bool contactPresentationStarted = false;
            // The first visible cutter contact is the common source for both
            // the dense native debris field and the pooled HUD voxels.  This
            // prevents the progress material from appearing as a second,
            // unrelated burst after the piece has already vanished.
            bool progressBurstScheduled = false;
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

                // Sample the visual contact before removing collision cells.
                // This guarantees that the entry burst is emitted on the exact
                // frame the block reaches the shredder mouth, rather than one
                // frame later after its lower geometry has been released.
                bool hasContactSurface = TryGetCutterContactSurface(
                    pieceRenderers,
                    debrisContactY,
                    out float contactWidth,
                    out Vector2 currentContactCenter);
                if (hasContactSurface)
                {
                    lastContactWidth = contactWidth;
                    lastContactCenter = currentContactCenter;
                }

                // Release cells that have reached the cutter mouth. The threshold includes
                // the entry mouth margin so that on the exact frame the piece enters the shredder,
                // the entering cells emit voxels immediately rather than waiting for physics descent.
                releasedCellsBuffer.Clear();
                float cutterThresholdY = shredderY + (shredderConfig != null
                    ? shredderConfig.CaptureApproachDistance + 0.12f
                    : 0.16f);
                piece.ReleaseCollisionCellsAtOrBelow(cutterThresholdY, releasedCellsBuffer);

                // Emit flying progress voxels precisely from each individual cell
                // that crosses the cutter on this frame. For irregular shapes like
                // L-pieces, voxels only spawn where cells actually touch the cutter,
                // never from empty concavities. For tall pieces, voxels emit progressively
                // as each row enters the teeth.
                if (releasedCellsBuffer.Count > 0 && remainingProgressToEmit > .0001f)
                {
                    LevelProgressManager progressManager = LevelProgressManager.Instance;

                    if (releasedCellsBuffer.Count > 1)
                    {
                        releasedCellsBuffer.Sort(CompareCellsByCenterX);
                    }

                    int segmentStart = 0;
                    while (segmentStart < releasedCellsBuffer.Count)
                    {
                        BoxCollider2D firstCell = releasedCellsBuffer[segmentStart];
                        if (firstCell == null)
                        {
                            segmentStart++;
                            continue;
                        }

                        int segmentEnd = segmentStart;
                        float segMinX = firstCell.bounds.min.x;
                        float segMaxX = firstCell.bounds.max.x;

                        // Find all contiguous cells horizontally adjacent to this segment
                        while (segmentEnd + 1 < releasedCellsBuffer.Count)
                        {
                            BoxCollider2D nextCell = releasedCellsBuffer[segmentEnd + 1];
                            if (nextCell == null)
                                break;

                            if (nextCell.bounds.min.x - segMaxX <= 0.35f)
                            {
                                segmentEnd++;
                                segMaxX = Mathf.Max(segMaxX, nextCell.bounds.max.x);
                            }
                            else
                            {
                                break;
                            }
                        }

                        int cellsInSegment = segmentEnd - segmentStart + 1;
                        float segProgress = 0f;
                        int segFlights = 0;

                        for (int i = 0; i < cellsInSegment; i++)
                        {
                            float cellProgress = remainingCellsToEmit > 1
                                ? remainingProgressToEmit / remainingCellsToEmit
                                : remainingProgressToEmit;
                            int cellFlights = remainingCellsToEmit > 1
                                ? Mathf.Max(1, remainingFlightsToEmit / remainingCellsToEmit)
                                : remainingFlightsToEmit;

                            remainingProgressToEmit = Mathf.Max(0f, remainingProgressToEmit - cellProgress);
                            remainingFlightsToEmit = Mathf.Max(0, remainingFlightsToEmit - cellFlights);
                            remainingCellsToEmit = Mathf.Max(0, remainingCellsToEmit - 1);

                            segProgress += cellProgress;
                            segFlights += cellFlights;
                        }

                        float segWidth = Mathf.Max(0.95f * cellsInSegment, segMaxX - segMinX);
                        float segCenterX = (segMinX + segMaxX) * 0.5f;

                        if (progressManager != null && segProgress > .0001f)
                        {
                            progressManager.SpawnFlyingVoxelBurst(
                                new Vector3(segCenterX, debrisContactY, 0f),
                                tileColor,
                                segProgress,
                                segFlights,
                                segWidth);
                            progressBurstScheduled = true;
                        }

                        segmentStart = segmentEnd + 1;
                    }
                }
                else if (totalCollisionCells == 0 && hasContactSurface && !progressBurstScheduled && remainingProgressToEmit > .0001f)
                {
                    // Fallback for pieces without individual collision cells: emit on first contact.
                    LevelProgressManager progressManager = LevelProgressManager.Instance;
                    if (progressManager != null)
                    {
                        progressManager.SpawnFlyingVoxelBurst(
                            new Vector3(currentContactCenter.x, debrisContactY, 0f),
                            tileColor,
                            remainingProgressToEmit,
                            remainingFlightsToEmit,
                            contactWidth);
                        progressBurstScheduled = true;
                        remainingProgressToEmit = 0f;
                        remainingFlightsToEmit = 0;
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
                            pieceRenderers,
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
                            pieceRenderers,
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
                        progressManager.SpawnFlyingVoxelBurst(
                            new Vector3(lastContactCenter.x, debrisContactY, 0f),
                            tileColor,
                            remainingProgressToEmit,
                            Mathf.Max(1, remainingFlightsToEmit),
                            lastContactWidth);
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
            SpriteRenderer[] renderers,
            float cutterY,
            out float totalWidth,
            out Vector2 weightedCenter)
        {
            totalWidth = 0f;
            weightedCenter = new Vector2(0f, cutterY);
            for (int index = 0; index < renderers.Length; index++)
            {
                SpriteRenderer renderer = renderers[index];
                if (renderer == null || !renderer.enabled ||
                    renderer.gameObject.name.StartsWith("Selected Fill") ||
                    renderer.gameObject.name.StartsWith("White Selection"))
                    continue;

                Bounds bounds = renderer.bounds;
                if (bounds.min.y > cutterY || bounds.max.y <= cutterY)
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

        /// <summary>
        /// Emits independently from every renderer touching the cutter. This
        /// keeps voxel sources glued to the teeth/material intersections even
        /// for stepped, split, and irregular piece silhouettes.
        /// </summary>
        private void EmitDebrisAtContactSurface(
            SpriteRenderer[] renderers,
            float cutterY,
            float totalWidth,
            Color color,
            int totalCount)
        {
            if (totalCount <= 0 || totalWidth <= 0f)
                return;

            int emittedCount = 0;
            float remainingWidth = totalWidth;
            for (int index = 0; index < renderers.Length; index++)
            {
                SpriteRenderer renderer = renderers[index];
                if (renderer == null || !renderer.enabled ||
                    renderer.gameObject.name.StartsWith("Selected Fill") ||
                    renderer.gameObject.name.StartsWith("White Selection"))
                    continue;

                Bounds bounds = renderer.bounds;
                if (bounds.min.y > cutterY || bounds.max.y <= cutterY)
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
            SpriteRenderer[] renderers,
            float cutterY,
            float totalWidth,
            Color color,
            int totalCount)
        {
            if (totalCount <= 0 || totalWidth <= 0f)
                return;

            int emittedCount = 0;
            float remainingWidth = totalWidth;
            for (int index = 0; index < renderers.Length; index++)
            {
                SpriteRenderer renderer = renderers[index];
                if (renderer == null || !renderer.enabled ||
                    renderer.gameObject.name.StartsWith("Selected Fill") ||
                    renderer.gameObject.name.StartsWith("White Selection"))
                    continue;

                Bounds bounds = renderer.bounds;
                if (bounds.min.y > cutterY || bounds.max.y <= cutterY)
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
