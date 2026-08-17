using System.Collections.Generic;
using GravityPuzzle.Core.Grid;
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
        private PuzzlePiece gridFallingPiece;
        private readonly Queue<GridGravityMove> pendingGridGravityMoves =
            new Queue<GridGravityMove>();
        private GridCoordinate selectedPieceStartAnchor;
        private bool hasSelectedPieceStartAnchor;
        private Vector2 grabOffset;
        private Vector2 dragTarget;
        private int activeFingerId = -1;
        private ContactFilter2D solidContactFilter;
        private readonly RaycastHit2D[] castHits = new RaycastHit2D[32];
        private readonly Vector2[] contactNormals = new Vector2[32];
        private readonly Dictionary<int, float> fallingSpeeds = new Dictionary<int, float>();
        private readonly Dictionary<int, float> snappingTargetsX = new Dictionary<int, float>();
        private readonly List<PuzzlePiece> activePieces = new List<PuzzlePiece>();
        private readonly Collider2D[] selectionHits = new Collider2D[32];
        private readonly Collider2D[] snapOverlapHits = new Collider2D[64];
        private const float MaximumDragSpeed = 10f;
        private const float MaximumFallSpeed = 8f;
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

        public static PuzzleDragController Instance { get; private set; }
        private bool hasMovingPieces = true;

        public static void WakeUpGravity()
        {
            if (Instance != null)
                Instance.hasMovingPieces = true;
        }

        private void Awake()
        {
            Instance = this;
            gameCamera = Camera.main;
            
            solidContactFilter = new ContactFilter2D();
            solidContactFilter.NoFilter();
            solidContactFilter.useTriggers = false;
        }

        private void Update()
        {
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

            if (Input.touchCount > 0)
                ProcessTouchInput();
            else
                ProcessMouseInput();
        }

        private void FixedUpdate()
        {
            Physics2D.SyncTransforms();
            // Player movement has priority for this tick. Every other piece then
            // advances under deterministic manual gravity, so holding one piece
            // never freezes the rest of the board and no body receives impulses.
            if (selectedPiece != null && !selectedPiece.IsBeingShredded && selectedPiece.Body != null)
            {
                Rigidbody2D body = selectedPiece.Body;
                PrepareKinematicBody(body);
                Vector2 requestedMove = Vector2.ClampMagnitude(
                    dragTarget - body.position,
                    MaximumDragSpeed * Time.fixedDeltaTime);
                MoveSelectedBody(selectedPiece, requestedMove);
                fallingSpeeds[selectedPiece.GetInstanceID()] = 0f;
            }

            // A registered grid piece must never fall through the legacy
            // presentation after the planner settles. That would move its
            // transform without moving its grid model.
            if (!AdvanceGridGravityPresentation())
                AdvanceManualGravityForUnregisteredPieces();
        }

        private bool AdvanceGridGravityPresentation()
        {
            if (gridFallingPiece != null)
            {
                if (gridFallingPiece.GridFallView != null &&
                    gridFallingPiece.GridFallView.IsAnimating)
                    return true;

                gridFallingPiece = null;
            }

            // Dragging and the existing horizontal release snap retain their
            // current presentation path. Grid gravity begins only after the
            // player has released a settled, committed board state.
            if (selectedPiece != null || snappingTargetsX.Count > 0)
                return false;

            PrototypeBoard activeBoard = PrototypeBoard.Active;
            LevelBoardSnapshot snapshot = activeBoard != null
                ? activeBoard.BoardSnapshot
                : null;
            if (snapshot == null)
                return false;

            // Resolve the complete logical cascade before starting its visual
            // presentation.  A lower piece can then vacate cells and let the
            // next piece above it calculate its final resting anchor in this
            // same gravity pass instead of waiting for another gameplay event.
            if (pendingGridGravityMoves.Count == 0 &&
                !TryBuildSettledGridGravityPlan(activeBoard, snapshot))
                return false;

            return TryPlayNextGridGravityMove(activeBoard, snapshot);
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
                   piece.GridFallView != null &&
                   piece.GridFallView.CanPlay;
        }

        private bool TryPlayNextGridGravityMove(
            PrototypeBoard activeBoard,
            LevelBoardSnapshot snapshot)
        {
            if (pendingGridGravityMoves.Count == 0)
                return false;

            GridGravityMove move = pendingGridGravityMoves.Dequeue();

            PuzzlePiece piece = FindActivePiece(move.PieceId);
            if (piece == null || piece.IsFrozen || piece.IsBeingShredded ||
                piece.GridFallView == null || !piece.GridFallView.CanPlay ||
                !snapshot.TryGetPiece(move.PieceId, out PieceModel model))
            {
                pendingGridGravityMoves.Clear();
                return false;
            }

            GridCoordinate targetPivot = new GridCoordinate(
                move.ToAnchor.X - model.PivotOffset.X,
                move.ToAnchor.Y - model.PivotOffset.Y);
            GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
            if (level == null)
            {
                pendingGridGravityMoves.Clear();
                return false;
            }

            PrepareKinematicBody(piece.Body);
            gridFallingPiece = piece;
            Vector2 targetPosition = GravityLevelGridCoordinates.FineCellToWorld(level, targetPivot);
            piece.GridFallView.PlayTo(targetPosition, () => CompleteGridGravityPresentation(piece));
            return true;
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
            if (gridFallingPiece != piece)
                return;

            PrototypeBoard.Active?.TrySetPieceState(piece, PieceState.Placed);
            gridFallingPiece = null;
            hasMovingPieces = true;
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

        private void AdvanceManualGravityForUnregisteredPieces()
        {
            if (selectedPiece == null && snappingTargetsX.Count == 0 && !hasMovingPieces)
                return;

            RefreshActivePieces();
            activePieces.Sort(CompareGravityOrder);

            bool anyPieceMoved = false;

            foreach (PuzzlePiece piece in activePieces)
            {
                if (piece == null || piece.Body == null)
                    continue;

                if (IsRegisteredWithGrid(piece))
                    continue;

                int pieceId = piece.GetInstanceID();
                if (piece == selectedPiece || piece.IsBeingShredded || piece.IsFrozen)
                {
                    fallingSpeeds[pieceId] = 0f;
                    continue;
                }

                Rigidbody2D body = piece.Body;
                PrepareKinematicBody(body);

                fallingSpeeds.TryGetValue(pieceId, out float fallingSpeed);
                float gravity = Mathf.Abs(Physics2D.gravity.y) * Mathf.Max(0f, body.gravityScale);
                fallingSpeed = Mathf.Min(
                    MaximumFallSpeed,
                    fallingSpeed + gravity * Time.fixedDeltaTime);

                float requestedDistance = fallingSpeed * Time.fixedDeltaTime;
                
                if (snappingTargetsX.TryGetValue(pieceId, out float targetX))
                {
                    float currentX = body.position.x;
                    if (Mathf.Abs(currentX - targetX) > 0.001f)
                    {
                        float moveX = Mathf.MoveTowards(currentX, targetX, 20f * Time.fixedDeltaTime) - currentX;
                        MoveSelectedBody(piece, new Vector2(moveX, 0f));
                        anyPieceMoved = true;
                        
                        if (Mathf.Abs(body.position.x - currentX) < 0.0001f)
                        {
                            // Physically blocked! Cancel the snap.
                            snappingTargetsX.Remove(pieceId);
                        }
                        else
                        {
                            fallingSpeeds[pieceId] = 0f; // Freeze falling while snapping horizontally
                            continue;
                        }
                    }
                    else
                    {
                        snappingTargetsX.Remove(pieceId);
                        if (CanOccupySnapPosition(piece, targetX))
                        {
                            MoveBody(body, new Vector2(targetX, body.position.y));
                            anyPieceMoved = true;
                        }
                    }
                }

                bool grounded = MovePieceDown(piece, requestedDistance);
                if (!grounded && requestedDistance >= MinimumMoveDistance)
                {
                    anyPieceMoved = true;
                    PrototypeBoard.Active?.TrySetPieceState(piece, PieceState.Falling);
                }

                fallingSpeeds[pieceId] = grounded ? 0f : fallingSpeed;
            }

            if (!anyPieceMoved && selectedPiece == null && snappingTargetsX.Count == 0)
            {
                hasMovingPieces = false;
            }
        }

        private static bool IsRegisteredWithGrid(PuzzlePiece piece)
        {
            return PrototypeBoard.Active != null &&
                   PrototypeBoard.Active.TryGetPieceModel(piece, out PieceModel model) &&
                   model.IsOnBoard;
        }

        private void RefreshActivePieces()
        {
            activePieces.Clear();
            IReadOnlyList<PuzzlePiece> registeredPieces = PuzzlePiece.ActivePieces;
            for (int i = 0; i < registeredPieces.Count; i++)
            {
                PuzzlePiece piece = registeredPieces[i];
                if (piece != null)
                    activePieces.Add(piece);
            }
        }

        private bool MovePieceDown(PuzzlePiece piece, float requestedDistance)
        {
            if (requestedDistance < MinimumMoveDistance)
                return false;

            Rigidbody2D body = piece.Body;
            float maximumClearance =
                piece.CurrentCollisionInset +
                GravityGridMetrics.DraggingPieceCollisionSkinInCells +
                CastContactPadding;
            int hitCount = body.Cast(
                Vector2.down,
                solidContactFilter,
                castHits,
                requestedDistance + maximumClearance);

            bool foundBlockingHit = false;
            float allowedDistance = requestedDistance;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit2D hit = castHits[i];
                if (hit.collider == null || hit.collider.isTrigger ||
                    Vector2.Dot(Vector2.down, hit.normal) >= BlockingNormalDotThreshold ||
                    !HasBlockingCrossSection(piece, hit.collider, Vector2.down))
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

            if (allowedDistance >= MinimumMoveDistance)
            {
                MoveBody(body, body.position + Vector2.down * allowedDistance);
            }

            body.velocity = Vector2.zero;
            body.angularVelocity = 0f;
            return foundBlockingHit &&
                   allowedDistance < requestedDistance - MinimumMoveDistance;
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

        private static int CompareGravityOrder(PuzzlePiece first, PuzzlePiece second)
        {
            float firstBottom = first.LowestColliderPoint();
            float secondBottom = second.LowestColliderPoint();
            int verticalOrder = firstBottom.CompareTo(secondBottom);
            return verticalOrder != 0
                ? verticalOrder
                : first.GetInstanceID().CompareTo(second.GetInstanceID());
        }

        private static void PrepareKinematicBody(Rigidbody2D body)
        {
            body.bodyType = RigidbodyType2D.Kinematic;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.useFullKinematicContacts = true;
            body.sleepMode = RigidbodySleepMode2D.NeverSleep;
            body.velocity = Vector2.zero;
            body.angularVelocity = 0f;
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

        private bool CanOccupySnapPosition(PuzzlePiece piece, float targetX)
        {
            Rigidbody2D body = piece.Body;
            Vector2 originalPosition = body.position;
            if (Mathf.Abs(originalPosition.x - targetX) > .0001f)
            {
                body.position = new Vector2(targetX, originalPosition.y);
                Physics2D.SyncTransforms();
            }

            bool overlapsSolid = false;
            Collider2D[] pieceColliders = piece.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < pieceColliders.Length && !overlapsSolid; i++)
            {
                Collider2D pieceCollider = pieceColliders[i];
                if (pieceCollider == null || !pieceCollider.enabled || pieceCollider.isTrigger)
                    continue;

                int overlapCount = pieceCollider.OverlapCollider(
                    solidContactFilter,
                    snapOverlapHits);
                for (int overlapIndex = 0; overlapIndex < overlapCount; overlapIndex++)
                {
                    Collider2D other = snapOverlapHits[overlapIndex];
                    if (other == null || other.isTrigger ||
                        other.GetComponentInParent<PuzzlePiece>() == piece)
                        continue;

                    overlapsSolid = true;
                    break;
                }
            }

            if (body.position != originalPosition)
            {
                body.position = originalPosition;
                Physics2D.SyncTransforms();
            }

            return !overlapsSolid;
        }

        private static void ClearSelectedPieceFromGrid(PuzzlePiece piece)
        {
            PrototypeBoard.Active?.TryClearPieceFromGrid(piece, PieceState.Dragging);
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

            if (selectedPiece != null && Input.GetMouseButton(0))
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

            selectedPiece = piece;
            Rigidbody2D body = piece.Body;
            PrepareKinematicBody(body);
            selectedPiece.SetSelected(true);

            // The grid removes the held piece while it is being dragged. Retain
            // its last committed anchor so an invalid release can return to a
            // known legal board position without asking physics to decide.
            LevelBoardSnapshot selectedSnapshot;
            PieceModel selectedModel;
            hasSelectedPieceStartAnchor =
                TryGetSnapshotPiece(selectedPiece, out selectedSnapshot, out selectedModel) &&
                selectedModel.IsOnBoard;
            if (hasSelectedPieceStartAnchor)
                selectedPieceStartAnchor = selectedModel.Anchor;

            ClearSelectedPieceFromGrid(selectedPiece);
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
            fallingSpeeds[pieceId] = 0f;

            // Once a piece has a board model, the grid owns both its legal
            // position and its rollback position.  Falling through to the
            // legacy physics snap after a rejected grid placement is what let
            // hammer fragments visually enter occupied cells and obstacles.
            // Capture this before committing: the model is intentionally
            // off-board while the player is holding it.
            bool isGridOwned = TryGetSnapshotPiece(
                selectedPiece,
                out _,
                out _);

            // Preserve the game's authored horizontal cadence: pieces move in
            // whole board-cell steps from their own spawn alignment. The grid
            // still validates every fine cell occupied by the resulting shape.
            float currentX = body.position.x;
            float startX = pieceStartingX[pieceId];
            float snappedX = startX + Mathf.Round(currentX - startX);

            // Phase 4 ownership: the grid decides whether the released shape
            // fits. Physics may still present the drag and fall, but it cannot
            // commit a piece into cells already owned by another piece.
            if (TryCommitGridRelease(selectedPiece, snappedX, out Vector2 releasePosition))
            {
                snappingTargetsX.Remove(pieceId);
                MoveBody(body, releasePosition);
                selectedPiece = null;
                hasSelectedPieceStartAnchor = false;
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
                hasMovingPieces = true;
                return;
            }

            if (isGridOwned)
            {
                // A grid-owned piece must never use the old collider-based
                // snap as a fallback.  It would update only the transform,
                // leaving the matrix at a different position.  Keep the
                // release rejected and leave the body at its last legal grid
                // presentation instead of allowing an illegal overlap.
                Debug.LogWarning(
                    $"[GridRelease] Rejected release for '{selectedPiece.name}'. " +
                    "Could not restore its committed grid anchor.",
                    selectedPiece);
                selectedPiece = null;
                hasSelectedPieceStartAnchor = false;
                hasMovingPieces = true;
                return;
            }

            // We use the ACTUAL current physical position (which has already been restricted by 
            // wall collisions in MoveSelectedBody) to determine the nearest valid grid cell.
            // Formula: StartingX + Mathf.Round((CurrentX - StartingX) / 1.0f) * 1.0f
            // This guarantees the block snaps exactly 1 coarse cell at a time relative to its true physics alignment!
            float offsetFromStart = currentX - startX;
            snappedX = startX + Mathf.Round(offsetFromStart); // Round to nearest 1.0
            
            // Keep the legacy snap active while the matrix is still being
            // synchronized with the physics-driven runtime. The snapshot is
            // updated only after this snap visibly completes.
            snappingTargetsX[pieceId] = snappedX;

            selectedPiece = null;
            hasSelectedPieceStartAnchor = false;
            hasMovingPieces = true;
        }

        private static bool TryCommitGridRelease(
            PuzzlePiece piece,
            float snappedX,
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

            GridCoordinate pivot = WorldToNearestFineCell(
                activeLevel,
                new Vector2(snappedX, piece.Body.position.y));
            GridCoordinate anchor = pivot.Offset(model.PivotOffset);
            GridPlacementResult placementResult;
            if (!snapshot.Grid.IsInside(anchor) ||
                !activeBoard.TryMovePieceOnGrid(piece, anchor, out placementResult))
                return false;

            releasePosition = GravityLevelGridCoordinates.FineCellToWorld(activeLevel, pivot);
            return true;
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
