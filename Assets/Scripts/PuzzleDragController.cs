using System.Collections.Generic;
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
        private const float MaximumDragSpeed = 10f;
        private const float MaximumFallSpeed = 8f;
        private const int MaximumSlideIterations = 4;
        private const float MinimumMoveDistance = .0005f;
        private const float CastContactPadding = .002f;
        private const float VisualContactTolerance = .01f;
        private const float MinimumBlockingCrossSection = .001f;
        private const float BlockingNormalDotThreshold = -.001f;
        private const float ContactManifoldTolerance = .004f;
        private const float DuplicateNormalDotThreshold = .9995f;
        private const float TouchSelectionRadiusInGridCells = .45f;
        private const float MouseSelectionRadiusInGridCells = .18f;

        // Track the true mathematical starting X coordinate for each piece
        // to guarantee it always snaps perfectly on its specific fine-cell alignment
        private readonly Dictionary<int, float> pieceStartingX = new Dictionary<int, float>();

        private void Awake()
        {
            gameCamera = Camera.main;
            
            solidContactFilter = new ContactFilter2D();
            solidContactFilter.NoFilter();
            solidContactFilter.useTriggers = false;
        }

        private void Update()
        {
            if (HammerBooster.IsTargeting)
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
            // Player movement has priority for this tick. Every other piece then
            // advances under deterministic manual gravity, so holding one piece
            // never freezes the rest of the board and no body receives impulses.
            if (selectedPiece != null && selectedPiece.Body != null)
            {
                Rigidbody2D body = selectedPiece.Body;
                PrepareKinematicBody(body);
                Vector2 requestedMove = Vector2.ClampMagnitude(
                    dragTarget - body.position,
                    MaximumDragSpeed * Time.fixedDeltaTime);
                MoveSelectedBody(selectedPiece, requestedMove);
                fallingSpeeds[selectedPiece.GetInstanceID()] = 0f;
            }

            AdvanceManualGravity();
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
                body.position += completedMove;
                Physics2D.SyncTransforms();

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

        private static float RequiredVisualClearance(
            PuzzlePiece movingPiece,
            Collider2D hitCollider)
        {
            float collisionInset = movingPiece.CurrentCollisionInset;
            PuzzlePiece hitPiece = hitCollider.GetComponentInParent<PuzzlePiece>();
            if (hitPiece != null && hitPiece != movingPiece)
                collisionInset += hitPiece.CurrentCollisionInset;

            // Equal-size pieces and openings cannot satisfy positive visual
            // clearance on both opposing faces at once. Keep a sub-pixel-scale
            // tolerance inside the per-cell collision skin so exact grid fits do
            // not lock at their corners, while the colliders still cap how far
            // any two visuals can overlap.
            return Mathf.Max(0f, collisionInset - VisualContactTolerance) +
                   CastContactPadding;
        }

        private void AdvanceManualGravity()
        {
            RefreshActivePieces();
            activePieces.Sort(CompareGravityOrder);

            foreach (PuzzlePiece piece in activePieces)
            {
                if (piece == null || piece.Body == null)
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
                        body.position = new Vector2(targetX, body.position.y);
                    }
                }
                
                bool grounded = MovePieceDown(piece, requestedDistance);
                fallingSpeeds[pieceId] = grounded ? 0f : fallingSpeed;
            }
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
                body.position += Vector2.down * allowedDistance;
                Physics2D.SyncTransforms();
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
            body.interpolation = RigidbodyInterpolation2D.None;
            body.useFullKinematicContacts = true;
            body.velocity = Vector2.zero;
            body.angularVelocity = 0f;
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
                if (piece != null && !piece.IsFrozen)
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
                    if (candidate == null || candidate.IsFrozen)
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
        }

        private void ReleasePiece()
        {
            Rigidbody2D body = selectedPiece.Body;
            selectedPiece.SetSelected(false);
            Physics2D.SyncTransforms();
            PrepareKinematicBody(body);
            int pieceId = selectedPiece.GetInstanceID();
            fallingSpeeds[pieceId] = 0f;

            // We use the ACTUAL current physical position (which has already been restricted by 
            // wall collisions in MoveSelectedBody) to determine the nearest valid grid cell.
            float currentX = body.position.x;
            
            // Formula: StartingX + Mathf.Round((CurrentX - StartingX) / 1.0f) * 1.0f
            // This guarantees the block snaps exactly 1 coarse cell at a time relative to its true physics alignment!
            float startX = pieceStartingX[pieceId];
            float offsetFromStart = currentX - startX;
            float snappedX = startX + Mathf.Round(offsetFromStart); // Round to nearest 1.0
            
            // Set the final target for the snap animation
            snappingTargetsX[pieceId] = snappedX;

            selectedPiece = null;
        }

        private Vector2 PointerWorldPosition(Vector2 screenPoint)
        {
            Vector3 screenPosition = screenPoint;
            screenPosition.z = -gameCamera.transform.position.z;
            return gameCamera.ScreenToWorldPoint(screenPosition);
        }

    }
}
