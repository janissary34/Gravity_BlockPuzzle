using System.Collections.Generic;
using UnityEngine;

namespace GravityPuzzle.Gameplay.Pieces
{
    public readonly struct PieceRuntimeSetup
    {
        public PieceRuntimeSetup(
            int sourcePieceId,
            int progressUnits,
            Color visualColor,
            CompositeCollider2D compositeCollider,
            List<BoxCollider2D> collisionCells,
            List<SpriteRenderer> collisionCellVisuals,
            int frozenMoveCount,
            float iceCounterFontSize,
            Color iceCounterTextColor,
            Color iceCounterOutlineColor,
            float iceCounterOutlineWidth,
            Vector2 iceCounterOffset)
        {
            SourcePieceId = sourcePieceId;
            ProgressUnits = progressUnits;
            VisualColor = visualColor;
            CompositeCollider = compositeCollider;
            CollisionCells = collisionCells;
            CollisionCellVisuals = collisionCellVisuals;
            FrozenMoveCount = frozenMoveCount;
            IceCounterFontSize = iceCounterFontSize;
            IceCounterTextColor = iceCounterTextColor;
            IceCounterOutlineColor = iceCounterOutlineColor;
            IceCounterOutlineWidth = iceCounterOutlineWidth;
            IceCounterOffset = iceCounterOffset;
        }

        public int SourcePieceId { get; }
        public int ProgressUnits { get; }
        public Color VisualColor { get; }
        public CompositeCollider2D CompositeCollider { get; }
        public List<BoxCollider2D> CollisionCells { get; }
        public List<SpriteRenderer> CollisionCellVisuals { get; }
        public int FrozenMoveCount { get; }
        public float IceCounterFontSize { get; }
        public Color IceCounterTextColor { get; }
        public Color IceCounterOutlineColor { get; }
        public float IceCounterOutlineWidth { get; }
        public Vector2 IceCounterOffset { get; }
    }
}
