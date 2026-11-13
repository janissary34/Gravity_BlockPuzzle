using UnityEngine;

namespace GravityPuzzle.Config
{
    /// <summary>Presentation-only one-shot sound cues. Each cue cycles its clips in shuffled order.</summary>
    [CreateAssetMenu(fileName = "SoundFeedbackConfig", menuName = "Gravity Puzzle/Config/Sound Feedback")]
    public sealed class SoundFeedbackConfig : ScriptableObject
    {
        [Header("UI")]
        [Tooltip("Played when any UI button is clicked. Clips are shuffled so the same one does not repeat in a row.")]
        [SerializeField] private AudioClip[] buttonClickClips = new AudioClip[0];
        [Range(0f, 1f)] [SerializeField] private float buttonClickVolume = 1f;

        [Header("Pieces")]
        [Tooltip("Played when a dragged piece is successfully placed on the grid.")]
        [SerializeField] private AudioClip[] pieceDropClips = new AudioClip[0];
        [Range(0f, 1f)] [SerializeField] private float pieceDropVolume = 1f;

        public AudioClip[] ButtonClickClips => buttonClickClips;
        public float ButtonClickVolume => buttonClickVolume;
        public AudioClip[] PieceDropClips => pieceDropClips;
        public float PieceDropVolume => pieceDropVolume;
    }
}
