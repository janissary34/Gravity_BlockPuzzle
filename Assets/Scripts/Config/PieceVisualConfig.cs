using System;
using System.Collections.Generic;
using UnityEngine;

namespace GravityPuzzle.Config
{
    [CreateAssetMenu(fileName = "PieceVisualConfig", menuName = "Gravity Puzzle/Config/Piece Visuals")]
    public sealed class PieceVisualConfig : ScriptableObject
    {
        [SerializeField] private List<PieceVisualDefinition> definitions = new List<PieceVisualDefinition>();

        [Header("Modular Block Presentation")]
        [Tooltip("Artist-authored 1x1 brick used when a complete piece has no matching silhouette, including hammer fragments.")]
        [SerializeField] private Sprite normalFallbackSprite;
        [Tooltip("Artist-authored 1x1 ice block used when a frozen piece has no matching silhouette.")]
        [SerializeField] private Sprite iceFallbackSprite;
        [Tooltip("Whole-piece atlas sprites keyed by a normalized silhouette. Normal art is required; ice art is optional and falls back to the ice overlay.")]
        [SerializeField] private List<PieceShapeVisualDefinition> shapeDefinitions = new List<PieceShapeVisualDefinition>();

        [Header("Ice Presentation")]
        [Tooltip("Sprite rendered only above pieces whose Block Type is Ice. Leave empty to retain the legacy source-sprite overlay.")]
        [SerializeField] private Sprite iceOverlaySprite;
        [SerializeField] private Color iceOverlayTint = new Color(1f, 1f, 1f, .42f);
        [SerializeField] private Color iceFrostTint = new Color(1f, 1f, 1f, .18f);

        [Header("Bomb Presentation")]
        [Tooltip("Sprite rendered above Bomb blocks. Leave empty to keep their normal piece visual.")]
        [SerializeField] private Sprite bombOverlaySprite;
        [SerializeField, Range(.1f, 1f)] private float bombOverlayFill = .82f;

        [Header("Outline Presentation")]
        [SerializeField, Min(0.001f)] private float restingOutlineWidth = 0.05f;
        [SerializeField, Min(0.001f)] private float selectedOutlineWidth = 0.04f;
        [SerializeField] private Color restingOutlineColor = Color.black;
        [SerializeField] private Color selectedOutlineColor = Color.white;
        [SerializeField] private int restingOutlineSortingOrder = 10;
        [SerializeField] private int selectedOutlineSortingOrder = 20;
        [SerializeField, Range(0, 8)] private int outlineCornerVertices = 4;
        [SerializeField, Range(0, 8)] private int outlineCapVertices = 4;

        public IReadOnlyList<PieceVisualDefinition> Definitions => definitions;
        public Sprite NormalFallbackSprite => normalFallbackSprite;
        public Sprite IceFallbackSprite => iceFallbackSprite;
        public Sprite IceOverlaySprite => iceOverlaySprite;
        public Color IceOverlayTint => iceOverlayTint;
        public Color IceFrostTint => iceFrostTint;
        public Sprite BombOverlaySprite => bombOverlaySprite;
        public float BombOverlayFill => bombOverlayFill;
        public float RestingOutlineWidth => restingOutlineWidth;
        public float SelectedOutlineWidth => selectedOutlineWidth;
        public Color RestingOutlineColor => restingOutlineColor;
        public Color SelectedOutlineColor => selectedOutlineColor;
        public int RestingOutlineSortingOrder => restingOutlineSortingOrder;
        public int SelectedOutlineSortingOrder => selectedOutlineSortingOrder;
        public int OutlineCornerVertices => outlineCornerVertices;
        public int OutlineCapVertices => outlineCapVertices;

        public bool TryGet(string visualId, out PieceVisualDefinition definition)
        {
            for (int index = 0; index < definitions.Count; index++)
            {
                PieceVisualDefinition candidate = definitions[index];
                if (candidate.VisualId == visualId)
                {
                    definition = candidate;
                    return true;
                }
            }

            definition = default;
            return false;
        }

        public bool TryGetShape(string shapeKey, out PieceShapeVisualDefinition definition)
        {
            for (int index = 0; index < shapeDefinitions.Count; index++)
            {
                PieceShapeVisualDefinition candidate = shapeDefinitions[index];
                if (candidate.ShapeKey == shapeKey)
                {
                    definition = candidate;
                    return true;
                }
            }

            definition = default;
            return false;
        }

        /// <summary>
        /// Finds an artist silhouette that can represent the requested shape.
        /// The atlas need not duplicate every rotation or mirrored version:
        /// the returned presentation transform is applied only to the renderer.
        /// </summary>
        public bool TryGetShapePresentation(
            string shapeKey,
            out PieceShapeVisualDefinition definition,
            out PieceShapeVisualTransform transform)
        {
            if (TryGetShape(shapeKey, out definition))
            {
                transform = PieceShapeVisualTransform.Identity;
                return true;
            }

            for (int definitionIndex = 0; definitionIndex < shapeDefinitions.Count; definitionIndex++)
            {
                PieceShapeVisualDefinition candidate = shapeDefinitions[definitionIndex];
                // Include zero turns as well: legacy authoring writes cells
                // row-first while runtime normalizes them ordinally, so an
                // identical silhouette can have a differently ordered key.
                for (int quarterTurns = 0; quarterTurns < 4; quarterTurns++)
                {
                    if (ShapeKeyMatches(candidate.ShapeKey, shapeKey, quarterTurns, false))
                    {
                        definition = candidate;
                        transform = new PieceShapeVisualTransform(quarterTurns, false);
                        return true;
                    }
                }

                for (int quarterTurns = 0; quarterTurns < 4; quarterTurns++)
                {
                    if (ShapeKeyMatches(candidate.ShapeKey, shapeKey, quarterTurns, true))
                    {
                        definition = candidate;
                        transform = new PieceShapeVisualTransform(quarterTurns, true);
                        return true;
                    }
                }
            }

            definition = default;
            transform = PieceShapeVisualTransform.Identity;
            return false;
        }

        private static bool ShapeKeyMatches(
            string sourceShapeKey,
            string targetShapeKey,
            int quarterTurns,
            bool flipX)
        {
            if (string.IsNullOrEmpty(sourceShapeKey) || string.IsNullOrEmpty(targetShapeKey))
                return false;

            string[] cells = sourceShapeKey.Split(';');
            List<Vector2Int> transformedCells = new List<Vector2Int>(cells.Length);
            int minimumX = int.MaxValue;
            int minimumY = int.MaxValue;
            for (int index = 0; index < cells.Length; index++)
            {
                string[] coordinates = cells[index].Split(',');
                if (coordinates.Length != 2 ||
                    !int.TryParse(coordinates[0], out int x) ||
                    !int.TryParse(coordinates[1], out int y))
                    return false;

                if (flipX)
                    x = -x;

                Vector2Int transformed = Rotate(new Vector2Int(x, y), quarterTurns);
                transformedCells.Add(transformed);
                minimumX = Mathf.Min(minimumX, transformed.x);
                minimumY = Mathf.Min(minimumY, transformed.y);
            }

            List<string> normalizedCells = new List<string>(transformedCells.Count);
            for (int index = 0; index < transformedCells.Count; index++)
            {
                Vector2Int cell = transformedCells[index];
                normalizedCells.Add((cell.x - minimumX) + "," + (cell.y - minimumY));
            }

            normalizedCells.Sort(StringComparer.Ordinal);
            return string.Join(";", normalizedCells) == targetShapeKey;
        }

        private static Vector2Int Rotate(Vector2Int point, int quarterTurns)
        {
            switch ((quarterTurns % 4 + 4) % 4)
            {
                case 1: return new Vector2Int(-point.y, point.x);
                case 2: return new Vector2Int(-point.x, -point.y);
                case 3: return new Vector2Int(point.y, -point.x);
                default: return point;
            }
        }
    }

    [Serializable]
    public struct PieceVisualDefinition
    {
        [SerializeField] private string visualId;
        [SerializeField] private Sprite sprite;
        [SerializeField] private Color tint;

        public string VisualId => visualId;
        public Sprite Sprite => sprite;
        public Color Tint => tint;
    }

    /// <summary>
    /// Presentation-only mapping for an artist-authored silhouette. Runtime
    /// normalizes compatible fine-cell geometry to this module key; it never
    /// affects board occupancy, collisions, or any gameplay decision.
    /// </summary>
    [Serializable]
    public struct PieceShapeVisualDefinition
    {
        [SerializeField] private string shapeKey;
        [SerializeField] private Sprite normalSprite;
        [Tooltip("Optional. When missing, the normal silhouette remains visible beneath the configured ice overlay.")]
        [SerializeField] private Sprite iceSprite;

        public string ShapeKey => shapeKey;
        public Sprite NormalSprite => normalSprite;
        public Sprite IceSprite => iceSprite;
    }

    /// <summary>
    /// Renderer-only transform used when an atlas contains one orientation of
    /// a silhouette but a level requests a rotated or mirrored counterpart.
    /// </summary>
    public struct PieceShapeVisualTransform
    {
        public static PieceShapeVisualTransform Identity => new PieceShapeVisualTransform(0, false);

        public PieceShapeVisualTransform(int quarterTurns, bool flipX)
        {
            QuarterTurns = (quarterTurns % 4 + 4) % 4;
            FlipX = flipX;
        }

        public int QuarterTurns { get; }
        public bool FlipX { get; }
    }
}
