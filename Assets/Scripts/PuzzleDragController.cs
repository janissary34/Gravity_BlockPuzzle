using System.Collections.Generic;
using GravityPuzzle.Core.Grid;
using GravityPuzzle.Core.StateMachine;
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
        private PuzzlePiece gridReleasePresentationPiece;
        private readonly HashSet<PuzzlePiece> gridFallingPieces = new HashSet<PuzzlePiece>();
        private readonly List<PuzzlePiece> finishedGridFalls = new List<PuzzlePiece>();
        private readonly Queue<GridGravityMove> pendingGridGravityMoves =
            new Queue<GridGravityMove>();
        private GridCoordinate selectedPieceStartAnchor;
        private bool hasSelectedPieceStartAnchor;
        private Vector2 grabOffset;
        // Latest pointer-derived target is retained only to calculate new input deltas.
        private Vector2 dragTarget;
        private Vector2 pendingDragIntent;
        private Vector2 lastResolvedDragPosition;
        private bool hasLastResolvedDragPosition;
        private int activeFingerId = -1;
        private readonly Collider2D[] selectionHits = new Collider2D[32];
        private const float MinimumMoveDistance = .0005f;
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

            // The shredder can capture a piece while it is still held. Drop the
            // drag controller's reference immediately so it cannot move the
            // piece again or queue a release snap during the shred animation.
            if (selectedPiece != null && selectedPiece.IsBeingShredded)
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
            selectedPiece = null;
            activeFingerId = -1;
            pendingDragIntent = Vector2.zero;
            hasLastResolvedDragPosition = false;
            hasSelectedPieceStartAnchor = false;
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
                if (piece == null || piece.IsBeingShredded || piece.IsFrozen)
                    continue;

                for (int zoneIndex = 0; zoneIndex < zones.Count; zoneIndex++)
                {
                    ShredderCatchZone zone = zones[zoneIndex];
                    if (zone == null || !zone.ContainsCaptureFootprint(piece))
                        continue;

                    // The feed cancels its presentation tween internally. Clear
                    // this controller's matching fall record first, otherwise a
                    // later cleanup pass attempts an invalid Shredding -> Placed
                    // transition for the captured root.
                    CancelGridFallForTargetedAction(piece);
                    if (shredder.TryCapturePiece(piece, zone.ShredY) && selectedPiece == piece)
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
            if (selectedPiece != null && !selectedPiece.IsBeingShredded)
                TryMoveSelectedPieceOnGrid(selectedPiece, ConsumeDragIntent(), applySpeedLimit: true);

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

            PrototypeBoard activeBoard = PrototypeBoard.Active;
            LevelBoardSnapshot snapshot = activeBoard != null
                ? activeBoard.BoardSnapshot
                : null;
            if (snapshot == null)
                return gridFallingPieces.Count > 0;

            // Per-piece cascade: do not gate on gridFallingPieces so that new
            // independent moves can be planned and started while other pieces
            // are still animating.  Pieces already falling have already been
            // committed to their target anchors in the grid, so TryGetFallTarget
            // will not double-assign the same cell.
            if (pendingGridGravityMoves.Count == 0)
                TryBuildSettledGridGravityPlan(activeBoard, snapshot);

            return TryPlayQueuedGridGravityMoves(activeBoard, snapshot) ||
                   gridFallingPieces.Count > 0;
        }

        private bool TryBuildSettledGridGravityPlan(
            PrototypeBoard activeBoard,
            LevelBoardSnapshot snapshot)
        {
            int remainingMoveBudget = snapshot.Pieces.Count;
            while (remainingMoveBudget-- > 0 &&
                   GridGravityPlanner.TryPlanNextMove(
                       snapshot,
                       IsEligibleForGridGravity,
                       out GridGravityMove move))
            {
                PuzzlePiece piece = FindActivePiece(move.PieceId);
                if (piece == null || piece.IsFrozen || piece.IsBeingShredded ||
                    piece.GridFallView == null || !piece.GridFallView.CanPlay ||
                    !activeBoard.TryCommitGridGravityMove(move, out _))
                {
                    // A failed commit means the snapshot changed while this
                    // cascade was being assembled. Keep the already committed
                    // moves and retry from a fresh snapshot next gravity tick.
                    break;
                }

                pendingGridGravityMoves.Enqueue(move);
            }

            return pendingGridGravityMoves.Count > 0;
        }

        private static bool IsEligibleForGridGravity(PieceModel model)
        {
            PuzzlePiece piece = FindActivePiece(model.Id);
            return piece != null &&
                   !piece.IsFrozen &&
                   !piece.IsBeingShredded &&
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

            bool playedAnyMove = false;
            while (pendingGridGravityMoves.Count > 0)
            {
                GridGravityMove move = pendingGridGravityMoves.Dequeue();
                PuzzlePiece piece = FindActivePiece(move.PieceId);
                if (piece == null || piece.IsFrozen || piece.IsBeingShredded ||
                    piece.GridFallView == null || !piece.GridFallView.CanPlay ||
                    !snapshot.TryGetPiece(move.PieceId, out PieceModel model))
                {
                    pendingGridGravityMoves.Clear();
                    break;
                }

                GridCoordinate targetPivot = new GridCoordinate(
                    move.ToAnchor.X - model.PivotOffset.X,
                    move.ToAnchor.Y - model.PivotOffset.Y);
                PrepareKinematicBody(piece.Body);
                Vector2 targetPosition = GravityLevelGridCoordinates.FineCellToWorld(level, targetPivot);
                gridFallingPieces.Add(piece);
                if (!piece.GridFallView.PlayFallTo(
                        targetPosition,
                        () => CompleteGridGravityPresentation(piece)))
                {
                    gridFallingPieces.Remove(piece);
                    activeBoard.TrySetPieceState(piece, PieceState.Placed);
                    continue;
                }

                playedAnyMove = true;
            }

            return playedAnyMove;
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

            PrototypeBoard.Active?.TrySetPieceState(piece, PieceState.Placed);
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
        // pieces cannot enter its footprint. It moves atomically without self-collision.
        private bool TryMoveSelectedPieceOnGrid(
            PuzzlePiece piece,
            Vector2 dragIntent,
            bool applySpeedLimit)
        {
            if (!TryResolveSelectedGridMovement(
                    piece,
                    dragIntent,
                    applySpeedLimit,
                    out LevelBoardSnapshot snapshot,
                    out PieceModel model,
                    out GravityLevelDefinition level,
                    out Vector2 clampedPosition))
                return false;

            piece.Body.MovePosition(clampedPosition);
            lastResolvedDragPosition = clampedPosition;
            hasLastResolvedDragPosition = true;

            // Mantıksal grid hücresini (model.Anchor), sürekli pozisyonun şu an
            // en çok örtüştüğü tam hücreye göre arka planda güncelle. Bu sadece
            // hangi hücrelerin "dolu" sayıldığını etkiler, görseli etkilemez.
            GridCoordinate pivotFromContinuous = WorldToDragPivot(level, clampedPosition);
            GridCoordinate anchorFromContinuous = pivotFromContinuous.Offset(model.PivotOffset);
            if (!anchorFromContinuous.Equals(model.Anchor))
                snapshot.Grid.TryMoveIgnoringPiece(model, anchorFromContinuous, model.Id, out _);

            return true;
        }

        /// <summary>
        /// Resolves only the current player input delta. The cursor is never a
        /// destination: every fine-grid transition is validated from the piece's
        /// current footprint, so blocked input cannot accumulate into a route.
        /// </summary>
        private bool TryResolveSelectedGridMovement(
            PuzzlePiece piece,
            Vector2 dragIntent,
            bool applySpeedLimit,
            out LevelBoardSnapshot snapshot,
            out PieceModel model,
            out GravityLevelDefinition level,
            out Vector2 clampedPosition)
        {
            snapshot = null;
            model = null;
            level = null;
            clampedPosition = default;
            if (piece == null || piece.Body == null || !hasSelectedPieceStartAnchor ||
                !TryGetSnapshotPiece(piece, out snapshot, out model))
            {
                return false;
            }

            level = GravityLevelRuntime.FindLevelToPlay();
            if (level == null)
                return false;

            Vector2 currentPosition = hasLastResolvedDragPosition
                ? lastResolvedDragPosition
                : piece.Body.position;
            Vector2 requestedMove = applySpeedLimit
                ? Vector2.ClampMagnitude(dragIntent, level.maxDragSpeed * Time.fixedDeltaTime)
                : dragIntent;
            float requestedDistance = requestedMove.magnitude;
            clampedPosition = currentPosition;
            if (requestedDistance < MinimumMoveDistance)
                return true;

            float fineCellSize = 1f / level.subdivisions;
            int stepCount = Mathf.Max(
                1,
                Mathf.CeilToInt(requestedDistance / (fineCellSize * .25f)));
            Vector2 step = requestedMove / stepCount;
            GridCoordinate currentPivot = WorldToDragPivot(level, currentPosition);

            for (int stepIndex = 0; stepIndex < stepCount; stepIndex++)
            {
                if (!TryResolveDragStep(
                        snapshot,
                        model,
                        level,
                        clampedPosition,
                        step,
                        ref currentPivot,
                        out Vector2 nextPosition))
                    break;

                clampedPosition = nextPosition;
            }

            return true;
        }

        private static bool TryResolveDragStep(
            LevelBoardSnapshot snapshot,
            PieceModel model,
            GravityLevelDefinition level,
            Vector2 currentPosition,
            Vector2 step,
            ref GridCoordinate currentPivot,
            out Vector2 resolvedPosition)
        {
            Vector2 requestedPosition = currentPosition + step;
            GridCoordinate requestedPivot = WorldToDragPivot(level, requestedPosition);
            bool xChanged = requestedPivot.X != currentPivot.X;
            bool yChanged = requestedPivot.Y != currentPivot.Y;
            if (!xChanged && !yChanged)
            {
                resolvedPosition = requestedPosition;
                return true;
            }

            GridCoordinate xPivot = new GridCoordinate(requestedPivot.X, currentPivot.Y);
            GridCoordinate yPivot = new GridCoordinate(currentPivot.X, requestedPivot.Y);
            bool xAllowed = !xChanged || CanMoveToPivot(snapshot, model, xPivot);
            bool yAllowed = !yChanged || CanMoveToPivot(snapshot, model, yPivot);

            if (xChanged && yChanged && xAllowed && yAllowed &&
                CanMoveToPivot(snapshot, model, requestedPivot))
            {
                currentPivot = requestedPivot;
                resolvedPosition = requestedPosition;
                return true;
            }

            if (xChanged && !yChanged && xAllowed)
            {
                currentPivot = xPivot;
                resolvedPosition = requestedPosition;
                return true;
            }

            if (yChanged && !xChanged && yAllowed)
            {
                currentPivot = yPivot;
                resolvedPosition = requestedPosition;
                return true;
            }

            // A blocked diagonal may retain only the independently valid input
            // component. This is sliding from the player's current gesture, not
            // a route search toward the cursor's old absolute position.
            if (xChanged && yChanged && xAllowed && !yAllowed)
            {
                currentPivot = xPivot;
                resolvedPosition = new Vector2(requestedPosition.x, currentPosition.y);
                return true;
            }

            if (xChanged && yChanged && yAllowed && !xAllowed)
            {
                currentPivot = yPivot;
                resolvedPosition = new Vector2(currentPosition.x, requestedPosition.y);
                return true;
            }

            resolvedPosition = currentPosition;
            return false;
        }

        private static bool CanMoveToPivot(
            LevelBoardSnapshot snapshot,
            PieceModel model,
            GridCoordinate pivot)
        {
            GridCoordinate anchor = pivot.Offset(model.PivotOffset);
            return snapshot.Grid.CheckPlacementIgnoringPiece(model, anchor, model.Id).IsSuccess;
        }

        private static void PrepareKinematicBody(Rigidbody2D body)
        {
            body.simulated = true;
            body.bodyType = RigidbodyType2D.Kinematic;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.useFullKinematicContacts = true;
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
            // Consume only fresh pointer movement. Retaining an old absolute
            // target after a collision would turn the drag controller into a
            // path-seeking cursor follower.
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
                    activeFingerId = touch.fingerId;
                    TrySelectPiece(
                        PointerWorldPosition(touch.position),
                        TouchSelectionRadiusInGridCells);
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
                TrySelectPiece(pointer, MouseSelectionRadiusInGridCells);

            if (selectedPiece != null &&
                (Input.GetMouseButton(0) || Input.GetMouseButtonUp(0)))
                UpdateDragTarget(pointer + grabOffset);

            if (selectedPiece != null && Input.GetMouseButtonUp(0))
                ReleasePiece();
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
                if (piece != null && !piece.IsFrozen && !piece.IsBeingShredded &&
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
                    if (candidate == null || candidate.IsFrozen || candidate.IsBeingShredded ||
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

            PrototypeBoard.Active?.StartTimer();

            // A fall can be visually complete while its final presentation
            // callback has not yet converged the model back to Placed. Settle
            // only a non-animating stale fall before trying to begin a drag.
            PrototypeBoard activeBoard = PrototypeBoard.Active;
            if (activeBoard != null &&
                activeBoard.TryGetPieceModel(piece, out PieceModel model) &&
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
            if (PrototypeBoard.Active == null || !PrototypeBoard.Active.TrySetPieceState(piece, PieceState.Dragging))
            {
                hasSelectedPieceStartAnchor = false;
                return;
            }

            Rigidbody2D body = piece.Body;
            selectedPiece = piece;
            PrepareKinematicBody(body);
            selectedPiece.SetSelected(true);
            Physics2D.SyncTransforms();

            grabOffset = body.position - pointerPosition;
            dragTarget = body.position;
            pendingDragIntent = Vector2.zero;
            lastResolvedDragPosition = body.position;
            hasLastResolvedDragPosition = true;
        }

        private void ReleasePiece()
        {
            Rigidbody2D body = selectedPiece.Body;
            TryMoveSelectedPieceOnGrid(selectedPiece, ConsumeDragIntent(), applySpeedLimit: false);

            selectedPiece.SetSelected(false);
            Physics2D.SyncTransforms();
            PrepareKinematicBody(body);
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
                return;
            }

            Debug.LogWarning(
                $"[GridRelease] Failed to restore '{selectedPiece.name}' to its committed grid anchor.",
                selectedPiece);

            selectedPiece = null;
            hasSelectedPieceStartAnchor = false;
            hasLastResolvedDragPosition = false;
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
            GridCoordinate referencePivot = new GridCoordinate(
                selectedPieceStartAnchor.X - model.PivotOffset.X,
                selectedPieceStartAnchor.Y - model.PivotOffset.Y);
            GridCoordinate snappedPivot = SnapToBackgroundAlignedPivot(
                level,
                currentPosition,
                referencePivot);
            Vector2 snappedPosition = GravityLevelGridCoordinates.FineCellToWorld(level, snappedPivot);

            // Snapping is only presentation alignment after the player has
            // traversed a valid path. It may not complete a blocked path or
            // move through a corner that the active drag could not cross.
            if (!TryResolveSelectedGridMovement(
                    piece,
                    snappedPosition - currentPosition,
                    applySpeedLimit: false,
                    out _,
                    out _,
                    out _,
                    out Vector2 resolvedPosition) ||
                (resolvedPosition - snappedPosition).sqrMagnitude >
                MinimumMoveDistance * MinimumMoveDistance)
            {
                return TryCommitCurrentGridRelease(piece, out releasePosition);
            }

            GridCoordinate snappedAnchor = snappedPivot.Offset(model.PivotOffset);
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

        private static GridCoordinate WorldToDragPivot(
            GravityLevelDefinition level,
            Vector2 worldPosition)
        {
            return WorldToNearestFineCell(level, worldPosition);
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
