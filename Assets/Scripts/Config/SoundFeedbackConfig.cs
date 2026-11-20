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
        [Tooltip("Played when a piece is grabbed/picked up for dragging.")]
        [SerializeField] private AudioClip[] pieceDropClips = new AudioClip[0];
        [Range(0f, 1f)] [SerializeField] private float pieceDropVolume = 1f;

        [Header("Progress")]
        [Tooltip("Played while shredded voxels fill the level progress bar.")]
        [SerializeField] private AudioClip[] barFillClips = new AudioClip[0];
        [Range(0f, 1f)] [SerializeField] private float barFillVolume = 1f;
        [Tooltip("Minimum real-time interval between bar-fill one-shots so a large burst cannot flood the mix.")]
        [Min(0f)] [SerializeField] private float barFillMinInterval = 0.12f;

        [Header("Timer")]
        [Tooltip("Looping clock-ticking track played during the final urgency seconds of an active countdown.")]
        [SerializeField] private AudioClip clockTickingClip;
        [Range(0f, 1f)] [SerializeField] private float clockTickingVolume = 1f;

        [Header("Boosters")]
        [Tooltip("Played when the hammer breaks a piece cell.")]
        [SerializeField] private AudioClip[] hammerBreakClips = new AudioClip[0];
        [Range(0f, 1f)] [SerializeField] private float hammerBreakVolume = 1f;
        [Tooltip("Played at the exact impact moment when the shrinking Freeze Timer clock arrives at the countdown and bursts.")]
        [SerializeField] private AudioClip[] freezeTimerImpactClips = new AudioClip[0];
        [Range(0f, 1f)] [SerializeField] private float freezeTimerImpactVolume = 1f;
        [Tooltip("Played when the Rocket booster launches a piece or box off the board.")]
        [SerializeField] private AudioClip[] rocketLaunchClips = new AudioClip[0];
        [Range(0f, 1f)] [SerializeField] private float rocketLaunchVolume = 1f;

        [Header("Reveal Areas")]
        [Tooltip("Played when an Elevator block opens to reveal its hidden pieces.")]
        [SerializeField] private AudioClip[] elevatorOpenClips = new AudioClip[0];
        [Range(0f, 1f)] [SerializeField] private float elevatorOpenVolume = 1f;
        [Tooltip("Played once each time a Box block's remaining counter decrements by one.")]
        [SerializeField] private AudioClip[] boxCounterStepClips = new AudioClip[0];
        [Range(0f, 1f)] [SerializeField] private float boxCounterStepVolume = 1f;
        [Tooltip("Played when a Box block shatters open after reaching zero.")]
        [SerializeField] private AudioClip[] boxBreakClips = new AudioClip[0];
        [Range(0f, 1f)] [SerializeField] private float boxBreakVolume = 1f;

        public AudioClip[] ButtonClickClips => buttonClickClips;
        public float ButtonClickVolume => buttonClickVolume;
        public AudioClip[] PieceDropClips => pieceDropClips;
        public float PieceDropVolume => pieceDropVolume;
        public AudioClip[] BarFillClips => barFillClips;
        public float BarFillVolume => barFillVolume;
        public float BarFillMinInterval => barFillMinInterval;
        public AudioClip ClockTickingClip => clockTickingClip;
        public float ClockTickingVolume => clockTickingVolume;
        public AudioClip[] HammerBreakClips => hammerBreakClips;
        public float HammerBreakVolume => hammerBreakVolume;
        public AudioClip[] FreezeTimerImpactClips => freezeTimerImpactClips;
        public float FreezeTimerImpactVolume => freezeTimerImpactVolume;
        public AudioClip[] RocketLaunchClips => rocketLaunchClips;
        public float RocketLaunchVolume => rocketLaunchVolume;
        public AudioClip[] ElevatorOpenClips => elevatorOpenClips;
        public float ElevatorOpenVolume => elevatorOpenVolume;
        public AudioClip[] BoxCounterStepClips => boxCounterStepClips;
        public float BoxCounterStepVolume => boxCounterStepVolume;
        public AudioClip[] BoxBreakClips => boxBreakClips;
        public float BoxBreakVolume => boxBreakVolume;
    }
}
