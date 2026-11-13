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
        /// Grid-bottom admission for the exit handoff. The piece needs an
        /// exposed cell on the final row inside this lane, and every other
        /// exposed branch must be unsupported, since freefall cannot collide
        /// with obstacles or the map floor.
        /// </summary>
        public bool ContainsMapExitFootprint(PuzzlePiece piece)
        {
            if (piece == null || piece.IsShredderInteractionLocked || piece.IsFrozen)
                return false;

            PrototypeBoard board = PrototypeBoard.Active;
            GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
            if (board == null || board.BoardSnapshot == null || level == null ||
                !board.TryGetPieceModel(piece, out PieceModel model) ||
                (model.State != PieceState.Placed && model.State != PieceState.Dragging && model.State != PieceState.Falling))
                return false;

            bool hasShredderEntryCell = false;
            GravityBoardGrid grid = board.BoardSnapshot.Grid;
            for (int index = 0; index < model.LocalCells.Count; index++)
            {
                GridCoordinate cell = model.GetWorldCell(index);
                GridCoordinate below = cell.Offset(Down);
                if (grid.TryGetOccupantId(below, out int occupantId) && occupantId == model.Id)
                    continue;

                if (cell.Y == 0 && IsInsideShredderLane(level, cell))
                {
                    hasShredderEntryCell = true;
                    continue;
                }

                // Open freefall ignores obstacles and the map floor (ADR-005).
                // A branch resting on anything other than the exit would pass
                // through it, so keep the piece under grid gravity until that
                // support is gone. Outside the grid reads as Blocked.
                if (grid.GetCellState(below) != GridCellState.Empty)
                    return false;
            }

            if (!hasShredderEntryCell)
                return false;

            // A falling piece is admitted once its visual cells arrive at or near
            // the mouth, preventing an unnatural freeze/snap at the boundary.
            if (model.State == PieceState.Falling &&
                (!TryGetCellVerticalExtents(piece, out float lowestCellY, out _) ||
                 lowestCellY > captureTopY + captureApproachDistance + 0.5f))
                return false;

            return true;
        }


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
        /// Freefall remains fully dynamic until real material reaches the
        /// authored cutter seam.  Unlike the island gate, this has no early
        /// approach distance because there is no motorized queue to enter.
        /// </summary>
        public bool ContainsFreefallCutterFootprint(
            PuzzlePiece piece,
            float gearClippingRatio)
        {
            if (piece == null || !piece.IsInOpenFreefall)
                return false;

            float cutterSeamY = ShredY - WheelRadius * Mathf.Clamp(gearClippingRatio, 0.1f, 0.9f);
            return TryGetCellVerticalExtents(piece, out float lowestCellY, out _) &&
                   lowestCellY <= cutterSeamY;
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
