using System;
using Solo.MOST_IN_ONE;
using UnityEngine;

namespace GravityPuzzle.Config
{
    [CreateAssetMenu(fileName = "HapticFeedbackConfig", menuName = "Gravity Puzzle/Config/Haptic Feedback")]
    public sealed class HapticFeedbackConfig : ScriptableObject
    {
        /// <summary>One haptic cue played through a MOST preset (iOS UIKit feedback / Android waveform).</summary>
        [Serializable]
        public struct Cue
        {
            [SerializeField] private bool enabled;
            [SerializeField] private MOST_HapticFeedback.HapticTypes type;
            public bool Enabled => enabled;
            public MOST_HapticFeedback.HapticTypes Type => type;
        }

        [Header("Global")]
        [Tooltip("Minimum time between any two haptics. A cue with higher priority than the last played one may bypass it.")]
        [Min(0)] [SerializeField] private int globalMinIntervalMilliseconds = 60;

        [Header("UI")]
        [SerializeField] private Cue pieceSelected = new Cue();
        [SerializeField] private Cue invalidDrop = new Cue();
        [SerializeField] private Cue validDrop = new Cue();
        [Min(0)] [SerializeField] private int invalidDropCooldownMilliseconds = 250;
        [Min(0)] [SerializeField] private int uiCooldownMilliseconds = 0;
        [SerializeField] private int uiPriority = 1;

        [Header("Booster Impacts")]
        [SerializeField] private Cue hammerImpact = new Cue();
        [SerializeField] private Cue bombDefused = new Cue();
        [SerializeField] private Cue rocketImpact = new Cue();
        [SerializeField] private Cue freezeActivated = new Cue();
        [Min(0)] [SerializeField] private int impactCooldownMilliseconds = 0;
        [SerializeField] private int impactPriority = 3;

        [Header("Shredder")]
        [Tooltip("Repeating tick played while at least one piece is being fed into the cutter.")]
        [SerializeField] private Cue shredderTick = new Cue();
        [Min(1)] [SerializeField] private int shredderTickIntervalMilliseconds = 90;
        [SerializeField] private int shredderTickPriority = 0;
        [SerializeField] private Cue shredderPieceCompleted = new Cue();
        [Min(0)] [SerializeField] private int shredderCompletionCooldownMilliseconds = 0;
        [SerializeField] private int shredderCompletionPriority = 2;

        [Header("Level Result")]
        [SerializeField] private Cue levelCompleted = new Cue();
        [SerializeField] private Cue levelFailed = new Cue();
        [Min(0)] [SerializeField] private int resultCooldownMilliseconds = 0;
        [SerializeField] private int resultPriority = 3;

        public int GlobalMinIntervalMilliseconds => globalMinIntervalMilliseconds;
        public Cue PieceSelected => pieceSelected;
        public Cue InvalidDrop => invalidDrop;
        public Cue ValidDrop => validDrop;
        public int InvalidDropCooldownMilliseconds => invalidDropCooldownMilliseconds;
        public int UiCooldownMilliseconds => uiCooldownMilliseconds;
        public int UiPriority => uiPriority;
        public Cue HammerImpact => hammerImpact;
        public Cue BombDefused => bombDefused;
        public Cue RocketImpact => rocketImpact;
        public Cue FreezeActivated => freezeActivated;
        public int ImpactCooldownMilliseconds => impactCooldownMilliseconds;
        public int ImpactPriority => impactPriority;
        public Cue ShredderTick => shredderTick;
        public int ShredderTickIntervalMilliseconds => shredderTickIntervalMilliseconds;
        public int ShredderTickPriority => shredderTickPriority;
        public Cue ShredderPieceCompleted => shredderPieceCompleted;
        public int ShredderCompletionCooldownMilliseconds => shredderCompletionCooldownMilliseconds;
        public int ShredderCompletionPriority => shredderCompletionPriority;
        public Cue LevelCompleted => levelCompleted;
        public Cue LevelFailed => levelFailed;
        public int ResultCooldownMilliseconds => resultCooldownMilliseconds;
        public int ResultPriority => resultPriority;
    }
}
