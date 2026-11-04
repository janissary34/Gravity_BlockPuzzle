using System;
using GravityPuzzle.Config;
using UnityEngine;

namespace GravityPuzzle.Creative.Config
{
    public enum CreativeBackgroundMode
    {
        /// <summary>The level's own backdrop and camera clear colour.</summary>
        Art = 0,
        /// <summary>Backdrop renderers hidden; camera clears to <see cref="CreativeTakeConfig.BackgroundColor"/>.</summary>
        SolidColor = 1,
    }

    public enum CreativeTimerMode
    {
        /// <summary>The level timer runs as in normal play.</summary>
        Normal = 0,
        /// <summary>The level timer is paused through the board's owner-scoped pause API.</summary>
        Frozen = 1,
    }

    /// <summary>
    /// Describes one creative-production take: which level, how the scene looks, and how it is
    /// captured. Immutable configuration; the mutable state of a take lives in
    /// <see cref="GravityPuzzle.Creative.Runtime.CreativeSession"/>.
    /// Creative Mode is off unless a take is explicitly requested (editor only, see CreativeTakeRequest).
    /// </summary>
    [CreateAssetMenu(menuName = "Gravity Puzzle/Creative/Take Config", fileName = "CreativeTakeConfig")]
    public sealed class CreativeTakeConfig : ScriptableObject
    {
        [Serializable]
        public struct BoosterCount
        {
            public BoosterRewardType booster;
            [Min(0)] public int count;
        }

        [Header("Level")]
        [Tooltip("Level played in this take. It does not need to belong to the campaign sequence.")]
        [SerializeField] private GravityLevelDefinition level;

        [Header("Presentation")]
        [SerializeField] private CreativeUIProfile uiProfile;
        [SerializeField] private CreativeBackgroundMode backgroundMode = CreativeBackgroundMode.Art;
        [Tooltip("Camera clear colour for SolidColor mode (for example #00FF00 for a chroma key).")]
        [SerializeField] private Color backgroundColor = Color.green;
        [Tooltip("Level number shown in HUD and result labels. 0 = the selected level's campaign number. " +
                 "Labels only: gameplay rules and the player's save are not affected.")]
        [Min(0)] [SerializeField] private int displayLevelNumber;

        [Header("Gameplay")]
        [SerializeField] private CreativeTimerMode timerMode = CreativeTimerMode.Frozen;
        [Tooltip("Booster counts for this take. Kept in memory only; the player's saved inventory is never touched.")]
        [SerializeField] private BoosterCount[] boosters = Array.Empty<BoosterCount>();
        [Tooltip("Optional scripted gameplay. Empty = the take is played by hand.")]
        [SerializeField] private CreativeScript script;

        [Header("Capture")]
        [Min(16)] [SerializeField] private int captureWidth = 1080;
        [Min(16)] [SerializeField] private int captureHeight = 1920;
        [Min(1)] [SerializeField] private int captureFrameRate = 30;
        [Tooltip("Frames after level.ready before recording starts, so the loading cover has cleared " +
                 "and the board has rendered (the cover hides about two frames after level.ready).")]
        [Min(0)] [SerializeField] private int captureStartDelayFrames = 3;
        [Tooltip("Frames kept after the level is cleared or failed, so the result moment is in the take.")]
        [Min(0)] [SerializeField] private int tailFrames = 90;
        [Tooltip("Safety limit: the take ends after this many seconds of game time even without a result.")]
        [Min(1f)] [SerializeField] private float maxSeconds = 180f;

        public GravityLevelDefinition Level => level;
        public CreativeUIProfile UIProfile => uiProfile;
        public CreativeBackgroundMode BackgroundMode => backgroundMode;
        public Color BackgroundColor => backgroundColor;
        public int DisplayLevelNumber => displayLevelNumber;
        public CreativeTimerMode TimerMode => timerMode;
        public int BoosterEntryCount => boosters != null ? boosters.Length : 0;
        public BoosterCount GetBoosterEntry(int index) => boosters[index];
        public CreativeScript Script => script;
        public int CaptureWidth => captureWidth;
        public int CaptureHeight => captureHeight;
        public int CaptureFrameRate => captureFrameRate;
        public int CaptureStartDelayFrames => captureStartDelayFrames;
        public int TailFrames => tailFrames;
        public float MaxSeconds => maxSeconds;
    }
}
