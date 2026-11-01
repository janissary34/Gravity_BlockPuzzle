using System;
using UnityEngine;

namespace GravityPuzzle.Config
{
    [CreateAssetMenu(fileName = "HapticFeedbackConfig", menuName = "Gravity Puzzle/Config/Haptic Feedback")]
    public sealed class HapticFeedbackConfig : ScriptableObject
    {
        [Serializable]
        public struct Pulse
        {
            [SerializeField] private bool enabled;
            [Range(0f, 1f)] [SerializeField] private float intensity;
            [Min(1)] [SerializeField] private int durationMilliseconds;
            public bool Enabled => enabled;
            public float Intensity => intensity;
            public int DurationMilliseconds => durationMilliseconds;
        }

        [Serializable]
        public struct Pattern
        {
            [SerializeField] private bool enabled;
            [SerializeField] private Pulse first;
            [SerializeField] private Pulse second;
            [SerializeField] private Pulse third;
            [Min(0)] [SerializeField] private int gapMilliseconds;
            public bool Enabled => enabled;
            public Pulse First => first;
            public Pulse Second => second;
            public Pulse Third => third;
            public int GapMilliseconds => gapMilliseconds;
        }

        [Header("UI")]
        [SerializeField] private Pulse pieceSelected = new Pulse();
        [SerializeField] private Pulse invalidDrop = new Pulse();
        [SerializeField] private Pulse validDrop = new Pulse();
        [Min(0)] [SerializeField] private int invalidDropCooldownMilliseconds = 250;
        [Min(0)] [SerializeField] private int uiCooldownMilliseconds = 0;
        [SerializeField] private int uiPriority = 1;

        [Header("Booster Impacts")]
        [SerializeField] private Pulse hammerImpact = new Pulse();
        [SerializeField] private Pattern bombDefused = new Pattern();
        [SerializeField] private Pulse rocketImpact = new Pulse();
        [SerializeField] private Pattern freezeActivated = new Pattern();
        [Min(0)] [SerializeField] private int impactCooldownMilliseconds = 0;
        [SerializeField] private int impactPriority = 3;

        [Header("Shredder")]
        [SerializeField] private bool shredderPatternEnabled = true;
        [Range(0f, 1f)] [SerializeField] private float shredderStartIntensity = .20f;
        [Range(0f, 1f)] [SerializeField] private float shredderEndIntensity = .08f;
        [SerializeField] private AnimationCurve shredderIntensityByFeedProgress = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [Min(1)] [SerializeField] private int shredderOnMilliseconds = 28;
        [Min(0)] [SerializeField] private int shredderOffMilliseconds = 92;
        [Range(0f, 1f)] [SerializeField] private float shredderIntensityUpdateThreshold = .02f;
        [SerializeField] private Pulse shredderPieceCompleted = new Pulse();
        [Min(0)] [SerializeField] private int shredderCompletionCooldownMilliseconds = 0;
        [SerializeField] private int shredderCompletionPriority = 2;

        [Header("Level Result")]
        [SerializeField] private Pattern levelCompleted = new Pattern();
        [SerializeField] private Pattern levelFailed = new Pattern();
        [Min(0)] [SerializeField] private int resultCooldownMilliseconds = 0;
        [SerializeField] private int resultPriority = 3;

        public Pulse PieceSelected => pieceSelected;
        public Pulse InvalidDrop => invalidDrop;
        public Pulse ValidDrop => validDrop;
        public int InvalidDropCooldownMilliseconds => invalidDropCooldownMilliseconds;
        public int UiCooldownMilliseconds => uiCooldownMilliseconds;
        public int UiPriority => uiPriority;
        public Pulse HammerImpact => hammerImpact;
        public Pattern BombDefused => bombDefused;
        public Pulse RocketImpact => rocketImpact;
        public Pattern FreezeActivated => freezeActivated;
        public int ImpactCooldownMilliseconds => impactCooldownMilliseconds;
        public int ImpactPriority => impactPriority;
        public bool ShredderPatternEnabled => shredderPatternEnabled;
        public float ShredderStartIntensity => shredderStartIntensity;
        public float ShredderEndIntensity => shredderEndIntensity;
        public AnimationCurve ShredderIntensityByFeedProgress => shredderIntensityByFeedProgress;
        public int ShredderOnMilliseconds => shredderOnMilliseconds;
        public int ShredderOffMilliseconds => shredderOffMilliseconds;
        public float ShredderIntensityUpdateThreshold => shredderIntensityUpdateThreshold;
        public Pulse ShredderPieceCompleted => shredderPieceCompleted;
        public int ShredderCompletionCooldownMilliseconds => shredderCompletionCooldownMilliseconds;
        public int ShredderCompletionPriority => shredderCompletionPriority;
        public Pattern LevelCompleted => levelCompleted;
        public Pattern LevelFailed => levelFailed;
        public int ResultCooldownMilliseconds => resultCooldownMilliseconds;
        public int ResultPriority => resultPriority;
    }
}
