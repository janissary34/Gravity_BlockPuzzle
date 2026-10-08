using System;
using System.Collections.Generic;
using UnityEngine;

namespace GravityPuzzle.Config
{
    /// <summary>
    /// Shared art direction for every runtime board. Level assets own their
    /// topology and gameplay values; this asset owns the reusable board skin.
    /// </summary>
    [CreateAssetMenu(fileName = "BoardPresentation", menuName = "Gravity Puzzle/Presentation/Board")]
    public sealed class BoardPresentationConfig : ScriptableObject
    {
        [Header("Palette")]
        [Tooltip("When enabled, every level uses this common board palette instead of an authored per-level background colour.")]
        [SerializeField] private bool overrideLevelBackground = true;
        [SerializeField] private Color backgroundColor = new Color(.035f, .055f, .13f, 1f);
        [SerializeField] private Color alternateBackgroundColor = new Color(.085f, .115f, .22f, 1f);
        [Tooltip("Solid colour outside the playable board. This is shared with the frame and obstacle palette for a continuous environment.")]
        [SerializeField] private Color exteriorColor = new Color(.047f, .032f, .321f, 1f);
        [Tooltip("Tint applied to the environment material behind the grid and below the board.")]
        [SerializeField] private Color environmentTint = Color.white;
        [Tooltip("Material used behind the checker grid to give every generated board the authored environment base.")]
        [SerializeField] private Material environmentBackdropMaterial;
        [Tooltip("Material used by static board modules such as authored obstacles.")]
        [SerializeField] private Material boardBlockMaterial;
        [Tooltip("Base map-atlas module for static gameplay blocks. It is scaled to the authored grid footprint until a dedicated obstacle silhouette is supplied.")]
        [SerializeField] private Sprite staticBlockSprite;
        [Tooltip("Shared tint for static gameplay obstacles. This keeps them in the same palette as the frame instead of retaining per-level placeholder colours.")]
        [SerializeField] private Color staticBlockTint = Color.white;
        [Tooltip("Whole map-atlas silhouettes keyed by normalized occupied board cells. Adjacent obstacle cells use these instead of rendering separate 1x1 modules.")]
        [SerializeField] private List<BoardShapeVisualDefinition> staticBlockShapes = new List<BoardShapeVisualDefinition>();
        [Tooltip("Material used by the darker checker-grid cells.")]
        [SerializeField] private Material gridDarkMaterial;
        [Tooltip("Material used by the lighter checker-grid cells.")]
        [SerializeField] private Material gridLightMaterial;
        [Tooltip("Artist-authored grid tile from the map atlas. When empty, the legacy generated square is used.")]
        [SerializeField] private Sprite gridSprite;
        [Tooltip("Designer-adjustable tint multiplied over the dark grid material.")]
        [SerializeField] private Color gridDarkTint = Color.white;
        [Tooltip("Designer-adjustable tint multiplied over the light grid material.")]
        [SerializeField] private Color gridLightTint = Color.white;

        [Header("Lower Screen Continuation")]
        [Tooltip("Extends the solid environment background and side walls from the shredder to the bottom of the gameplay viewport.")]
        [SerializeField] private bool extendPresentationToViewportBottom = true;
        [Tooltip("Extra world-space coverage below the camera edge so aspect-ratio rounding cannot expose a seam.")]
        [SerializeField, Min(0f)] private float viewportBottomPadding = .35f;
        [Tooltip("Darkest colour reached at the viewport bottom. The gradient begins at Exterior Color directly below the shredder.")]
        [SerializeField] private Color lowerGradientTint = new Color(.01f, .015f, .09f, 1f);
        [Tooltip("Number of static bands used to approximate the lower-screen gradient.")]
        [SerializeField, Range(2, 48)] private int lowerGradientBandCount = 16;

        [Header("Camera Framing")]
        [Tooltip("Shared safety margin applied after a level has calculated its camera fit. Keeps the complete board frame visible on every level.")]
        [SerializeField, Min(1f)] private float cameraSizeMultiplier = 1.2f;

        [Header("Scene Tuna Frame Atlas")]
        [SerializeField] private Sprite topEdge;
        [SerializeField] private Sprite bottomEdge;
        [SerializeField] private Sprite leftEdge;
        [SerializeField] private Sprite rightEdge;
        [SerializeField] private Sprite topLeftCorner;
        [SerializeField] private Sprite topRightCorner;
        [SerializeField] private Sprite bottomLeftCorner;
        [SerializeField] private Sprite bottomRightCorner;
        [Tooltip("Draws short bottom-frame segments beside the shredder opening. Disable for the open-bottom Scene Tuna frame.")]
        [SerializeField] private bool renderBottomEdgeSegments;
        [SerializeField] private Material frameMaterial;
        [SerializeField] private Color frameTint = Color.white;
        [Tooltip("Fine correction from the exact intersection of the adjoining straight edges. Zero joins the corner to both edges; positive values move it outward.")]
        [SerializeField] private Vector2 cornerOutset = Vector2.zero;
        [Tooltip("Moves the top-left corner right and the top-right corner left. The top straight edge is shortened by the same amount so the sprites meet without overlap.")]
        [SerializeField, Min(0f)] private float topCornerHorizontalInset = .25f;
        [Tooltip("Moves both top corners downward. The side edges are shortened by the same amount so the sprites meet without overlap.")]
        [SerializeField, Min(0f)] private float topCornerVerticalInset = .25f;

        public bool OverrideLevelBackground => overrideLevelBackground;
        public Color BackgroundColor => backgroundColor;
        public Color AlternateBackgroundColor => alternateBackgroundColor;
        public Color ExteriorColor => exteriorColor;
        public Color EnvironmentTint => environmentTint;
        public float CameraSizeMultiplier => Mathf.Max(1f, cameraSizeMultiplier);
        public Material EnvironmentBackdropMaterial => environmentBackdropMaterial;
        public Material BoardBlockMaterial => boardBlockMaterial;
        public Sprite StaticBlockSprite => staticBlockSprite;
        public Color StaticBlockTint => staticBlockTint;
        public Sprite GridSprite => gridSprite;
        public Color GridDarkTint => gridDarkTint;
        public Color GridLightTint => gridLightTint;
        public bool ExtendPresentationToViewportBottom => extendPresentationToViewportBottom;
        public float ViewportBottomPadding => Mathf.Max(0f, viewportBottomPadding);
        public Color LowerGradientTint => lowerGradientTint;
        public int LowerGradientBandCount => Mathf.Clamp(lowerGradientBandCount, 2, 48);
        public bool RenderBottomEdgeSegments => renderBottomEdgeSegments;
        public Material FrameMaterial => frameMaterial;
        public Color FrameTint => frameTint;
        public Vector2 CornerOutset => Vector2.Max(Vector2.zero, cornerOutset);
        public float TopCornerHorizontalInset => Mathf.Max(0f, topCornerHorizontalInset);
        public float TopCornerVerticalInset => Mathf.Max(0f, topCornerVerticalInset);

        public bool TryGetStaticBlockShape(string shapeKey, out Sprite sprite)
        {
            if (staticBlockShapes == null)
            {
                sprite = null;
                return false;
            }

            for (int index = 0; index < staticBlockShapes.Count; index++)
            {
                BoardShapeVisualDefinition definition = staticBlockShapes[index];
                if (definition.ShapeKey == shapeKey && definition.Sprite != null)
                {
                    sprite = definition.Sprite;
                    return true;
                }
            }

            sprite = null;
            return false;
        }

        public Material GetGridMaterial(bool alternateCell)
        {
            return alternateCell ? gridLightMaterial : gridDarkMaterial;
        }

        public Color GetGridTint(bool alternateCell)
        {
            return alternateCell ? gridLightTint : gridDarkTint;
        }

        public Sprite GetEdgeSprite(BoardFrameEdge edge)
        {
            switch (edge)
            {
                case BoardFrameEdge.Top:
                    return topEdge;
                case BoardFrameEdge.Bottom:
                    return bottomEdge;
                case BoardFrameEdge.Left:
                    return leftEdge;
                case BoardFrameEdge.Right:
                    return rightEdge;
                case BoardFrameEdge.TopLeftCorner:
                    return topLeftCorner;
                case BoardFrameEdge.TopRightCorner:
                    return topRightCorner;
                case BoardFrameEdge.BottomLeftCorner:
                    return bottomLeftCorner;
                case BoardFrameEdge.BottomRightCorner:
                    return bottomRightCorner;
                default:
                    return null;
            }
        }
    }

    [Serializable]
    public struct BoardShapeVisualDefinition
    {
        [SerializeField] private string shapeKey;
        [SerializeField] private Sprite sprite;

        public string ShapeKey => shapeKey;
        public Sprite Sprite => sprite;
    }

    public enum BoardFrameEdge
    {
        Top,
        Bottom,
        Left,
        Right,
        TopLeftCorner,
        TopRightCorner,
        BottomLeftCorner,
        BottomRightCorner
    }
}
