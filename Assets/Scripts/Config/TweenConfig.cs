using DG.Tweening;
using UnityEngine;

namespace GravityPuzzle.Config
{
    [CreateAssetMenu(fileName = "TweenConfig", menuName = "Gravity Puzzle/Config/Tween")]
    public sealed class TweenConfig : ScriptableObject
    {
        [Min(1)] [SerializeField] private int tweenCapacity = 200;
        [Min(1)] [SerializeField] private int sequenceCapacity = 125;
        [SerializeField] private float pieceMoveDuration = .12f;
        [SerializeField] private Ease pieceMoveEase = Ease.OutQuad;
        [Min(.001f)] [SerializeField] private float gridFallDuration = .12f;
        [SerializeField] private Ease gridFallEase = Ease.OutQuad;
        [SerializeField] private float shredDuration = .3f;
        [SerializeField] private Ease shredEase = Ease.InQuad;

        [Header("Ice Presentation")]
        [Min(.001f)] [SerializeField] private float iceCrackDuration = .16f;
        [Min(0f)] [SerializeField] private float iceCrackScaleMultiplier = .12f;
        [Min(.001f)] [SerializeField] private float iceReleaseDuration = .2f;
        [Min(0f)] [SerializeField] private float iceReleaseScaleMultiplier = .16f;

        [Header("Progress Presentation")]
        [Min(.001f)] [SerializeField] private float progressSliderFillDuration = .12f;
        [SerializeField] private Ease progressSliderFillEase = Ease.OutQuad;
        [Min(.001f)] [SerializeField] private float progressVoxelFlightDuration = .55f;
        [SerializeField] private Ease progressVoxelFlightEase = Ease.InOutSine;
        [Min(.001f)] [SerializeField] private float progressSliderPunchDuration = .12f;

        [Header("Gem Presentation")]
        [Min(.001f)] [SerializeField] private float gemFlightDuration = .75f;
        [SerializeField] private Ease gemFlightEase = Ease.InBack;
        [Min(.001f)] [SerializeField] private float gemUiPunchDuration = .22f;

        [Header("Timer Booster Presentation")]
        [Min(.001f)] [SerializeField] private float timerEntranceDuration = .75f;
        [SerializeField] private Ease timerEntranceEase = Ease.OutCubic;
        [Min(.001f)] [SerializeField] private float timerFreezeFillDuration = .6f;
        [SerializeField] private Ease timerFreezeFillEase = Ease.Linear;
        [Min(.001f)] [SerializeField] private float timerFlyToTargetDuration = .5f;
        [SerializeField] private Ease timerFlyToTargetEase = Ease.InQuad;

        public int TweenCapacity => tweenCapacity;
        public int SequenceCapacity => sequenceCapacity;
        public float PieceMoveDuration => pieceMoveDuration;
        public Ease PieceMoveEase => pieceMoveEase;
        public float GridFallDuration => gridFallDuration;
        public Ease GridFallEase => gridFallEase;
        public float ShredDuration => shredDuration;
        public Ease ShredEase => shredEase;
        public float IceCrackDuration => iceCrackDuration;
        public float IceCrackScaleMultiplier => iceCrackScaleMultiplier;
        public float IceReleaseDuration => iceReleaseDuration;
        public float IceReleaseScaleMultiplier => iceReleaseScaleMultiplier;
        public float ProgressSliderFillDuration => progressSliderFillDuration;
        public Ease ProgressSliderFillEase => progressSliderFillEase;
        public float ProgressVoxelFlightDuration => progressVoxelFlightDuration;
        public Ease ProgressVoxelFlightEase => progressVoxelFlightEase;
        public float ProgressSliderPunchDuration => progressSliderPunchDuration;
        public float GemFlightDuration => gemFlightDuration;
        public Ease GemFlightEase => gemFlightEase;
        public float GemUiPunchDuration => gemUiPunchDuration;
        public float TimerEntranceDuration => timerEntranceDuration;
        public Ease TimerEntranceEase => timerEntranceEase;
        public float TimerFreezeFillDuration => timerFreezeFillDuration;
        public Ease TimerFreezeFillEase => timerFreezeFillEase;
        public float TimerFlyToTargetDuration => timerFlyToTargetDuration;
        public Ease TimerFlyToTargetEase => timerFlyToTargetEase;
    }
}
