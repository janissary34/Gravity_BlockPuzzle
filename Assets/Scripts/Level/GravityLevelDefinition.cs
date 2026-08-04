using System;
using System.Collections.Generic;
using UnityEngine;

namespace GravityPuzzle
{
    public static class GravityGridMetrics
    {
        public const int FineCellsPerGridCell = 4;
        public const float BoardViewportWidth = .88f;
        public const float BoardViewportHeight = .70f;
        public const float ReferencePortraitWidth = 375f;
        public const float ReferencePortraitHeight = 812f;
        public const float MinimumTouchTargetPoints = 44f;
        public const float FrameThicknessInCells = .12f;
        // Collision geometry uses a tiny inset at rest and a larger one while
        // dragging so pieces can pass through exact-size grid openings. The
        // matching visual scale is applied by PuzzlePiece.
        public const float RestingPieceCollisionSkinInCells = .005f;
        public const float DraggingPieceCollisionSkinInCells = .055f;

        public static float EstimatedCellSizeInPoints(int columns, int rows)
        {
            float widthLimited = ReferencePortraitWidth * BoardViewportWidth / Mathf.Max(1, columns);
            float heightLimited = ReferencePortraitHeight * BoardViewportHeight / Mathf.Max(1, rows);
            return Mathf.Min(widthLimited, heightLimited);
        }

        public static float CameraSize(
            int columns,
            int rows,
            float aspect,
            float safeWidthFraction = 1f,
            float safeHeightFraction = 1f)
        {
            float widthLimited = columns * .5f /
                                 (Mathf.Max(.25f, aspect) * BoardViewportWidth *
                                  Mathf.Clamp(safeWidthFraction, .5f, 1f));
            float heightLimited = rows * .5f /
                                  (BoardViewportHeight * Mathf.Clamp(safeHeightFraction, .5f, 1f));
            return Mathf.Max(widthLimited, heightLimited);
        }
    }

    [CreateAssetMenu(fileName = "GravityLevel", menuName = "Gravity Puzzle/Level")]
    public sealed class GravityLevelDefinition : ScriptableObject
    {
        public string levelName = "New Level";
        [Tooltip("Time limit in seconds. Set to 0 for unlimited time.")]
        [Min(0f)] public float timeLimit = 60f;
        [Min(3)] public int boardColumns = 5;
        [Min(3)] public int boardRows = 7;
        [Range(2, 8)] public int subdivisions = 4;
        public Color backgroundColor = new Color(.06f, .07f, .14f);
        public Color frameColor = new Color(.16f, .18f, .32f);
        [Min(.1f)] public float gravityScale = 1.5f;

        [Header("Bottom Exit & Shredders")]
        [Min(1f)] public float exitWidth = 3f;
        [Range(1, 3)] public int shredderCount = 2;
        [Range(.2f, .65f)] public float shredderRadius = .42f;
        [Min(0f)] public float shredderRotationSpeed = 220f;

        [Header("Öğütme Ayarları (Tuning)")]
        [Tooltip("Bloğun öğütücüye çekilme ve inme hızı (birim/saniye). Varsayılan: 0.7")]
        public float shredlenmeHizi = 0.7f;

        [Tooltip("Öğütülme esnasındaki mekanik titreme/sarsıntı genliği. Varsayılan: 0.045")]
        public float titremeMiktari = 0.045f;

        public List<PieceDefinition> pieces = new List<PieceDefinition>();
        public List<PinDefinition> pins = new List<PinDefinition>();
        public List<ObstacleDefinition> obstacles = new List<ObstacleDefinition>();
        public List<ShredderDefinition> shredders = new List<ShredderDefinition>();
        [Tooltip("Coarse board cells removed with the Map Shape tool.")]
        public List<Vector2Int> inactiveBoardCells = new List<Vector2Int>();
        [Tooltip("Fine-grid cells removed with the Map Shape tool.")]
        public List<Vector2Int> inactiveFineCells = new List<Vector2Int>();

        public int FineColumns => boardColumns * subdivisions;
        public int FineRows => boardRows * subdivisions;
    }

    public enum PieceCellType
    {
        Block,
        Hook
    }

    [Serializable]
    public sealed class PieceDefinition
    {
        public string name = "Puzzle Piece";
        public Color color = new Color(.2f, .65f, 1f);
        public Vector2Int origin = new Vector2Int(8, 10);
        [Range(0, 3)] public int quarterTurns;
        [Tooltip("Piece stays frozen until this many other pieces have been destroyed. 0 disables ice.")]
        [Min(0)] public int frozenMoveCount;
        [Min(1f)] public float iceCounterFontSize = 36f;
        public Color iceCounterTextColor = Color.black;
        public Color iceCounterOutlineColor = Color.white;
        [Range(0f, 1f)] public float iceCounterOutlineWidth = .18f;
        public Vector2 iceCounterOffset = Vector2.zero;
        public List<PieceCellDefinition> cells = new List<PieceCellDefinition>();
    }

    [Serializable]
    public sealed class PieceCellDefinition
    {
        public Vector2Int localCell;
        public PieceCellType type;

        public PieceCellDefinition(Vector2Int localCell, PieceCellType type)
        {
            this.localCell = localCell;
            this.type = type;
        }
    }

    [Serializable]
    public sealed class PinDefinition
    {
        public string name = "Pin";
        public Vector2Int cell;
        [Min(.1f)] public float radiusInFineCells = .45f;
        public Color color = new Color(1f, .74f, .12f);
    }

    [Serializable]
    public sealed class ObstacleDefinition
    {
        public string name = "Obstacle";
        [Tooltip("New obstacles use whole board cells. Legacy obstacles retain their fine-grid data until converted in the level editor.")]
        public bool usesGridCells;
        public Vector2Int gridCell;
        public Vector2Int sizeInGridCells = Vector2Int.one;

        [Tooltip("Legacy fine-grid centre. Only used when Uses Grid Cells is disabled.")]
        public Vector2Int centreCell;
        [Tooltip("Legacy fine-grid size. Only used when Uses Grid Cells is disabled.")]
        public Vector2Int sizeInFineCells = Vector2Int.one;
        [Range(0, 3)] public int quarterTurns;
        public Color color = new Color(.38f, .42f, .55f);
    }

    [Serializable]
    public sealed class ShredderDefinition
    {
        public string name = "Shredder";
        public Vector2Int cell;
        [Min(.5f)] public float radiusInFineCells = 1.7f;
        [Min(0f)] public float rotationSpeed = 220f;
        public bool clockwise;
    }

    public static class QuarterTurnUtility
    {
        public static Vector2Int Rotate(Vector2Int point, int quarterTurns)
        {
            switch ((quarterTurns % 4 + 4) % 4)
            {
                case 1: return new Vector2Int(-point.y, point.x);
                case 2: return new Vector2Int(-point.x, -point.y);
                case 3: return new Vector2Int(point.y, -point.x);
                default: return point;
            }
        }

        public static Vector2Int InverseRotate(Vector2Int point, int quarterTurns)
        {
            return Rotate(point, 4 - quarterTurns);
        }
    }
}
