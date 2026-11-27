using System.Collections.Generic;
using GravityPuzzle.Core.Grid;
using GravityPuzzle.Core.StateMachine;
using GravityPuzzle.Gameplay.Tutorial;
using GravityPuzzle.Gameplay.Gravity;
using GravityPuzzle.Infrastructure.Services;
using GravityPuzzle.Gameplay.Pieces;
using GravityPuzzle.Presentation.Views;
using UnityEngine;

namespace GravityPuzzle
{
    /// <summary>
    /// Handles input for every piece from one place. We raycast through whichever
    /// child collider was touched, then move the complete parent piece.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class PuzzleDragController : MonoBehaviour
    {
        private Camera gameCamera;
        private PuzzlePiece selectedPiece;
        // Feedback-only selection for a movable member whose combined group
        // is locked by Ice. It never enters the Dragging lifecycle state.
        private PuzzlePiece frozenGroupFeedbackPiece;
        private PuzzlePiece gridReleasePresentationPiece;
        // A joined release commits one grid transaction, so its members must
        // share one presentation batch instead of some members snapping early.
        private readonly List<PuzzlePiece> gridReleasePresentationPieces = new List<PuzzlePiece>();
        private readonly List<Vector2> gridReleasePresentationTargets = new List<Vector2>();
        private int pendingGridReleasePresentationCount;
        private readonly HashSet<PuzzlePiece> gridFallingPieces = new HashSet<PuzzlePiece>();
        private readonly List<PuzzlePiece> finishedGridFalls = new List<PuzzlePiece>();
        private readonly List<PuzzlePiece> retargetableGridFalls = new List<PuzzlePiece>();
        private readonly List<PuzzlePiece> selectedJointGroupPieces = new List<PuzzlePiece>();
        private readonly Queue<GridGravityMove> pendingGridGravityMoves =
            new Queue<GridGravityMove>();
        private readonly List<GridGravityMove> candidateJointGravityMoves = new List<GridGravityMove>();
        private readonly List<GridGravityMove> selectedJointGravityMoves = new List<GridGravityMove>();
        private readonly HashSet<int> gravityBatchColumns = new HashSet<int>();
        private readonly HashSet<int> rejectedGravityPieceIds = new HashSet<int>();
        private readonly List<PieceModel> gravityStackModels = new List<PieceModel>();
        private readonly List<GridCoordinate> gravityStackTargets = new List<GridCoordinate>();
        private readonly HashSet<int> gravityStackIds = new HashSet<int>();
        private readonly List<PieceModel> gravityStackCandidateModels = new List<PieceModel>();
        private readonly HashSet<int> gravityStackCandidateIds = new HashSet<int>();
        private readonly HashSet<int> retargetVisitedIds = new HashSet<int>();
        private readonly List<GridGravityMove> retargetGroupMoves = new List<GridGravityMove>();
        private readonly List<PuzzlePiece> captureSelectedGroupBuffer = new List<PuzzlePiece>();
        private readonly List<PuzzlePiece> completionJointGroupBuffer = new List<PuzzlePiece>();
        private readonly Dictionary<int, bool> coordinatedFallReleaseMode = new Dictionary<int, bool>();
        private readonly List<PuzzlePiece> gravityGroupPieces = new List<PuzzlePiece>();
        private GridCoordinate selectedPieceStartAnchor;
        private bool hasSelectedPieceStartAnchor;
        private GridCoordinate lastCanonicalDragAnchor;
        private bool hasLastCanonicalDragAnchor;
        private readonly List<GridCoordinate> snapCandidateBuffer = new List<GridCoordinate>(36);
        private readonly List<PuzzlePiece> selectionResetGroupBuffer = new List<PuzzlePiece>();
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
        private const float FineCellDragThreshold = 1f;
        private const float SubCellAxisDeadzone = .02f;
        private const float DragStepEpsilon = 1e-5f;
        private const int MaxDragSubSteps = 256;
        public static PuzzleDragController Instance { get; private set; }
        private PrototypeBoard subscribedBoard;
        private bool inputLockedByGameState;
        private bool isAdvancingGravity;
        private bool isRetargetingGravity;
        private bool hasPendingGravityWakeup;

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
            Instance.hasPendingGravityWakeup = true;

            // A committed grid fall already owns a distinct target footprint,
            // so it cannot conflict with a fresh cascade plan. Do not delay a
            // shredder-cleared row until an unrelated fall presentation ends.
            if (Instance.selectedPiece == null)
            {
                Instance.hasPendingGravityWakeup = false;
                Instance.RetargetActiveGridFalls();
                Instance.AdvanceGridGravityPresentation();
            }
        }

        /// <summary>
        /// Clears grid fall and release bookkeeping when a piece is captured
        /// by the shredder. This does not snap position or force state to Placed,
        /// leaving full transform and lifecycle ownership to the shredder.
        /// </summary>
        public static void CancelGridFallForShredderCapture(PuzzlePiece piece)
        {
            if (piece == null)
                return;

            piece.GridFallView?.Cancel();

            if (Instance == null)
                return;

            Instance.gridFallingPieces.Remove(piece);
            Instance.coordinatedFallReleaseMode.Remove(piece.SourcePieceId);
            Instance.CancelGridReleasePresentationIfContains(piece);
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
                    piece.SetGridPresentationPosition(correctedPosition);
                }
            }

            if (Instance == null)
                return;

            Instance.gridFallingPieces.Remove(piece);
            Instance.coordinatedFallReleaseMode.Remove(piece.SourcePieceId);
            Instance.CancelGridReleasePresentationIfContains(piece);
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
                if (selectedPiece != null)
                {
                    CommitDragEnd(allowDropFeedback: false, allowGravityAdvance: false);
                    activeFingerId = -1;
                }
                else
                {
                    ClearInputSelection();
                }
                return;
            }

            if (BoosterFirstUseTutorialRuntime.Gate != null &&
                BoosterFirstUseTutorialRuntime.Gate.BlocksBoardDrag)
            {
                if (selectedPiece != null)
                {
                    CommitDragEnd(allowDropFeedback: false, allowGravityAdvance: true);
                    activeFingerId = -1;
                }
                else
                {
                    ClearInputSelection();
                }
                return;
            }

            // The shredder can capture a piece while it is still held. Drop the
            // drag controller's reference immediately so it cannot move the
            // piece again or queue a release snap during the shred animation.
            if (selectedPiece != null && selectedPiece.IsShredderInteractionLocked)
            {
                ClearInputSelection();
            }

            if (HammerBooster.IsTargeting || RocketBooster.IsTargeting)
            {
                if (selectedPiece != null)
                {
                    CommitDragEnd(allowDropFeedback: false, allowGravityAdvance: true);
                    activeFingerId = -1;
                }
                return;
            }

            if (LevelTimerUI.IsGameOver)
            {
                if (selectedPiece != null)
                {
                    CommitDragEnd(allowDropFeedback: false, allowGravityAdvance: false);
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
            {
                subscribedBoard.GameStateChanged -= HandleGameStateChanged;
                subscribedBoard.BoardOccupancyChanged -= HandleBoardOccupancyChanged;
            }

            subscribedBoard = nextBoard;
            inputLockedByGameState = IsGameplayInteractionLocked(subscribedBoard);
            hasPendingGravityWakeup = nextBoard != null && !inputLockedByGameState;

            if (subscribedBoard != null)
            {
                subscribedBoard.GameStateChanged += HandleGameStateChanged;
                subscribedBoard.BoardOccupancyChanged += HandleBoardOccupancyChanged;
            }
        }

        private void HandleBoardOccupancyChanged()
        {
            if (!isAdvancingGravity && !isRetargetingGravity)
                hasPendingGravityWakeup = true;
        }

        private void HandleGameStateChanged(GameState previousState, GameState nextState)
        {
            inputLockedByGameState = IsGameplayInteractionLocked(subscribedBoard);

            if (inputLockedByGameState)
                StopGameplayPresentation();
            else
                hasPendingGravityWakeup = true;
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
            if (selectedPiece != null)
            {
                CommitDragEnd(allowDropFeedback: false, allowGravityAdvance: false);
                activeFingerId = -1;
            }

            ClearInputSelection();
            HammerBooster.CancelActiveSelection();
            RocketBooster.CancelActiveSelection();
            pendingGridGravityMoves.Clear();

            foreach (PuzzlePiece fallingPiece in gridFallingPieces)
                fallingPiece.GridFallView?.Cancel();
            gridFallingPieces.Clear();
            coordinatedFallReleaseMode.Clear();
            retargetVisitedIds.Clear();
            retargetGroupMoves.Clear();
            gravityStackCandidateModels.Clear();
            gravityStackCandidateIds.Clear();
            captureSelectedGroupBuffer.Clear();
            completionJointGroupBuffer.Clear();
            hasPendingGravityWakeup = false;

            CancelGridReleasePresentation();

        }

        private void ClearInputSelection()
        {
            PrototypeBoard board = PrototypeBoard.Active;
            if (selectedPiece != null)
            {
                board?.CollectJointGroupPieces(selectedPiece, selectionResetGroupBuffer);
                for (int index = 0; index < selectionResetGroupBuffer.Count; index++)
                    selectionResetGroupBuffer[index]?.SetSelected(false);
                selectionResetGroupBuffer.Clear();
            }

            ClearFrozenGroupFeedback();
            ResetDragSelectionState();
            activeFingerId = -1;
        }

        private void ResetDragSelectionState()
        {
            selectedPiece = null;
            hasLastResolvedDragPosition = false;
            hasSelectedPieceStartAnchor = false;
            hasLastCanonicalDragAnchor = false;
            pendingDragIntent = Vector2.zero;
            fineDragRemainder = Vector2.zero;
        }

        private void CapturePiecesAtShredderBoundary()
        {
            BlockShredder shredder = BlockShredder.Instance;
            IReadOnlyList<ShredderCatchZone> zones = ShredderCatchZone.ActiveZones;
            if (shredder == null || zones.Count == 0)
                return;

            captureSelectedGroupBuffer.Clear();
            if (selectedPiece != null)
                PrototypeBoard.Active?.CollectJointGroupPieces(selectedPiece, captureSelectedGroupBuffer);

            IReadOnlyList<PuzzlePiece> pieces = PuzzlePiece.ActivePieces;
            for (int pieceIndex = 0; pieceIndex < pieces.Count; pieceIndex++)
            {
                PuzzlePiece piece = pieces[pieceIndex];
                if (piece == null || piece == selectedPiece || piece.IsSelected ||
                    piece.IsShredderInteractionLocked || piece.IsMovementLocked ||
                    ContainsPieceReference(captureSelectedGroupBuffer, piece))
                    continue;

                for (int zoneIndex = 0; zoneIndex < zones.Count; zoneIndex++)
                {
                    ShredderCatchZone zone = zones[zoneIndex];
                    if (zone == null)
                        continue;
                    if (!zone.ContainsMapExitFootprint(piece))
                        continue;

                    // The feed cancels its presentation tween internally. Clear
                    // this controller's matching fall record first, without snapping
                    // or forcing a Placed state, so shredder owns the handoff.
                    CancelGridFallForShredderCapture(piece);
                    if (shredder.TryCapturePiece(piece, zone) && selectedPiece == piece)
                    {
                        selectedPiece = null;
                        activeFingerId = -1;
                    }

                    break;
                }
            }
        }

        private static bool ContainsPieceReference(IReadOnlyList<PuzzlePiece> list, PuzzlePiece target)
        {
            for (int index = 0; index < list.Count; index++)
            {
                if (list[index] == target)
                    return true;
            }

            return false;
        }

        private void FixedUpdate()
        {
            if (inputLockedByGameState)
                return;

            CapturePiecesAtShredderBoundary();
            if (selectedPiece != null)
                return;

            if (hasPendingGravityWakeup)
            {
                hasPendingGravityWakeup = false;
                if (gridFallingPieces.Count > 0)
                    RetargetActiveGridFalls();
                AdvanceGridGravityPresentation();
            }
            else if (gridFallingPieces.Count == 0)
            {
                AdvanceGridGravityPresentation();
            }
        }

        private bool AdvanceGridGravityPresentation()
        {
            if (isAdvancingGravity)
                return gridFallingPieces.Count > 0;

            isAdvancingGravity = true;
            try
            {
                if (gridReleasePresentationPiece != null && !IsGridReleasePresentationAnimating())
                    ClearGridReleasePresentationState();

                RemoveFinishedGridFalls();

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
            finally
            {
                isAdvancingGravity = false;
            }
        }

        /// <summary>
        /// Extends an active fall when a shredder releases additional board
        /// rows. The grid computes the newly legal target first, so this never
        /// lets presentation enter an obstacle, a reservation, or another
        /// piece's committed footprint.
        /// </summary>
        private void RetargetActiveGridFalls()
        {
            if (gridFallingPieces.Count == 0 || isRetargetingGravity)
                return;

            PrototypeBoard activeBoard = PrototypeBoard.Active;
            LevelBoardSnapshot snapshot = activeBoard != null
                ? activeBoard.BoardSnapshot
                : null;
            GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
            if (snapshot == null || level == null)
                return;

            isRetargetingGravity = true;
            try
            {
                retargetableGridFalls.Clear();
                foreach (PuzzlePiece piece in gridFallingPieces)
                {
                    if (piece != null && piece.GridFallView != null && piece.GridFallView.IsAnimating)
                        retargetableGridFalls.Add(piece);
                }

                // Process lowest occupied pieces first (by grid cell Y, not pivot Y)
                // so chained extensions unlock space for pieces above.
                SortPiecesByLowestGridY(retargetableGridFalls, snapshot);

                retargetVisitedIds.Clear();
                for (int index = 0; index < retargetableGridFalls.Count; index++)
                {
                    PuzzlePiece piece = retargetableGridFalls[index];
                    if (piece == null || piece.IsShredderInteractionLocked ||
                        !retargetVisitedIds.Add(piece.SourcePieceId) ||
                        !snapshot.TryGetPiece(piece.SourcePieceId, out PieceModel model) ||
                        model.State != PieceState.Falling)
                    {
                        continue;
                    }

                    activeBoard.CollectJointGroupPieces(model.Id, gravityGroupPieces);
                    if (gravityGroupPieces.Count <= 1)
                    {
                        if (!snapshot.Grid.TryGetFallTarget(model, out GridCoordinate targetAnchor, activeBoard.CanMovePieceDelegate) ||
                            !TryTrimSingleFallTargetToCanonicalY(activeBoard, snapshot, level, model, ref targetAnchor) ||
                            targetAnchor.Equals(model.Anchor))
                        {
                            continue;
                        }

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
                        PuzzlePiece capturedPiece = piece;
                        capturedPiece.GridFallView.RetargetReleaseTo(
                            targetPosition,
                            () => CompleteGridGravityPresentation(capturedPiece));
                        continue;
                    }

                    bool allMembersEligible = true;
                    for (int m = 0; m < gravityGroupPieces.Count; m++)
                    {
                        PuzzlePiece memberPiece = gravityGroupPieces[m];
                        if (memberPiece != null)
                            retargetVisitedIds.Add(memberPiece.SourcePieceId);

                        if (memberPiece == null || memberPiece.IsShredderInteractionLocked || memberPiece.IsMovementLocked ||
                            memberPiece.GridFallView == null || !memberPiece.GridFallView.IsAnimating ||
                            !gridFallingPieces.Contains(memberPiece) ||
                            !snapshot.TryGetPiece(memberPiece.SourcePieceId, out PieceModel memberModel) ||
                            memberModel.State != PieceState.Falling)
                        {
                            allMembersEligible = false;
                        }
                    }

                    if (!allMembersEligible)
                        continue;

                    retargetGroupMoves.Clear();
                    if (!activeBoard.TryPlanJointGroupGravityRetarget(model.Id, retargetGroupMoves))
                        continue;

                    bool anyChanged = false;
                    for (int m = 0; m < retargetGroupMoves.Count; m++)
                    {
                        if (!retargetGroupMoves[m].ToAnchor.Equals(retargetGroupMoves[m].FromAnchor))
                        {
                            anyChanged = true;
                            break;
                        }
                    }

                    if (!anyChanged || !activeBoard.TryCommitJointGroupGravityRetarget(retargetGroupMoves, out _))
                        continue;

                    for (int m = 0; m < retargetGroupMoves.Count; m++)
                    {
                        GridGravityMove move = retargetGroupMoves[m];
                        PuzzlePiece memberPiece = FindActivePiece(move.PieceId);
                        if (memberPiece == null || !snapshot.TryGetPiece(move.PieceId, out PieceModel memberModel))
                            continue;

                        GridCoordinate targetPivot = new GridCoordinate(
                            move.ToAnchor.X - memberModel.PivotOffset.X,
                            move.ToAnchor.Y - memberModel.PivotOffset.Y);
                        Vector2 targetPosition = GravityLevelGridCoordinates.FineCellToWorld(level, targetPivot);
                        PuzzlePiece capturedMember = memberPiece;
                        capturedMember.GridFallView.RetargetReleaseTo(
                            targetPosition,
                            () => CompleteGridGravityPresentation(capturedMember));
                    }
                }

                retargetableGridFalls.Clear();
                retargetVisitedIds.Clear();
            }
            finally
            {
                isRetargetingGravity = false;
            }
        }

        private static void SortPiecesByLowestGridY(List<PuzzlePiece> pieces, LevelBoardSnapshot snapshot)
        {
            for (int i = 1; i < pieces.Count; i++)
            {
                PuzzlePiece current = pieces[i];
                int currentKey = GetSortLowestGridY(snapshot, current);
                int j = i - 1;
                while (j >= 0 && GetSortLowestGridY(snapshot, pieces[j]) > currentKey)
                {
                    pieces[j + 1] = pieces[j];
                    j--;
                }
                pieces[j + 1] = current;
            }
        }

        private static int GetSortLowestGridY(LevelBoardSnapshot snapshot, PuzzlePiece piece)
        {
            if (piece != null && snapshot != null && snapshot.TryGetPiece(piece.SourcePieceId, out PieceModel model))
                return model.GetLowestWorldCellY();

            if (piece != null && piece.TryGetActiveCellVerticalExtents(out float lowestCellY, out _))
                return Mathf.RoundToInt(lowestCellY * 1000f);

            return int.MaxValue;
        }

        private bool TryBuildSettledGridGravityPlan(
            PrototypeBoard activeBoard,
            LevelBoardSnapshot snapshot)
        {
            gravityBatchColumns.Clear();
            coordinatedFallReleaseMode.Clear();
            rejectedGravityPieceIds.Clear();
            bool committedAny = false;
            while (TryCommitIndependentGravityGroup(activeBoard, snapshot))
                committedAny = true;
            return committedAny;
        }

        private bool TryCommitIndependentGravityGroup(
            PrototypeBoard activeBoard,
            LevelBoardSnapshot snapshot)
        {
            while (true)
            {
                selectedJointGravityMoves.Clear();
                int selectedLandingY = int.MaxValue;
                int selectedSourceId = int.MaxValue;
                IReadOnlyList<PieceModel> models = snapshot.Pieces;
                for (int index = 0; index < models.Count; index++)
                {
                    PieceModel model = models[index];
                    if (rejectedGravityPieceIds.Contains(model.Id) || !IsEligibleForGridGravity(model))
                        continue;

                    activeBoard.CollectJointGroupPieces(model.Id, gravityGroupPieces);
                    if (!IsEligibleJointGravityGroup(gravityGroupPieces, model.Id))
                        continue;

                    bool containsRejectedMember = false;
                    for (int memberIndex = 0; memberIndex < gravityGroupPieces.Count; memberIndex++)
                    {
                        if (gravityGroupPieces[memberIndex] != null &&
                            rejectedGravityPieceIds.Contains(gravityGroupPieces[memberIndex].SourcePieceId))
                        {
                            containsRejectedMember = true;
                            break;
                        }
                    }
                    if (containsRejectedMember)
                        continue;

                    candidateJointGravityMoves.Clear();
                    if (!activeBoard.TryPlanJointGroupGravityMove(model.Id, candidateJointGravityMoves))
                        continue;

                    bool sharesBatchColumn = false;
                    for (int moveIndex = 0; moveIndex < candidateJointGravityMoves.Count && !sharesBatchColumn; moveIndex++)
                    {
                        if (!snapshot.TryGetPiece(candidateJointGravityMoves[moveIndex].PieceId, out PieceModel member))
                        {
                            sharesBatchColumn = true;
                            break;
                        }
                        for (int cellIndex = 0; cellIndex < member.LocalCells.Count; cellIndex++)
                        {
                            if (gravityBatchColumns.Contains(member.GetWorldCell(cellIndex).X))
                            {
                                sharesBatchColumn = true;
                                break;
                            }
                        }
                    }
                    if (sharesBatchColumn)
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

                if (selectedJointGravityMoves.Count == 0)
                    return false;

                int initialMoveCount = selectedJointGravityMoves.Count;
                int rootDistance = selectedJointGravityMoves[0].FromAnchor.Y - selectedJointGravityMoves[0].ToAnchor.Y;
                bool allSameDistance = rootDistance > 0;
                for (int i = 1; i < selectedJointGravityMoves.Count && allSameDistance; i++)
                {
                    if (selectedJointGravityMoves[i].FromAnchor.Y - selectedJointGravityMoves[i].ToAnchor.Y != rootDistance)
                        allSameDistance = false;
                }

                bool coordinated = allSameDistance &&
                    TryExpandGravitySupportStack(activeBoard, snapshot, selectedJointGravityMoves);
                bool committed = false;
                if (coordinated)
                {
                    committed = activeBoard.TryCommitCoordinatedGravityMove(selectedJointGravityMoves, out _);
                    if (!committed)
                    {
                        // Fall back to attempting the root move(s) before rejecting the group.
                        while (selectedJointGravityMoves.Count > initialMoveCount)
                            selectedJointGravityMoves.RemoveAt(selectedJointGravityMoves.Count - 1);
                        coordinated = false;
                        committed = activeBoard.TryCommitJointGroupGravityMove(selectedJointGravityMoves, out _);
                    }
                }
                else
                {
                    committed = activeBoard.TryCommitJointGroupGravityMove(selectedJointGravityMoves, out _);
                }

                if (!committed)
                {
                    for (int moveIndex = 0; moveIndex < selectedJointGravityMoves.Count; moveIndex++)
                        rejectedGravityPieceIds.Add(selectedJointGravityMoves[moveIndex].PieceId);
                    continue;
                }

                if (coordinated && snapshot.TryGetPiece(selectedJointGravityMoves[0].PieceId, out PieceModel stackRoot))
                {
                    bool releaseMode = IsSettlingAboveShredderReservation(snapshot, stackRoot);
                    for (int index = 0; index < selectedJointGravityMoves.Count; index++)
                        coordinatedFallReleaseMode[selectedJointGravityMoves[index].PieceId] = releaseMode;
                }

                for (int index = 0; index < selectedJointGravityMoves.Count; index++)
                {
                    if (snapshot.TryGetPiece(selectedJointGravityMoves[index].PieceId, out PieceModel member))
                    {
                        for (int cellIndex = 0; cellIndex < member.LocalCells.Count; cellIndex++)
                            gravityBatchColumns.Add(member.GetWorldCell(cellIndex).X);
                    }
                    pendingGridGravityMoves.Enqueue(selectedJointGravityMoves[index]);
                }
                return true;
            }
        }

        private bool TryExpandGravitySupportStack(
            PrototypeBoard board, LevelBoardSnapshot snapshot, List<GridGravityMove> rootMoves)
        {
            if (rootMoves == null || rootMoves.Count == 0)
                return false;

            gravityStackModels.Clear();
            gravityStackIds.Clear();

            int rootDistance = rootMoves[0].FromAnchor.Y - rootMoves[0].ToAnchor.Y;
            if (rootDistance <= 0)
                return false;

            PuzzlePiece rootPiece = FindActivePiece(rootMoves[0].PieceId);
            if (rootPiece == null || rootPiece.GridFallView == null)
                return false;

            for (int i = 0; i < rootMoves.Count; i++)
            {
                if (!snapshot.TryGetPiece(rootMoves[i].PieceId, out PieceModel rootModel))
                    return false;
                gravityStackModels.Add(rootModel);
                gravityStackIds.Add(rootModel.Id);
            }

            int initialCount = gravityStackModels.Count;
            bool added;
            do
            {
                added = false;
                for (int index = 0; index < snapshot.Pieces.Count; index++)
                {
                    PieceModel candidate = snapshot.Pieces[index];
                    if (gravityStackIds.Contains(candidate.Id) || !IsEligibleForGridGravity(candidate))
                        continue;

                    board.CollectJointGroupPieces(candidate.Id, gravityGroupPieces);
                    if (gravityGroupPieces.Count == 0 ||
                        !IsEligibleJointGravityGroup(gravityGroupPieces, candidate.Id))
                        continue;

                    bool validGroup = true;
                    gravityStackCandidateModels.Clear();
                    gravityStackCandidateIds.Clear();
                    for (int m = 0; m < gravityGroupPieces.Count; m++)
                    {
                        PuzzlePiece memberPiece = gravityGroupPieces[m];
                        if (memberPiece == null ||
                            gravityStackIds.Contains(memberPiece.SourcePieceId) ||
                            memberPiece.GridFallView == null ||
                            memberPiece.GridFallView.Config != rootPiece.GridFallView.Config ||
                            !snapshot.TryGetPiece(memberPiece.SourcePieceId, out PieceModel memberModel))
                        {
                            validGroup = false;
                            break;
                        }

                        gravityStackCandidateModels.Add(memberModel);
                        gravityStackCandidateIds.Add(memberModel.Id);
                    }
                    if (!validGroup)
                        continue;

                    bool supportedByStack = false;
                    bool independentSupport = false;
                    for (int m = 0; m < gravityStackCandidateModels.Count && !independentSupport; m++)
                    {
                        PieceModel groupMember = gravityStackCandidateModels[m];
                        for (int cellIndex = 0; cellIndex < groupMember.LocalCells.Count; cellIndex++)
                        {
                            GridCoordinate worldCell = groupMember.GetWorldCell(cellIndex);
                            if (gravityBatchColumns.Contains(worldCell.X))
                            {
                                independentSupport = true;
                                break;
                            }
                            GridCoordinate below = worldCell.Offset(new GridCoordinate(0, -1));
                            if (snapshot.Grid.TryGetOccupantId(below, out int occupantId))
                            {
                                if (gravityStackCandidateIds.Contains(occupantId))
                                    continue;
                                if (gravityStackIds.Contains(occupantId))
                                    supportedByStack = true;
                                else
                                    independentSupport = true;
                            }
                            else if (snapshot.Grid.GetCellState(below) != GridCellState.Empty)
                                independentSupport = true;
                        }
                    }
                    if (!supportedByStack || independentSupport)
                        continue;

                    int countBeforeCandidate = gravityStackModels.Count;
                    for (int m = 0; m < gravityStackCandidateModels.Count; m++)
                        gravityStackModels.Add(gravityStackCandidateModels[m]);

                    bool safe = snapshot.Grid.TryGetGroupFallTarget(gravityStackModels, gravityStackTargets, board.CanMovePieceDelegate) &&
                        rootMoves[0].FromAnchor.Y - gravityStackTargets[0].Y == rootDistance;
                    if (!safe)
                    {
                        gravityStackModels.RemoveRange(countBeforeCandidate, gravityStackModels.Count - countBeforeCandidate);
                        continue;
                    }
                    for (int m = 0; m < gravityStackCandidateModels.Count; m++)
                        gravityStackIds.Add(gravityStackCandidateModels[m].Id);
                    added = true;
                }
            }
            while (added);

            if (gravityStackModels.Count == initialCount)
                return false;

            for (int index = initialCount; index < gravityStackModels.Count; index++)
            {
                PieceModel member = gravityStackModels[index];
                rootMoves.Add(new GridGravityMove(member.Id, member.Anchor,
                    member.Anchor.Offset(new GridCoordinate(0, -rootDistance))));
            }
            return true;
        }

        private bool IsPieceInGridReleasePresentation(int sourcePieceId)
        {
            if (gridReleasePresentationPiece != null && gridReleasePresentationPiece.SourcePieceId == sourcePieceId)
                return true;

            for (int index = 0; index < gridReleasePresentationPieces.Count; index++)
            {
                PuzzlePiece releasePiece = gridReleasePresentationPieces[index];
                if (releasePiece != null && releasePiece.SourcePieceId == sourcePieceId)
                    return true;
            }

            return false;
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
                if (piece == null || piece.IsMovementLocked || piece.IsShredderInteractionLocked ||
                    piece.GridFallView == null || !piece.GridFallView.CanPlay ||
                    (Instance != null && Instance.IsPieceInGridReleasePresentation(piece.SourcePieceId)) ||
                    !board.TryGetPieceModel(piece, out PieceModel model) ||
                    model.State != PieceState.Placed ||
                    !board.CanPieceParticipateInGravity(model))
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
            PrototypeBoard board = PrototypeBoard.Active;
            return piece != null &&
                   !piece.IsMovementLocked &&
                   board != null && board.CanPieceParticipateInGravity(model) &&
                   !piece.IsShredderInteractionLocked &&
                   model.State == PieceState.Placed &&
                   (Instance == null || !Instance.IsPieceInGridReleasePresentation(model.Id)) &&
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
                piece.PrepareGridPresentationProfile();
                GridCoordinate sourcePivot = new GridCoordinate(
                    move.FromAnchor.X - model.PivotOffset.X,
                    move.FromAnchor.Y - model.PivotOffset.Y);
                piece.SetGridPresentationPosition(
                    GravityLevelGridCoordinates.FineCellToWorld(level, sourcePivot));
                Vector2 targetPosition = GravityLevelGridCoordinates.FineCellToWorld(level, targetPivot);
                gridFallingPieces.Add(piece);
                bool settlesAboveShredder = coordinatedFallReleaseMode.TryGetValue(move.PieceId, out bool releaseMode)
                    ? releaseMode
                    : IsSettlingAboveShredderReservation(snapshot, model);
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

            return PrototypeBoard.Active != null &&
                   PrototypeBoard.Active.IsLandingSupportedByShredderReservation(model, model.Anchor);
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

            coordinatedFallReleaseMode.Remove(piece.SourcePieceId);
            SnapPieceToCommittedGridPosition(piece);
            PrototypeBoard board = PrototypeBoard.Active;
            board?.TrySetPieceState(piece, PieceState.Placed);
            AssertCanonicalRestingAnchorIfApplicable(piece, "gravity settle");

            // When a combined block lands together, wait until every sibling in the
            // same fall batch finishes its tween before capturing into the shredder or
            // breaking its joints, preventing mid-callback partner state splits.
            if (HasFallingJointPartner(board, piece))
            {
                hasPendingGravityWakeup = true;
                return;
            }

            // Immediately check if this piece reached the shredder mouth so it
            // transitions into freefall without a multi-frame dead stop.
            CapturePiecesAtShredderBoundary();

            // Immediately advance gravity for any piece waiting above it,
            // so the upper piece starts falling in the exact same frame!
            AdvanceGridGravityPresentation();
        }

        private bool HasFallingJointPartner(PrototypeBoard board, PuzzlePiece piece)
        {
            if (board == null || piece == null || gridFallingPieces.Count == 0)
                return false;

            completionJointGroupBuffer.Clear();
            board.CollectJointGroupPieces(piece, completionJointGroupBuffer);
            for (int index = 0; index < completionJointGroupBuffer.Count; index++)
            {
                PuzzlePiece partner = completionJointGroupBuffer[index];
                if (partner != null && partner != piece && gridFallingPieces.Contains(partner))
                {
                    completionJointGroupBuffer.Clear();
                    return true;
                }
            }

            completionJointGroupBuffer.Clear();
            return false;
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
            piece.SetGridPresentationPosition(
                GravityLevelGridCoordinates.FineCellToWorld(level, pivot));
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

            int alignmentStride = AlignmentStride(level);
            board.CollectJointGroupPieces(piece, selectedJointGroupPieces);
            bool isCombined = selectedJointGroupPieces.Count > 1;
            Vector2 offset = fineDragRemainder;
            Vector2 delta = dragIntent * level.subdivisions;

            int subStep = 0;
            while ((Mathf.Abs(delta.x) > DragStepEpsilon || Mathf.Abs(delta.y) > DragStepEpsilon) &&
                   subStep++ < MaxDragSubSteps)
            {
                if (Mathf.Abs(delta.x) <= DragStepEpsilon)
                    delta.x = 0f;
                if (Mathf.Abs(delta.y) <= DragStepEpsilon)
                    delta.y = 0f;

                int sideX = Mathf.Abs(offset.x) > DragStepEpsilon
                    ? (offset.x > 0f ? 1 : -1)
                    : (delta.x != 0f ? (delta.x > 0f ? 1 : -1) : 0);
                int sideY = Mathf.Abs(offset.y) > DragStepEpsilon
                    ? (offset.y > 0f ? 1 : -1)
                    : (delta.y != 0f ? (delta.y > 0f ? 1 : -1) : 0);

                bool xLegal = sideX == 0 ||
                    CanMoveSelectionBy(board, snapshot.Grid, piece, model, isCombined, new GridCoordinate(sideX, 0));
                bool yLegal = sideY == 0 ||
                    CanMoveSelectionBy(board, snapshot.Grid, piece, model, isCombined, new GridCoordinate(0, sideY));

                if (!xLegal)
                {
                    if (TryResolveBlockedAxis(
                            board,
                            snapshot.Grid,
                            piece,
                            model,
                            isCombined,
                            alignmentStride,
                            blockedIsHorizontal: true,
                            sideBlocked: sideX,
                            sideFree: sideY,
                            ref offset.x,
                            ref offset.y,
                            ref delta.x,
                            ref delta.y))
                    {
                        continue;
                    }
                }

                if (!yLegal)
                {
                    if (TryResolveBlockedAxis(
                            board,
                            snapshot.Grid,
                            piece,
                            model,
                            isCombined,
                            alignmentStride,
                            blockedIsHorizontal: false,
                            sideBlocked: sideY,
                            sideFree: sideX,
                            ref offset.y,
                            ref offset.x,
                            ref delta.y,
                            ref delta.x))
                    {
                        continue;
                    }
                }

                if (delta.x == 0f && delta.y == 0f)
                    break;

                sideX = Mathf.Abs(offset.x) > DragStepEpsilon
                    ? (offset.x > 0f ? 1 : -1)
                    : (delta.x != 0f ? (delta.x > 0f ? 1 : -1) : 0);
                sideY = Mathf.Abs(offset.y) > DragStepEpsilon
                    ? (offset.y > 0f ? 1 : -1)
                    : (delta.y != 0f ? (delta.y > 0f ? 1 : -1) : 0);

                bool diagBlocked = sideX != 0 && sideY != 0 &&
                    !CanMoveSelectionBy(board, snapshot.Grid, piece, model, isCombined, new GridCoordinate(sideX, sideY));
                if (diagBlocked)
                {
                    bool xActive = Mathf.Abs(offset.x) > DragStepEpsilon;
                    bool yActive = Mathf.Abs(offset.y) > DragStepEpsilon;
                    if (xActive && !yActive)
                    {
                        ResolveOverhangingCornerStep(sideX, sideY, ref offset.x, ref delta.x, ref delta.y);
                        if (Mathf.Abs(delta.x) > DragStepEpsilon || Mathf.Abs(delta.y) > DragStepEpsilon)
                            continue;
                        break;
                    }

                    if (yActive && !xActive)
                    {
                        ResolveOverhangingCornerStep(sideY, sideX, ref offset.y, ref delta.y, ref delta.x);
                        if (Mathf.Abs(delta.x) > DragStepEpsilon || Mathf.Abs(delta.y) > DragStepEpsilon)
                            continue;
                        break;
                    }

                    if (xActive && yActive)
                    {
                        float budget = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y));
                        if (Mathf.Abs(offset.x) <= Mathf.Abs(offset.y))
                        {
                            float shrink = Mathf.Min(Mathf.Abs(offset.x), budget);
                            offset.x = shrink >= Mathf.Abs(offset.x) - DragStepEpsilon ? 0f : offset.x - sideX * shrink;
                        }
                        else
                        {
                            float shrink = Mathf.Min(Mathf.Abs(offset.y), budget);
                            offset.y = shrink >= Mathf.Abs(offset.y) - DragStepEpsilon ? 0f : offset.y - sideY * shrink;
                        }
                        delta = Vector2.zero;
                        break;
                    }

                    offset = Vector2.zero;
                    if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y))
                        delta.y = 0f;
                    else
                        delta.x = 0f;
                }

                float tx = ComputeAxisBoundaryFraction(offset.x, delta.x);
                float ty = ComputeAxisBoundaryFraction(offset.y, delta.y);
                float t = Mathf.Min(tx, ty);
                if (t > DragStepEpsilon)
                {
                    offset.x += delta.x * t;
                    offset.y += delta.y * t;
                    if (t >= 1f - DragStepEpsilon)
                    {
                        delta = Vector2.zero;
                    }
                    else
                    {
                        delta.x *= 1f - t;
                        delta.y *= 1f - t;
                    }
                }
                else
                {
                    delta = Vector2.zero;
                }

                if (Mathf.Abs(offset.x) <= DragStepEpsilon)
                    offset.x = 0f;
                if (Mathf.Abs(offset.y) <= DragStepEpsilon)
                    offset.y = 0f;

                bool steppedX = TryCommitFineAxisStep(
                    board,
                    snapshot.Grid,
                    piece,
                    model,
                    isCombined,
                    alignmentStride,
                    horizontalAxis: true,
                    ref offset);
                bool steppedY = TryCommitFineAxisStep(
                    board,
                    snapshot.Grid,
                    piece,
                    model,
                    isCombined,
                    alignmentStride,
                    horizontalAxis: false,
                    ref offset);

                if (!steppedX && Mathf.Abs(offset.x) >= FineCellDragThreshold - DragStepEpsilon)
                {
                    offset.x = (offset.x > 0f ? 1f : -1f) * (FineCellDragThreshold - DragStepEpsilon * 4f);
                    delta.x = 0f;
                }
                if (!steppedY && Mathf.Abs(offset.y) >= FineCellDragThreshold - DragStepEpsilon)
                {
                    offset.y = (offset.y > 0f ? 1f : -1f) * (FineCellDragThreshold - DragStepEpsilon * 4f);
                    delta.y = 0f;
                }
            }

            fineDragRemainder = offset;
            GridCoordinate selectedPivot = new GridCoordinate(
                model.Anchor.X - model.PivotOffset.X,
                model.Anchor.Y - model.PivotOffset.Y);
            Vector2 selectedGridPosition = GravityLevelGridCoordinates.FineCellToWorld(level, selectedPivot);
            Vector2 selectedPresentationOffset = offset / level.subdivisions;
            Vector2 selectedPresentationPosition = selectedGridPosition + selectedPresentationOffset;
            piece.SetGridPresentationPosition(selectedPresentationPosition, synchronizeTransforms: false);

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
                groupPiece.SetGridPresentationPosition(
                    groupPosition + selectedPresentationOffset,
                    synchronizeTransforms: false);
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

        private bool TryResolveBlockedAxis(
            PrototypeBoard board,
            GravityBoardGrid grid,
            PuzzlePiece piece,
            PieceModel model,
            bool isCombined,
            int alignmentStride,
            bool blockedIsHorizontal,
            int sideBlocked,
            int sideFree,
            ref float blockedOffset,
            ref float freeOffset,
            ref float blockedDelta,
            ref float freeDelta)
        {
            // Returning toward Anchor along a blocked axis clears any remaining sub-cell overlap safely.
            if (Mathf.Abs(blockedOffset) > DragStepEpsilon)
            {
                if (blockedDelta * sideBlocked < -DragStepEpsilon)
                {
                    float retreat = Mathf.Min(Mathf.Abs(blockedOffset), Mathf.Abs(blockedDelta));
                    blockedOffset -= sideBlocked * retreat;
                    if (Mathf.Abs(blockedOffset) <= DragStepEpsilon)
                        blockedOffset = 0f;
                    blockedDelta += sideBlocked * retreat;
                    return true;
                }

                blockedOffset = 0f;
                blockedDelta = 0f;
                return true;
            }

            float inwardBlocked = blockedDelta * sideBlocked;
            if (inwardBlocked <= DragStepEpsilon)
            {
                blockedDelta = 0f;
                return false;
            }

            // Funnel 1:1 into an adjacent 1-fine-cell opening when already leaned >0.02 fine cells toward it.
            if (Mathf.Abs(freeOffset) > SubCellAxisDeadzone && sideFree != 0)
            {
                GridCoordinate freeStep = blockedIsHorizontal
                    ? new GridCoordinate(0, sideFree)
                    : new GridCoordinate(sideFree, 0);
                GridCoordinate funnelStep = blockedIsHorizontal
                    ? new GridCoordinate(sideBlocked, sideFree)
                    : new GridCoordinate(sideFree, sideBlocked);

                if (CanMoveSelectionBy(board, grid, piece, model, isCombined, freeStep) &&
                    CanMoveSelectionBy(board, grid, piece, model, isCombined, funnelStep))
                {
                    float freeInward = freeDelta * sideFree;
                    float distToBoundary = Mathf.Max(0f, FineCellDragThreshold - Mathf.Abs(freeOffset));
                    if (freeInward >= 0f)
                    {
                        float drive = Mathf.Max(freeInward, inwardBlocked);
                        float stepMag = Mathf.Min(distToBoundary, drive);
                        float freeUsed = Mathf.Min(freeInward, stepMag);
                        float blockedUsed = Mathf.Min(inwardBlocked, Mathf.Max(0f, stepMag - freeUsed));
                        freeOffset += sideFree * stepMag;
                        freeDelta -= sideFree * freeUsed;
                        blockedDelta -= sideBlocked * blockedUsed;
                    }
                    else
                    {
                        float away = -freeInward;
                        float bevel = inwardBlocked - away;
                        if (bevel > DragStepEpsilon)
                        {
                            float stepMag = Mathf.Min(distToBoundary, bevel);
                            float ratio = Mathf.Clamp01(stepMag / bevel);
                            freeOffset += sideFree * stepMag;
                            freeDelta *= 1f - ratio;
                            blockedDelta -= sideBlocked * (ratio * inwardBlocked);
                        }
                        else
                        {
                            blockedDelta = 0f;
                            return false;
                        }
                    }

                    Vector2 offsetCopy = blockedIsHorizontal
                        ? new Vector2(blockedOffset, freeOffset)
                        : new Vector2(freeOffset, blockedOffset);
                    if (TryCommitFineAxisStep(
                            board,
                            grid,
                            piece,
                            model,
                            isCombined,
                            alignmentStride,
                            !blockedIsHorizontal,
                            ref offsetCopy))
                    {
                        blockedOffset = blockedIsHorizontal ? offsetCopy.x : offsetCopy.y;
                        freeOffset = blockedIsHorizontal ? offsetCopy.y : offsetCopy.x;
                    }
                    else if (Mathf.Abs(freeOffset) >= FineCellDragThreshold - DragStepEpsilon)
                    {
                        freeOffset = sideFree * (FineCellDragThreshold - DragStepEpsilon * 4f);
                        freeDelta = 0f;
                        blockedDelta = 0f;
                    }

                    return true;
                }
            }

            blockedDelta = 0f;
            return false;
        }

        private static void ResolveOverhangingCornerStep(
            int sideActive,
            int sideBlocked,
            ref float activeOffset,
            ref float activeDelta,
            ref float blockedDelta)
        {
            if (activeDelta * sideActive < -DragStepEpsilon)
            {
                float retreat = Mathf.Min(Mathf.Abs(activeOffset), Mathf.Abs(activeDelta));
                activeOffset -= sideActive * retreat;
                if (Mathf.Abs(activeOffset) <= DragStepEpsilon)
                    activeOffset = 0f;
                activeDelta += sideActive * retreat;
                return;
            }

            float outward = Mathf.Max(0f, activeDelta * sideActive);
            float inwardBlocked = Mathf.Max(0f, blockedDelta * sideBlocked);
            if (inwardBlocked <= outward + DragStepEpsilon)
            {
                blockedDelta = 0f;
                return;
            }

            float bevel = inwardBlocked - outward;
            float distToZero = Mathf.Abs(activeOffset);
            if (distToZero <= bevel + DragStepEpsilon)
            {
                activeOffset = 0f;
                activeDelta = 0f;
                blockedDelta -= sideBlocked * (outward + distToZero);
            }
            else
            {
                activeOffset -= sideActive * bevel;
                activeDelta = 0f;
                blockedDelta = 0f;
            }
        }

        private static float ComputeAxisBoundaryFraction(float currentOffset, float axisDelta)
        {
            if (Mathf.Abs(axisDelta) <= DragStepEpsilon)
                return 1f;

            float targetOffset = currentOffset + axisDelta;
            if ((currentOffset > DragStepEpsilon && targetOffset < -DragStepEpsilon) ||
                (currentOffset < -DragStepEpsilon && targetOffset > DragStepEpsilon))
            {
                return Mathf.Clamp01(-currentOffset / axisDelta);
            }

            float threshold = axisDelta > 0f ? FineCellDragThreshold : -FineCellDragThreshold;
            if (Mathf.Abs(targetOffset) >= FineCellDragThreshold - DragStepEpsilon)
                return Mathf.Clamp01((threshold - currentOffset) / axisDelta);

            return 1f;
        }

        private bool TryCommitFineAxisStep(
            PrototypeBoard board,
            GravityBoardGrid grid,
            PuzzlePiece piece,
            PieceModel model,
            bool isCombined,
            int alignmentStride,
            bool horizontalAxis,
            ref Vector2 offset)
        {
            float axisOffset = horizontalAxis ? offset.x : offset.y;
            if (Mathf.Abs(axisOffset) < FineCellDragThreshold - DragStepEpsilon)
                return false;

            int stepDir = axisOffset > 0f ? 1 : -1;
            GridCoordinate step = horizontalAxis
                ? new GridCoordinate(stepDir, 0)
                : new GridCoordinate(0, stepDir);

            float perpOffset = horizontalAxis ? offset.y : offset.x;
            if (Mathf.Abs(perpOffset) > DragStepEpsilon)
            {
                int perpDir = perpOffset > 0f ? 1 : -1;
                GridCoordinate combinedDelta = horizontalAxis
                    ? new GridCoordinate(stepDir, perpDir)
                    : new GridCoordinate(perpDir, stepDir);
                if (!CanMoveSelectionBy(board, grid, piece, model, isCombined, combinedDelta))
                    return false;
            }

            if (!board.TryMoveDraggingPieceOnGrid(piece, model.Anchor.Offset(step), out _))
                return false;

            TryRecordCanonicalDragAnchor(board, piece, model.Anchor, alignmentStride);
            if (horizontalAxis)
                offset.x = 0f;
            else
                offset.y = 0f;
            return true;
        }

        private static bool CanMoveSelectionBy(
            PrototypeBoard board,
            GravityBoardGrid grid,
            PuzzlePiece piece,
            PieceModel model,
            bool isCombined,
            GridCoordinate delta)
        {
            return isCombined
                ? board.CanMovePieceGroupBy(piece, delta)
                : CanMoveSingleModelTo(board, grid, model, model.Anchor.Offset(delta));
        }

        private static bool CanMoveSingleModelTo(
            PrototypeBoard board,
            GravityBoardGrid grid,
            PieceModel model,
            GridCoordinate targetAnchor)
        {
            return board.CanMovePiece(model, targetAnchor) &&
                   grid.CheckPlacementIgnoringPiece(model, targetAnchor, model.Id).IsSuccess;
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
            if (selectedPiece == null)
            {
                Touch touch = Input.GetTouch(0);
                if (touch.phase == TouchPhase.Began)
                {
                    ClearFrozenGroupFeedback();
                    activeFingerId = touch.fingerId;
                    TrySelectPiece(PointerWorldPosition(touch.position));
                }
                else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                {
                    ClearFrozenGroupFeedback();
                    activeFingerId = -1;
                }
                return;
            }

            if (!TryFindActiveTouch(out Touch activeTouch))
            {
                ReleasePiece();
                activeFingerId = -1;
                return;
            }

            if (activeTouch.phase == TouchPhase.Ended || activeTouch.phase == TouchPhase.Canceled)
            {
                UpdateDragTarget(PointerWorldPosition(activeTouch.position) + grabOffset);
                ReleasePiece();
                activeFingerId = -1;
                return;
            }

            UpdateDragTarget(PointerWorldPosition(activeTouch.position) + grabOffset);
        }

        private void ProcessMouseInput()
        {
            bool mouseDown = Input.GetMouseButtonDown(0);
            bool mouseUp = Input.GetMouseButtonUp(0);
            bool mouseHeld = Input.GetMouseButton(0);

            if (selectedPiece == null && !mouseDown)
            {
                if (mouseUp)
                    ClearFrozenGroupFeedback();
                return;
            }

            Vector2 pointer = PointerWorldPosition(Input.mousePosition);

            if (mouseDown)
            {
                ClearFrozenGroupFeedback();
                TrySelectPiece(pointer);
            }

            if (selectedPiece != null && (mouseHeld || mouseUp))
                UpdateDragTarget(pointer + grabOffset);

            if (selectedPiece != null && mouseUp)
                ReleasePiece();
        }

        private bool TryFindActiveTouch(out Touch activeTouch)
        {
            if (activeFingerId < 0)
            {
                activeTouch = default;
                return false;
            }

            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                if (touch.fingerId == activeFingerId)
                {
                    activeTouch = touch;
                    return true;
                }
            }

            activeTouch = default;
            return false;
        }

        private void TrySelectPiece(Vector2 pointerPosition)
        {
            // Grid selection is not a physics query. The occupied cell is the
            // authoritative answer, so overlapping visuals, pins, and
            // off-board freefall colliders cannot steal a drag.
            PuzzlePiece piece = TryResolveGridPieceAt(pointerPosition);

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
            // One pulse per pick-up. Pulsing from the per-frame drag path ran a
            // native vibrator call every frame on Android and stalled the drag.
            HapticFeedbackRuntime.PieceSelected();
            SoundFeedbackRuntime.PieceSelected();
            PrototypeBoard.Active.CollectJointGroupPieces(piece, selectedJointGroupPieces);
            // A queued gravity plan or a still-owned fall tween can otherwise
            // restore an old body pose after this drag has started.
            pendingGridGravityMoves.Clear();
            Vector2 preGrabVisualPosition = body.position;
            bool hasCommittedPosition = TryGetCommittedGridPresentationPosition(piece, out Vector2 primaryCommittedPosition);
            for (int groupIndex = 0; groupIndex < selectedJointGroupPieces.Count; groupIndex++)
            {
                PuzzlePiece groupPiece = selectedJointGroupPieces[groupIndex];
                if (groupPiece == null || groupPiece.Body == null)
                    continue;

                groupPiece.PrepareGridPresentationProfile();
                TakeGridPresentationOwnershipForDrag(groupPiece);
                if (TryGetCommittedGridPresentationPosition(groupPiece, out Vector2 committedPosition))
                    groupPiece.SetGridPresentationPosition(committedPosition, synchronizeTransforms: false);
                groupPiece.SetSelected(true);
            }
            if (selectedJointGroupPieces.Count > 1)
                Physics2D.SyncTransforms();

            dragTarget = body.position;
            pendingDragIntent = Vector2.zero;
            fineDragRemainder = Vector2.zero;
            hasLastCanonicalDragAnchor = false;
            TryRecordCanonicalDragAnchor(
                PrototypeBoard.Active,
                piece,
                selectedPieceStartAnchor,
                AlignmentStride(GravityLevelRuntime.FindLevelToPlay()));
            lastResolvedDragPosition = body.position;
            hasLastResolvedDragPosition = true;

            if (hasCommittedPosition && (preGrabVisualPosition - primaryCommittedPosition).sqrMagnitude > 1e-6f)
                TryMoveSelectedPieceOnGrid(piece, preGrabVisualPosition - primaryCommittedPosition);

            grabOffset = body.position - pointerPosition;
            dragTarget = body.position;
            pendingDragIntent = Vector2.zero;
            lastResolvedDragPosition = body.position;
        }

        private static PuzzlePiece TryResolveGridPieceAt(Vector2 worldPosition)
        {
            PrototypeBoard board = PrototypeBoard.Active;
            GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
            if (board == null || level == null || board.BoardSnapshot == null)
                return null;

            GridCoordinate coordinate = GravityLevelGridCoordinates.WorldToFineCell(level, worldPosition);
            if (!board.BoardSnapshot.Grid.TryGetOccupantId(coordinate, out int pieceId))
                return null;

            return FindActivePiece(pieceId);
        }

        private void ShowFrozenGroupFeedbackIfNeeded(PrototypeBoard board, PuzzlePiece piece)
        {
            if (piece == null || piece.IsMovementLocked)
                return;

            board.CollectJointGroupPieces(piece, selectedJointGroupPieces);
            for (int index = 0; index < selectedJointGroupPieces.Count; index++)
            {
                if (selectedJointGroupPieces[index] == null || !selectedJointGroupPieces[index].IsMovementLocked)
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
            CancelGridReleasePresentationIfContains(piece);
        }

        private void ReleasePiece()
        {
            CommitDragEnd(allowDropFeedback: true, allowGravityAdvance: true);
        }

        private void CommitDragEnd(bool allowDropFeedback, bool allowGravityAdvance)
        {
            if (selectedPiece == null)
                return;

            if (selectedPiece.IsShredderInteractionLocked)
            {
                ClearInputSelection();
                return;
            }

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
                groupPiece.PrepareGridPresentationProfile();
            }
            Physics2D.SyncTransforms();
            selectedPiece.PrepareGridPresentationProfile();

            if (!TryGetSnapshotPiece(selectedPiece, out _, out PieceModel currentModel) ||
                !currentModel.IsOnBoard ||
                currentModel.State != PieceState.Dragging)
            {
                ClearInputSelection();
                return;
            }

            if (TryCommitCanonicalSnapRelease(selectedPiece, isCombined, out Vector2 releasePosition))
            {
                if (allowDropFeedback)
                    HapticFeedbackRuntime.ValidDrop();

                PuzzlePiece releasePiece = selectedPiece;
                if (isCombined)
                    StartGridReleasePresentation(releasePiece, selectedJointGroupPieces);
                else
                    StartGridReleasePresentation(releasePiece, releasePosition);

                AssertCanonicalRestingGroupInvariant(releasePiece, selectedJointGroupPieces, "drag release");
                ResetDragSelectionState();
                if (allowGravityAdvance)
                    AdvanceGridGravityPresentation();
                return;
            }

            bool restored = TryRestoreDragEndFallback(selectedPiece, selectedJointGroupPieces, isCombined, currentModel.Anchor);
            if (allowDropFeedback)
                HapticFeedbackRuntime.InvalidDrop();

            if (!restored)
            {
                Debug.LogWarning(
                    $"[GridRelease] Failed to restore '{selectedPiece.name}' cleanly to a Placed state.",
                    selectedPiece);
            }

            ResetDragSelectionState();
            if (allowGravityAdvance)
                AdvanceGridGravityPresentation();
        }

        private void StartGridReleasePresentation(PuzzlePiece releasePiece, Vector2 releasePosition)
        {
            gridReleasePresentationPieces.Clear();
            gridReleasePresentationTargets.Clear();
            gridReleasePresentationPieces.Add(releasePiece);
            gridReleasePresentationTargets.Add(releasePosition);
            BeginGridReleasePresentation(releasePiece);
        }

        private void StartGridReleasePresentation(
            PuzzlePiece releasePiece,
            IReadOnlyList<PuzzlePiece> releasePieces)
        {
            gridReleasePresentationPieces.Clear();
            gridReleasePresentationTargets.Clear();
            for (int index = 0; index < releasePieces.Count; index++)
            {
                PuzzlePiece member = releasePieces[index];
                if (member == null || !TryGetCommittedGridPresentationPosition(member, out Vector2 target))
                    continue;

                gridReleasePresentationPieces.Add(member);
                gridReleasePresentationTargets.Add(target);
            }

            BeginGridReleasePresentation(releasePiece);
        }

        // Grid commit is authoritative before this visual pass begins. Every
        // member gets the same transform-owned presentation lifecycle so joined
        // pieces no longer skip their release animation.
        private void BeginGridReleasePresentation(PuzzlePiece releasePiece)
        {
            if (releasePiece == null || gridReleasePresentationPieces.Count == 0)
                return;

            gridReleasePresentationPiece = releasePiece;
            pendingGridReleasePresentationCount = gridReleasePresentationPieces.Count;
            for (int index = 0; index < gridReleasePresentationPieces.Count; index++)
            {
                PuzzlePiece member = gridReleasePresentationPieces[index];
                Vector2 target = gridReleasePresentationTargets[index];
                bool played = member != null && member.GridFallView != null &&
                    member.GridFallView.PlayReleaseTo(
                        target,
                        () => CompleteGridReleasePresentationMember(member));
                if (!played)
                {
                    member?.SetGridPresentationPosition(target);
                    CompleteGridReleasePresentationMember(member);
                }
            }
        }

        private void CompleteGridReleasePresentationMember(PuzzlePiece piece)
        {
            if (gridReleasePresentationPiece == null ||
                !gridReleasePresentationPieces.Contains(piece) ||
                pendingGridReleasePresentationCount <= 0)
                return;

            pendingGridReleasePresentationCount--;
            if (pendingGridReleasePresentationCount > 0)
                return;

            // Align the non-simulated Rigidbody pose with the tween's final
            // transform once, after every member reaches the committed grid
            // position. This prevents a stale body pose at the next handoff.
            for (int index = 0; index < gridReleasePresentationPieces.Count; index++)
            {
                PuzzlePiece member = gridReleasePresentationPieces[index];
                if (member != null && TryGetCommittedGridPresentationPosition(member, out Vector2 target))
                    member.SetGridPresentationPosition(target, synchronizeTransforms: false);
            }
            Physics2D.SyncTransforms();
            ClearGridReleasePresentationState();
            if (!inputLockedByGameState)
            {
                hasPendingGravityWakeup = true;
                CapturePiecesAtShredderBoundary();
                AdvanceGridGravityPresentation();
            }
        }

        private bool IsGridReleasePresentationAnimating()
        {
            for (int index = 0; index < gridReleasePresentationPieces.Count; index++)
            {
                PieceGridFallView view = gridReleasePresentationPieces[index]?.GridFallView;
                if (view != null && view.IsAnimating)
                    return true;
            }

            return false;
        }

        private void CancelGridReleasePresentationIfContains(PuzzlePiece piece)
        {
            if (piece != null && gridReleasePresentationPieces.Contains(piece))
                CancelGridReleasePresentation();
        }

        private void CancelGridReleasePresentation()
        {
            for (int index = 0; index < gridReleasePresentationPieces.Count; index++)
                gridReleasePresentationPieces[index]?.GridFallView?.Cancel();
            ClearGridReleasePresentationState();
        }

        private void ClearGridReleasePresentationState()
        {
            gridReleasePresentationPiece = null;
            pendingGridReleasePresentationCount = 0;
            gridReleasePresentationPieces.Clear();
            gridReleasePresentationTargets.Clear();
        }

        private static bool TryGetCommittedGridPresentationPosition(
            PuzzlePiece piece,
            out Vector2 position)
        {
            position = default;
            if (piece == null || !TryGetSnapshotPiece(piece, out _, out PieceModel model))
                return false;

            GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
            if (level == null)
                return false;

            GridCoordinate pivot = new GridCoordinate(
                model.Anchor.X - model.PivotOffset.X,
                model.Anchor.Y - model.PivotOffset.Y);
            position = GravityLevelGridCoordinates.FineCellToWorld(level, pivot);
            return true;
        }

        private bool TryCommitCanonicalSnapRelease(
            PuzzlePiece piece,
            bool isCombined,
            out Vector2 releasePosition)
        {
            releasePosition = default;
            if (piece == null || piece.Body == null ||
                !TryGetSnapshotPiece(piece, out _, out PieceModel model))
                return false;

            GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
            PrototypeBoard board = PrototypeBoard.Active;
            if (level == null || board == null)
                return false;

            int alignmentStride = AlignmentStride(level);
            Vector2 currentPosition = hasLastResolvedDragPosition
                ? lastResolvedDragPosition
                : piece.Body.position;
            GridCoordinate visualAnchor = WorldToNearestFineCell(level, currentPosition).Offset(model.PivotOffset);

            GridReleaseSnapPlanner.CollectCanonicalCandidates(
                visualAnchor,
                model.Anchor,
                hasLastCanonicalDragAnchor,
                lastCanonicalDragAnchor,
                hasSelectedPieceStartAnchor,
                selectedPieceStartAnchor,
                alignmentStride,
                snapCandidateBuffer);

            if (TryCommitFirstTraversableCandidate(
                    piece,
                    board,
                    model,
                    level,
                    alignmentStride,
                    requireAllMembersCanonical: isCombined,
                    out releasePosition))
            {
                return true;
            }

            // If an unrelated group member started off-grid, still align the primary
            // member to a canonical coordinate rather than stranding at a half-block.
            if (isCombined &&
                TryCommitFirstTraversableCandidate(
                    piece,
                    board,
                    model,
                    level,
                    alignmentStride,
                    requireAllMembersCanonical: false,
                    out releasePosition))
            {
                return true;
            }

            return false;
        }

        private bool TryCommitFirstTraversableCandidate(
            PuzzlePiece piece,
            PrototypeBoard board,
            PieceModel model,
            GravityLevelDefinition level,
            int alignmentStride,
            bool requireAllMembersCanonical,
            out Vector2 releasePosition)
        {
            releasePosition = default;
            for (int index = 0; index < snapCandidateBuffer.Count; index++)
            {
                GridCoordinate candidateAnchor = snapCandidateBuffer[index];
                if (requireAllMembersCanonical &&
                    !board.WouldJointGroupRemainCanonical(piece, candidateAnchor, alignmentStride))
                {
                    continue;
                }

                // Snapping is only presentation alignment after the player has
                // traversed a valid path. It may not complete a blocked path or
                // move through a corner that the active drag could not cross.
                if (!board.CanTraversePieceGroupTo(piece, candidateAnchor))
                    continue;

                if (!board.TryMovePieceOnGrid(piece, candidateAnchor, out _))
                    continue;

                GridCoordinate snappedPivot = new GridCoordinate(
                    candidateAnchor.X - model.PivotOffset.X,
                    candidateAnchor.Y - model.PivotOffset.Y);
                Vector2 snappedPosition = GravityLevelGridCoordinates.FineCellToWorld(level, snappedPivot);
                lastResolvedDragPosition = snappedPosition;
                releasePosition = snappedPosition;
                return true;
            }

            return false;
        }

        private bool TryRestoreDragEndFallback(
            PuzzlePiece piece,
            IReadOnlyList<PuzzlePiece> groupPieces,
            bool isCombined,
            GridCoordinate currentAnchor)
        {
            GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
            PrototypeBoard board = PrototypeBoard.Active;
            if (level == null || board == null)
                return false;

            int alignmentStride = AlignmentStride(level);
            bool committed = false;
            if (hasLastCanonicalDragAnchor &&
                GridReleaseSnapPlanner.IsCanonicalAnchor(lastCanonicalDragAnchor, alignmentStride) &&
                board.CanTraversePieceGroupTo(piece, lastCanonicalDragAnchor) &&
                TryRestoreGridRelease(piece, lastCanonicalDragAnchor, out _))
            {
                committed = true;
            }
            else if (hasSelectedPieceStartAnchor &&
                     GridReleaseSnapPlanner.IsCanonicalAnchor(selectedPieceStartAnchor, alignmentStride) &&
                     board.CanTraversePieceGroupTo(piece, selectedPieceStartAnchor) &&
                     TryRestoreGridRelease(piece, selectedPieceStartAnchor, out _))
            {
                committed = true;
            }
            else if (TryRestoreGridRelease(piece, currentAnchor, out _))
            {
                committed = true;
                if (alignmentStride > 1 &&
                    !GridReleaseSnapPlanner.IsCanonicalAnchor(currentAnchor, alignmentStride))
                {
                    Debug.LogError(
                        $"[GridAlignment] All canonical snap candidates failed for '{piece.name}'; " +
                        $"fell back to ({currentAnchor.X},{currentAnchor.Y}) (subdivisions={alignmentStride}).",
                        piece);
                }
            }

            if (!committed)
                return false;

            if (isCombined)
            {
                for (int index = 0; index < groupPieces.Count; index++)
                {
                    PuzzlePiece member = groupPieces[index];
                    if (member != null && TryGetCommittedGridPresentationPosition(member, out Vector2 target))
                        member.SetGridPresentationPosition(target, synchronizeTransforms: false);
                }
                Physics2D.SyncTransforms();
            }
            else if (TryGetCommittedGridPresentationPosition(piece, out Vector2 singleTarget))
            {
                piece.SetGridPresentationPosition(singleTarget);
            }

            return true;
        }

        private static bool TryTrimSingleFallTargetToCanonicalY(
            PrototypeBoard board,
            LevelBoardSnapshot snapshot,
            GravityLevelDefinition level,
            PieceModel model,
            ref GridCoordinate targetAnchor)
        {
            int alignmentStride = AlignmentStride(level);
            if (alignmentStride <= 1 ||
                GridReleaseSnapPlanner.IsCanonicalCoordinate(targetAnchor.Y, alignmentStride) ||
                board.IsLandingSupportedByShredderReservation(model, targetAnchor))
            {
                return true;
            }

            for (int candidateY = targetAnchor.Y + 1; candidateY < model.Anchor.Y; candidateY++)
            {
                if (!GridReleaseSnapPlanner.IsCanonicalCoordinate(candidateY, alignmentStride))
                    continue;

                GridCoordinate candidate = new GridCoordinate(targetAnchor.X, candidateY);
                if (snapshot.Grid.CheckPlacementIgnoringPiece(model, candidate, model.Id).IsSuccess &&
                    board.CanMovePiece(model, candidate))
                {
                    targetAnchor = candidate;
                    return true;
                }
            }

            return false;
        }

        private void TryRecordCanonicalDragAnchor(
            PrototypeBoard board,
            PuzzlePiece piece,
            GridCoordinate candidateAnchor,
            int alignmentStride)
        {
            if (board == null || piece == null)
                return;

            if (GridReleaseSnapPlanner.IsCanonicalAnchor(candidateAnchor, alignmentStride) &&
                board.AreAllJointGroupMembersCanonical(piece, alignmentStride))
            {
                lastCanonicalDragAnchor = candidateAnchor;
                hasLastCanonicalDragAnchor = true;
            }
        }

        private static int AlignmentStride(GravityLevelDefinition level)
        {
            return level != null ? Mathf.Max(1, level.subdivisions) : 1;
        }

        private static void AssertCanonicalRestingGroupInvariant(
            PuzzlePiece primaryPiece,
            IReadOnlyList<PuzzlePiece> groupPieces,
            string phase)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            AssertCanonicalRestingAnchorIfApplicable(primaryPiece, phase);
            if (groupPieces == null)
                return;

            for (int index = 0; index < groupPieces.Count; index++)
                AssertCanonicalRestingAnchorIfApplicable(groupPieces[index], phase);
#endif
        }

        private static void AssertCanonicalRestingAnchorIfApplicable(PuzzlePiece piece, string phase)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (piece == null || piece.IsShredderInteractionLocked)
                return;

            PrototypeBoard board = PrototypeBoard.Active;
            GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
            if (board == null || level == null || !board.TryGetPieceModel(piece, out PieceModel model))
                return;

            int stride = AlignmentStride(level);
            if (stride <= 1 || !model.IsOnBoard || model.State != PieceState.Placed)
                return;

            if (GridReleaseSnapPlanner.IsCanonicalAnchor(model.Anchor, stride))
                return;

            if (board.IsLandingSupportedByShredderReservation(model, model.Anchor))
                return;

            Debug.LogError(
                $"[GridAlignment] Piece '{piece.name}' (id={model.Id}) rested off canonical grid " +
                $"at ({model.Anchor.X},{model.Anchor.Y}) after {phase} (subdivisions={stride}).",
                piece);
#endif
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
            if (gameCamera == null)
                return Vector2.zero;

            Vector3 screenPosition = screenPoint;
            screenPosition.z = -gameCamera.transform.position.z;
            return gameCamera.ScreenToWorldPoint(screenPosition);
        }

    }
}
