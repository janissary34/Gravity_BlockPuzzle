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

        [Tooltip("Distance from the board top to the progress bar's centre. 0 = the bar stays where the game puts it. " +
                 "The timer hangs below the bar, so a profile that shows the timer needs a larger gap.")]
        [Range(0f, .2f)] [SerializeField] private float progressBarBoardGap;

        public float BoardTopViewportInset => boardTopViewportInset;
        public float ProgressBarBoardGap => progressBarBoardGap;
    }
}
