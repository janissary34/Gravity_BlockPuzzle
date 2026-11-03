using System;
using System.Collections.Generic;
using GravityPuzzle.Config;
using GravityPuzzle.Gameplay.Pieces;
using GravityPuzzle.Infrastructure.Services;
using GravityPuzzle.Presentation.VFX;
using GravityPuzzle.Presentation.Views;
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
        private float nextParticleShredSoundTime;
        private readonly List<ShredderPhysicsIsland> activePhysicsIslands =
            new List<ShredderPhysicsIsland>();
        private readonly List<ExitHandoff> activeFreefallPieces = new List<ExitHandoff>();
        private readonly List<PhysicsVisualFeed> activePhysicsVisualFeeds =
            new List<PhysicsVisualFeed>();

        private sealed class ExitHandoff
        {
            public PuzzlePiece Piece;
            public ShredderCatchZone Zone;
        }

        /// <summary>
        /// Per-piece presentation state observed from BlockShredder's single
        /// FixedUpdate. It deliberately owns no physical state; Rigidbody2D
        /// remains the only movement owner below the board.
        /// </summary>
        private sealed class PhysicsVisualFeed
        {
            public PuzzlePiece Piece;
            public int PieceId;
            public float ClippingSeamY;
            public Color TileColor;
            public SpriteRenderer[] Renderers;
            public readonly List<VoxelShard> Shards = new List<VoxelShard>();
            public readonly HashSet<VoxelShard> ProcessedShards = new HashSet<VoxelShard>();
            public readonly HashSet<Transform> ShardTransforms = new HashSet<Transform>();
            public readonly List<Bounds> CellBounds = new List<Bounds>(32);
            public float TotalRendererArea;
            public float RemainingProgress;
            public int RemainingFlights;
            public float ProgressVoxelAccumulator;
            public float DebrisAccumulator;
            public float Elapsed;
            public bool ContactPresentationStarted;
            public bool IsShreddingSoundActive;
            public bool HapticSessionActive;
            public Vector2 LastContactCenter;
            public float LastContactWidth;
            public ShredderContactGlowView Glow;
        }
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
        private readonly List<Bounds> guideDepthCellBoundsBuffer = new List<Bounds>(32);

#if UNITY_EDITOR
        private readonly Dictionary<int, string> admissionDiagnosticStates =
            new Dictionary<int, string>();
#endif

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
            // OpenFreefall always needs its isolated collision layer. The
            // optional support island must not decide whether map-exit
            // freefall receives that layer.
            bool shredderPhysicsLayersAvailable = shredderConfig != null &&
                                                  ShredderPhysicsLayers.Configure();
            physicsIslandAvailable = shredderConfig != null &&
                                     shredderConfig.EnablePhysicsIsland &&
                                     shredderPhysicsLayersAvailable;
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
            if (shredderConfig != null && shredderConfig.ContactGlowPrefab != null)
                ShredderContactGlowPool.Configure(
                    shredderConfig.ContactGlowPrefab,
                    transform,
                    shredderConfig.ContactGlowPoolCapacity);
        }

        private void FixedUpdate()
        {
            TickFreefallPieces();
            TickPhysicsVisualFeeds();
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

        private void TickFreefallPieces()
        {
            for (int index = activeFreefallPieces.Count - 1; index >= 0; index--)
            {
                ExitHandoff freefall = activeFreefallPieces[index];
                if (freefall.Piece == null || !freefall.Piece.IsInOpenFreefall)
                {
                    activeFreefallPieces.RemoveAt(index);
                    continue;
                }

                freefall.Piece.ClampOffBoardAngularVelocity(shredderConfig);

                if (TryCaptureFreefallPieceAtCutter(freefall.Piece, freefall.Zone))
                    activeFreefallPieces.RemoveAt(index);
            }
        }

        /// <summary>
        /// The map-exit freefall route never joins the legacy island queue.
        /// It remains Dynamic until the lowest occupied cell crosses the same
        /// seam hidden by the teeth, then starts only the established shred
        /// presentation.  Grid ownership was released at the handoff line.
        /// </summary>
        private bool TryCaptureFreefallPieceAtCutter(
            PuzzlePiece piece,
            ShredderCatchZone zone)
        {
            if (piece == null || zone == null || shredderConfig == null ||
                !zone.ContainsFreefallCutterFootprint(piece, shredderConfig.GearClippingRatio))
            {
                return false;
            }

            if (!piece.TryBeginOffBoardFreefallShredding(shredderConfig))
            {
                Debug.LogError(
                    "[ShredderFreefall] Cutter capture rejected an off-board handoff piece.",
                    piece);
                return false;
            }

            PrototypeBoard board = PrototypeBoard.Active;
            board?.JointSpringPresenter?.PrepareForShredder(piece);
            board?.BreakJointsForPiece(piece);
            // Preserve the solved Dynamic pose. The feed coroutine below owns
            // only visual consumption; Rigidbody2D remains the movement owner.
            BeginPhysicsVisualFeed(piece, zone);
            return true;
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
            if (piece == null || piece.IsFrozen || zone == null || shredderConfig == null ||
                !zone.ContainsMapExitFootprint(piece))
                return false;

            if (!piece.TryBeginShredderExitHandoff())
                return false;

            PrototypeBoard board = PrototypeBoard.Active;
            if (board == null || !board.TryEnterOpenFreefallAtMapExit(piece))
            {
                Debug.LogError(
                    "[ShredderFreefall] Accepted map exit could not enter isolated freefall.",
                    piece);
                return false;
            }

            // There is deliberately no transform-driven approach phase here.
            // The exact grid-exit pose becomes the initial Dynamic pose and
            // Rigidbody2D is the sole movement owner until the cutter seam.
            Physics2D.SyncTransforms();
            // Carry the last grid fall's authored vertical tempo into the
            // Dynamic phase unchanged. Configuration supplies a fallback only
            // when this route has no measured downward presentation speed.
            float presentationDownwardSpeed = piece.GridFallView != null
                ? piece.GridFallView.LastDownwardPresentationSpeed
                : 0f;
            piece.ApplyLifecyclePhysicsProfile(
                PieceState.OpenFreefall,
                shredderConfig,
                presentationDownwardSpeed);
            activeFreefallPieces.Add(new ExitHandoff { Piece = piece, Zone = zone });
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
            board.JointSpringPresenter?.PrepareForShredder(root);
            board.BreakJointsForPiece(root);
            BeginPhysicsVisualFeed(root, zone);
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
            if (zone == null || HasActivePhysicsIslandForZone(zone) ||
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
                model.State != PieceState.HandoffToPhysics)
                return false;

            if (!piece.TryBeginShredderFeedFromPhysicsIsland())
                return false;

            board.JointSpringPresenter?.PrepareForShredder(piece);
            board.BreakJointsForPiece(piece);
            BeginPhysicsVisualFeed(piece, zone);
            return true;
        }

        /// <summary>
        /// Starts the visual cutter-consumption pass for a support member
        /// whose full vertical route was validated before island reservation.
        /// It does not own descent; the live Rigidbody2D continues through
        /// the cutter and visuals consume only crossed material.
        /// </summary>
        internal bool TryBeginPhysicsIslandApproachFeed(
            PuzzlePiece piece,
            ShredderCatchZone zone)
        {
            if (piece == null || zone == null || !piece.IsInShredderPhysicsHandoff)
                return false;

            PrototypeBoard board = PrototypeBoard.Active;
            if (board == null || !board.TryGetPieceModel(piece, out PieceModel model) ||
                model.State != PieceState.HandoffToPhysics ||
                !piece.TryBeginShredderFeedFromPhysicsIsland())
            {
                return false;
            }

            board.JointSpringPresenter?.PrepareForShredder(piece);
            board.BreakJointsForPiece(piece);
            BeginPhysicsVisualFeed(piece, zone);
            return true;
        }

#if UNITY_EDITOR
        internal void DebugLogAdmission(
            PuzzlePiece piece, ShredderCatchZone zone, string stage, string reason, PuzzlePiece blocker)
        {
            if (piece == null || zone == null)
                return;

            int key = piece.GetInstanceID();
            string state = stage + ":" + reason + ":" + (blocker != null ? blocker.GetInstanceID() : 0) +
                           ":" + zone.GetInstanceID();
            if (admissionDiagnosticStates.TryGetValue(key, out string previous) && previous == state)
                return;
            admissionDiagnosticStates[key] = state;

            string candidate = DebugDescribePiece(piece);
            string other = blocker != null ? DebugDescribePiece(blocker) : "none";
            Debug.Log($"[ShredAdmission] frame={Time.frameCount} stage={stage} reason={reason} " +
                      $"zone={zone.GetInstanceID()} candidate={candidate} blocker={other}", piece);
        }

        private string DebugDescribePiece(PuzzlePiece piece)
        {
            PrototypeBoard board = PrototypeBoard.Active;
            string modelInfo = "model=unavailable";
            if (board != null && board.TryGetPieceModel(piece, out PieceModel model))
            {
                var columns = new System.Text.StringBuilder();
                for (int i = 0; i < model.LocalCells.Count; i++)
                {
                    if (i > 0) columns.Append(',');
                    columns.Append(model.GetWorldCell(i).X);
                }
                modelInfo = $"state={model.State} anchor=({model.Anchor.X},{model.Anchor.Y}) gridX=[{columns}]";
            }

            var cells = new List<Bounds>();
            piece.GetActiveCellBounds(cells);
            var intervals = new System.Text.StringBuilder();
            for (int i = 0; i < cells.Count; i++)
            {
                if (i > 0) intervals.Append(',');
                intervals.AppendFormat("{0:F3}..{1:F3}", cells[i].min.x, cells[i].max.x);
            }
            float angle = piece.Body != null ? piece.Body.rotation : piece.transform.eulerAngles.z;
            bool falling = piece.GridFallView != null && piece.GridFallView.IsAnimating;
            return $"'{piece.name}' id={piece.SourcePieceId} {modelInfo} " +
                   $"fallTween={falling} angle={angle:F3} cellX=[{intervals}]";
        }
#endif

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

        /// <summary>
        /// Starts a visual cutter observer for a body that has already entered
        /// the Shredding lifecycle phase. This is intentionally not a
        /// coroutine: all active feeds are observed by one FixedUpdate owner.
        /// </summary>
        private void BeginPhysicsVisualFeed(PuzzlePiece piece, ShredderCatchZone zone)
        {
            if (piece == null || zone == null || shredderConfig == null)
                return;

            for (int index = 0; index < activePhysicsVisualFeeds.Count; index++)
            {
                if (activePhysicsVisualFeeds[index].Piece == piece)
                    return;
            }

            float wheelRadius = zone.WheelRadius > 0f ? zone.WheelRadius : 0.4f;
            float clippingSeamY = zone.ShredY - wheelRadius * shredderConfig.GearClippingRatio;
            AcquireFeedMask(clippingSeamY);

            // The lifecycle profile is idempotent. It opens the Feed layer and
            // colliders but deliberately preserves the current body velocity.
            piece.SetSelected(false);
            piece.ApplyLifecyclePhysicsProfile(PieceState.Shredding, shredderConfig);
            piece.PrepareModularShredderSurface();

            PhysicsVisualFeed feed = new PhysicsVisualFeed
            {
                Piece = piece,
                PieceId = piece.SourcePieceId,
                ClippingSeamY = clippingSeamY,
                Renderers = piece.ConfiguredShredderRenderers ?? EmptyRenderers,
                TileColor = Opaque(piece.VisualColor),
                RemainingProgress = Mathf.Max(0f, piece.RemainingProgressUnits)
            };
            piece.BeginShredderPresentation(feed.Renderers, clippingSeamY, shredderConfig);
            piece.ApplyShredderPresentationClipping();
            piece.SetShredderPresentationDepth(0);

            IReadOnlyList<VoxelShard> configuredShards = piece.ConfiguredVoxelShards;
            for (int index = 0; index < configuredShards.Count; index++)
            {
                VoxelShard shard = configuredShards[index];
                if (shard == null)
                    continue;

                feed.Shards.Add(shard);
                feed.ShardTransforms.Add(shard.transform);
            }

            piece.GetActiveCellBounds(feed.CellBounds);
            int blockCount = Mathf.Max(1, Mathf.Max(piece.ProgressUnits, feed.CellBounds.Count));
            feed.TotalRendererArea = Mathf.Max(.5f, blockCount);
            feed.RemainingFlights = shredderConfig.GetProgressVoxelFlightCount(blockCount);
            ShredderContactGlowPool.TryRent(out feed.Glow);
            activePhysicsVisualFeeds.Add(feed);
        }

        private void TickPhysicsVisualFeeds()
        {
            for (int index = activePhysicsVisualFeeds.Count - 1; index >= 0; index--)
            {
                PhysicsVisualFeed feed = activePhysicsVisualFeeds[index];
                if (feed.Piece == null || !feed.Piece.IsBeingShredded)
                {
                    FinishPhysicsVisualFeed(feed, false);
                    activePhysicsVisualFeeds.RemoveAt(index);
                    continue;
                }

                feed.Elapsed += Time.fixedDeltaTime;
                feed.Piece.ClampOffBoardAngularVelocity(shredderConfig);
                PrototypeBoard.Active?.JointSpringPresenter?.UpdateShredderSprings(
                    feed.Piece,
                    feed.ClippingSeamY,
                    shredderConfig);
                feed.Piece.GetActiveCellBounds(feed.CellBounds);

                bool hasContact = TryGetCutterContactSurface(
                    feed.CellBounds,
                    feed.ClippingSeamY,
                    out float contactWidth,
                    out Vector2 contactCenter);
                if (hasContact)
                    UpdatePhysicsVisualFeedContact(feed, contactWidth, contactCenter);
                else if (feed.Glow != null)
                    feed.Glow.Hide();

                bool complete = ConsumeCrossedFeedVisuals(feed);
                if (complete || feed.Elapsed >= shredderConfig.PhysicsFeedMaximumDuration)
                {
                    FinishPhysicsVisualFeed(feed, complete);
                    activePhysicsVisualFeeds.RemoveAt(index);
                }
            }
        }

        private void UpdatePhysicsVisualFeedContact(
            PhysicsVisualFeed feed,
            float contactWidth,
            Vector2 contactCenter)
        {
            feed.LastContactCenter = contactCenter;
            feed.LastContactWidth = contactWidth;
            if (!feed.IsShreddingSoundActive)
            {
                feed.IsShreddingSoundActive = true;
                StartContinuousShredSound();
            }

            if (!feed.HapticSessionActive)
            {
                feed.HapticSessionActive = true;
                HapticFeedbackRuntime.BeginShredder(feed.PieceId);
            }

            float physicalDownwardSpeed = feed.Piece.Body != null
                ? Mathf.Max(0f, -feed.Piece.Body.velocity.y)
                : 0f;
            float travelledArea = contactWidth * physicalDownwardSpeed * Time.fixedDeltaTime;
            feed.ProgressVoxelAccumulator += feed.RemainingFlights *
                                             (travelledArea / feed.TotalRendererArea);
            int flights = Mathf.Min(
                feed.RemainingFlights,
                Mathf.FloorToInt(feed.ProgressVoxelAccumulator));
            if (flights > 0)
            {
                float progress = feed.RemainingFlights > 0
                    ? Mathf.Min(feed.RemainingProgress, feed.RemainingProgress * flights / feed.RemainingFlights)
                    : 0f;
                feed.ProgressVoxelAccumulator -= flights;
                feed.RemainingFlights -= flights;
                feed.RemainingProgress = Mathf.Max(0f, feed.RemainingProgress - progress);
                LevelProgressManager progressManager = LevelProgressManager.Instance;
                if (progressManager != null && progress > 0.00001f)
                {
                    progressManager.SpawnFlyingVoxelBurst(
                        new Vector3(contactCenter.x, feed.ClippingSeamY, 0f),
                        feed.TileColor,
                        progress,
                        flights,
                        contactWidth);
                }
            }

            if (feed.Glow != null)
                feed.Glow.UpdateGlow(contactCenter, contactWidth, feed.TileColor, shredderConfig.GlowIntensity);

            if (debrisParticleSystem != null && shredderConfig.EnableContactDebrisPresentation)
            {
                if (!feed.ContactPresentationStarted)
                {
                    int entryCount = Mathf.Min(
                        shredderConfig.MaxDebrisEntryBurstPerFrame,
                        Mathf.CeilToInt(contactWidth * shredderConfig.DebrisEntryBurstPerWorldUnit));
                    EmitEntryBurstAtContactSurface(
                        feed.CellBounds,
                        feed.ClippingSeamY,
                        contactWidth,
                        feed.TileColor,
                        entryCount);
                }

                feed.DebrisAccumulator += Time.fixedDeltaTime *
                                          shredderConfig.DebrisPerSecondPerWorldUnit * contactWidth;
                int count = Mathf.Min(shredderConfig.MaxDebrisPerFrame, Mathf.FloorToInt(feed.DebrisAccumulator));
                if (count > 0)
                {
                    feed.DebrisAccumulator -= count;
                    EmitDebrisAtContactSurface(
                        feed.CellBounds,
                        feed.ClippingSeamY,
                        contactWidth,
                        feed.TileColor,
                        count);
                }
            }

            feed.ContactPresentationStarted = true;
            HapticFeedbackRuntime.UpdateShredderProgress(
                feed.PieceId,
                feed.RemainingProgress > 0f
                    ? 1f - Mathf.Clamp01(feed.RemainingProgress / Mathf.Max(.0001f, feed.Piece.RemainingProgressUnits))
                    : 1f);
        }

        private bool ConsumeCrossedFeedVisuals(PhysicsVisualFeed feed)
        {
            for (int index = 0; index < feed.Shards.Count; index++)
            {
                VoxelShard shard = feed.Shards[index];
                if (shard != null && !feed.ProcessedShards.Contains(shard) &&
                    shard.transform.position.y <= feed.ClippingSeamY)
                {
                    feed.ProcessedShards.Add(shard);
                    shard.Recycle();
                }
            }

            int activeRendererCount = 0;
            for (int index = 0; index < feed.Renderers.Length; index++)
            {
                SpriteRenderer renderer = feed.Renderers[index];
                if (renderer == null || !renderer.enabled ||
                    renderer.gameObject.name.StartsWith("Selected Fill") ||
                    renderer.gameObject.name.StartsWith("White Selection") ||
                    feed.ShardTransforms.Contains(renderer.transform))
                {
                    continue;
                }

                if (renderer.bounds.max.y <= feed.ClippingSeamY)
                {
                    feed.Piece.HideShredderRenderer(renderer);
                    if (debrisParticleSystem != null && shredderConfig.EnableContactDebrisPresentation)
                    {
                        debrisParticleSystem.EmitAtCutter(
                            new Vector2(renderer.transform.position.x, feed.ClippingSeamY),
                            Mathf.Max(.05f, renderer.bounds.size.x),
                            feed.TileColor,
                            shredderConfig.DebrisBurstPerCrossedCell);
                    }
                    continue;
                }

                activeRendererCount++;
            }

            bool allShardsDone = feed.Shards.Count == 0 ||
                                 feed.ProcessedShards.Count >= feed.Shards.Count;
            return activeRendererCount == 0 && allShardsDone;
        }

        private void FinishPhysicsVisualFeed(PhysicsVisualFeed feed, bool completedNormally)
        {
            if (feed == null)
                return;

            PrototypeBoard.Active?.JointSpringPresenter?.FinishShredderSprings(feed.Piece);
            if (feed.Glow != null)
                ShredderContactGlowPool.Return(feed.Glow);
            if (feed.IsShreddingSoundActive)
                StopContinuousShredSound();
            if (feed.HapticSessionActive)
                HapticFeedbackRuntime.EndShredder(feed.PieceId, completedNormally);

            if (feed.RemainingProgress > 0.00001f)
            {
                LevelProgressManager progressManager = LevelProgressManager.Instance;
                if (progressManager != null)
                    progressManager.AddProgress(feed.RemainingProgress);
            }

            if (completedNormally)
                EmitFinalDebrisBurst(feed.LastContactCenter, feed.LastContactWidth, feed.TileColor);
            if (feed.Piece != null)
                feed.Piece.ReleaseInstance();
            ReleaseFeedMask();
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
                yield break;
            }

            float shredderY = zone.ShredY;
            float wheelRadius = zone.WheelRadius > 0f ? zone.WheelRadius : 0.4f;
            float gearClippingRatio = shredderConfig != null ? shredderConfig.GearClippingRatio : 0.45f;
            float clippingSeamY = shredderY - (wheelRadius * gearClippingRatio);

            AcquireFeedMask(clippingSeamY);

            // 1. The cutter has already ended OpenFreefall. From this point
            // onward the feed is presentation-only; lower cells are clipped
            // only as they reach the cutter line below.
            piece.SetSelected(false);
            piece.ApplyLifecyclePhysicsProfile(PieceState.Shredding, shredderConfig);

            // 2. Capture the current presentation before the feed mask begins
            // clipping the piece beneath the cutter.
            // An intact atlas is a single large SpriteRenderer and can only
            // ever read as a rectangular mask. Switch to the authored modular
            // cells before caching renderers so each row becomes a tangible
            // cutter surface with its own irregular edge.
            piece.PrepareModularShredderSurface();
            SpriteRenderer[] pieceRenderers = piece.ConfiguredShredderRenderers ?? EmptyRenderers;
            piece.BeginShredderPresentation(pieceRenderers, clippingSeamY, shredderConfig);
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

            // Keep the visible progress stream proportional to the authored
            // piece size. The config owns the direct grains-per-block budget;
            // multiplying it by a hidden base burst scheduled 180 HUD grains
            // for one three-block piece.
            int progressFlightCount = shredderConfig != null
                ? shredderConfig.GetProgressVoxelFlightCount(blockCount)
                : blockCount;

            float feedSpeed = shredderConfig != null ? shredderConfig.FeedSpeed : 3.8f;
            float maxTime = shredderConfig != null
                ? shredderConfig.PhysicsFeedMaximumDuration
                : 6.0f;
            float elapsed = 0f;
            float debrisAccumulator = 0f;
            // The clipping seam sits at the center/upper-middle line of the wheels.
            float visualCutterY = clippingSeamY;
            float debrisContactY = clippingSeamY;

            // Total area based on true cell blocks (1 unit per block)
            float totalRendererArea = Mathf.Max(0.5f, blockCount * 1.0f);
            float remainingProgressToEmit = totalProgress;
            int remainingFlightsToEmit = progressFlightCount;
            float progressVoxelAccumulator = 0f;

            bool contactPresentationStarted = false;
            Vector2 lastContactCenter = Vector2.zero;
            float lastContactWidth = 0f;
            bool isShreddingSoundActive = false;
            bool hapticSessionActive = false;
            bool feedCompletedNormally = false;
            int hapticPieceId = piece.SourcePieceId;
            ShredderContactGlowView activeGlowView = null;
            ShredderContactGlowPool.TryRent(out activeGlowView);

            try
            {
                // Rigidbody2D is the sole movement owner after cutter entry.
                // This visual pass observes that live pose; it never writes a
                // transform, velocity, or rotation.
                while (piece != null && elapsed < maxTime)
            {
                yield return FeedPhysicsStep;
                float feedDeltaTime = Time.fixedDeltaTime;
                elapsed += feedDeltaTime;
                PrototypeBoard.Active?.JointSpringPresenter?.UpdateShredderSprings(piece, clippingSeamY, shredderConfig);
                piece.ClampOffBoardAngularVelocity(shredderConfig);

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

                    if (!hapticSessionActive)
                    {
                        // Match the established continuous-audio ownership: the pattern begins
                        // on real cutter contact, not on the earlier physics handoff.
                        HapticFeedbackRuntime.BeginShredder(hapticPieceId);
                        hapticSessionActive = true;
                    }

                    // Progress is derived from material already consumed by the feed, rather
                    // than wall-clock time, so pauses or a slower physical entry do not fade it.
                    float feedProgress = totalProgress > 0f
                        ? 1f - Mathf.Clamp01(remainingProgressToEmit / totalProgress)
                        : 0f;
                    HapticFeedbackRuntime.UpdateShredderProgress(hapticPieceId, feedProgress);

                    // Accelerate gears and vibrate active contact gears touching the block
                    IReadOnlyList<ShredderWheel> activeWheels = ShredderWheel.ActiveWheels;
                    float halfContact = contactWidth * 0.55f;
                    float contactMinX = currentContactCenter.x - halfContact;
                    float contactMaxX = currentContactCenter.x + halfContact;
                    for (int w = 0; w < activeWheels.Count; w++)
                    {
                        ShredderWheel wheel = activeWheels[w];
                        if (wheel == null) continue;
                        float wx = wheel.transform.position.x;
                        bool isTouching = wx >= contactMinX && wx <= contactMaxX;
                        wheel.SetShredding(true, isTouching);
                    }

                    if (activeGlowView != null)
                    {
                        float glowIntensity = shredderConfig != null ? shredderConfig.GlowIntensity : 1.4f;
                        activeGlowView.UpdateGlow(currentContactCenter, contactWidth, tileColor, glowIntensity);
                    }
                }
                else
                {
                    IReadOnlyList<ShredderWheel> activeWheels = ShredderWheel.ActiveWheels;
                    for (int w = 0; w < activeWheels.Count; w++)
                    {
                        ShredderWheel wheel = activeWheels[w];
                        if (wheel != null)
                            wheel.SetShredding(false, false);
                    }

                    if (activeGlowView != null)
                        activeGlowView.Hide();
                }

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

                    if (shard.transform.position.y <= clippingSeamY)
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
                    if (activeGlowView != null)
                        activeGlowView.Hide();
                    if (isShreddingSoundActive)
                    {
                        isShreddingSoundActive = false;
                        StopContinuousShredSound();
                    }
                    feedCompletedNormally = true;
                    break;
                }
            }
            }
            finally
            {
                PrototypeBoard.Active?.JointSpringPresenter?.FinishShredderSprings(piece);
                IReadOnlyList<ShredderWheel> activeWheels = ShredderWheel.ActiveWheels;
                for (int w = 0; w < activeWheels.Count; w++)
                {
                    ShredderWheel wheel = activeWheels[w];
                    if (wheel != null)
                        wheel.SetShredding(false, false);
                }

                if (piece != null)
                    piece.ResetShredderVisualJitter();

                if (activeGlowView != null)
                {
                    ShredderContactGlowPool.Return(activeGlowView);
                    activeGlowView = null;
                }

                if (isShreddingSoundActive)
                {
                    isShreddingSoundActive = false;
                    StopContinuousShredSound();
                }

                if (hapticSessionActive)
                    HapticFeedbackRuntime.EndShredder(hapticPieceId, feedCompletedNormally);
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
