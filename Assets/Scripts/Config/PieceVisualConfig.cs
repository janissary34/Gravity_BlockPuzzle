using System;
using System.Collections.Generic;
using UnityEngine;

namespace GravityPuzzle.Config
{
    [CreateAssetMenu(fileName = "PieceVisualConfig", menuName = "Gravity Puzzle/Config/Piece Visuals")]
    public sealed class PieceVisualConfig : ScriptableObject
    {
        [SerializeField] private List<PieceVisualDefinition> definitions = new List<PieceVisualDefinition>();

        [Header("Material Palette")]
        [Tooltip("Shared ice material used while an ice silhouette or frost layer is visible.")]
        [SerializeField] private Material iceMaterial;
        [Tooltip("Named palette entries used by level piece definitions. The material owns the brick colour; level definitions do not need a renderer tint.")]
        [SerializeField] private List<PiecePaletteDefinition> paletteDefinitions = new List<PiecePaletteDefinition>();

        [Header("Modular Block Presentation")]
        [Tooltip("Artist-authored 1x1 brick used when a complete piece has no matching silhouette, including hammer fragments.")]
        [SerializeField] private Sprite normalFallbackSprite;
        [Tooltip("Artist-authored 1x1 ice block used when a frozen piece has no matching silhouette.")]
        [SerializeField] private Sprite iceFallbackSprite;
        [Tooltip("Small presentation-only overlap for adjacent modular ice cells. It closes transparent sprite gutters without changing board or collider scale.")]
        [SerializeField, Min(1f)] private float modularIceCellScale = 1.045f;
        [Tooltip("Whole-piece atlas sprites keyed by a normalized silhouette. Frozen shapes without a matching ice sprite use the modular brick-and-frost fallback.")]
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
        public Material IceMaterial => iceMaterial;
        public IReadOnlyList<PiecePaletteDefinition> PaletteDefinitions => paletteDefinitions;
        public Sprite NormalFallbackSprite => normalFallbackSprite;
        public Sprite IceFallbackSprite => iceFallbackSprite;
        public float ModularIceCellScale => Mathf.Max(1f, modularIceCellScale);
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

        public bool TryGetPaletteMaterial(string paletteId, out Material material)
        {
            if (!string.IsNullOrWhiteSpace(paletteId))
            {
                for (int index = 0; index < paletteDefinitions.Count; index++)
                {
                    PiecePaletteDefinition candidate = paletteDefinitions[index];
                    if (candidate.PaletteId == paletteId && candidate.Material != null)
                    {
                        material = candidate.Material;
                        return true;
                    }
                }
            }

            material = null;
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
        /// Finds an artist silhouette for the requested shape without rotating
        /// or mirroring its renderer. Brick lighting is baked into the atlas,
        /// therefore a geometric transform would invert the shadow direction.
        /// Shapes missing an exact authored orientation deliberately use the
        /// modular brick fallback instead.
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

            definition = default;
            transform = PieceShapeVisualTransform.Identity;
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
    /// Data-only bridge between a level's semantic palette key and the
    /// artist-authored shared material. Keeping this lookup in the visual
    /// config avoids duplicated material references across every level asset.
    /// </summary>
    [Serializable]
    public struct PiecePaletteDefinition
    {
        [SerializeField] private string paletteId;
        [SerializeField] private Material material;

        public string PaletteId => paletteId;
        public Material Material => material;
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
        [Tooltip("Optional. When missing, frozen pieces use the modular brick-and-frost fallback rather than this normal silhouette.")]
        [SerializeField] private Sprite iceSprite;
        [Tooltip("Keeps the intact normal piece on its authored 1x1 modules instead of replacing it with one atlas silhouette.")]
        [SerializeField] private bool preferModularNormal;
        [Tooltip("Keeps frozen presentation on full-size 1x1 brick modules instead of covering the shape with an opaque ice silhouette. Use for structures whose per-cell stud detail must remain visible.")]
        [SerializeField] private bool preferModularIce;

        public string ShapeKey => shapeKey;
        public Sprite NormalSprite => normalSprite;
        public Sprite IceSprite => iceSprite;
        public bool PreferModularNormal => preferModularNormal;
        public bool PreferModularIce => preferModularIce;
    }

    /// <summary>
    /// Renderer-only transform retained for serialized compatibility. Atlas
    /// matching now returns identity because baked brick lighting must not be
    /// rotated or mirrored at runtime.
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
