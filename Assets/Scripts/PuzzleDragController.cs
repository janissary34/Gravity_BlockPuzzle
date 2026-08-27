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
        private GridCoordinate selectedDragPivot;
        private bool hasSelectedDragPivot;
        private Vector2 grabOffset;
        private Vector2 dragTarget;
        private int activeFingerId = -1;
        private ContactFilter2D solidContactFilter;
        private readonly RaycastHit2D[] castHits = new RaycastHit2D[32];
        private readonly Vector2[] contactNormals = new Vector2[32];
        private readonly Dictionary<int, float> snappingTargetsX = new Dictionary<int, float>();
        private readonly Collider2D[] selectionHits = new Collider2D[32];
        private const float MaximumDragSpeed = 10f;
        private const int MaximumSlideIterations = 4;
        private const float MinimumMoveDistance = .0005f;
        private const float CastContactPadding = .002f;
        private const float VisualContactTolerance = .01f;
        private const float ShredderCollisionGuardDistance = 1f;
        private const float MinimumBlockingCrossSection = .001f;
        private const float BlockingNormalDotThreshold = -.001f;
        private const float ContactManifoldTolerance = .004f;
        private const float DuplicateNormalDotThreshold = .9995f;
        private const float TouchSelectionRadiusInGridCells = .45f;
        private const float MouseSelectionRadiusInGridCells = .18f;

        // Track the true mathematical starting X coordinate for each piece
        // to guarantee it always snaps perfectly on its specific fine-cell alignment
        private readonly Dictionary<int, float> pieceStartingX = new Dictionary<int, float>();
        private readonly Dictionary<int, GridCoordinate> lastLoggedDragRejectionAnchors =
            new Dictionary<int, GridCoordinate>();

        public static PuzzleDragController Instance { get; private set; }
        private bool hasMovingPieces = true;
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
            Instance.hasMovingPieces = true;

            // Topology changes must not wait for an unrelated input/fixed
            // update to resume gravity.  In particular, hammer-created
            // remainders can have no physical support after their shared cell
            // is removed. Start their new grid-owned cascade immediately when
            // no existing fall presentation is in flight.
            if (Instance.selectedPiece == null && Instance.gridFallingPieces.Count == 0)
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
            Instance.hasMovingPieces = true;
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
            
            solidContactFilter = new ContactFilter2D();
            solidContactFilter.NoFilter();
            solidContactFilter.useTriggers = false;
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

            snappingTargetsX.Clear();
        }

        private void ClearInputSelection()
        {
            selectedPiece = null;
            activeFingerId = -1;
            hasSelectedPieceStartAnchor = false;
            hasSelectedDragPivot = false;
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
                TryMoveSelectedPieceOnGrid(selectedPiece, dragTarget);

            AdvanceGridGravityPresentation();
        }

        private bool AdvanceGridGravityPresentation()
        {
            if (gridReleasePresentationPiece != null)
            {
                if (gridReleasePresentationPiece.GridFallView != null &&
                    gridReleasePresentationPiece.GridFallView.IsAnimating)
                {
                    return true;
                }

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
                   model.State != PieceState.Dragging &&
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
            hasMovingPieces = true;
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
        private bool TryMoveSelectedPieceOnGrid(PuzzlePiece piece, Vector2 requestedWorldPosition)
        {
            if (piece == null || piece.Body == null || !hasSelectedPieceStartAnchor ||
                !TryGetSnapshotPiece(piece, out LevelBoardSnapshot snapshot, out PieceModel model))
                return false;

            Debug.Log($"[DragDiag] IN req=({requestedWorldPosition.x:F2},{requestedWorldPosition.y:F2}) " +
                $"anchor=({model.Anchor.X},{model.Anchor.Y})");

            GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
            if (level == null)
                return false;

            float fineCellSize = 1f / level.subdivisions;
            Vector2 currentPosition = piece.Body.position;

            // İstenen hareketi X ve Y eksenlerinde AYRI AYRI kelepçele.
            // Her eksende, o yönde bir sonraki bloklu/dolu hücrenin sınırını
            // bul ve pozisyonu o sınırın gerisinde tut — parça duvara doğru
            // sürekli kayar, duvarda durur, parmak daha ileri gitse bile.
            Vector2 clampedPosition = currentPosition;
            clampedPosition.x = ClampAxis(
                snapshot, model, level, currentPosition, requestedWorldPosition.x, fineCellSize, isXAxis: true);
            clampedPosition.y = ClampAxis(
                snapshot, model, level, currentPosition, requestedWorldPosition.y, fineCellSize, isXAxis: false);

            piece.Body.MovePosition(clampedPosition);

            // Mantıksal grid hücresini (model.Anchor), sürekli pozisyonun şu an
            // en çok örtüştüğü tam hücreye göre arka planda güncelle. Bu sadece
            // hangi hücrelerin "dolu" sayıldığını etkiler, görseli etkilemez.
            GridCoordinate pivotFromContinuous = WorldToDragPivot(level, clampedPosition);
            GridCoordinate anchorFromContinuous = pivotFromContinuous.Offset(model.PivotOffset);
            if (!anchorFromContinuous.Equals(model.Anchor))
                snapshot.Grid.TryMoveIgnoringPiece(model, anchorFromContinuous, model.Id, out _);

            lastLoggedDragRejectionAnchors.Remove(piece.GetInstanceID());
            selectedDragPivot = pivotFromContinuous;
            hasSelectedDragPivot = true;
            Debug.Log($"[DragDiag] OUT body=({piece.Body.position.x:F2},{piece.Body.position.y:F2}) " +
                $"anchor=({model.Anchor.X},{model.Anchor.Y}) pivot=({selectedDragPivot.X},{selectedDragPivot.Y})");
            return true;
        }

        // Tek bir eksende, hedefe doğru ne kadar ilerlenebileceğini hesaplar.
        // O yönde ilerlerken karşılaşılan ilk dolu/bloklu hücrenin sınırında durur.
        private float ClampAxis(
            LevelBoardSnapshot snapshot,
            PieceModel model,
            GravityLevelDefinition level,
            Vector2 currentPosition,
            float requestedValueOnAxis,
            float fineCellSize,
            bool isXAxis)
        {
            float currentValue = isXAxis ? currentPosition.x : currentPosition.y;
            float requested = requestedValueOnAxis;
            if (Mathf.Approximately(requested, currentValue))
                return currentValue;

            float direction = Mathf.Sign(requested - currentValue);
            float step = fineCellSize * 0.5f; // ince adımlarla tara, tam sınırı bul
            float testValue = currentValue;

            while (Mathf.Abs(testValue - currentValue) < Mathf.Abs(requested - currentValue))
            {
                float nextTest = testValue + step * direction;
                bool overshoot = direction > 0f
                    ? nextTest > requested
                    : nextTest < requested;
                if (overshoot)
                    nextTest = requested;

                Vector2 testPosition = isXAxis
                    ? new Vector2(nextTest, currentPosition.y)
                    : new Vector2(currentPosition.x, nextTest);

                GridCoordinate testPivot = WorldToDragPivot(level, testPosition);
                GridCoordinate testAnchor = testPivot.Offset(model.PivotOffset);
                GridPlacementResult result = snapshot.Grid.CheckPlacementIgnoringPiece(model, testAnchor, model.Id);
                if (!result.IsSuccess)
                    return testValue; // engellendi, bir önceki geçerli değerde dur

                testValue = nextTest;
                Debug.Log($"[ClampDiag] axis={(isXAxis ? "X" : "Y")} test={testValue:F3} " +
                    $"requested={requested:F3}");
                if (nextTest == requested)
                    break;
            }

            return testValue;
        }

        private void LogDragPlacementRejection(
            PuzzlePiece piece,
            PieceModel model,
            GridCoordinate anchor,
            GridPlacementResult placement)
        {
            int pieceId = piece.GetInstanceID();
            if (lastLoggedDragRejectionAnchors.TryGetValue(pieceId, out GridCoordinate previous) &&
                previous.Equals(anchor))
            {
                return;
            }

            lastLoggedDragRejectionAnchors[pieceId] = anchor;
            Debug.LogWarning(
                $"[GridDrag] Rejected '{piece.name}' id={model.Id}, " +
                $"modelOnBoard={model.IsOnBoard}, targetAnchor=({anchor.X},{anchor.Y}), " +
                $"reason={placement.Reason}, cell=({placement.Coordinate.X},{placement.Coordinate.Y}), " +
                $"cellState={placement.CellState}, occupant={placement.OccupantId}.",
                piece);

            if (PrototypeBoard.Active != null && PrototypeBoard.Active.BoardSnapshot != null)
            {
                int occupant = PrototypeBoard.Active.BoardSnapshot.Grid.GetOccupantId(placement.Coordinate);
                Debug.Log($"[Diag] blocked cell=({placement.Coordinate.X},{placement.Coordinate.Y}) occupant piece id={occupant}");
            }
        }

        private void MoveSelectedBody(PuzzlePiece piece, Vector2 requestedMove)
        {
            Rigidbody2D body = piece.Body;
            Vector2 remainingMove = requestedMove;

            for (int iteration = 0; iteration < MaximumSlideIterations; iteration++)
            {
                float requestedDistance = remainingMove.magnitude;
                if (requestedDistance < MinimumMoveDistance)
                    break;

                Vector2 direction = remainingMove / requestedDistance;
                float maximumClearance =
                    piece.CurrentCollisionInset +
                    GravityGridMetrics.DraggingPieceCollisionSkinInCells +
                    CastContactPadding;
                int hitCount = body.Cast(
                    direction,
                    solidContactFilter,
                    castHits,
                    requestedDistance + maximumClearance);

                bool foundBlockingHit = false;
                float allowedDistance = requestedDistance;
                for (int i = 0; i < hitCount; i++)
                {
                    RaycastHit2D hit = castHits[i];
                    if (hit.collider == null || hit.collider.isTrigger)
                        continue;

                    // Rigidbody2D.Cast may report a collider the piece is already
                    // touching even when the requested motion is away from it or
                    // parallel to it. Treating that zero-distance contact as a
                    // blocker is what made tightly packed pieces feel glued down.
                    if (Vector2.Dot(direction, hit.normal) >= BlockingNormalDotThreshold)
                        continue;

                    if (!HasBlockingCrossSection(piece, hit.collider, direction))
                        continue;

                    float clearance = RequiredVisualClearance(piece, hit.collider);
                    if (hit.distance > requestedDistance + clearance)
                        continue;

                    float candidateDistance = Mathf.Max(0f, hit.distance - clearance);
                    if (foundBlockingHit && candidateDistance >= allowedDistance)
                        continue;

                    foundBlockingHit = true;
                    allowedDistance = candidateDistance;
                }

                int contactNormalCount = foundBlockingHit
                    ? CollectContactNormals(
                        piece,
                        direction,
                        hitCount,
                        requestedDistance,
                        allowedDistance)
                    : 0;

                Vector2 completedMove = direction * allowedDistance;
                MoveBody(body, body.position + completedMove);

                remainingMove -= completedMove;
                if (!foundBlockingHit || allowedDistance >= requestedDistance - MinimumMoveDistance)
                    break;

                // Use every surface reached at the same time, rather than whichever
                // one happened to be first in Box2D's hit array. The projection is
                // order-independent, so concave pieces cannot alternate between two
                // normals and shake while the pointer is held still.
                remainingMove = ProjectOntoContactManifold(remainingMove, contactNormalCount);
                if (remainingMove.sqrMagnitude < MinimumMoveDistance * MinimumMoveDistance)
                    break;
            }

            body.velocity = Vector2.zero;
            body.angularVelocity = 0f;
        }

        private int CollectContactNormals(
            PuzzlePiece movingPiece,
            Vector2 direction,
            int hitCount,
            float requestedDistance,
            float allowedDistance)
        {
            int normalCount = 0;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit2D hit = castHits[i];
                if (hit.collider == null || hit.collider.isTrigger)
                    continue;

                Vector2 normal = hit.normal;
                if (normal.sqrMagnitude < .5f ||
                    Vector2.Dot(direction, normal) >= BlockingNormalDotThreshold ||
                    !HasBlockingCrossSection(movingPiece, hit.collider, direction))
                    continue;

                float clearance = RequiredVisualClearance(movingPiece, hit.collider);
                if (hit.distance > requestedDistance + clearance)
                    continue;

                float candidateDistance = Mathf.Max(0f, hit.distance - clearance);
                if (candidateDistance > allowedDistance + ContactManifoldTolerance)
                    continue;

                normal.Normalize();
                bool duplicate = false;
                for (int existing = 0; existing < normalCount; existing++)
                {
                    if (Vector2.Dot(contactNormals[existing], normal) >= DuplicateNormalDotThreshold)
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (!duplicate && normalCount < contactNormals.Length)
                    contactNormals[normalCount++] = normal;
            }

            return normalCount;
        }

        private Vector2 ProjectOntoContactManifold(Vector2 movement, int normalCount)
        {
            if (normalCount == 0 || SatisfiesEveryContact(movement, normalCount))
                return movement;

            // In 2D the closest legal vector is either unchanged, projected onto
            // one contact face, or zero at the intersection of multiple faces.
            // Testing those candidates avoids the order dependence of repeatedly
            // clipping against normals returned in a non-deterministic hit order.
            Vector2 best = Vector2.zero;
            float bestError = movement.sqrMagnitude;
            for (int i = 0; i < normalCount; i++)
            {
                Vector2 normal = contactNormals[i];
                Vector2 candidate = movement - normal * Vector2.Dot(movement, normal);
                if (!SatisfiesEveryContact(candidate, normalCount))
                    continue;

                float error = (candidate - movement).sqrMagnitude;
                if (error < bestError)
                {
                    best = candidate;
                    bestError = error;
                }
            }

            return best;
        }

        private bool SatisfiesEveryContact(Vector2 movement, int normalCount)
        {
            for (int i = 0; i < normalCount; i++)
            {
                if (Vector2.Dot(movement, contactNormals[i]) < BlockingNormalDotThreshold)
                    return false;
            }

            return true;
        }

        private float RequiredVisualClearance(
            PuzzlePiece movingPiece,
            Collider2D hitCollider)
        {
            float collisionInset = movingPiece.CurrentCollisionInset;
            PuzzlePiece hitPiece = hitCollider.GetComponentInParent<PuzzlePiece>();
            if (hitPiece != null && hitPiece != movingPiece)
                collisionInset += hitPiece.CurrentCollisionInset;

            if (IsInShredderApproach(movingPiece) ||
                (hitPiece != null && IsInShredderApproach(hitPiece)))
            {
                // In the final cell before the shredder, preserve the full visual
                // footprint. This prevents two falling pieces from appearing to
                // weave together as they enter the physical shredder sequence.
                return collisionInset + CastContactPadding;
            }

            if (hitPiece == null)
            {
                // Obstacles and the board frame must use the full footprint.
                // The small visual overlap tolerated between moving pieces would
                // otherwise let a dragged piece enter an obstacle before release.
                return collisionInset + CastContactPadding;
            }

            if (movingPiece.IsSelected || hitPiece.IsSelected)
            {
                // A held piece must keep its complete visible footprint. The
                // old fine-grid tolerance let the pointer push two pieces into
                // each other, which made their voxel artwork visibly intersect.
                return collisionInset + CastContactPadding;
            }

            // Retain the original fine-grid tolerance only between movable pieces,
            // so they can still pass through exact-size authored openings.
            return Mathf.Max(0f, collisionInset - VisualContactTolerance) +
                   CastContactPadding;
        }

        private static bool IsInShredderApproach(PuzzlePiece piece)
        {
            IReadOnlyList<ShredderCatchZone> zones = ShredderCatchZone.ActiveZones;
            for (int i = 0; i < zones.Count; i++)
            {
                ShredderCatchZone zone = zones[i];
                if (zone != null &&
                    piece.LowestColliderPoint() <= zone.ShredY + ShredderCollisionGuardDistance)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasBlockingCrossSection(
            PuzzlePiece movingPiece,
            Collider2D hitCollider,
            Vector2 movementDirection)
        {
            // A sweep can return a diagonal normal where two sharp corners only
            // touch. Project both AABBs onto the axis perpendicular to movement:
            // without a real cross-section overlap, the contact is tangent and
            // must not stop a piece sliding past a staggered obstacle corner.
            Vector2 perpendicular = new Vector2(
                -movementDirection.y,
                movementDirection.x).normalized;
            Bounds movingBounds = movingPiece.CollisionBounds;
            Bounds hitBounds = hitCollider.bounds;

            Vector2 centreDelta = (Vector2)hitBounds.center -
                                  (Vector2)movingBounds.center;
            float centreSeparation = Mathf.Abs(Vector2.Dot(centreDelta, perpendicular));
            float movingRadius =
                movingBounds.extents.x * Mathf.Abs(perpendicular.x) +
                movingBounds.extents.y * Mathf.Abs(perpendicular.y);
            float hitRadius =
                hitBounds.extents.x * Mathf.Abs(perpendicular.x) +
                hitBounds.extents.y * Mathf.Abs(perpendicular.y);

            return movingRadius + hitRadius - centreSeparation >
                   MinimumBlockingCrossSection;
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
            // This is intentionally a Rigidbody2D position write, never a transform
            // write. The target has already been swept with Rigidbody2D.Cast and must
            // be applied immediately so the remaining slide iterations cast from the
            // new position in this same FixedUpdate.
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
                dragTarget = PointerWorldPosition(touch.position) + grabOffset;
                ReleasePiece();
                activeFingerId = -1;
                return;
            }

            dragTarget = PointerWorldPosition(touch.position) + grabOffset;
        }

        private void ProcessMouseInput()
        {
            Vector2 pointer = PointerWorldPosition(Input.mousePosition);

            if (Input.GetMouseButtonDown(0))
                TrySelectPiece(pointer, MouseSelectionRadiusInGridCells);

            if (selectedPiece != null &&
                (Input.GetMouseButton(0) || Input.GetMouseButtonUp(0)))
                dragTarget = pointer + grabOffset;

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
            LevelBoardSnapshot selectedSnapshot;
            PieceModel selectedModel;
            hasSelectedPieceStartAnchor =
                TryGetSnapshotPiece(piece, out selectedSnapshot, out selectedModel) &&
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

            selectedPiece = piece;
            if (hasSelectedPieceStartAnchor)
            {
                selectedDragPivot = new GridCoordinate(
                    selectedPieceStartAnchor.X - selectedModel.PivotOffset.X,
                    selectedPieceStartAnchor.Y - selectedModel.PivotOffset.Y);
                hasSelectedDragPivot = true;
            }
            Rigidbody2D body = piece.Body;
            PrepareKinematicBody(body);
            selectedPiece.SetSelected(true);
            Physics2D.SyncTransforms();

            int pieceId = piece.GetInstanceID();

            // Record the piece's mathematically true origin X the very first time it is grabbed.
            // Because pieces spawn perfectly aligned to the grid, we can guarantee that 
            // any valid snapped position is simply a whole integer offset from this starting X!
            if (!pieceStartingX.ContainsKey(pieceId))
            {
                GravityLevelDefinition activeLevel = GravityLevelRuntime.FindLevelToPlay();
                int boardColumns = activeLevel != null ? activeLevel.boardColumns : 6;
                int subdivisions = activeLevel != null ? activeLevel.subdivisions : 4;
                float fineCellSize = 1f / subdivisions;
                
                float boardLeftEdge = -boardColumns * 0.5f;
                float offset = fineCellSize * 0.5f;
                
                // Reconstruct the exact mathematical spawn point of this fine cell
                float kFloat = (body.position.x - offset - boardLeftEdge) / fineCellSize;
                int kRound = Mathf.RoundToInt(kFloat);
                float trueOriginX = boardLeftEdge + kRound * fineCellSize + offset;
                
                pieceStartingX[pieceId] = trueOriginX;
            }

            snappingTargetsX.Remove(pieceId);
            grabOffset = body.position - pointerPosition;
            dragTarget = body.position;
            hasMovingPieces = true;
        }

        private void ReleasePiece()
        {
            Rigidbody2D body = selectedPiece.Body;
            selectedPiece.SetSelected(false);
            Physics2D.SyncTransforms();
            PrepareKinematicBody(body);
            int pieceId = selectedPiece.GetInstanceID();
            if (TryCommitGridRelease(selectedPiece, dragTarget, out Vector2 releasePosition))
            {
                snappingTargetsX.Remove(pieceId);
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
                hasSelectedDragPivot = false;
                hasMovingPieces = true;
                return;
            }

            if (hasSelectedPieceStartAnchor &&
                TryRestoreGridRelease(selectedPiece, selectedPieceStartAnchor, out Vector2 restorePosition))
            {
                snappingTargetsX.Remove(pieceId);
                MoveBody(body, restorePosition);
                selectedPiece = null;
                hasSelectedPieceStartAnchor = false;
                hasSelectedDragPivot = false;
                hasMovingPieces = true;
                return;
            }

            Debug.LogWarning(
                $"[GridRelease] Failed to restore '{selectedPiece.name}' to its committed grid anchor.",
                selectedPiece);

            selectedPiece = null;
            hasSelectedPieceStartAnchor = false;
            hasSelectedDragPivot = false;
            hasMovingPieces = true;
        }

        private void CompleteGridReleasePresentation(PuzzlePiece piece)
        {
            if (gridReleasePresentationPiece == piece)
                gridReleasePresentationPiece = null;
        }

        private static bool TryCommitGridRelease(
            PuzzlePiece piece,
            Vector2 requestedWorldPosition,
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

            if (!TryFindNearestValidReleasePivot(
                    snapshot,
                    model,
                    activeLevel,
                    requestedWorldPosition,
                    out GridCoordinate pivot))
            {
                Debug.LogWarning(
                    $"[GridRelease] No valid anchor found for '{piece.name}' id={model.Id}.",
                    piece);
                return false;
            }

            GridCoordinate anchor = pivot.Offset(model.PivotOffset);
            GridPlacementResult placementResult = GridPlacementResult.Failure(
                GridPlacementFailureReason.OutOfBounds,
                anchor,
                GridCellState.Blocked,
                default);
            if (!snapshot.Grid.IsInside(anchor) ||
                !activeBoard.TryMovePieceOnGrid(piece, anchor, out placementResult))
            {
                Debug.LogWarning(
                    $"[GridRelease] Rejected '{piece.name}' id={model.Id}, " +
                    $"modelOnBoard={model.IsOnBoard}, targetAnchor=({anchor.X},{anchor.Y}), " +
                    $"reason={placementResult.Reason}, cell=({placementResult.Coordinate.X},{placementResult.Coordinate.Y}), " +
                    $"cellState={placementResult.CellState}, occupant={placementResult.OccupantId}.",
                    piece);
                return false;
            }

            releasePosition = GravityLevelGridCoordinates.FineCellToWorld(activeLevel, pivot);
            return true;
        }

        private static bool TryFindNearestValidReleasePivot(
            LevelBoardSnapshot snapshot,
            PieceModel model,
            GravityLevelDefinition level,
            Vector2 requestedWorldPosition,
            out GridCoordinate nearestPivot)
        {
            nearestPivot = default;
            bool found = false;
            float nearestDistanceSquared = float.PositiveInfinity;
            int fineColumns = level.boardColumns * level.subdivisions;
            int fineRows = level.boardRows * level.subdivisions;

            for (int y = 0; y < fineRows; y++)
            {
                for (int x = 0; x < fineColumns; x++)
                {
                    GridCoordinate candidatePivot = new GridCoordinate(x, y);
                    GridCoordinate candidateAnchor = candidatePivot.Offset(model.PivotOffset);
                    if (!snapshot.Grid.CheckPlacementIgnoringPiece(model, candidateAnchor, model.Id).IsSuccess)
                        continue;

                    Vector2 candidatePosition = GravityLevelGridCoordinates.FineCellToWorld(
                        level,
                        candidatePivot);
                    float distanceSquared = (candidatePosition - requestedWorldPosition).sqrMagnitude;
                    if (found && distanceSquared >= nearestDistanceSquared)
                        continue;

                    nearestPivot = candidatePivot;
                    nearestDistanceSquared = distanceSquared;
                    found = true;
                }
            }

            return found;
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
