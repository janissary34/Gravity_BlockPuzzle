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
        private float leftX;
        private float rightX;
        private float captureTopY;
        private float captureApproachDistance;
        private static readonly GridCoordinate Down = new GridCoordinate(0, -1);

        private void Awake()
        {
            trigger = GetComponent<BoxCollider2D>();
        }

        public void Configure(Vector2 position, Vector2 size, float shredY, float approachDistance)
        {
            transform.position = position;
            trigger.size = size;
            trigger.isTrigger = true;
            trigger.enabled = true;
            ShredY = shredY;
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
            if (piece == null || piece.IsBeingShredded)
                return false;

            if (piece.GridFallView != null && piece.GridFallView.IsAnimating)
                return false;

            Bounds bounds = piece.CollisionBounds;
            if (bounds.max.x < leftX || bounds.min.x > rightX ||
                bounds.min.y > captureTopY + captureApproachDistance)
                return false;

            PrototypeBoard board = PrototypeBoard.Active;
            GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
            if (board == null || board.BoardSnapshot == null || level == null ||
                !board.TryGetPieceModel(piece, out PieceModel model))
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

        private bool IsInsideShredderLane(GravityLevelDefinition level, GridCoordinate coordinate)
        {
            Vector2 worldCentre = GravityLevelGridCoordinates.FineCellToWorld(level, coordinate);
            return worldCentre.x >= leftX && worldCentre.x <= rightX;
        }

        public void OnSpawn()
        {
            ShredY = 0f;
            leftX = 0f;
            rightX = 0f;
            captureTopY = 0f;
            captureApproachDistance = 0f;
        }

        public void OnDespawn()
        {
            trigger.enabled = false;
            ShredY = 0f;
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
