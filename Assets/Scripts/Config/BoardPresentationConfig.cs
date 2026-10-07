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
        [Tooltip("Material used behind the checker grid to give every generated board the authored environment base.")]
        [SerializeField] private Material environmentBackdropMaterial;
        [Tooltip("Material used by static board modules such as authored obstacles.")]
        [SerializeField] private Material boardBlockMaterial;
        [Tooltip("Base map-atlas module for static gameplay blocks. It is scaled to the authored grid footprint until a dedicated obstacle silhouette is supplied.")]
        [SerializeField] private Sprite staticBlockSprite;
        [Tooltip("Material used by the darker checker-grid cells.")]
        [SerializeField] private Material gridDarkMaterial;
        [Tooltip("Material used by the lighter checker-grid cells.")]
        [SerializeField] private Material gridLightMaterial;
        [Tooltip("Artist-authored grid tile from the map atlas. When empty, the legacy generated square is used.")]
        [SerializeField] private Sprite gridSprite;

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
        public float CameraSizeMultiplier => Mathf.Max(1f, cameraSizeMultiplier);
        public Material EnvironmentBackdropMaterial => environmentBackdropMaterial;
        public Material BoardBlockMaterial => boardBlockMaterial;
        public Sprite StaticBlockSprite => staticBlockSprite;
        public Sprite GridSprite => gridSprite;
        public bool RenderBottomEdgeSegments => renderBottomEdgeSegments;
        public Material FrameMaterial => frameMaterial;
        public Color FrameTint => frameTint;
        public Vector2 CornerOutset => Vector2.Max(Vector2.zero, cornerOutset);
        public float TopCornerHorizontalInset => Mathf.Max(0f, topCornerHorizontalInset);
        public float TopCornerVerticalInset => Mathf.Max(0f, topCornerVerticalInset);

        public Material GetGridMaterial(bool alternateCell)
        {
            return alternateCell ? gridLightMaterial : gridDarkMaterial;
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
