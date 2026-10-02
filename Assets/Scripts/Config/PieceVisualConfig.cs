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
        [Tooltip("Whole-piece atlas sprites keyed by a normalized silhouette. Runtime matches compatible fine-cell geometry at a uniform scale.")]
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
        [SerializeField] private Sprite iceSprite;

        public string ShapeKey => shapeKey;
        public Sprite NormalSprite => normalSprite;
        public Sprite IceSprite => iceSprite;
    }
}
