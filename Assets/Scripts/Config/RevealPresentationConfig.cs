using DG.Tweening;
using UnityEngine;

namespace GravityPuzzle.Config
{
    [CreateAssetMenu(fileName = "RevealPresentationConfig", menuName = "Gravity Puzzle/Config/Reveal Presentation")]
    public sealed class RevealPresentationConfig : ScriptableObject
    {
        [Header("Art")]
        [Tooltip("Sprite shown on generated Box reveal covers.")]
        public Sprite boxCoverSprite;
        [Tooltip("Nine-sliced full Elevator frame, kept static while its doors open.")]
        public Sprite elevatorFrameSprite;
        [Tooltip("Cropped left Elevator door panel used by the opening animation.")]
        public Sprite elevatorLeftDoorSprite;
        [Tooltip("Cropped right Elevator door panel used by the opening animation.")]
        public Sprite elevatorRightDoorSprite;
        [Tooltip("Legacy full Elevator sprite. Used only when split door art is unavailable.")]
        public Sprite elevatorDoorSprite;

        [Header("Box")]
        [Min(.01f)] public float boxOpenDuration = .45f;
        [Tooltip("Additional clearance above the fixed shutter top before the final burst.")]
        [Min(0f)] public float boxOpenDistance = .08f;
        public Ease boxOpenEase = Ease.OutCubic;
        [Tooltip("Remaining shutter height before the top-edge burst begins.")]
        [Min(.01f)] public float boxBurstResidualHeight = .16f;
        [Min(.01f)] public float boxBurstDuration = .22f;
        [Tooltip("How far the broken shutter pieces separate sideways.")]
        [Min(0f)] public float boxBreakSeparation = .07f;
        [Tooltip("Maximum extra sideways scatter applied independently to each broken shutter piece.")]
        [Min(0f)] public float boxBreakScatterDistance = .18f;
        [Tooltip("How far the broken shutter pieces sag downward before fading.")]
        [Min(0f)] public float boxBreakDropDistance = .16f;
        [Min(0f)] public float boxBreakRotation = 8f;
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
        [Tooltip("Colour shown in the doorway while the fixed interior content is being activated.")]
        public Color elevatorOpeningColor = new Color(.04f, .08f, .14f, 1f);
        [Tooltip("Elevator doors must stay behind normal piece visuals (which render at order 5).")]
        public int elevatorSortingOrder = 2;
        [Tooltip("Box cover layer. Keep this higher than Elevator so overlapping Boxes remain in front.")]
        public int boxSortingOrder = 200;
    }
}
