using UnityEngine;

namespace GravityPuzzle.Config
{
    /// <summary>
    /// Tuning for the presentation-only motion streak drawn behind falling and
    /// dragged pieces. Gameplay never reads these values.
    /// </summary>
    [CreateAssetMenu(fileName = "FallTrailConfig", menuName = "Gravity Puzzle/Config/Fall Trail")]
    public sealed class FallTrailConfig : ScriptableObject
    {
        [Header("Visibility")]
        [SerializeField] private bool trailEnabled = true;
        [Tooltip("Show the streak while the player drags a piece.")]
        [SerializeField] private bool showWhileDragging;
        [Tooltip("Streaks shorter than this (world units) are hidden, so tiny grid slides do not flicker.")]
        [Min(0f)] [SerializeField] private float minimumVisibleLength = .45f;
        [Tooltip("Board falls shorter than this distance (world units; 1 = one board block) never show a streak. Decided when the fall starts.")]
        [Min(0f)] [SerializeField] private float minimumGridFallDistance = 1.5f;

        [Header("Shape")]
        [Tooltip("How long (seconds) of recent motion the streak covers. Longer = longer streak at the same speed.")]
        [Min(.01f)] [SerializeField] private float trailDuration = .1f;
        [Tooltip("Hard cap on the streak length in world units.")]
        [Min(.01f)] [SerializeField] private float maximumLength = 1.125f;
        [Tooltip("Pulls each side of the streak inward (world units) so it lines up with the visible block art instead of the grid-cell border.")]
        [Min(0f)] [SerializeField] private float sideInset = .075f;
        [Tooltip("How far (world units) the streak starts inside the piece edge. It renders behind the piece, so this hides the seam at the art's transparent border.")]
        [Min(0f)] [SerializeField] private float headOverlap = .1f;

        [Header("Colour")]
        [Tooltip("How far the piece colour is blended toward white (0 = piece colour, 1 = pure white).")]
        [Range(0f, 1f)] [SerializeField] private float whiteBlend = .55f;
        [Tooltip("Opacity right behind the piece.")]
        [Range(0f, 1f)] [SerializeField] private float headAlpha = .28f;
        [Tooltip("Opacity along the whole streak: 0 = at the piece, 1 = at the far end. The default drops fastest near the piece so the trace dissolves gradually.")]
        [SerializeField] private AnimationCurve alphaOverLength = new AnimationCurve(
            new Keyframe(0f, 1f, -3f, -3f),
            new Keyframe(1f, 0f, 0f, 0f));

        [Header("Rendering")]
        [Tooltip("Sorting order of the streak mesh. Keep it below piece art (5) and above the board background.")]
        [SerializeField] private int sortingOrder = -1;
        [Tooltip("A per-frame jump larger than this (world units) is treated as a teleport and resets the streak.")]
        [Min(.01f)] [SerializeField] private float teleportDistance = 3f;
        [Tooltip("Maximum motion samples kept per piece.")]
        [Range(4, 64)] [SerializeField] private int maximumSamples = 24;

        public bool Enabled => trailEnabled;
        public bool ShowWhileDragging => showWhileDragging;
        public float MinimumVisibleLength => minimumVisibleLength;
        public float MinimumGridFallDistance => minimumGridFallDistance;
        public float TrailDuration => trailDuration;
        public float MaximumLength => maximumLength;
        public float SideInset => sideInset;
        public float HeadOverlap => headOverlap;
        public float WhiteBlend => whiteBlend;
        public float HeadAlpha => headAlpha;
        public int SortingOrder => sortingOrder;
        public float TeleportDistance => teleportDistance;
        public int MaximumSamples => maximumSamples;

        public float EvaluateAlpha(float normalizedLength) =>
            alphaOverLength != null && alphaOverLength.length > 0
                ? Mathf.Clamp01(alphaOverLength.Evaluate(normalizedLength))
                : 1f - normalizedLength;
    }
}
