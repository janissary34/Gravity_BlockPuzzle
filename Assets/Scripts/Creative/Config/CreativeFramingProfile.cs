using UnityEngine;

namespace GravityPuzzle.Creative.Config
{
    /// <summary>
    /// Where the board and progress bar sit on screen during a creative take. Values are fractions
    /// of the screen height, so one profile works for any level. Nothing here changes gameplay:
    /// the view moves, and world positions, input and events do not.
    /// </summary>
    [CreateAssetMenu(menuName = "Gravity Puzzle/Creative/Framing Profile", fileName = "CreativeFramingProfile")]
    public sealed class CreativeFramingProfile : ScriptableObject
    {
        [Tooltip("Distance from the top of the screen to the board top. 0 = the game's framing. " +
                 "Smaller values show more lower grid, which uses more level visual slots; a level near capacity fails to build.")]
        [Range(0f, .4f)] [SerializeField] private float boardTopViewportInset;

        [Tooltip("Gap between the top of the board frame and the bottom of the progress bar, or of the timer when the " +
                 "UI profile shows it. 0 = the bar stays where the game puts it. A take that would overlap the board " +
                 "or leave the safe screen area stops before recording.")]
        [Range(0f, .2f)] [SerializeField] private float progressBarBoardGap;

        [Tooltip("Transparent padding at the top of the board frame art, in world units. The frame sprites are full " +
                 "rectangles, so their top edge is above the visible frame by this much. Measured 0.31-0.37 across " +
                 "levels; 0 is safe but leaves a larger gap.")]
        [Range(0f, .6f)] [SerializeField] private float boardFrameArtPadding;

        public float BoardTopViewportInset => boardTopViewportInset;
        public float ProgressBarBoardGap => progressBarBoardGap;
        public float BoardFrameArtPadding => boardFrameArtPadding;
    }
}
