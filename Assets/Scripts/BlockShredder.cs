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
            bool contactPresentationStarted = false;

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
                bool hasContactSpan = TryGetCutterContactSpan(
                    pieceRenderers,
                    debrisContactY,
                    out float contactMinX,
                    out float contactMaxX);
                float contactWidth = hasContactSpan
                    ? Mathf.Max(.05f, contactMaxX - contactMinX)
                    : 0f;

                // Release only the cells that have now crossed the cutter.
                // This transaction also wakes grid gravity, allowing an upper
                // piece to follow the shrinking shredder footprint in the same
                // feed instead of waiting for this complete piece to despawn.
                piece.ReleaseCollisionCellsAtOrBelow(shredderY);

                // This is deliberately time-based rather than a final burst.
                // Do not begin until visible material reaches the hidden cutter
                // edge; before that moment no crumbs should appear at the mouth.
                if (hasContactSpan && debrisParticleSystem != null && shredderConfig != null &&
                    shredderConfig.EnableContactDebrisPresentation)
                {
                    Vector2 contactCenter = new Vector2(
                        (contactMinX + contactMaxX) * .5f,
                        debrisContactY);
                    if (!contactPresentationStarted)
                    {
                        int entryBurstCount = Mathf.Min(
                            shredderConfig.MaxDebrisPerFrame,
                            Mathf.CeilToInt(contactWidth * shredderConfig.DebrisEntryBurstPerWorldUnit));
                        debrisParticleSystem.EmitAtCutter(
                            contactCenter,
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
                        debrisParticleSystem.EmitAtCutter(
                            contactCenter,
                            contactWidth,
                            tileColor,
                            debrisCount);
                    }
                }

                if (hasContactSpan)
                    contactPresentationStarted = true;

                // A) Shred voxel shards crossing the cutter line. Their visual
                // material conversion is handled by the local cutter particles;
                // progress remains a separate, single HUD acknowledgement after
                // the feed completes instead of launching from the mouth.
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
                    break;
                }

                yield return null;
            }

            if (piece != null)
            {
                // World-space debris is the only shred visual. Progress is
                // committed after the material has fully converted so no HUD
                // voxel can be mistaken for a crumb escaping the cutter.
                if (totalProgress > 0.0001f)
                {
                    LevelProgressManager progressManager = LevelProgressManager.Instance;
                    if (progressManager != null)
                    {
                        progressManager.AddProgress(totalProgress);
                    }
                }

                piece.ReleaseInstance();
            }

            ReleaseFeedMask();
            ReleaseFeedLane(shredderY);
        }

        private static Color Opaque(Color color) => new Color(color.r, color.g, color.b, 1f);

        private static bool TryGetCutterContactSpan(
            SpriteRenderer[] renderers,
            float cutterY,
            out float minX,
            out float maxX)
        {
            minX = float.PositiveInfinity;
            maxX = float.NegativeInfinity;
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

                minX = Mathf.Min(minX, bounds.min.x);
                maxX = Mathf.Max(maxX, bounds.max.x);
            }

            return minX <= maxX;
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
