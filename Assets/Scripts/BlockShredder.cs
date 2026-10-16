using System;
using System.Collections.Generic;
using GravityPuzzle.Config;
using GravityPuzzle.Gameplay.Pieces;
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
        private static readonly WaitForFixedUpdate FeedPhysicsStep = new WaitForFixedUpdate();

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
        // A feed reservation owns one horizontal material footprint in an
        // authored mouth. Multiple non-overlapping bodies may use the same
        // mouth, while material that could physically meet remains serial.
        // This is intentionally local to the existing shredder owner rather
        // than a second routing or column-management system.
        private readonly List<FeedReservation> activeFeedReservations =
            new List<FeedReservation>(4);
        private static readonly Comparison<BoxCollider2D> CompareCellsByCenterX = (a, b) =>
        {
            if (a == null || b == null) return 0;
            return a.bounds.center.x.CompareTo(b.bounds.center.x);
        };
        private float nextParticleShredSoundTime;
        private readonly List<ShredderPhysicsIsland> activePhysicsIslands =
            new List<ShredderPhysicsIsland>();
        private readonly List<PuzzlePiece> physicsIslandMemberBuffer =
            new List<PuzzlePiece>(8);
        // One pooled mouth is owned by each authored catch zone while its
        // support island or its final feed is still alive. A mouth must never
        // be silently reused at a different world-space cutter.
        private readonly Dictionary<ShredderCatchZone, ShredderPhysicsMouth> activePhysicsMouths =
            new Dictionary<ShredderCatchZone, ShredderPhysicsMouth>();
        private bool physicsIslandAvailable;

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
        private readonly List<Bounds> candidateFeedCellBoundsBuffer = new List<Bounds>(32);
        private readonly List<Bounds> reservedFeedCellBoundsBuffer = new List<Bounds>(32);
        private readonly List<Bounds> guideDepthCellBoundsBuffer = new List<Bounds>(32);

        private readonly struct FeedReservation
        {
            public readonly PuzzlePiece Piece;
            public readonly ShredderCatchZone Zone;

            public FeedReservation(PuzzlePiece piece, ShredderCatchZone zone)
            {
                Piece = piece;
                Zone = zone;
            }
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
            physicsIslandAvailable = shredderConfig != null &&
                                     shredderConfig.EnablePhysicsIsland &&
                                     ShredderPhysicsLayers.Configure();
            if (physicsIslandAvailable && shredderConfig.PhysicsMouthPrefab == null)
            {
                physicsIslandAvailable = false;
                Debug.LogWarning(
                    "[ShredderPhysicsIsland] Physics Mouth prefab is not assigned; using the legacy single-piece feed.",
                    this);
            }
            else if (physicsIslandAvailable)
            {
                ShredderPhysicsMouthPool.Configure(
                    shredderConfig.PhysicsMouthPrefab,
                    transform,
                    shredderConfig.PhysicsMouthPoolCapacity);
            }
            if (shredderConfig != null && shredderConfig.FeedMaskPrefab != null)
                ShredderFeedMaskPool.Configure(shredderConfig.FeedMaskPrefab, transform);
        }

        private void FixedUpdate()
        {
            for (int index = activePhysicsIslands.Count - 1; index >= 0; index--)
            {
                ShredderPhysicsIsland island = activePhysicsIslands[index];
                island.FixedTick();
                if (!island.IsComplete)
                    continue;

                activePhysicsIslands.RemoveAt(index);
                ReleasePhysicsMouthIfIdle(island.Zone);
            }
        }

        private void OnDisable()
        {
            if (particleShredAudioSource != null && particleShredAudioSource.isPlaying)
                particleShredAudioSource.Stop();
            activeShredSoundCount = 0;
        }

        private void OnDestroy()
        {
            if (particleShredAudioSource != null && particleShredAudioSource.isPlaying)
                particleShredAudioSource.Stop();
            activeShredSoundCount = 0;

            ReturnAllPhysicsMouths();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// Starts the coordinate-authorized handoff for a piece that has
        /// reached the shredder mouth itself.
        /// </summary>
        public bool TryCapturePiece(PuzzlePiece piece, ShredderCatchZone zone)
        {
            if (piece == null || zone == null ||
                !zone.ContainsCaptureFootprint(piece))
                return false;

            if (!TryAcquireFeedGate(piece, zone))
                return false;

            if (TryStartPhysicsIsland(piece, zone))
                return true;

            if (!piece.TryBeginShredderHandoff())
            {
                ReleaseFeedGate(piece, zone);
                return false;
            }

            // A combined member leaves its graph component only once the
            // shredder handoff has been accepted. Failed capture attempts
            // must leave the authoritative topology untouched.
            PrototypeBoard.Active?.BreakJointsForPiece(piece);
            StartCoroutine(FeedPieceIntoShredder(piece, zone));
            return true;
        }

        private bool TryStartPhysicsIsland(
            PuzzlePiece root,
            ShredderCatchZone zone)
        {
            if (!physicsIslandAvailable || root == null || zone == null || shredderConfig == null)
                return false;

            PrototypeBoard board = PrototypeBoard.Active;
            if (board == null ||
                !ShredderPhysicsIslandPlanner.TryBuild(
                    board,
                    root,
                    zone,
                    physicsIslandMemberBuffer) ||
                physicsIslandMemberBuffer.Count <= 1)
                return false;

            if (!EnsurePhysicsMouth(zone, physicsIslandMemberBuffer))
                return false;

            if (!board.TryReserveShredderPhysicsIsland(physicsIslandMemberBuffer))
                return false;

            for (int index = 0; index < physicsIslandMemberBuffer.Count; index++)
            {
                if (physicsIslandMemberBuffer[index].TryEnterShredderPhysicsIslandHandoff())
                    continue;

                // This should be unreachable after the board transaction above.
                // Do not launch a partial visual island if an invariant was broken.
                Debug.LogError(
                    "[ShredderPhysicsIsland] A reserved member rejected its handoff; the feed was not started.",
                    this);
                AbortPhysicsIslandStart(board, physicsIslandMemberBuffer);
                return false;
            }

            if (!root.TryBeginShredderFeedFromPhysicsIsland())
            {
                // The board reservation and per-member handoff were
                // prevalidated above, so this represents a broken lifecycle
                // invariant rather than a condition that can safely fall back.
                Debug.LogError(
                    "[ShredderPhysicsIsland] Reserved root rejected its feed promotion; the reservation was rolled back.",
                    this);
                AbortPhysicsIslandStart(board, physicsIslandMemberBuffer);
                return false;
            }

            // The root is now committed to the feed. Split it from its
            // combined graph before the presentation-only island continues.
            board.BreakJointsForPiece(root);
            StartCoroutine(FeedPieceIntoShredder(root, zone));
            activePhysicsIslands.Add(new ShredderPhysicsIsland(
                this,
                shredderConfig,
                zone,
                physicsIslandMemberBuffer));
            return true;
        }

        private bool EnsurePhysicsMouth(
            ShredderCatchZone zone,
            IReadOnlyList<PuzzlePiece> members)
        {
            if (zone == null || members == null)
                return false;

            float requiredGuideDepth = CalculateRequiredGuideDepth(zone, members);
            if (activePhysicsMouths.TryGetValue(zone, out ShredderPhysicsMouth activeMouth))
            {
                // A second independent island can be taller than the first.
                // Growing only the upward guide extent preserves the current
                // cutter/shelf plane and avoids exposing its upper members.
                if (requiredGuideDepth <= activeMouth.GuideDepth)
                    return true;

                if (activeMouth.Configure(
                        zone.LeftX,
                        zone.RightX,
                        zone.ShredY,
                        shredderConfig,
                        requiredGuideDepth))
                {
                    return true;
                }

                Debug.LogError(
                    "[ShredderPhysicsIsland] The active physics mouth could not grow for a concurrent support island.",
                    this);
                return false;
            }

            if (!ShredderPhysicsMouthPool.TryRent(out ShredderPhysicsMouth mouth))
            {
                Debug.LogWarning(
                    "[ShredderPhysicsIsland] Physics-mouth pool is unavailable; using the legacy single-piece feed.",
                    this);
                return false;
            }

            if (!mouth.Configure(
                    zone.LeftX,
                    zone.RightX,
                    zone.ShredY,
                    shredderConfig,
                    requiredGuideDepth))
            {
                ShredderPhysicsMouthPool.Return(mouth);
                Debug.LogError(
                    "[ShredderPhysicsIsland] Physics-mouth prefab is missing required guides or its throat shelf; using the legacy single-piece feed.",
                    this);
                return false;
            }

            activePhysicsMouths.Add(zone, mouth);
            return true;
        }

        private float CalculateRequiredGuideDepth(
            ShredderCatchZone zone,
            IReadOnlyList<PuzzlePiece> members)
        {
            float highestPoint = zone.ShredY;
            for (int index = 0; index < members.Count; index++)
            {
                PuzzlePiece member = members[index];
                if (member == null)
                    continue;

                member.GetActiveCellBounds(guideDepthCellBoundsBuffer);
                for (int cellIndex = 0; cellIndex < guideDepthCellBoundsBuffer.Count; cellIndex++)
                    highestPoint = Mathf.Max(highestPoint, guideDepthCellBoundsBuffer[cellIndex].max.y);
            }

            // Include one guide thickness above the top-most initial body so
            // a tall contact chain cannot rotate around an uncovered edge.
            return Mathf.Max(0f, highestPoint - zone.ShredY) +
                   shredderConfig.PhysicsIslandGuideThickness;
        }

        private bool HasActivePhysicsIslandForZone(ShredderCatchZone zone)
        {
            for (int index = 0; index < activePhysicsIslands.Count; index++)
            {
                if (activePhysicsIslands[index].Zone == zone)
                    return true;
            }

            return false;
        }

        private void ReleasePhysicsMouthIfIdle(ShredderCatchZone zone)
        {
            if (zone == null || HasActiveFeedReservationForZone(zone) ||
                HasActivePhysicsIslandForZone(zone) ||
                !activePhysicsMouths.TryGetValue(zone, out ShredderPhysicsMouth mouth))
            {
                return;
            }

            activePhysicsMouths.Remove(zone);
            ShredderPhysicsMouthPool.Return(mouth);
        }

        private void ReturnAllPhysicsMouths()
        {
            foreach (KeyValuePair<ShredderCatchZone, ShredderPhysicsMouth> pair in activePhysicsMouths)
            {
                if (pair.Value != null)
                    ShredderPhysicsMouthPool.Return(pair.Value);
            }

            activePhysicsMouths.Clear();
        }

        private void AbortPhysicsIslandStart(
            PrototypeBoard board,
            IReadOnlyList<PuzzlePiece> members)
        {
            if (board == null || !board.TryAbortShredderPhysicsIsland(members))
            {
                Debug.LogError(
                    "[ShredderPhysicsIsland] Could not roll back a failed handoff reservation.",
                    this);
                return;
            }

            for (int index = 0; index < members.Count; index++)
            {
                if (members[index] != null)
                    members[index].CancelShredderPhysicsIslandHandoff();
            }
        }

        /// <summary>
        /// The physics island reaches the cutter through a coordinate capture
        /// plane, not a trigger. From this point the established feed owns the
        /// piece as a kinematic motor and presentation pipeline.
        /// </summary>
        internal bool TryCapturePhysicsIslandMember(
            PuzzlePiece piece,
            ShredderCatchZone zone)
        {
            if (piece == null || zone == null ||
                !piece.IsInShredderPhysicsHandoff ||
                !zone.ContainsPhysicsIslandCaptureFootprint(
                    piece,
                    shredderConfig != null ? shredderConfig.PhysicsIslandCaptureDistance : 0f))
                return false;

            PrototypeBoard board = PrototypeBoard.Active;
            if (board == null || !board.TryGetPieceModel(piece, out PieceModel model) ||
                model.State != PieceState.HandoffToPhysics ||
                !TryAcquireFeedGate(piece, zone))
                return false;

            if (!piece.TryBeginShredderFeedFromPhysicsIsland())
            {
                ReleaseFeedGate(piece, zone);
                return false;
            }

            board.BreakJointsForPiece(piece);
            StartCoroutine(FeedPieceIntoShredder(piece, zone));
            return true;
        }

        private bool TryAcquireFeedGate(PuzzlePiece piece, ShredderCatchZone zone)
        {
            if (piece == null || zone == null)
                return false;

            piece.GetActiveCellBounds(candidateFeedCellBoundsBuffer);
            if (candidateFeedCellBoundsBuffer.Count == 0)
                return false;

            float candidateRotationAllowance = GetFeedRotationAllowance(
                piece,
                candidateFeedCellBoundsBuffer);
            for (int index = 0; index < activeFeedReservations.Count; index++)
            {
                FeedReservation reservation = activeFeedReservations[index];
                if (reservation.Zone != zone || reservation.Piece == null)
                    continue;

                reservation.Piece.GetActiveCellBounds(reservedFeedCellBoundsBuffer);
                if (reservedFeedCellBoundsBuffer.Count == 0)
                    return false;

                float reservationRotationAllowance = GetFeedRotationAllowance(
                    reservation.Piece,
                    reservedFeedCellBoundsBuffer);
                if (HaveOverlappingHorizontalFootprints(
                        candidateFeedCellBoundsBuffer,
                        candidateRotationAllowance,
                        reservedFeedCellBoundsBuffer,
                        reservationRotationAllowance))
                {
                    return false;
                }
            }

            activeFeedReservations.Add(new FeedReservation(piece, zone));
            return true;
        }

        private void ReleaseFeedGate(PuzzlePiece piece, ShredderCatchZone zone)
        {
            if (zone == null)
                return;

            for (int index = activeFeedReservations.Count - 1; index >= 0; index--)
            {
                FeedReservation reservation = activeFeedReservations[index];
                if (reservation.Zone == zone && ReferenceEquals(reservation.Piece, piece))
                {
                    activeFeedReservations.RemoveAt(index);
                    break;
                }
            }

            ReleasePhysicsMouthIfIdle(zone);
        }

        private bool HasActiveFeedReservationForZone(ShredderCatchZone zone)
        {
            for (int index = 0; index < activeFeedReservations.Count; index++)
            {
                if (activeFeedReservations[index].Zone == zone)
                    return true;
            }

            return false;
        }

        private float GetFeedRotationAllowance(
            PuzzlePiece piece,
            List<Bounds> cellBounds)
        {
            if (piece == null || cellBounds == null || cellBounds.Count == 0 ||
                shredderConfig == null)
            {
                return 0f;
            }

            Rigidbody2D body = piece.Body;
            Vector2 pivot = body != null ? body.position : piece.transform.position;
            float currentAngle = body != null ? body.rotation : piece.transform.eulerAngles.z;
            float maxRotationDelta = Mathf.Abs(currentAngle) + shredderConfig.MaxFeedTiltAngle;
            float sine = Mathf.Sin(Mathf.Deg2Rad * Mathf.Min(90f, maxRotationDelta));
            if (sine <= 0f)
                return 0f;

            float allowance = 0f;
            for (int index = 0; index < cellBounds.Count; index++)
            {
                Bounds bounds = cellBounds[index];
                float halfDiagonal = new Vector2(
                    bounds.extents.x,
                    bounds.extents.y).magnitude;
                Vector2 center = new Vector2(bounds.center.x, bounds.center.y);
                float radius = Vector2.Distance(center, pivot) + halfDiagonal;
                allowance = Mathf.Max(allowance, radius * sine);
            }

            return allowance;
        }

        private static bool HaveOverlappingHorizontalFootprints(
            List<Bounds> first,
            float firstAllowance,
            List<Bounds> second,
            float secondAllowance)
        {
            const float TouchingEpsilon = .0001f;
            for (int firstIndex = 0; firstIndex < first.Count; firstIndex++)
            {
                Bounds firstBounds = first[firstIndex];
                float firstMin = firstBounds.min.x - firstAllowance;
                float firstMax = firstBounds.max.x + firstAllowance;
                for (int secondIndex = 0; secondIndex < second.Count; secondIndex++)
                {
                    Bounds secondBounds = second[secondIndex];
                    float secondMin = secondBounds.min.x - secondAllowance;
                    float secondMax = secondBounds.max.x + secondAllowance;
                    if (firstMax > secondMin + TouchingEpsilon &&
                        secondMax > firstMin + TouchingEpsilon)
                    {
                        return true;
                    }
                }
            }

            return false;
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
            ShredderCatchZone zone)
        {
            if (piece == null || zone == null)
            {
                ReleaseFeedGate(piece, zone);
                yield break;
            }

            float shredderY = zone.ShredY;

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
            piece.SetShredderPresentationDepth(0);
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

            float feedSpeed = shredderConfig != null ? shredderConfig.FeedSpeed : 3.8f;
            float maxTime = 6.0f;
            float elapsed = 0f;
            float previousShakeOffsetX = 0f;
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
            bool isShreddingSoundActive = false;

            try
            {
                // 3. The kinematic feed owns the descent while this coroutine watches
                // the crossing cells and converts them to shred effects.
                while (piece != null && elapsed < maxTime)
            {
                yield return FeedPhysicsStep;
                float feedDeltaTime = Time.fixedDeltaTime;
                elapsed += feedDeltaTime;

                if (rb != null)
                {
                    // Apply continuous high-frequency horizontal tremor
                    float shakeOffsetX = Mathf.Sin(Time.time * tremorFrequency) * tremorIntensity * shakeAmplitude;
                    // The feed is kinematic, so commit its position here rather
                    // than waiting for a later physics step to integrate a
                    // velocity.  The grid release immediately below must see
                    // the same cutter crossing as the presentation; otherwise
                    // pieces above keep treating already-shredded cells as solid
                    // until the whole root is returned to its pool.
                    rb.MovePosition(rb.position + new Vector2(
                        shakeOffsetX - previousShakeOffsetX,
                        -feedSpeed * feedDeltaTime));
                    previousShakeOffsetX = shakeOffsetX;

                    // Kinematic bodies do not integrate torque. Advance the
                    // authored feed angular velocity explicitly so a dynamic
                    // support above reacts to the same tilted collider the
                    // player sees.
                    float currentAngle = Mathf.DeltaAngle(0f, rb.rotation);
                    float nextAngle = currentAngle + rb.angularVelocity * feedDeltaTime;
                    if (Mathf.Abs(nextAngle) > maxTiltAngle)
                    {
                        nextAngle = Mathf.Clamp(nextAngle, -maxTiltAngle, maxTiltAngle);
                        rb.angularVelocity = -rb.angularVelocity * 0.5f;
                    }
                    rb.MoveRotation(nextAngle);
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

                    if (!isShreddingSoundActive)
                    {
                        isShreddingSoundActive = true;
                        StartContinuousShredSound();
                    }
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
                    float dy = feedSpeed * feedDeltaTime;
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

                    debrisAccumulator += feedDeltaTime *
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
                    if (isShreddingSoundActive)
                    {
                        isShreddingSoundActive = false;
                        StopContinuousShredSound();
                    }
                    break;
                }
            }
            }
            finally
            {
                if (isShreddingSoundActive)
                {
                    isShreddingSoundActive = false;
                    StopContinuousShredSound();
                }
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
            ReleaseFeedGate(piece, zone);
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

        private int activeShredSoundCount;

        private void StartContinuousShredSound()
        {
            activeShredSoundCount++;
            if (particleShredAudioSource != null && particleShredClip != null)
            {
                particleShredAudioSource.clip = particleShredClip;
                particleShredAudioSource.loop = true;
                if (!particleShredAudioSource.isPlaying)
                {
                    particleShredAudioSource.Play();
                }
            }
        }

        private void StopContinuousShredSound()
        {
            activeShredSoundCount = Mathf.Max(0, activeShredSoundCount - 1);
            if (activeShredSoundCount == 0 && particleShredAudioSource != null && particleShredAudioSource.isPlaying)
            {
                particleShredAudioSource.Stop();
            }
        }
    }
}
