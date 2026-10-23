using DG.Tweening;
using GravityPuzzle.Presentation.Views;
using TMPro;
using UnityEngine;

namespace GravityPuzzle.Config
{
    [CreateAssetMenu(fileName = "RevealPresentationConfig", menuName = "Gravity Puzzle/Config/Reveal Presentation")]
    public sealed class RevealPresentationConfig : ScriptableObject
    {
        public const int DefaultBoxPreviewSlotCapacity = 32;

        [Header("Prefabs & Pooling")]
        [Tooltip("Authored prefab for Box reveal presentation.")]
        public BoxRevealAreaView boxPrefab;
        [Min(1)] public int boxPoolCapacity = 8;
        [Tooltip("Authored prefab for Elevator reveal presentation.")]
        public ElevatorRevealAreaView elevatorPrefab;
        [Min(1)] public int elevatorPoolCapacity = 4;

        [Header("Box Counter Presentation")]
        public TMP_FontAsset boxCounterFont;
        public float boxCounterFontSize = 1.2f;
        public Color boxCounterTextColor = Color.white;
        public Color boxCounterOutlineColor = new Color(.055f, .08f, .28f, 1f);
        [Range(0f, 1f)] public float boxCounterOutlineWidth = .22f;
        public Vector2 boxCounterOffset = Vector2.zero;
        public int boxCounterSortingOrder = 201;

        [Header("Art")]
        [Tooltip("Sprite shown on generated Box reveal covers.")]
        public Sprite boxCoverSprite;
        [Tooltip("Scene Tuna Elevator sprite fitted to the authored Elevator area and kept static while its doors open.")]
        public Sprite elevatorFrameSprite;
        [Tooltip("Scene Tuna Elevator door panel used by the opening animation.")]
        public Sprite elevatorLeftDoorSprite;
        [Tooltip("Cropped right Elevator door panel used by the opening animation.")]
        public Sprite elevatorRightDoorSprite;
        [Tooltip("Legacy full Elevator sprite. Used only when split door art is unavailable.")]
        public Sprite elevatorDoorSprite;

        [Header("Box")]
        [Tooltip("Frame border thickness relative to the authored sprite. Lower values make the border thinner. Reload the level to apply changes.")]
        [Range(.25f, 2f)] public float boxFrameThickness = 1f;
        [Tooltip("Independent size multiplier for the rectangular shutter interior. This is not derived from the Tunnel frame's 9-slice border.")]
        public Vector2 boxPanelSizeMultiplier = Vector2.one;
        [Tooltip("Independent local alignment of the shutter interior behind the static Tunnel frame.")]
        public Vector2 boxPanelOffset = Vector2.zero;
        [Tooltip("Normalized x, y, width, height source rectangle sampled from TunnelShutter. It excludes that artwork's rounded perimeter so the outer Tunnel remains the only visible frame.")]
        public Vector4 boxPanelSourceUvRect = new Vector4(.02f, .02f, .96f, .96f);
        [Tooltip("Translucent tint of the cavity behind an opened shutter. Keep alpha low so the board grid stays visible.")]
        public Color boxBackingColor = new Color(.035f, .025f, .16f, .18f);
        [Header("Box Hidden Content Preview")]
        [Tooltip("Pre-authored renderer slots on each Box view. Increase this through the Box preview authoring command if a Box needs more visual parts.")]
        [Min(1)] public int boxPreviewSlotCapacity = DefaultBoxPreviewSlotCapacity;
        [Tooltip("Scale used by preview blocks while they are recessed inside a closed Box.")]
        [Range(.85f, 1f)] public float boxPreviewRecessedScale = .85f;
        [Tooltip("Downward visual offset applied to preview blocks before the shutter fully opens.")]
        [Min(0f)] public float boxPreviewRecessDistance = .1f;
        [Tooltip("Brightness multiplier applied while preview blocks are recessed inside the Box.")]
        [Range(.4f, 1f)] public float boxPreviewRecessedBrightness = .45f;
        [Tooltip("Shutter-opening progress at which the cavity backing fades out before the Box frame ruptures.")]
        [Range(.5f, 1f)] public float boxBackingFadeStart = .88f;
        [Tooltip("Tint and maximum opacity of the block-shaped shadow retained behind previews during the frame burst.")]
        public Color boxPreviewShadowColor = new Color(.045f, .035f, .12f, .26f);
        [Tooltip("Local offset applied to the block-shaped preview shadow.")]
        public Vector2 boxPreviewShadowOffset = new Vector2(.018f, -.026f);
        [Tooltip("Small scale expansion that keeps the preview shadow visible around a block silhouette.")]
        [Range(1f, 1.1f)] public float boxPreviewShadowScale = 1.025f;
        [Tooltip("Fraction of the frame-burst duration used to fade the block-shaped preview shadows.")]
        [Range(.1f, 1f)] public float boxPreviewShadowBurstFadeFraction = .55f;
        [Min(.01f)] public float boxOpenDuration = .45f;
        [Tooltip("Additional clearance above the fixed shutter top before the final burst.")]
        [Min(0f)] public float boxOpenDistance = .08f;
        public Ease boxOpenEase = Ease.OutCubic;
        [Tooltip("Remaining shutter height before the top-edge burst begins.")]
        [Min(.01f)] public float boxBurstResidualHeight = .16f;
        [Tooltip("Brief frame recoil after the shutter reaches the top.")]
        [Min(0f)] public float boxBreakAnticipationDuration = .06f;
        [Tooltip("Frame recoil as a fraction of its current scale.")]
        [Range(0f, .2f)] public float boxBreakAnticipationScale = .04f;
        [Min(.01f)] public float boxBurstDuration = .38f;
        [Tooltip("Scale multiplier applied to the top and side frame fragments retained around the opening.")]
        [Range(1f, 1.5f)] public float boxBurstEdgeFragmentScale = 1.12f;
        [Tooltip("Fraction of each edge fragment's lifetime spent holding at the opening before it scatters.")]
        [Range(0f, .5f)] public float boxBurstEdgeHoldFraction = .18f;
        [Tooltip("Normalized source-sprite rectangles (x, y, width, height) used for the blue frame fragments.")]
        public Vector4 boxFragmentTopRegion = new Vector4(.15f, .93f, .7f, .055f);
        public Vector4 boxFragmentBottomRegion = new Vector4(.15f, .015f, .7f, .055f);
        public Vector4 boxFragmentLeftRegion = new Vector4(.015f, .15f, .055f, .7f);
        public Vector4 boxFragmentRightRegion = new Vector4(.93f, .15f, .055f, .7f);
        [Tooltip("Initial upward movement of fragments during the burst.")]
        [Min(0f)] public float boxBreakLiftDistance = .22f;
        [Tooltip("How far the broken shutter pieces separate sideways.")]
        [Min(0f)] public float boxBreakSeparation = .12f;
        [Tooltip("Maximum extra sideways scatter applied independently to each broken shutter piece.")]
        [Min(0f)] public float boxBreakScatterDistance = .48f;
        [Tooltip("How far the broken shutter pieces sag downward before fading.")]
        [Min(0f)] public float boxBreakDropDistance = .42f;
        [Min(0f)] public float boxBreakRotation = 70f;
        [Header("Elevator")]
        [Min(.01f)] public float elevatorOpenDuration = .4f;
        [Min(0f)] public float elevatorDoorDistance = 1.2f;
        public Ease elevatorOpenEase = Ease.OutQuad;
        [Min(.01f)] public float elevatorFadeDuration = .2f;
        [Tooltip("Door aperture width as a fraction of the authored Elevator area.")]
        [Range(.1f, .9f)] public float elevatorDoorWidthRatio = .44f;
        [Tooltip("Door aperture height as a fraction of the authored Elevator area.")]
        [Range(.1f, .9f)] public float elevatorDoorHeightRatio = .62f;
        [Tooltip("Vertical position of the two door panels within the authored Elevator area.")]
        [Range(-.4f, .4f)] public float elevatorDoorCenterYOffset = -.04f;
        [Tooltip("Small horizontal correction for the tall source artwork when it is shown inside a wide authored Elevator area.")]
        [Range(.8f, 1.4f)] public float elevatorVisualWidthScale = 1.15f;
        [Tooltip("Colour shown in the doorway while the fixed interior content is being activated.")]
        public Color elevatorOpeningColor = new Color(.04f, .08f, .14f, 1f);
        [Tooltip("The Elevator is board decoration: frame, opening and both doors render behind normal piece visuals (which render at order 5).")]
        public int elevatorSortingOrder = 1;
        [Tooltip("Box cover layer. Keep this higher than Elevator so overlapping Boxes remain in front.")]
        public int boxSortingOrder = 200;
    }
}
