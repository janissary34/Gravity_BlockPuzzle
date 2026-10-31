using System.Collections.Generic;
using GravityPuzzle.Core.Grid;
using GravityPuzzle.Gameplay.Pieces;
using GravityPuzzle.Infrastructure.Pooling;
using UnityEngine;

namespace GravityPuzzle
{
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class ShredderCatchZone : MonoBehaviour, IPoolable
    {
        private static readonly List<ShredderCatchZone> activeZones = new List<ShredderCatchZone>();

        private BoxCollider2D trigger;
        public static IReadOnlyList<ShredderCatchZone> ActiveZones => activeZones;
        public float ShredY { get; private set; }
        public float WheelRadius { get; private set; }
        public float LeftX => leftX;
        public float RightX => rightX;
        private float leftX;
        private float rightX;
        private float captureTopY;
        private float captureApproachDistance;
        private static readonly GridCoordinate Down = new GridCoordinate(0, -1);
        private const float CorridorEpsilon = .0001f;
        // Catch decisions use real occupied cell geometry rather than the
        // enclosing CompositeCollider2D AABB. This keeps notched shapes from
        // claiming empty space in a mouth lane.
        private readonly List<Bounds> activeCellBoundsBuffer = new List<Bounds>(32);

        private void Awake()
        {
            trigger = GetComponent<BoxCollider2D>();
        }

        public void Configure(Vector2 position, Vector2 size, float shredY, float approachDistance, float wheelRadius = 0.4f)
        {
            transform.position = position;
            trigger.size = size;
            trigger.isTrigger = true;
            trigger.enabled = true;
            ShredY = shredY;
            WheelRadius = Mathf.Max(0.1f, wheelRadius);
            leftX = position.x - size.x * .5f;
            rightX = position.x + size.x * .5f;
            captureTopY = position.y + size.y * .5f;
            captureApproachDistance = Mathf.Max(0f, approachDistance);
        }

        /// <summary>
        /// Validates a shredder handoff against both the displayed footprint and
        /// the authoritative grid. A piece must have an exposed cell on the
        /// final board row in this shredder lane; an obstacle-supported branch
        /// cannot bypass that obstacle by entering the visual feed.
        /// </summary>
        public bool ContainsCaptureFootprint(PuzzlePiece piece)
        {
            if (piece == null || piece.IsShredderInteractionLocked || piece.IsFrozen)
                return false;

            if (piece.GridFallView != null && piece.GridFallView.IsAnimating)
                return false;

            if (!TryGetCellExtents(piece, out float lowestCellY, out _ ) ||
                lowestCellY > captureTopY + captureApproachDistance)
                return false;

            PrototypeBoard board = PrototypeBoard.Active;
            GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
            if (board == null || board.BoardSnapshot == null || level == null ||
                !board.TryGetPieceModel(piece, out PieceModel model))
                return false;

            // A gravity destination is committed before its tween reaches the
            // destination. Capture only a settled board piece (or a piece the
            // player is actively dragging), never that in-flight projection.
            // This prevents the cutter from taking ownership of a body whose
            // visual position and authoritative anchor have not converged.
            if (model.State != PieceState.Placed && model.State != PieceState.Dragging)
                return false;

            bool hasShredderEntryCell = false;
            for (int index = 0; index < model.LocalCells.Count; index++)
            {
                GridCoordinate coordinate = model.GetWorldCell(index);
                GridCoordinate below = coordinate.Offset(Down);
                if (board.BoardSnapshot.Grid.TryGetOccupantId(below, out int occupantId) &&
                    occupantId == model.Id)
                    continue;

                if (coordinate.Y == 0 && IsInsideShredderLane(level, coordinate))
                {
                    hasShredderEntryCell = true;
                    continue;
                }

                // A separate support below a different branch means the piece
                // would have to pass through that blocker during the feed.
                // Keep it under grid gravity until that support is removed.
                GridCellState supportState = board.BoardSnapshot.Grid.GetCellState(below);
                if (supportState == GridCellState.Blocked ||
                    supportState == GridCellState.Occupied ||
                    supportState == GridCellState.Reserved)
                    return false;
            }

            return hasShredderEntryCell;
        }

        /// <summary>Grid-bottom admission for the exit handoff; unlike mouth
        /// capture it deliberately ignores world-space distance to the wheels.</summary>
        public bool ContainsMapExitFootprint(PuzzlePiece piece)
        {
            if (piece == null || piece.IsShredderInteractionLocked || piece.IsFrozen)
                return false;

            PrototypeBoard board = PrototypeBoard.Active;
            GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
            if (board == null || board.BoardSnapshot == null || level == null ||
                !board.TryGetPieceModel(piece, out PieceModel model) ||
                (model.State != PieceState.Placed && model.State != PieceState.Dragging))
                return false;

            for (int index = 0; index < model.LocalCells.Count; index++)
            {
                GridCoordinate cell = model.GetWorldCell(index);
                if (cell.Y == 0 && IsInsideShredderLane(level, cell))
                    return true;
            }

            return false;
        }

#if UNITY_EDITOR
        // Diagnostic only: evaluate the rejected candidate without changing
        // the authoritative capture decision or emitting per-frame logs.
        internal void LogCaptureRejection(PuzzlePiece piece, BlockShredder shredder)
        {
            if (piece == null || shredder == null)
                return;

            piece.GetActiveCellBounds(activeCellBoundsBuffer);
            float lowest = float.PositiveInfinity;
            bool intersectsLane = false;
            bool outsideCorridor = false;
            for (int i = 0; i < activeCellBoundsBuffer.Count; i++)
            {
                Bounds cell = activeCellBoundsBuffer[i];
                lowest = Mathf.Min(lowest, cell.min.y);
                intersectsLane |= cell.max.x > leftX && cell.min.x < rightX;
                outsideCorridor |= cell.min.x < leftX - CorridorEpsilon ||
                                   cell.max.x > rightX + CorridorEpsilon;
            }

            // Limit diagnostics to pieces close enough to be relevant to the mouth.
            if (!intersectsLane || lowest > captureTopY + captureApproachDistance + 1f)
                return;

            string reason;
            PrototypeBoard board = PrototypeBoard.Active;
            GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
            if (piece.IsShredderInteractionLocked)
                reason = "interaction_locked";
            else if (piece.IsFrozen)
                reason = "frozen";
            else if (piece.GridFallView != null && piece.GridFallView.IsAnimating)
                reason = "grid_fall_animating";
            else if (outsideCorridor)
                reason = "cell_outside_corridor";
            else if (lowest > captureTopY + captureApproachDistance)
                reason = "above_capture_band";
            else if (board == null || board.BoardSnapshot == null || level == null ||
                     !board.TryGetPieceModel(piece, out PieceModel model))
                reason = "missing_board_or_model";
            else if (model.State != PieceState.Placed && model.State != PieceState.Dragging)
                reason = "piece_state_not_ready";
            else
            {
                reason = "no_entry_cell";
                bool hasEntry = false;
                for (int i = 0; i < model.LocalCells.Count; i++)
                {
                    GridCoordinate coordinate = model.GetWorldCell(i);
                    GridCoordinate below = coordinate.Offset(Down);
                    if (board.BoardSnapshot.Grid.TryGetOccupantId(below, out int occupantId) &&
                        occupantId == model.Id)
                        continue;
                    if (coordinate.Y == 0 && IsInsideShredderLane(level, coordinate))
                    {
                        hasEntry = true;
                        continue;
                    }
                    GridCellState support = board.BoardSnapshot.Grid.GetCellState(below);
                    if (support == GridCellState.Blocked || support == GridCellState.Occupied ||
                        support == GridCellState.Reserved)
                    {
                        reason = "blocked_or_reserved_support";
                        break;
                    }
                }
                if (hasEntry && reason == "no_entry_cell")
                    reason = "capture_recheck_mismatch";
            }

            shredder.DebugLogAdmission(piece, this, "capture_rejected", reason, null);
        }
#endif

        /// <summary>
        /// Preflights a grid-owned support-chain member before it is handed to
        /// Unity physics. The full authored footprint must fit the real mouth
        /// corridor; the visual body's transform is not used as rule authority.
        /// </summary>
        public bool ContainsPhysicsIslandFootprint(
            PrototypeBoard board,
            PieceModel model)
        {
            GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
            if (board == null || board.BoardSnapshot == null || model == null || level == null)
                return false;

            float halfFineCell = .5f / Mathf.Max(1, level.subdivisions);
            for (int index = 0; index < model.LocalCells.Count; index++)
            {
                Vector2 center = GravityLevelGridCoordinates.FineCellToWorld(
                    level,
                    model.GetWorldCell(index));
                if (center.x - halfFineCell < leftX - CorridorEpsilon ||
                    center.x + halfFineCell > rightX + CorridorEpsilon)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// A dynamic island member enters the feed once its lowest real cell
        /// crosses the authored capture half-space. This is a coordinate gate,
        /// not a trigger callback; a fast body that crosses below the former
        /// finite band must not remain in an unrecoverable handoff state.
        /// </summary>
        public bool ContainsPhysicsIslandCaptureFootprint(
            PuzzlePiece piece,
            float captureDistance)
        {
            if (piece == null || !piece.IsInShredderPhysicsHandoff)
                return false;

            float captureTop = ShredY + Mathf.Max(0f, captureDistance);
            // The approach corridor was validated from authoritative cells
            // before the island was reserved, and its body is now kinematic
            // with X frozen. Rechecking renderer/collider X extents here is
            // both redundant and unsafe: tiny artwork bounds may otherwise
            // leave a valid reserved member permanently locked above the cut.
            return TryGetCellVerticalExtents(piece, out float lowestCellY, out _) &&
                   lowestCellY <= captureTop;
        }

        /// <summary>
        /// Returns the lowest real occupied cell for stable island capture
        /// ordering. The enclosing collision bounds are deliberately avoided:
        /// their empty notches are not physical material.
        /// </summary>
        public bool TryGetPhysicsIslandCaptureFloorY(
            PuzzlePiece piece,
            out float lowestCellY)
        {
            return TryGetCellVerticalExtents(piece, out lowestCellY, out _);
        }

        private bool TryGetCellVerticalExtents(
            PuzzlePiece piece,
            out float lowestCellY,
            out float highestCellY)
        {
            lowestCellY = float.PositiveInfinity;
            highestCellY = float.NegativeInfinity;
            if (piece == null)
                return false;

            piece.GetActiveCellBounds(activeCellBoundsBuffer);
            for (int index = 0; index < activeCellBoundsBuffer.Count; index++)
            {
                Bounds bounds = activeCellBoundsBuffer[index];
                lowestCellY = Mathf.Min(lowestCellY, bounds.min.y);
                highestCellY = Mathf.Max(highestCellY, bounds.max.y);
            }

            return lowestCellY != float.PositiveInfinity;
        }

        private bool TryGetCellExtents(
            PuzzlePiece piece,
            out float lowestCellY,
            out float highestCellY)
        {
            lowestCellY = float.PositiveInfinity;
            highestCellY = float.NegativeInfinity;
            if (piece == null)
                return false;

            piece.GetActiveCellBounds(activeCellBoundsBuffer);
            if (activeCellBoundsBuffer.Count == 0)
                return false;

            for (int index = 0; index < activeCellBoundsBuffer.Count; index++)
            {
                Bounds bounds = activeCellBoundsBuffer[index];
                if (bounds.min.x < leftX - CorridorEpsilon ||
                    bounds.max.x > rightX + CorridorEpsilon)
                {
                    return false;
                }

                lowestCellY = Mathf.Min(lowestCellY, bounds.min.y);
                highestCellY = Mathf.Max(highestCellY, bounds.max.y);
            }

            return lowestCellY != float.PositiveInfinity;
        }

        private bool IsInsideShredderLane(GravityLevelDefinition level, GridCoordinate coordinate)
        {
            Vector2 worldCentre = GravityLevelGridCoordinates.FineCellToWorld(level, coordinate);
            return worldCentre.x >= leftX && worldCentre.x <= rightX;
        }

        public void OnSpawn()
        {
            ShredY = 0f;
            WheelRadius = 0f;
            leftX = 0f;
            rightX = 0f;
            captureTopY = 0f;
            captureApproachDistance = 0f;
        }

        public void OnDespawn()
        {
            trigger.enabled = false;
            ShredY = 0f;
            WheelRadius = 0f;
            leftX = 0f;
            rightX = 0f;
            captureTopY = 0f;
            captureApproachDistance = 0f;
        }

        private void OnEnable()
        {
            if (!activeZones.Contains(this))
                activeZones.Add(this);
        }

        private void OnDisable()
        {
            activeZones.Remove(this);
        }
    }
}
