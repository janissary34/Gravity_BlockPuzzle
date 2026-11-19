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
        [Tooltip("Elevator cabinet sprite from map.png, fitted to the authored Elevator area behind its doors.")]
        public Sprite elevatorFrameSprite;
        [Tooltip("Shared Elevator door panel from map.png; the left renderer is mirrored when both panel references are the same.")]
        public Sprite elevatorLeftDoorSprite;
        [Tooltip("Right Elevator door panel. Assign the same map.png door sprite to use the mirrored shared-panel pair.")]
        public Sprite elevatorRightDoorSprite;
        [Tooltip("Generated Elevator door rim (Elevator_Door with its blue panel cut out). Stays fixed while only the panel collapses.")]
        public Sprite elevatorDoorRimSprite;
        [Tooltip("Opaque sprite tinted with elevatorDoorPanelColor to draw the collapsing door panel inside the rim.")]
        public Sprite elevatorDoorPanelSprite;
        [Tooltip("Legacy Elevator cabinet fallback used when the primary cabinet sprite is unavailable.")]
        public Sprite elevatorDoorSprite;

        [Header("Box")]
        [Tooltip("Frame border thickness relative to the authored sprite. Lower values make the border thinner. Reload the level to apply changes.")]
        [Range(.25f, 2f)] public float boxFrameThickness = 1f;
        [Tooltip("Transparent sprite rows below the Tunnel frame legs, in Tunnel sprite pixels. The frame is extended downward by this amount so its legs end at the reveal-area bottom at every size.")]
        [Min(0f)] public float boxFramePaddingBottomPixels = 46f;
        [Tooltip("How far the Tunnel frame extends past the reveal area's left, right, and top edges, in Tunnel sprite pixels. Covers the square area corners behind the frame's rounded outer corners.")]
        [Min(0f)] public float boxFrameOutsetPixels = 8f;
        [Tooltip("Shutter inset from the frame's outer left, right, top edges and the area bottom, in Tunnel sprite pixels scaled by Box Frame Thickness. Keeps the rectangular shutter edges hidden under the frame tube at every Tunnel size.")]
        public Vector4 boxPanelInsetPixels = new Vector4(13f, 13f, 21f, 0f);
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
        public Ease elevatorOpenEase = Ease.OutQuad;
        [Tooltip("Door aperture width as a fraction of the cabinet artwork. The cabinet keeps its aspect-correct rim; only its middle stretches to fill the area.")]
        [Range(.1f, .9f)] public float elevatorDoorWidthRatio = .86f;
        [Tooltip("Door aperture height as a fraction of the cabinet artwork.")]
        [Range(.1f, .9f)] public float elevatorDoorHeightRatio = .83f;
        [Tooltip("Vertical position of the two door panels within the authored Elevator area.")]
        [Range(-.4f, .4f)] public float elevatorDoorCenterYOffset;
        [Tooltip("Optional horizontal fit correction for the authored Elevator cabinet.")]
        [Range(.8f, 1.4f)] public float elevatorVisualWidthScale = 1f;
        [Tooltip("Flat colour of the collapsing door panels (matches the Elevator_Door blue).")]
        public Color elevatorDoorPanelColor = new Color(.41f, .46f, .95f, 1f);
        [Tooltip("Door panel inset from the rim's outer edge, in door-art pixels; the panel tucks just under the rim's inner bevel.")]
        [Min(0f)] public float elevatorDoorPanelInsetPixels = 24f;
        [Tooltip("Colour shown in the doorway behind the interior pieces while the doors slide open.")]
        public Color elevatorOpeningColor = new Color(.04f, .08f, .14f, 1f);
        [Tooltip("Cabinet frame and doorway render behind normal piece visuals (which render at order 5).")]
        public int elevatorSortingOrder = 1;
        [Tooltip("While opening, the sliding doors render above the interior pieces (and their counters) so the pieces are revealed as the doors slide apart. Closed doors stay behind pieces at elevatorSortingOrder + 2. Keep below Box.")]
        public int elevatorDoorSortingOrder = 60;
        [Tooltip("Box cover layer. Keep this higher than Elevator so overlapping Boxes remain in front.")]
        public int boxSortingOrder = 200;
    }
}
