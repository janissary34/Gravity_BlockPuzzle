using System.Collections.Generic;
using TMPro;
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
            Sprite iceOverlaySprite,
            Color iceOverlayTint,
            Color iceFrostTint,
            Sprite bombOverlaySprite,
            float bombOverlayFill,
            Material bombDefuseSplitMaterial,
            int frozenMoveCount,
            TMP_FontAsset iceCounterFont,
            float iceCounterFontSize,
            Color iceCounterTextColor,
            Color iceCounterOutlineColor,
            float iceCounterOutlineWidth,
            Vector2 iceCounterOffset,
            int iceCounterSortingOrder,
            PieceSpecialBlockType specialBlockType,
            float bombTimerSeconds,
            TMP_FontAsset bombCounterFont,
            float bombCounterFontSize,
            Color bombCounterTextColor,
            Color bombCounterOutlineColor,
            float bombCounterOutlineWidth,
            Vector2 bombCounterOffset,
            int bombCounterSortingOrder)
        {
            SourcePieceId = sourcePieceId;
            ProgressUnits = progressUnits;
            VisualColor = visualColor;
            CompositeCollider = compositeCollider;
            CollisionCells = collisionCells;
            CollisionCellVisuals = collisionCellVisuals;
            IceOverlaySprite = iceOverlaySprite;
            IceOverlayTint = iceOverlayTint;
            IceFrostTint = iceFrostTint;
            BombOverlaySprite = bombOverlaySprite;
            BombOverlayFill = bombOverlayFill;
            BombDefuseSplitMaterial = bombDefuseSplitMaterial;
            FrozenMoveCount = frozenMoveCount;
            IceCounterFont = iceCounterFont;
            IceCounterFontSize = iceCounterFontSize;
            IceCounterTextColor = iceCounterTextColor;
            IceCounterOutlineColor = iceCounterOutlineColor;
            IceCounterOutlineWidth = iceCounterOutlineWidth;
            IceCounterOffset = iceCounterOffset;
            IceCounterSortingOrder = iceCounterSortingOrder;
            SpecialBlockType = specialBlockType;
            BombTimerSeconds = bombTimerSeconds;
            BombCounterFont = bombCounterFont;
            BombCounterFontSize = bombCounterFontSize;
            BombCounterTextColor = bombCounterTextColor;
            BombCounterOutlineColor = bombCounterOutlineColor;
            BombCounterOutlineWidth = bombCounterOutlineWidth;
            BombCounterOffset = bombCounterOffset;
            BombCounterSortingOrder = bombCounterSortingOrder;
        }

        public int SourcePieceId { get; }
        public int ProgressUnits { get; }
        public Color VisualColor { get; }
        public CompositeCollider2D CompositeCollider { get; }
        public List<BoxCollider2D> CollisionCells { get; }
        public List<SpriteRenderer> CollisionCellVisuals { get; }
        public Sprite IceOverlaySprite { get; }
        public Color IceOverlayTint { get; }
        public Color IceFrostTint { get; }
        public Sprite BombOverlaySprite { get; }
        public float BombOverlayFill { get; }
        public Material BombDefuseSplitMaterial { get; }
        public int FrozenMoveCount { get; }
        public TMP_FontAsset IceCounterFont { get; }
        public float IceCounterFontSize { get; }
        public Color IceCounterTextColor { get; }
        public Color IceCounterOutlineColor { get; }
        public float IceCounterOutlineWidth { get; }
        public Vector2 IceCounterOffset { get; }
        public int IceCounterSortingOrder { get; }
        public PieceSpecialBlockType SpecialBlockType { get; }
        public float BombTimerSeconds { get; }
        public TMP_FontAsset BombCounterFont { get; }
        public float BombCounterFontSize { get; }
        public Color BombCounterTextColor { get; }
        public Color BombCounterOutlineColor { get; }
        public float BombCounterOutlineWidth { get; }
        public Vector2 BombCounterOffset { get; }
        public int BombCounterSortingOrder { get; }
    }
}
