using System.Collections.Generic;
using GravityPuzzle.Core.Grid;
using GravityPuzzle.Core.StateMachine;
using GravityPuzzle.Gameplay.Tutorial;
using GravityPuzzle.Gameplay.Gravity;
using GravityPuzzle.Gameplay.Pieces;
using UnityEngine;

namespace GravityPuzzle
{
    /// <summary>
    /// Handles input for every piece from one place. We raycast through whichever
    /// child collider was touched, then move the complete parent piece.
    /// </summary>
    public sealed class PuzzleDragController : MonoBehaviour
    {
        private Camera gameCamera;
        private PuzzlePiece selectedPiece;
        // Feedback-only selection for a movable member whose combined group
        // is locked by Ice. It never enters the Dragging lifecycle state.
        private PuzzlePiece frozenGroupFeedbackPiece;
        private PuzzlePiece gridReleasePresentationPiece;
        private readonly HashSet<PuzzlePiece> gridFallingPieces = new HashSet<PuzzlePiece>();
        private readonly List<PuzzlePiece> finishedGridFalls = new List<PuzzlePiece>();
        private readonly List<PuzzlePiece> retargetableGridFalls = new List<PuzzlePiece>();
        private readonly List<PuzzlePiece> selectedJointGroupPieces = new List<PuzzlePiece>();
        private readonly Queue<GridGravityMove> pendingGridGravityMoves =
            new Queue<GridGravityMove>();
        private readonly List<GridGravityMove> candidateJointGravityMoves = new List<GridGravityMove>();
        private readonly List<GridGravityMove> selectedJointGravityMoves = new List<GridGravityMove>();
        private readonly List<PuzzlePiece> gravityGroupPieces = new List<PuzzlePiece>();
        private GridCoordinate selectedPieceStartAnchor;
        private bool hasSelectedPieceStartAnchor;
        // Every combined drag has one stable, deterministic reference pivot.
        // The selected member can vary, but all members keep this offset from
        // the reference for the entire input transaction.
        private GridCoordinate selectedGroupReferencePivot;
        private GridCoordinate selectedPivotOffsetFromGroup;
        private bool hasSelectedGroupReference;
        private Vector2 grabOffset;
        // Pointer motion is consumed once per input frame. Keeping it as a
        // delta prevents a rejected move from turning into a later path seek.
        private Vector2 dragTarget;
        private Vector2 pendingDragIntent;
        // Input that has not yet crossed a fine-grid cell. This is presentation
        // state only; grid anchors remain the sole gameplay authority.
        private Vector2 fineDragRemainder;
        private Vector2 lastResolvedDragPosition;
        private bool hasLastResolvedDragPosition;
        private int activeFingerId = -1;
        private readonly Collider2D[] selectionHits = new Collider2D[32];
        private const float FineCellDragThreshold = .5f;
        private const float TouchSelectionRadiusInGridCells = .45f;
        private const float MouseSelectionRadiusInGridCells = .18f;
        public static PuzzleDragController Instance { get; private set; }
        private PrototypeBoard subscribedBoard;
        private bool inputLockedByGameState;

        public static void WakeUpGravity()
        {
            if (Instance == null)
                return;

            // A topology edit (hammer split/remove) changes both the set of
            // pieces and the cells owned by each piece. Any already queued
            // cascade was calculated against the pre-edit snapshot and can
            // otherwise move a newly-created fragment to an old piece's
            // target. Drop that stale presentation work and calculate the
            // next cascade exclusively from the committed grid state.
            Instance.pendingGridGravityMoves.Clear();

            // A committed grid fall already owns a distinct target footprint,
            // so it cannot conflict with a fresh cascade plan. Do not delay a
            // shredder-cleared row until an unrelated fall presentation ends.
            if (Instance.selectedPiece == null)
            {
                Instance.RetargetActiveGridFalls();
                Instance.AdvanceGridGravityPresentation();
            }
        }

        /// <summary>
        /// Transfers a currently falling piece to a targeted gameplay action.
        /// Grid gravity commits its destination before the fall tween finishes,
        /// therefore the tween and its controller bookkeeping must be stopped
        /// together before Hammer or Rocket takes transform ownership.
        /// </summary>
        public static void CancelGridFallForTargetedAction(PuzzlePiece piece)
        {
            if (piece == null)
                return;

            piece.GridFallView?.Cancel();

            // Tween yarıda kesildi; görsel pozisyonu modelin gerçek anchor'ına
            // anında sabitle, aksi halde parça ara bir noktada asılı kalır.
            if (Instance != null &&
                PrototypeBoard.Active != null &&
                PrototypeBoard.Active.TryGetPieceModel(piece, out var model))
            {
                GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
                if (level != null && piece.Body != null)
                {
                    GridCoordinate pivot = new GridCoordinate(
                        model.Anchor.X - model.PivotOffset.X,
                        model.Anchor.Y - model.PivotOffset.Y);
                    Vector2 correctedPosition = GravityLevelGridCoordinates.FineCellToWorld(level, pivot);
                    piece.Body.position = correctedPosition;
                    Physics2D.SyncTransforms();
                }
            }

            if (Instance == null)
                return;

            Instance.gridFallingPieces.Remove(piece);
            if (Instance.gridReleasePresentationPiece == piece)
                Instance.gridReleasePresentationPiece = null;
            Instance.pendingGridGravityMoves.Clear();
            PrototypeBoard.Active?.TrySetPieceState(piece, PieceState.Placed);
        }

        /// <summary>
        /// Converts pointer screen coordinates using the controller's cached
        /// gameplay camera. Targeting tools must use this instead of resolving
        /// Camera.main independently on every tap.
        /// </summary>
        public static bool TryScreenToBoardWorld(Vector2 screenPosition, out Vector2 worldPosition)
        {
            worldPosition = default;
            if (Instance == null || Instance.gameCamera == null)
                return false;

            Camera camera = Instance.gameCamera;
            Vector3 screenPoint = new Vector3(
                screenPosition.x,
                screenPosition.y,
                -camera.transform.position.z);
            worldPosition = camera.ScreenToWorldPoint(screenPoint);
            return true;
        }

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            gameCamera = PrototypeBootstrap.SceneCamera;
            if (gameCamera == null)
                Debug.LogError("[PuzzleDrag] No gameplay camera is configured on Runtime Piece Factory Bootstrap.", this);
        }

        private void OnEnable()
        {
            BindBoardEvents(PrototypeBoard.Active);
        }

        private void OnDisable()
        {
            BindBoardEvents(null);

            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            BindBoardEvents(PrototypeBoard.Active);

            if (inputLockedByGameState)
            {
                ClearInputSelection();
                return;
            }

            if (BoosterFirstUseTutorialRuntime.Gate != null &&
                BoosterFirstUseTutorialRuntime.Gate.BlocksBoardDrag)
            {
                ClearInputSelection();
                return;
            }

            // The shredder can capture a piece while it is still held. Drop the
            // drag controller's reference immediately so it cannot move the
            // piece again or queue a release snap during the shred animation.
            if (selectedPiece != null && selectedPiece.IsShredderInteractionLocked)
            {
                selectedPiece = null;
                activeFingerId = -1;
            }

            if (HammerBooster.IsTargeting || RocketBooster.IsTargeting)
            {
                if (selectedPiece != null)
                {
                    ReleasePiece();
                    activeFingerId = -1;
                }
                return;
            }

            if (LevelTimerUI.IsGameOver)
            {
                if (selectedPiece != null)
                {
                    ReleasePiece();
                    activeFingerId = -1;
                }
                return;
            }

            CapturePiecesAtShredderBoundary();

            if (Input.touchCount > 0)
                ProcessTouchInput();
            else
                ProcessMouseInput();

            // A held piece consumes only this frame's pointer motion. The grid
            // validates each fine-grid step while presentation retains the
            // safe fractional remainder between those steps.
            if (selectedPiece != null && !selectedPiece.IsShredderInteractionLocked)
                TryMoveSelectedPieceOnGrid(selectedPiece, ConsumeDragIntent());
        }

        private void BindBoardEvents(PrototypeBoard nextBoard)
        {
            if (subscribedBoard == nextBoard)
                return;

            if (subscribedBoard != null)
                subscribedBoard.GameStateChanged -= HandleGameStateChanged;

            subscribedBoard = nextBoard;
            inputLockedByGameState = IsGameplayInteractionLocked(subscribedBoard);

            if (subscribedBoard != null)
                subscribedBoard.GameStateChanged += HandleGameStateChanged;
        }

        private void HandleGameStateChanged(GameState previousState, GameState nextState)
        {
            inputLockedByGameState = IsGameplayInteractionLocked(subscribedBoard);

            if (inputLockedByGameState)
                StopGameplayPresentation();
        }

        private static bool IsGameplayInteractionLocked(PrototypeBoard board)
        {
            return board == null || !board.IsLevelRunning;
        }

        // Result and bootstrap states have no gameplay owner. Clear every
        // input/gravity presentation owned by this controller together so an
        // old drag or fall tween cannot continue after the board has reached
        // a terminal state.
        private void StopGameplayPresentation()
        {
            ClearInputSelection();
            HammerBooster.CancelActiveSelection();
            RocketBooster.CancelActiveSelection();
            pendingGridGravityMoves.Clear();

            foreach (PuzzlePiece fallingPiece in gridFallingPieces)
                fallingPiece.GridFallView?.Cancel();
            gridFallingPieces.Clear();

            if (gridReleasePresentationPiece != null)
            {
                gridReleasePresentationPiece.GridFallView?.Cancel();
                gridReleasePresentationPiece = null;
            }

        }

        private void ClearInputSelection()
        {
            PrototypeBoard.Active?.CollectJointGroupPieces(selectedPiece, selectedJointGroupPieces);
            for (int index = 0; index < selectedJointGroupPieces.Count; index++)
                selectedJointGroupPieces[index]?.SetSelected(false);

            selectedPiece = null;
            ClearFrozenGroupFeedback();
            activeFingerId = -1;
            hasLastResolvedDragPosition = false;
            hasSelectedPieceStartAnchor = false;
            hasSelectedGroupReference = false;
            pendingDragIntent = Vector2.zero;
            fineDragRemainder = Vector2.zero;
        }

        private void CapturePiecesAtShredderBoundary()
        {
            BlockShredder shredder = BlockShredder.Instance;
            IReadOnlyList<ShredderCatchZone> zones = ShredderCatchZone.ActiveZones;
            if (shredder == null || zones.Count == 0)
                return;

            IReadOnlyList<PuzzlePiece> pieces = PuzzlePiece.ActivePieces;
            for (int pieceIndex = 0; pieceIndex < pieces.Count; pieceIndex++)
            {
                PuzzlePiece piece = pieces[pieceIndex];
                if (piece == null || piece.IsShredderInteractionLocked || piece.IsFrozen)
                    continue;

                for (int zoneIndex = 0; zoneIndex < zones.Count; zoneIndex++)
                {
                    ShredderCatchZone zone = zones[zoneIndex];
                    if (zone == null)
                        continue;
                    if (!zone.ContainsCaptureFootprint(piece))
                    {
#if UNITY_EDITOR
                        zone.LogCaptureRejection(piece, shredder);
#endif
                        continue;
                    }

                    // The feed cancels its presentation tween internally. Clear
                    // this controller's matching fall record first, otherwise a
                    // later cleanup pass attempts an invalid Shredding -> Placed
                    // transition for the captured root.
                    CancelGridFallForTargetedAction(piece);
                    if (shredder.TryCapturePiece(piece, zone) && selectedPiece == piece)
                    {
                        selectedPiece = null;
                        activeFingerId = -1;
                    }

                    break;
                }
            }
        }

        private void FixedUpdate()
        {
            if (inputLockedByGameState)
                return;

            Physics2D.SyncTransforms();
            AdvanceGridGravityPresentation();
        }

        private bool AdvanceGridGravityPresentation()
        {
            if (gridReleasePresentationPiece != null)
            {
                if (gridReleasePresentationPiece.GridFallView == null ||
                    !gridReleasePresentationPiece.GridFallView.IsAnimating)
                    gridReleasePresentationPiece = null;
            }

            RemoveFinishedGridFalls();

            // A release commits its destination in the authoritative grid before
            // the short visual snap reaches that destination.  Starting a new
            // gravity tween now would let a newly unsupported piece fall through
            // the releasing piece's in-flight visual path. Existing falls retain
            // their presentation ownership; only new cascade planning waits for
            // the release pose to converge with the committed grid anchor.
            if (gridReleasePresentationPiece != null)
                return gridFallingPieces.Count > 0;

            // The grid commits a fall destination before its presentation tween
            // completes. Starting another fall against that future occupancy
            // makes otherwise legal pieces visually pass through one another.
            // A fall therefore owns presentation until it reaches its committed
            // anchor; the next move is planned from that settled visual state.
            if (gridFallingPieces.Count > 0)
                return true;

            PrototypeBoard activeBoard = PrototypeBoard.Active;
            LevelBoardSnapshot snapshot = activeBoard != null
                ? activeBoard.BoardSnapshot
                : null;
            if (snapshot == null)
                return gridFallingPieces.Count > 0;

            if (pendingGridGravityMoves.Count == 0)
                TryBuildSettledGridGravityPlan(activeBoard, snapshot);

            return TryPlayQueuedGridGravityMoves(activeBoard, snapshot) ||
                   gridFallingPieces.Count > 0;
        }

        /// <summary>
        /// Extends an active fall when a shredder releases additional board
        /// rows. The grid computes the newly legal target first, so this never
        /// lets presentation enter an obstacle, a reservation, or another
        /// piece's committed footprint.
        /// </summary>
        private void RetargetActiveGridFalls()
        {
            if (gridFallingPieces.Count == 0)
                return;

            PrototypeBoard activeBoard = PrototypeBoard.Active;
            LevelBoardSnapshot snapshot = activeBoard != null
                ? activeBoard.BoardSnapshot
                : null;
            GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
            if (snapshot == null || level == null)
                return;

            retargetableGridFalls.Clear();
            foreach (PuzzlePiece piece in gridFallingPieces)
            {
                if (piece != null && piece.GridFallView != null && piece.GridFallView.IsAnimating)
                    retargetableGridFalls.Add(piece);
            }

            for (int index = 0; index < retargetableGridFalls.Count; index++)
            {
                PuzzlePiece piece = retargetableGridFalls[index];
                if (piece.IsShredderInteractionLocked ||
                    !snapshot.TryGetPiece(piece.SourcePieceId, out PieceModel model) ||
                    model.State != PieceState.Falling ||
                    !snapshot.Grid.TryGetFallTarget(model, out GridCoordinate targetAnchor) ||
                    targetAnchor.Equals(model.Anchor))
                    continue;

                GridGravityMove extension = new GridGravityMove(
                    model.Id,
                    model.Anchor,
                    targetAnchor);
                if (!activeBoard.TryCommitGridGravityMove(extension, out _))
                    continue;

                GridCoordinate targetPivot = new GridCoordinate(
                    targetAnchor.X - model.PivotOffset.X,
                    targetAnchor.Y - model.PivotOffset.Y);
                Vector2 targetPosition = GravityLevelGridCoordinates.FineCellToWorld(level, targetPivot);
                piece.GridFallView.RetargetReleaseTo(
                    targetPosition,
                    () => CompleteGridGravityPresentation(piece));
            }

            retargetableGridFalls.Clear();
        }

        private bool TryBuildSettledGridGravityPlan(
            PrototypeBoard activeBoard,
            LevelBoardSnapshot snapshot)
        {
            selectedJointGravityMoves.Clear();
            int selectedLandingY = int.MaxValue;
            int selectedSourceId = int.MaxValue;
            IReadOnlyList<PieceModel> models = snapshot.Pieces;
            for (int index = 0; index < models.Count; index++)
            {
                PieceModel model = models[index];
                if (!IsEligibleForGridGravity(model))
                    continue;

                activeBoard.CollectJointGroupPieces(model.Id, gravityGroupPieces);
                if (!IsEligibleJointGravityGroup(gravityGroupPieces, model.Id))
                    continue;

                candidateJointGravityMoves.Clear();
                if (!activeBoard.TryPlanJointGroupGravityMove(model.Id, candidateJointGravityMoves))
                    continue;

                int groupLandingY = int.MaxValue;
                int groupSourceId = int.MaxValue;
                for (int moveIndex = 0; moveIndex < candidateJointGravityMoves.Count; moveIndex++)
                {
                    GridGravityMove candidate = candidateJointGravityMoves[moveIndex];
                    groupLandingY = Mathf.Min(groupLandingY, candidate.ToAnchor.Y);
                    groupSourceId = Mathf.Min(groupSourceId, candidate.PieceId);
                }

                if (groupLandingY > selectedLandingY ||
                    groupLandingY == selectedLandingY && groupSourceId >= selectedSourceId)
                {
                    continue;
                }

                selectedJointGravityMoves.Clear();
                selectedJointGravityMoves.AddRange(candidateJointGravityMoves);
                selectedLandingY = groupLandingY;
                selectedSourceId = groupSourceId;
            }

            if (selectedJointGravityMoves.Count == 0 ||
                !activeBoard.TryCommitJointGroupGravityMove(selectedJointGravityMoves, out _))
            {
                return false;
            }

            for (int index = 0; index < selectedJointGravityMoves.Count; index++)
                pendingGridGravityMoves.Enqueue(selectedJointGravityMoves[index]);
            return true;
        }

        private static bool IsEligibleJointGravityGroup(
            IReadOnlyList<PuzzlePiece> pieces,
            int expectedMemberId)
        {
            PrototypeBoard board = PrototypeBoard.Active;
            if (pieces == null || pieces.Count == 0 || board == null)
                return false;

            bool containsExpectedMember = false;
            for (int index = 0; index < pieces.Count; index++)
            {
                PuzzlePiece piece = pieces[index];
                // Frozen prevents player input and shredder capture, not a legal
                // board fall. Excluding it here leaves ice pieces suspended.
                if (piece == null || piece.IsShredderInteractionLocked ||
                    piece.GridFallView == null || !piece.GridFallView.CanPlay ||
                    !board.TryGetPieceModel(piece, out PieceModel model) ||
                    model.State != PieceState.Placed)
                {
                    return false;
                }

                containsExpectedMember |= piece.SourcePieceId == expectedMemberId;
            }

            return containsExpectedMember;
        }

        private static bool IsEligibleForGridGravity(PieceModel model)
        {
            PuzzlePiece piece = FindActivePiece(model.Id);
            // IsFrozen intentionally is not a gravity gate; the grid remains the
            // sole authority for whether this piece has support below it.
            return piece != null &&
                   !piece.IsShredderInteractionLocked &&
                   model.State == PieceState.Placed &&
                   (Instance == null ||
                    Instance.gridReleasePresentationPiece == null ||
                    Instance.gridReleasePresentationPiece.SourcePieceId != model.Id) &&
                   piece.GridFallView != null &&
                   piece.GridFallView.CanPlay;
        }

        private bool TryPlayQueuedGridGravityMoves(
            PrototypeBoard activeBoard,
            LevelBoardSnapshot snapshot)
        {
            GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
            if (level == null)
            {
                pendingGridGravityMoves.Clear();
                return false;
            }

            if (pendingGridGravityMoves.Count == 0)
                return false;

            bool playedAny = false;
            while (pendingGridGravityMoves.Count > 0)
            {
                GridGravityMove move = pendingGridGravityMoves.Dequeue();
                PuzzlePiece piece = FindActivePiece(move.PieceId);
                if (piece == null || piece.IsShredderInteractionLocked ||
                    piece.GridFallView == null || !piece.GridFallView.CanPlay ||
                    !snapshot.TryGetPiece(move.PieceId, out PieceModel model))
                {
                    pendingGridGravityMoves.Clear();
                    return playedAny;
                }

                GridCoordinate targetPivot = new GridCoordinate(
                    move.ToAnchor.X - model.PivotOffset.X,
                    move.ToAnchor.Y - model.PivotOffset.Y);
                PrepareKinematicBody(piece.Body);
                GridCoordinate sourcePivot = new GridCoordinate(
                    move.FromAnchor.X - model.PivotOffset.X,
                    move.FromAnchor.Y - model.PivotOffset.Y);
                MoveBody(
                    piece.Body,
                    GravityLevelGridCoordinates.FineCellToWorld(level, sourcePivot));
                Vector2 targetPosition = GravityLevelGridCoordinates.FineCellToWorld(level, targetPivot);
                gridFallingPieces.Add(piece);
                bool settlesAboveShredder = IsSettlingAboveShredderReservation(snapshot, model);
                bool played = settlesAboveShredder
                    ? piece.GridFallView.PlayReleaseTo(
                        targetPosition,
                        () => CompleteGridGravityPresentation(piece))
                    : piece.GridFallView.PlayFallTo(
                        targetPosition,
                        () => CompleteGridGravityPresentation(piece));
                if (played)
                {
                    playedAny = true;
                    continue;
                }

                gridFallingPieces.Remove(piece);
                activeBoard.TrySetPieceState(piece, PieceState.Placed);
            }

            return playedAny;
        }

        private static bool IsSettlingAboveShredderReservation(
            LevelBoardSnapshot snapshot,
            PieceModel model)
        {
            if (snapshot == null || model == null)
                return false;

            GridCoordinate down = new GridCoordinate(0, -1);
            for (int index = 0; index < model.LocalCells.Count; index++)
            {
                GridCoordinate below = model.GetWorldCell(index).Offset(down);
                if (snapshot.Grid.GetCellState(below) != GridCellState.Reserved ||
                    !snapshot.Grid.TryGetOccupantId(below, out int occupantId) ||
                    !snapshot.TryGetPiece(occupantId, out PieceModel occupant))
                    continue;

                if (occupant.State == PieceState.HandoffToPhysics ||
                    occupant.State == PieceState.Shredding)
                    return true;
            }

            return false;
        }

        private static PuzzlePiece FindActivePiece(int sourcePieceId)
        {
            IReadOnlyList<PuzzlePiece> pieces = PuzzlePiece.ActivePieces;
            for (int index = 0; index < pieces.Count; index++)
            {
                PuzzlePiece piece = pieces[index];
                if (piece != null && piece.SourcePieceId == sourcePieceId)
                    return piece;
            }

            return null;
        }

        private void CompleteGridGravityPresentation(PuzzlePiece piece)
        {
            if (piece == null || !gridFallingPieces.Remove(piece))
                return;

            SnapPieceToCommittedGridPosition(piece);
            PrototypeBoard.Active?.TrySetPieceState(piece, PieceState.Placed);
        }

        private static void SnapPieceToCommittedGridPosition(PuzzlePiece piece)
        {
            PrototypeBoard board = PrototypeBoard.Active;
            GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
            if (piece == null || piece.Body == null || board == null || level == null ||
                !board.TryGetPieceModel(piece, out PieceModel model))
                return;

            GridCoordinate pivot = new GridCoordinate(
                model.Anchor.X - model.PivotOffset.X,
                model.Anchor.Y - model.PivotOffset.Y);
            MoveBody(piece.Body, GravityLevelGridCoordinates.FineCellToWorld(level, pivot));
        }

        private void RemoveFinishedGridFalls()
        {
            if (gridFallingPieces.Count == 0)
                return;

            finishedGridFalls.Clear();
            foreach (PuzzlePiece piece in gridFallingPieces)
            {
                if (piece == null || piece.GridFallView == null || !piece.GridFallView.IsAnimating)
                    finishedGridFalls.Add(piece);
            }

            for (int index = 0; index < finishedGridFalls.Count; index++)
                CompleteGridGravityPresentation(finishedGridFalls[index]);
        }

        // A selected piece keeps its dynamic occupancy in the grid while held so falling
        // pieces cannot enter its footprint. Each accepted fine-grid step commits a new
        // legal anchor; the remaining safe fractional motion is presentation only.
        private bool TryMoveSelectedPieceOnGrid(PuzzlePiece piece, Vector2 dragIntent)
        {
            if (piece == null || piece.Body == null || !hasSelectedPieceStartAnchor ||
                !TryGetSnapshotPiece(piece, out LevelBoardSnapshot snapshot, out PieceModel model))
            {
                return false;
            }

            GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
            PrototypeBoard board = PrototypeBoard.Active;
            if (level == null || board == null)
                return false;

            fineDragRemainder += dragIntent * level.subdivisions;
            while (TryGetNextFineDragStep(out GridCoordinate step))
            {
                if (board.TryMoveDraggingPieceOnGrid(piece, model.Anchor.Offset(step), out _))
                {
                    if (step.X != 0)
                        fineDragRemainder.x -= step.X;
                    else
                        fineDragRemainder.y -= step.Y;
                    continue;
                }

                // Discard only the blocked component. The other axis remains
                // free to slide this same frame.
                if (step.X != 0)
                    fineDragRemainder.x = 0f;
                else
                    fineDragRemainder.y = 0f;
            }

            board.CollectJointGroupPieces(piece, selectedJointGroupPieces);
            bool isCombined = selectedJointGroupPieces.Count > 1;
            GridCoordinate selectedPivot = new GridCoordinate(
                model.Anchor.X - model.PivotOffset.X,
                model.Anchor.Y - model.PivotOffset.Y);
            Vector2 selectedGridPosition = GravityLevelGridCoordinates.FineCellToWorld(level, selectedPivot);
            Vector2 selectedPresentationOffset = isCombined
                ? GetContinuousGroupPresentationOffset(board, piece, level, fineDragRemainder)
                : GetContinuousDragPresentationOffset(snapshot.Grid, model, level, fineDragRemainder);
            Vector2 selectedPresentationPosition = selectedGridPosition + selectedPresentationOffset;
            piece.Body.position = selectedPresentationPosition;

            for (int groupIndex = 0; groupIndex < selectedJointGroupPieces.Count; groupIndex++)
            {
                PuzzlePiece groupPiece = selectedJointGroupPieces[groupIndex];
                if (groupPiece == null || groupPiece == piece || groupPiece.Body == null ||
                    !snapshot.TryGetPiece(groupPiece.SourcePieceId, out PieceModel groupModel))
                {
                    continue;
                }

                GridCoordinate groupPivot = new GridCoordinate(
                    groupModel.Anchor.X - groupModel.PivotOffset.X,
                    groupModel.Anchor.Y - groupModel.PivotOffset.Y);
                Vector2 groupPosition = GravityLevelGridCoordinates.FineCellToWorld(level, groupPivot);
                groupPiece.Body.position = groupPosition + selectedPresentationOffset;
            }

            lastResolvedDragPosition = selectedPresentationPosition;
            hasLastResolvedDragPosition = true;
            if (isCombined)
                Physics2D.SyncTransforms();
            return true;
        }

        // Retained for deterministic controller tests and release handling;
        // production Update supplies the fresh pointer delta explicitly.
        private bool TryMoveSelectedPieceOnGrid(PuzzlePiece piece)
        {
            return TryMoveSelectedPieceOnGrid(piece, ConsumeDragIntent());
        }

        private static Vector2 GetContinuousGroupPresentationOffset(
            PrototypeBoard board,
            PuzzlePiece piece,
            GravityLevelDefinition level,
            Vector2 remainder)
        {
            int horizontalStep = remainder.x > 0f ? 1 : remainder.x < 0f ? -1 : 0;
            int verticalStep = remainder.y > 0f ? 1 : remainder.y < 0f ? -1 : 0;
            bool horizontalLegal = horizontalStep != 0 &&
                board.CanMovePieceGroupBy(piece, new GridCoordinate(horizontalStep, 0));
            bool verticalLegal = verticalStep != 0 &&
                board.CanMovePieceGroupBy(piece, new GridCoordinate(0, verticalStep));

            Vector2 offset = Vector2.zero;
            if (horizontalLegal && verticalLegal && horizontalStep != 0 && verticalStep != 0)
            {
                if (board.CanMovePieceGroupBy(piece, new GridCoordinate(horizontalStep, verticalStep)))
                    offset = remainder;
                else if (Mathf.Abs(remainder.x) >= Mathf.Abs(remainder.y))
                    offset.x = remainder.x;
                else
                    offset.y = remainder.y;
            }
            else if (horizontalLegal)
            {
                offset.x = remainder.x;
            }
            else if (verticalLegal)
            {
                offset.y = remainder.y;
            }

            return offset / level.subdivisions;
        }

        private static Vector2 GetContinuousDragPresentationOffset(
            GravityBoardGrid grid,
            PieceModel model,
            GravityLevelDefinition level,
            Vector2 remainder)
        {
            int horizontalStep = remainder.x > 0f ? 1 : remainder.x < 0f ? -1 : 0;
            int verticalStep = remainder.y > 0f ? 1 : remainder.y < 0f ? -1 : 0;
            bool horizontalLegal = horizontalStep != 0 &&
                grid.CheckPlacementIgnoringPiece(
                    model,
                    model.Anchor.Offset(new GridCoordinate(horizontalStep, 0)),
                    model.Id).IsSuccess;
            bool verticalLegal = verticalStep != 0 &&
                grid.CheckPlacementIgnoringPiece(
                    model,
                    model.Anchor.Offset(new GridCoordinate(0, verticalStep)),
                    model.Id).IsSuccess;

            Vector2 offset = Vector2.zero;
            if (horizontalLegal && verticalLegal && horizontalStep != 0 && verticalStep != 0)
            {
                GridCoordinate diagonal = new GridCoordinate(horizontalStep, verticalStep);
                if (grid.CheckPlacementIgnoringPiece(
                        model,
                        model.Anchor.Offset(diagonal),
                        model.Id).IsSuccess)
                {
                    offset = remainder;
                }
                else if (Mathf.Abs(remainder.x) >= Mathf.Abs(remainder.y))
                {
                    offset.x = remainder.x;
                }
                else
                {
                    offset.y = remainder.y;
                }
            }
            else if (horizontalLegal)
            {
                offset.x = remainder.x;
            }
            else if (verticalLegal)
            {
                offset.y = remainder.y;
            }

            return offset / level.subdivisions;
        }

        private bool TryGetNextFineDragStep(out GridCoordinate step)
        {
            step = default;
            bool xReady = Mathf.Abs(fineDragRemainder.x) >= FineCellDragThreshold;
            bool yReady = Mathf.Abs(fineDragRemainder.y) >= FineCellDragThreshold;
            if (!xReady && !yReady)
                return false;

            if (xReady && (!yReady || Mathf.Abs(fineDragRemainder.x) >= Mathf.Abs(fineDragRemainder.y)))
            {
                step = new GridCoordinate(fineDragRemainder.x > 0f ? 1 : -1, 0);
                return true;
            }

            step = new GridCoordinate(0, fineDragRemainder.y > 0f ? 1 : -1);
            return true;
        }

        private void LogDragStepFailure(
            PrototypeBoard board,
            GravityLevelDefinition level,
            PuzzlePiece piece,
            PieceModel model,
            GridCoordinate desiredPivot,
            GridCoordinate attemptedAnchor,
            GridPlacementResult result)
        {
            if (board == null || level == null || piece == null || piece.Body == null || model == null)
                return;

            Vector2 pointerPosition = dragTarget - grabOffset;
            board.CollectJointGroupPieces(piece, selectedJointGroupPieces);
            string groupAnchors = string.Empty;
            for (int index = 0; index < selectedJointGroupPieces.Count; index++)
            {
                PuzzlePiece groupPiece = selectedJointGroupPieces[index];
                if (groupPiece == null || !board.TryGetPieceModel(groupPiece, out PieceModel groupModel))
                    continue;

                if (groupAnchors.Length > 0)
                    groupAnchors += ",";

                groupAnchors += $"{groupModel.Id}:({groupModel.Anchor.X},{groupModel.Anchor.Y})";
            }

            Debug.Log(
                $"[DragTarget] FAIL member={model.Id} " +
                $"pointer=({pointerPosition.x:F3},{pointerPosition.y:F3}) " +
                $"grabOffset=({grabOffset.x:F3},{grabOffset.y:F3}) " +
                $"dragTarget=({dragTarget.x:F3},{dragTarget.y:F3}) " +
                $"body=({piece.Body.position.x:F3},{piece.Body.position.y:F3}) " +
                $"anchor=({model.Anchor.X},{model.Anchor.Y}) " +
                $"pivotOffset=({model.PivotOffset.X},{model.PivotOffset.Y}) " +
                $"desiredPivot=({desiredPivot.X},{desiredPivot.Y}) " +
                $"attemptedAnchor=({attemptedAnchor.X},{attemptedAnchor.Y}) " +
                $"failureCell=({result.Coordinate.X},{result.Coordinate.Y}) " +
                $"reason={result.Reason} cellState={result.CellState} occupant={result.OccupantId} " +
                $"boardFineSize=({level.FineColumns},{level.FineRows}) " +
                $"groupAnchors=[{groupAnchors}]");
        }

        private static void PrepareKinematicBody(Rigidbody2D body, bool interpolate = true)
        {
            body.simulated = true;
            body.bodyType = RigidbodyType2D.Kinematic;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.interpolation = interpolate
                ? RigidbodyInterpolation2D.Interpolate
                : RigidbodyInterpolation2D.None;
            body.useFullKinematicContacts = false;
            body.sleepMode = RigidbodySleepMode2D.NeverSleep;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            body.velocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.angularDrag = 0f;
            body.rotation = 0f;
        }

        private static void MoveBody(Rigidbody2D body, Vector2 targetPosition)
        {
            // The grid has already validated this authored position. Apply it
            // immediately so the visual and Physics2D query transforms agree.
            body.position = targetPosition;
            Physics2D.SyncTransforms();
        }



        private static bool TryGetSnapshotPiece(
            PuzzlePiece piece,
            out LevelBoardSnapshot snapshot,
            out PieceModel model)
        {
            snapshot = null;
            model = null;

            PrototypeBoard activeBoard = PrototypeBoard.Active;
            if (activeBoard == null)
                return false;

            snapshot = activeBoard.BoardSnapshot;
            return activeBoard.TryGetPieceModel(piece, out model);
        }

        private void UpdateDragTarget(Vector2 nextTarget)
        {
            pendingDragIntent += nextTarget - dragTarget;
            dragTarget = nextTarget;
        }

        private Vector2 ConsumeDragIntent()
        {
            Vector2 intent = pendingDragIntent;
            pendingDragIntent = Vector2.zero;
            return intent;
        }

        private void ProcessTouchInput()
        {
            Touch touch = FindActiveTouch();

            if (selectedPiece == null)
            {
                if (touch.phase == TouchPhase.Began)
                {
                    ClearFrozenGroupFeedback();
                    activeFingerId = touch.fingerId;
                    TrySelectPiece(
                        PointerWorldPosition(touch.position),
                        TouchSelectionRadiusInGridCells);
                }
                else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                {
                    ClearFrozenGroupFeedback();
                    activeFingerId = -1;
                }
                return;
            }

            if (touch.fingerId != activeFingerId)
                return;

            if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
            {
                UpdateDragTarget(PointerWorldPosition(touch.position) + grabOffset);
                ReleasePiece();
                activeFingerId = -1;
                return;
            }

            UpdateDragTarget(PointerWorldPosition(touch.position) + grabOffset);
        }

        private void ProcessMouseInput()
        {
            Vector2 pointer = PointerWorldPosition(Input.mousePosition);

            if (Input.GetMouseButtonDown(0))
            {
                ClearFrozenGroupFeedback();
                TrySelectPiece(pointer, MouseSelectionRadiusInGridCells);
            }

            if (selectedPiece != null &&
                (Input.GetMouseButton(0) || Input.GetMouseButtonUp(0)))
                UpdateDragTarget(pointer + grabOffset);

            if (selectedPiece != null && Input.GetMouseButtonUp(0))
                ReleasePiece();
            else if (selectedPiece == null && Input.GetMouseButtonUp(0))
                ClearFrozenGroupFeedback();
        }

        private Touch FindActiveTouch()
        {
            if (activeFingerId < 0)
                return Input.GetTouch(0);

            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                if (touch.fingerId == activeFingerId)
                    return touch;
            }

            return Input.GetTouch(0);
        }

        private void TrySelectPiece(Vector2 pointerPosition, float fallbackRadius)
        {
            // Pins and pieces can intentionally overlap. OverlapPoint returns only
            // one arbitrary collider, which made a pin hide the movable piece
            // beneath it. Check every hit and give PuzzlePiece colliders priority.
            PuzzlePiece piece = null;
            int hitCount = Physics2D.OverlapPointNonAlloc(pointerPosition, selectionHits);
            for (int i = 0; i < hitCount; i++)
            {
                Collider2D hit = selectionHits[i];
                piece = hit.GetComponentInParent<PuzzlePiece>();
                if (piece != null && !piece.IsFrozen && !piece.IsShredderInteractionLocked &&
                    (piece.GridFallView == null || !piece.GridFallView.IsAnimating))
                    break;
                piece = null;
            }

            if (piece == null)
            {
                float closestDistance = float.PositiveInfinity;
                hitCount = Physics2D.OverlapCircleNonAlloc(pointerPosition, fallbackRadius, selectionHits);
                for (int i = 0; i < hitCount; i++)
                {
                    Collider2D hit = selectionHits[i];
                    PuzzlePiece candidate = hit.GetComponentInParent<PuzzlePiece>();
                    if (candidate == null || candidate.IsFrozen || candidate.IsShredderInteractionLocked ||
                        (candidate.GridFallView != null && candidate.GridFallView.IsAnimating))
                        continue;

                    float distance = Vector2.SqrMagnitude(hit.ClosestPoint(pointerPosition) - pointerPosition);
                    if (distance >= closestDistance)
                        continue;

                    closestDistance = distance;
                    piece = candidate;
                }
            }

            if (piece == null)
                return;

            // A fall can be visually complete while its final presentation
            // callback has not yet converged the model back to Placed. Settle
            // only a non-animating stale fall before trying to begin a drag.
            PrototypeBoard activeBoard = PrototypeBoard.Active;
            if (activeBoard == null)
                return;

            if (!activeBoard.CanBeginPieceInteraction(piece))
            {
                ShowFrozenGroupFeedbackIfNeeded(activeBoard, piece);
                return;
            }

            activeBoard.StartTimer();
            if (activeBoard.TryGetPieceModel(piece, out PieceModel model) &&
                model.State == PieceState.Falling &&
                (piece.GridFallView == null || !piece.GridFallView.IsAnimating))
            {
                activeBoard.TrySetPieceState(piece, PieceState.Placed);
            }

            // The grid removes the held piece while it is being dragged. Retain
            // its last committed anchor so an invalid release can return to a
            // known legal board position without asking physics to decide.
            PieceModel selectedModel;
            hasSelectedPieceStartAnchor =
                TryGetSnapshotPiece(piece, out _, out selectedModel) &&
                selectedModel.IsOnBoard;
            if (hasSelectedPieceStartAnchor)
                selectedPieceStartAnchor = selectedModel.Anchor;

            // A grid-owned selection is only valid after its transition to
            // Dragging succeeds. Keep the piece logically on the board and occupied.
            if (PrototypeBoard.Active == null || !PrototypeBoard.Active.TryBeginPieceDrag(piece))
            {
                hasSelectedPieceStartAnchor = false;
                return;
            }

            Rigidbody2D body = piece.Body;
            selectedPiece = piece;
            PrototypeBoard.Active.CollectJointGroupPieces(piece, selectedJointGroupPieces);
            // A queued gravity plan or a still-owned fall tween can otherwise
            // restore an old body pose after this drag has started.
            pendingGridGravityMoves.Clear();
            for (int groupIndex = 0; groupIndex < selectedJointGroupPieces.Count; groupIndex++)
            {
                PuzzlePiece groupPiece = selectedJointGroupPieces[groupIndex];
                if (groupPiece == null || groupPiece.Body == null)
                    continue;

                PrepareKinematicBody(groupPiece.Body, interpolate: false);
                groupPiece.SetSelected(true);
            }
            Physics2D.SyncTransforms();

            grabOffset = body.position - pointerPosition;
            dragTarget = body.position;
            pendingDragIntent = Vector2.zero;
            fineDragRemainder = Vector2.zero;
            hasSelectedGroupReference = PrototypeBoard.Active.TryGetJointGroupDragReference(
                piece,
                out selectedGroupReferencePivot,
                out selectedPivotOffsetFromGroup);
            lastResolvedDragPosition = body.position;
            hasLastResolvedDragPosition = true;
        }

        private void ShowFrozenGroupFeedbackIfNeeded(PrototypeBoard board, PuzzlePiece piece)
        {
            if (piece == null || piece.IsFrozen)
                return;

            board.CollectJointGroupPieces(piece, selectedJointGroupPieces);
            for (int index = 0; index < selectedJointGroupPieces.Count; index++)
            {
                if (selectedJointGroupPieces[index] == null || !selectedJointGroupPieces[index].IsFrozen)
                    continue;

                frozenGroupFeedbackPiece = piece;
                piece.SetSelected(true);
                return;
            }
        }

        private void ClearFrozenGroupFeedback()
        {
            if (frozenGroupFeedbackPiece != null && frozenGroupFeedbackPiece != selectedPiece)
                frozenGroupFeedbackPiece.SetSelected(false);

            frozenGroupFeedbackPiece = null;
        }

        private void TakeGridPresentationOwnershipForDrag(PuzzlePiece piece)
        {
            if (piece == null)
                return;

            piece.GridFallView?.Cancel();
            gridFallingPieces.Remove(piece);
            if (gridReleasePresentationPiece == piece)
                gridReleasePresentationPiece = null;
        }

        private void ReleasePiece()
        {
            Rigidbody2D body = selectedPiece.Body;
            TryMoveSelectedPieceOnGrid(selectedPiece);

            PrototypeBoard activeBoard = PrototypeBoard.Active;
            activeBoard?.CollectJointGroupPieces(selectedPiece, selectedJointGroupPieces);
            bool isCombined = selectedJointGroupPieces.Count > 1;
            for (int groupIndex = 0; groupIndex < selectedJointGroupPieces.Count; groupIndex++)
            {
                PuzzlePiece groupPiece = selectedJointGroupPieces[groupIndex];
                if (groupPiece == null || groupPiece.Body == null)
                    continue;

                groupPiece.SetSelected(false);
                PrepareKinematicBody(groupPiece.Body);
            }
            Physics2D.SyncTransforms();
            PrepareKinematicBody(body);

            // Combined members share the same grid-snap transaction, but do
            // not run independent release tweens afterwards: that would make
            // their visuals diverge despite one authoritative group delta.
            if (isCombined)
            {
                if (TryCommitSafeSnapRelease(selectedPiece, out _))
                {
                    for (int groupIndex = 0; groupIndex < selectedJointGroupPieces.Count; groupIndex++)
                        SnapPieceToCommittedGridPosition(selectedJointGroupPieces[groupIndex]);
                }

                selectedPiece = null;
                hasSelectedPieceStartAnchor = false;
                hasLastResolvedDragPosition = false;
                hasSelectedGroupReference = false;
                pendingDragIntent = Vector2.zero;
                fineDragRemainder = Vector2.zero;
                return;
            }

            if (TryCommitSafeSnapRelease(selectedPiece, out Vector2 releasePosition))
            {
                PuzzlePiece releasePiece = selectedPiece;
                gridReleasePresentationPiece = releasePiece;
                if (!releasePiece.GridFallView.PlayReleaseTo(
                        releasePosition,
                        () => CompleteGridReleasePresentation(releasePiece)))
                {
                    gridReleasePresentationPiece = null;
                    MoveBody(body, releasePosition);
                }
                selectedPiece = null;
                hasSelectedPieceStartAnchor = false;
                hasLastResolvedDragPosition = false;
                hasSelectedGroupReference = false;
                pendingDragIntent = Vector2.zero;
                fineDragRemainder = Vector2.zero;
                return;
            }

            GridCoordinate restoreAnchor = selectedPieceStartAnchor;
            if (TryGetSnapshotPiece(selectedPiece, out _, out PieceModel currentModel))
                restoreAnchor = currentModel.Anchor;

            if (hasSelectedPieceStartAnchor &&
                TryRestoreGridRelease(selectedPiece, restoreAnchor, out Vector2 restorePosition))
            {
                MoveBody(body, restorePosition);
                selectedPiece = null;
                hasSelectedPieceStartAnchor = false;
                hasLastResolvedDragPosition = false;
                hasSelectedGroupReference = false;
                pendingDragIntent = Vector2.zero;
                fineDragRemainder = Vector2.zero;
                return;
            }

            Debug.LogWarning(
                $"[GridRelease] Failed to restore '{selectedPiece.name}' to its committed grid anchor.",
                selectedPiece);

            selectedPiece = null;
            hasSelectedPieceStartAnchor = false;
            hasLastResolvedDragPosition = false;
            hasSelectedGroupReference = false;
            pendingDragIntent = Vector2.zero;
            fineDragRemainder = Vector2.zero;
        }

        private void CompleteGridReleasePresentation(PuzzlePiece piece)
        {
            if (gridReleasePresentationPiece != piece)
                return;

            if (piece != null && piece.Body != null &&
                TryGetSnapshotPiece(piece, out _, out PieceModel model))
            {
                GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
                if (level != null)
                {
                    GridCoordinate pivot = new GridCoordinate(
                        model.Anchor.X - model.PivotOffset.X,
                        model.Anchor.Y - model.PivotOffset.Y);
                    MoveBody(piece.Body, GravityLevelGridCoordinates.FineCellToWorld(level, pivot));
                }
            }

            gridReleasePresentationPiece = null;
        }

        private bool TryCommitSafeSnapRelease(
            PuzzlePiece piece,
            out Vector2 releasePosition)
        {
            releasePosition = default;
            if (piece == null || !hasSelectedPieceStartAnchor ||
                !TryGetSnapshotPiece(piece, out _, out PieceModel model))
            {
                return TryCommitCurrentGridRelease(piece, out releasePosition);
            }

            GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
            PrototypeBoard board = PrototypeBoard.Active;
            if (level == null || board == null)
                return TryCommitCurrentGridRelease(piece, out releasePosition);

            Vector2 currentPosition = hasLastResolvedDragPosition
                ? lastResolvedDragPosition
                : piece.Body.position;
            GridCoordinate snappedPivot;
            if (hasSelectedGroupReference)
            {
                GridCoordinate currentSelectedPivot = WorldToNearestFineCell(level, currentPosition);
                GridCoordinate currentGroupPivot = new GridCoordinate(
                    currentSelectedPivot.X - selectedPivotOffsetFromGroup.X,
                    currentSelectedPivot.Y - selectedPivotOffsetFromGroup.Y);
                GridCoordinate snappedGroupPivot = SnapToBackgroundAlignedPivot(
                    level,
                    GravityLevelGridCoordinates.FineCellToWorld(level, currentGroupPivot),
                    selectedGroupReferencePivot);
                snappedPivot = snappedGroupPivot.Offset(selectedPivotOffsetFromGroup);
            }
            else
            {
                GridCoordinate referencePivot = new GridCoordinate(
                    selectedPieceStartAnchor.X - model.PivotOffset.X,
                    selectedPieceStartAnchor.Y - model.PivotOffset.Y);
                snappedPivot = SnapToBackgroundAlignedPivot(
                    level,
                    currentPosition,
                    referencePivot);
            }
            Vector2 snappedPosition = GravityLevelGridCoordinates.FineCellToWorld(level, snappedPivot);

            // Snapping is only presentation alignment after the player has
            // traversed a valid path. It may not complete a blocked path or
            // move through a corner that the active drag could not cross.
            GridCoordinate snappedAnchor = snappedPivot.Offset(model.PivotOffset);
            if (!board.CanTraversePieceGroupTo(piece, snappedAnchor))
            {
                return TryCommitCurrentGridRelease(piece, out releasePosition);
            }

            if (!board.TryMovePieceOnGrid(piece, snappedAnchor, out _))
                return TryCommitCurrentGridRelease(piece, out releasePosition);

            lastResolvedDragPosition = snappedPosition;
            releasePosition = snappedPosition;
            return true;
        }

        private static bool TryCommitCurrentGridRelease(
            PuzzlePiece piece,
            out Vector2 releasePosition)
        {
            releasePosition = default;
            if (piece == null || piece.Body == null ||
                !TryGetSnapshotPiece(piece, out LevelBoardSnapshot snapshot, out PieceModel model))
                return false;

            GravityLevelDefinition activeLevel = GravityLevelRuntime.FindLevelToPlay();
            PrototypeBoard activeBoard = PrototypeBoard.Active;
            if (activeLevel == null || activeBoard == null)
                return false;

            GridPlacementResult placementResult = GridPlacementResult.Failure(
                GridPlacementFailureReason.OutOfBounds,
                model.Anchor,
                GridCellState.Blocked,
                default);
            if (!snapshot.Grid.IsInside(model.Anchor) ||
                !activeBoard.TryMovePieceOnGrid(piece, model.Anchor, out placementResult))
            {
                Debug.LogWarning(
                    $"[GridRelease] Rejected '{piece.name}' id={model.Id}, " +
                    $"modelOnBoard={model.IsOnBoard}, targetAnchor=({model.Anchor.X},{model.Anchor.Y}), " +
                    $"reason={placementResult.Reason}, cell=({placementResult.Coordinate.X},{placementResult.Coordinate.Y}), " +
                    $"cellState={placementResult.CellState}, occupant={placementResult.OccupantId}.",
                    piece);
                return false;
            }

            GridCoordinate pivot = new GridCoordinate(
                model.Anchor.X - model.PivotOffset.X,
                model.Anchor.Y - model.PivotOffset.Y);
            releasePosition = GravityLevelGridCoordinates.FineCellToWorld(activeLevel, pivot);
            return true;
        }

        private static GridCoordinate SnapToBackgroundAlignedPivot(
            GravityLevelDefinition level,
            Vector2 currentPosition,
            GridCoordinate referencePivot)
        {
            GridCoordinate nearestPivot = WorldToNearestFineCell(level, currentPosition);
            int subdivisions = Mathf.Max(1, level.subdivisions);
            return new GridCoordinate(
                referencePivot.X +
                Mathf.RoundToInt((nearestPivot.X - referencePivot.X) / (float)subdivisions) * subdivisions,
                referencePivot.Y +
                Mathf.RoundToInt((nearestPivot.Y - referencePivot.Y) / (float)subdivisions) * subdivisions);
        }

        private static bool TryRestoreGridRelease(
            PuzzlePiece piece,
            GridCoordinate restoreAnchor,
            out Vector2 restorePosition)
        {
            restorePosition = default;
            LevelBoardSnapshot snapshot;
            PieceModel model;
            if (!TryGetSnapshotPiece(piece, out snapshot, out model) ||
                PrototypeBoard.Active == null)
                return false;

            GravityLevelDefinition activeLevel = GravityLevelRuntime.FindLevelToPlay();
            GridPlacementResult placementResult;
            if (activeLevel == null ||
                !PrototypeBoard.Active.TryMovePieceOnGrid(piece, restoreAnchor, out placementResult))
                return false;

            GridCoordinate restorePivot = new GridCoordinate(
                restoreAnchor.X - model.PivotOffset.X,
                restoreAnchor.Y - model.PivotOffset.Y);
            restorePosition = GravityLevelGridCoordinates.FineCellToWorld(activeLevel, restorePivot);
            return true;
        }

        private static GridCoordinate WorldToNearestFineCell(
            GravityLevelDefinition level,
            Vector2 worldPosition)
        {
            // WorldToFineCell deliberately returns the cell containing a point.
            // A released piece instead needs the nearest cell centre; otherwise
            // it is biased down and left by up to one fine cell on every drop.
            float x = (worldPosition.x + level.boardColumns * .5f) * level.subdivisions - .5f;
            float y = (worldPosition.y + level.boardRows * .5f) * level.subdivisions - .5f;
            return new GridCoordinate(Mathf.RoundToInt(x), Mathf.RoundToInt(y));
        }

        private Vector2 PointerWorldPosition(Vector2 screenPoint)
        {
            Vector3 screenPosition = screenPoint;
            screenPosition.z = -gameCamera.transform.position.z;
            return gameCamera.ScreenToWorldPoint(screenPosition);
        }

    }
}
