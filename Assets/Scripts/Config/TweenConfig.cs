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
        [SerializeField] private Ease gridFallEase = Ease.OutQuad;
        [Min(.001f)] [SerializeField] private float gridFallUnitsPerSecond = 8f;
        [Min(.001f)] [SerializeField] private float gridFallMinimumDuration = .08f;
        [Min(.001f)] [SerializeField] private float gridFallMaximumDuration = .4f;
        [Min(.001f)] [SerializeField] private float gridReleaseUnitsPerSecond = 12f;
        [Min(.001f)] [SerializeField] private float gridReleaseMinimumDuration = .06f;
        [Min(.001f)] [SerializeField] private float gridReleaseMaximumDuration = .28f;
        [SerializeField] private float shredDuration = .3f;
        [SerializeField] private Ease shredEase = Ease.InQuad;

        [Header("Ice Presentation")]
        [Min(.001f)] [SerializeField] private float iceCrackDuration = .16f;
        [Min(0f)] [SerializeField] private float iceCrackScaleMultiplier = .12f;
        [Min(1)] [SerializeField] private int iceCrackVibrato = 5;
        [Range(0f, 1f)] [SerializeField] private float iceCrackElasticity = .55f;
        [Min(.001f)] [SerializeField] private float iceReleaseDuration = .2f;
        [Min(0f)] [SerializeField] private float iceReleaseScaleMultiplier = .16f;
        [Min(1)] [SerializeField] private int iceReleaseVibrato = 6;
        [Range(0f, 1f)] [SerializeField] private float iceReleaseElasticity = .55f;

        [Header("Progress Presentation")]
        [Min(.001f)] [SerializeField] private float progressSliderFillDuration = .12f;
        [SerializeField] private Ease progressSliderFillEase = Ease.OutQuad;
        [Min(.001f)] [SerializeField] private float progressVoxelFlightDuration = .55f;
        [SerializeField] private Ease progressVoxelFlightEase = Ease.InOutSine;
        [Min(0f)] [SerializeField] private float progressVoxelRotationRange = 160f;
        [Min(0f)] [SerializeField] private float progressVoxelCurveDropMultiplier = .8f;
        [Min(.001f)] [SerializeField] private float progressVoxelUiSize = .32f;
        [Min(.001f)] [SerializeField] private float progressSliderPunchDuration = .12f;
        [SerializeField] private Vector3 progressSliderPunchScale = new Vector3(.045f, .045f, 0f);
        [Min(1)] [SerializeField] private int progressSliderPunchVibrato = 6;
        [Range(0f, 1f)] [SerializeField] private float progressSliderPunchElasticity = .5f;
        [Min(0f)] [SerializeField] private float progressSliderPulseCooldown = .09f;

        [Header("Timer Booster Presentation")]
        [Min(.001f)] [SerializeField] private float timerEntranceDuration = .75f;
        [SerializeField] private Ease timerEntranceEase = Ease.OutCubic;
        [Min(.001f)] [SerializeField] private float timerFreezeFillDuration = .6f;
        [SerializeField] private Ease timerFreezeFillEase = Ease.Linear;
        [Min(.001f)] [SerializeField] private float timerFlyToTargetDuration = .5f;
        [SerializeField] private Ease timerFlyToTargetEase = Ease.InQuad;

        [Header("Hammer Booster Presentation")]
        [Min(.001f)] [SerializeField] private float hammerEntranceDuration = .35f;
        [SerializeField] private Ease hammerEntranceMoveEase = Ease.OutQuart;
        [SerializeField] private Ease hammerEntranceScaleEase = Ease.OutSine;
        [Min(.001f)] [SerializeField] private float hammerApproachDuration = .45f;
        [SerializeField] private Ease hammerApproachEase = Ease.OutSine;
        [Min(0f)] [SerializeField] private float hammerWindUpDelay = .1f;
        [Min(.001f)] [SerializeField] private float hammerStrikeDuration = .12f;
        [SerializeField] private Ease hammerStrikeEase = Ease.InQuad;
        [Min(.001f)] [SerializeField] private float hammerExitDuration = .2f;
        [SerializeField] private Ease hammerExitEase = Ease.OutSine;
        [SerializeField] private Ease hammerExitScaleEase = Ease.InBack;
        [Min(.001f)] [SerializeField] private float hammerCameraShakeDuration = .15f;
        [Min(0f)] [SerializeField] private float hammerCameraShakeStrength = .12f;
        [Min(1)] [SerializeField] private int hammerCameraShakeVibrato = 18;
        [Min(0f)] [SerializeField] private float hammerCameraShakeRandomness = 90f;

        [Header("Rocket Booster Presentation")]
        [Min(.001f)] [SerializeField] private float rocketEntranceDuration = .75f;
        [SerializeField] private Ease rocketEntranceEase = Ease.OutCubic;
        [Min(0f)] [SerializeField] private float rocketTargetPauseDuration = 1f;
        [Min(.001f)] [SerializeField] private float rocketLaunchDuration = .55f;
        [SerializeField] private Ease rocketLaunchEase = Ease.InQuad;

        [Header("Button Presentation")]
        [Min(.001f)] [SerializeField] private float buttonPressDuration = .1f;
        [SerializeField] private Ease buttonPressEase = Ease.OutQuad;
        [Min(.001f)] [SerializeField] private float buttonReleaseDuration = .25f;
        [SerializeField] private Ease buttonReleaseEase = Ease.OutBack;

        [Header("Timer Freeze Presentation")]
        [Min(0f)] [SerializeField] private float timerCenterPauseDuration = 1f;
        [Min(.001f)] [SerializeField] private float timerUrgencyFadeInDuration = .45f;
        [SerializeField] private Ease timerUrgencyFadeInEase = Ease.OutSine;
        [Min(.001f)] [SerializeField] private float timerUrgencyFadeOutDuration = .45f;
        [SerializeField] private Ease timerUrgencyFadeOutEase = Ease.InSine;
        [Min(.001f)] [SerializeField] private float timerImpactDuration = .42f;
        [SerializeField] private Ease timerImpactScaleEase = Ease.OutQuad;
        [Min(0f)] [SerializeField] private float timerImpactStartScale = .22f;
        [Range(0f, 1f)] [SerializeField] private float timerImpactFadeInFraction = .22f;
        [Range(0f, 1f)] [SerializeField] private float timerImpactScaleFraction = .55f;
        [Range(0f, 1f)] [SerializeField] private float timerImpactFadeOutFraction = .45f;
        [Min(.001f)] [SerializeField] private float timerFreezeGlowFadeInDuration = .25f;
        [SerializeField] private Ease timerFreezeGlowFadeInEase = Ease.OutSine;
        [Min(.001f)] [SerializeField] private float timerFreezeGlowFadeOutDuration = .35f;
        [SerializeField] private Ease timerFreezeGlowFadeOutEase = Ease.InSine;

        [Header("Freeze FX Presentation")]
        [Tooltip("Duration of the subtle scale punch when the clock arrives at center (anticipation).")]
        [Min(.001f)] [SerializeField] private float timerAnticipationDuration = .12f;
        [Tooltip("Target localScale multiplier applied to timer_obj while it flies toward the timer display.")]
        [Min(0f)] [SerializeField] private float timerFlightScaleTarget = .60f;
        [Tooltip("FreezeImpactImage starting scale (before burst).")]
        [Min(0f)] [SerializeField] private float freezeImpactStartScale = .40f;
        [Tooltip("FreezeImpactImage peak scale at burst apex.")]
        [Min(0f)] [SerializeField] private float freezeImpactPeakScale = 1.40f;
        [Tooltip("Full period of one TimerGlow alpha+scale pulse loop.")]
        [Min(.001f)] [SerializeField] private float freezeGlowPulseDuration = 1.50f;
        [Tooltip("TimerGlow alpha at the trough of the pulse.")]
        [Range(0f, 1f)] [SerializeField] private float freezeGlowPulseMinAlpha = .65f;
        [Tooltip("TimerGlow alpha at the peak of the pulse.")]
        [Range(0f, 1f)] [SerializeField] private float freezeGlowPulseMaxAlpha = .85f;
        [Tooltip("FreezeTimerIndicator fade-in duration.")]
        [Min(.001f)] [SerializeField] private float freezeIndicatorFadeDuration = .25f;
        [Tooltip("Atmosphere elements (Glow/FrostBorder/EdgeGlow/Vignette) fade-in duration.")]
        [Min(.001f)] [SerializeField] private float freezeAtmoFadeInDuration = .28f;
        [Tooltip("Atmosphere elements fade-out duration during expiration.")]
        [Min(.001f)] [SerializeField] private float freezeAtmoFadeOutDuration = .70f;

        public int TweenCapacity => tweenCapacity;
        public int SequenceCapacity => sequenceCapacity;
        public float PieceMoveDuration => pieceMoveDuration;
        public Ease PieceMoveEase => pieceMoveEase;
        public Ease GridFallEase => gridFallEase;
        public float GetGridFallDuration(float distance)
        {
            return Mathf.Clamp(
                distance / gridFallUnitsPerSecond,
                gridFallMinimumDuration,
                gridFallMaximumDuration);
        }

        public float GetGridReleaseDuration(float distance)
        {
            return Mathf.Clamp(
                distance / gridReleaseUnitsPerSecond,
                gridReleaseMinimumDuration,
                gridReleaseMaximumDuration);
        }
        public float ShredDuration => shredDuration;
        public Ease ShredEase => shredEase;
        public float IceCrackDuration => iceCrackDuration;
        public float IceCrackScaleMultiplier => iceCrackScaleMultiplier;
        public int IceCrackVibrato => iceCrackVibrato;
        public float IceCrackElasticity => iceCrackElasticity;
        public float IceReleaseDuration => iceReleaseDuration;
        public float IceReleaseScaleMultiplier => iceReleaseScaleMultiplier;
        public int IceReleaseVibrato => iceReleaseVibrato;
        public float IceReleaseElasticity => iceReleaseElasticity;
        public float ProgressSliderFillDuration => progressSliderFillDuration;
        public Ease ProgressSliderFillEase => progressSliderFillEase;
        public float ProgressVoxelFlightDuration => progressVoxelFlightDuration;
        public Ease ProgressVoxelFlightEase => progressVoxelFlightEase;
        public float ProgressVoxelRotationRange => progressVoxelRotationRange;
        public float ProgressVoxelCurveDropMultiplier => progressVoxelCurveDropMultiplier;
        public float ProgressVoxelUiSize => progressVoxelUiSize;
        public float ProgressSliderPunchDuration => progressSliderPunchDuration;
        public Vector3 ProgressSliderPunchScale => progressSliderPunchScale;
        public int ProgressSliderPunchVibrato => progressSliderPunchVibrato;
        public float ProgressSliderPunchElasticity => progressSliderPunchElasticity;
        public float ProgressSliderPulseCooldown => progressSliderPulseCooldown;
        public float TimerEntranceDuration => timerEntranceDuration;
        public Ease TimerEntranceEase => timerEntranceEase;
        public float TimerFreezeFillDuration => timerFreezeFillDuration;
        public Ease TimerFreezeFillEase => timerFreezeFillEase;
        public float TimerFlyToTargetDuration => timerFlyToTargetDuration;
        public Ease TimerFlyToTargetEase => timerFlyToTargetEase;
        public float HammerEntranceDuration => hammerEntranceDuration;
        public Ease HammerEntranceMoveEase => hammerEntranceMoveEase;
        public Ease HammerEntranceScaleEase => hammerEntranceScaleEase;
        public float HammerApproachDuration => hammerApproachDuration;
        public Ease HammerApproachEase => hammerApproachEase;
        public float HammerWindUpDelay => hammerWindUpDelay;
        public float HammerStrikeDuration => hammerStrikeDuration;
        public Ease HammerStrikeEase => hammerStrikeEase;
        public float HammerExitDuration => hammerExitDuration;
        public Ease HammerExitEase => hammerExitEase;
        public Ease HammerExitScaleEase => hammerExitScaleEase;
        public float HammerCameraShakeDuration => hammerCameraShakeDuration;
        public float HammerCameraShakeStrength => hammerCameraShakeStrength;
        public int HammerCameraShakeVibrato => hammerCameraShakeVibrato;
        public float HammerCameraShakeRandomness => hammerCameraShakeRandomness;
        public float RocketEntranceDuration => rocketEntranceDuration;
        public Ease RocketEntranceEase => rocketEntranceEase;
        public float RocketTargetPauseDuration => rocketTargetPauseDuration;
        public float RocketLaunchDuration => rocketLaunchDuration;
        public Ease RocketLaunchEase => rocketLaunchEase;
        public float ButtonPressDuration => buttonPressDuration;
        public Ease ButtonPressEase => buttonPressEase;
        public float ButtonReleaseDuration => buttonReleaseDuration;
        public Ease ButtonReleaseEase => buttonReleaseEase;
        public float TimerCenterPauseDuration => timerCenterPauseDuration;
        public float TimerUrgencyFadeInDuration => timerUrgencyFadeInDuration;
        public Ease TimerUrgencyFadeInEase => timerUrgencyFadeInEase;
        public float TimerUrgencyFadeOutDuration => timerUrgencyFadeOutDuration;
        public Ease TimerUrgencyFadeOutEase => timerUrgencyFadeOutEase;
        public float TimerImpactDuration => timerImpactDuration;
        public Ease TimerImpactScaleEase => timerImpactScaleEase;
        public float TimerImpactStartScale => timerImpactStartScale;
        public float TimerImpactFadeInFraction => timerImpactFadeInFraction;
        public float TimerImpactScaleFraction => timerImpactScaleFraction;
        public float TimerImpactFadeOutFraction => timerImpactFadeOutFraction;
        public float TimerFreezeGlowFadeInDuration => timerFreezeGlowFadeInDuration;
        public Ease TimerFreezeGlowFadeInEase => timerFreezeGlowFadeInEase;
        public float TimerFreezeGlowFadeOutDuration => timerFreezeGlowFadeOutDuration;
        public Ease TimerFreezeGlowFadeOutEase => timerFreezeGlowFadeOutEase;
        public float TimerAnticipationDuration => timerAnticipationDuration;
        public float TimerFlightScaleTarget => timerFlightScaleTarget;
        public float FreezeImpactStartScale => freezeImpactStartScale;
        public float FreezeImpactPeakScale => freezeImpactPeakScale;
        public float FreezeGlowPulseDuration => freezeGlowPulseDuration;
        public float FreezeGlowPulseMinAlpha => freezeGlowPulseMinAlpha;
        public float FreezeGlowPulseMaxAlpha => freezeGlowPulseMaxAlpha;
        public float FreezeIndicatorFadeDuration => freezeIndicatorFadeDuration;
        public float FreezeAtmoFadeInDuration => freezeAtmoFadeInDuration;
        public float FreezeAtmoFadeOutDuration => freezeAtmoFadeOutDuration;
    }
}
