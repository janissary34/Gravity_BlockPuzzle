using System.Collections;
using DG.Tweening;
using TMPro;
using GravityPuzzle.Config;
using UnityEngine;
using UnityEngine.UI;

namespace GravityPuzzle
{
    /// <summary>
    /// Timer Booster sequence controller.
    /// Animates timer_obj from screen bottom at native scale to screen center,
    /// pauses for 1 second while performing a 360-degree DOFillAmount freeze transition on frozenClockImage,
    /// flies strictly vertically to timer_txt's location, triggers a blue particle explosion VFX on arrival,
    /// deactivates timer_obj upon arrival, and freezes the timer for 8 seconds.
    /// Uses direct Inspector assigned references only.
    /// </summary>
    public class TimerBooster : MonoBehaviour
    {
        [Header("UI & Animation References (Assign in Inspector)")]
        [Tooltip("The floating timer visual object that animates from bottom to center, then to timer_txt.")]
        [SerializeField] private GameObject timer_obj;

        [Tooltip("Target UI transform for timer text.")]
        [SerializeField] private Transform timer_txt;

        [Tooltip("Optional UI Button to trigger the Timer Booster.")]
        [SerializeField] private Button boosterButton;

        [Tooltip("Booster count component on this timer booster button.")]
        [SerializeField] private BoosterButton boosterButtonRef;

        [Tooltip("Camera used for timer presentation. Cached once during Awake when unassigned.")]
        [SerializeField] private Camera presentationCamera;

        [Tooltip("Optional Canvas authored on timer_obj when its flight must render above every other UI element.")]
        [SerializeField] private Canvas timerPresentationCanvas;

        [Tooltip("Render priority for the travelling clock and its arrival VFX. Canvas orders are limited to 32767.")]
        [Range(0, short.MaxValue)]
        [SerializeField] private int timerPresentationSortingOrder = 32000;

        [Tooltip("Optional FreezeTimerBooster reference. Auto-cached in Awake if unassigned.")]
        [SerializeField] private FreezeTimerBooster freezeTimerBooster;

        [SerializeField, Tooltip("Owns timer-booster tween timing and easing.")]
        private TweenConfig tweenConfig;

        [Header("Visual Effects (VFX)")]
        [Tooltip("Scene-authored blue particle explosion triggered when the clock arrives at timer_txt. Assign an active-scene instance, not a prefab asset.")]
        [SerializeField] private ParticleSystem blueParticleVFX;

        [Header("Urgency Presentation")]
        [Tooltip("Full-screen timer-freeze presentation root. It is shown as soon as the timer booster begins and remains visible for the freeze window.")]
        [SerializeField] private GameObject timerUrgencyPresentation;

        [Tooltip("Optional CanvasGroup on the presentation root. Assigning it enables a smooth fade instead of an instant visibility change.")]
        [SerializeField] private CanvasGroup timerUrgencyCanvasGroup;

        [Min(.01f)] [SerializeField] private float urgencyFadeInDuration = .45f;
        [SerializeField] private Ease urgencyFadeInEase = Ease.OutSine;
        [Min(.01f)] [SerializeField] private float urgencyFadeOutDuration = .45f;
        [SerializeField] private Ease urgencyFadeOutEase = Ease.InSine;

        [Header("Freeze VFX")]
        [Tooltip("One-shot sparkle burst played when the animated timer reaches the countdown.")]
        [SerializeField] private ParticleSystem timerImpactParticles;

        [Tooltip("Preferred timer impact presentation. Assign a UI Image using TimerImpactIceBurst-v2 for a reliable timer-local burst.")]
        [SerializeField] private Image timerImpactImage;

        [SerializeField] private CanvasGroup timerImpactCanvasGroup;
        [SerializeField] private Vector2 timerImpactImageSize = new Vector2(180f, 180f);
        [Min(.01f)] [SerializeField] private float timerImpactDuration = .42f;
        [SerializeField] private Ease timerImpactScaleEase = Ease.OutQuad;

        [Tooltip("Looping ambient snow played throughout the frozen timer window.")]
        [SerializeField] private ParticleSystem freezeSnowParticles;

        [Tooltip("Sorting order used to keep the timer-freeze particles above the timer canvas.")]
        [SerializeField] private int freezeVfxSortingOrder = 100;

        [Header("Freeze FX \u2014 FreezeFXRoot atmosphere")]
        [Tooltip("FreezeFrostBorder CanvasGroup.")]
        [SerializeField] private CanvasGroup freezeFrostBorder;

        [Tooltip("FreezeEdgeGlow CanvasGroup.")]
        [SerializeField] private CanvasGroup freezeEdgeGlow;

        [Tooltip("FreezeDarkVignette CanvasGroup.")]
        [SerializeField] private CanvasGroup freezeDarkVignette;

        [Tooltip("FreezeTimerGlow RectTransform (for pulse scale).")]
        [SerializeField] private RectTransform freezeTimerGlowRect;

        [Tooltip("UI-space offset applied after FreezeTimerGlow is aligned to the timer target.")]
        [SerializeField] private Vector2 freezeTimerGlowOffset;

        [Tooltip("FreezeTimerGlow CanvasGroup (for pulse alpha and fade).")]
        [SerializeField] private CanvasGroup freezeTimerGlow;

        [Tooltip("FreezeImpactImage RectTransform (for burst scale).")]
        [SerializeField] private RectTransform freezeImpactImageRect;

        [Tooltip("FreezeImpactImage CanvasGroup (for burst fade).")]
        [SerializeField] private CanvasGroup freezeImpactImageCG;

        [Tooltip("FreezeTimerIndicator CanvasGroup. Null-safe \u2014 leave unassigned to skip.")]
        [SerializeField] private CanvasGroup freezeTimerIndicator;

        [Tooltip("FreezeTimerIndicator RectTransform for scale animation. Null-safe.")]
        [SerializeField] private RectTransform freezeTimerIndicatorRect;

        [Header("Freeze FX \u2014 Impact Particles")]
        [Tooltip("ImpactShard_Long ParticleSystem.")]
        [SerializeField] private ParticleSystem impactShardLong;

        [Tooltip("ImpactShard_Diamond ParticleSystem.")]
        [SerializeField] private ParticleSystem impactShardDiamond;

        [Tooltip("ImpactShard_Chunk ParticleSystem.")]
        [SerializeField] private ParticleSystem impactShardChunk;

        [Tooltip("FreezeImpactVapor UI Image RectTransform (for DOTween scale animation).")]
        [SerializeField] private RectTransform freezeImpactVaporRect;

        [Tooltip("FreezeImpactVapor CanvasGroup (for DOTween alpha animation).")]
        [SerializeField] private CanvasGroup freezeImpactVaporCG;

        [Header("Freeze FX \u2014 Ambient Snow")]
        [Tooltip("SnowTop ParticleSystem.")]
        [SerializeField] private ParticleSystem snowTop;

        [Tooltip("SnowBottom ParticleSystem.")]
        [SerializeField] private ParticleSystem snowBottom;

        [Tooltip("SnowLeft ParticleSystem.")]
        [SerializeField] private ParticleSystem snowLeft;

        [Tooltip("SnowRight ParticleSystem.")]
        [SerializeField] private ParticleSystem snowRight;

        [Tooltip("Soft UI glow placed behind the timer countdown.")]
        [SerializeField] private GameObject timerFreezeGlow;

        [SerializeField] private CanvasGroup timerFreezeGlowCanvasGroup;

        [Header("Freeze Slider Presentation")]
        [Tooltip("Optional UI Slider that shows the remaining/elapsed freeze duration.")]
        [SerializeField] private Slider freezeSlider;

        [Tooltip("Optional UI Image with Image Type: Filled that shows the freeze duration.")]
        [SerializeField] private Image freezeFillImage;

        [Tooltip("Optional CanvasGroup on the freeze slider root for smooth fade-in and fade-out.")]
        [SerializeField] private CanvasGroup freezeSliderCanvasGroup;

        [Tooltip("If true, slider starts full (1.0) and drains to 0.0 as freeze time passes. If false, fills from 0 to 1.")]
        [SerializeField] private bool freezeSliderDrains = true;
        [Tooltip("UI-canvas pixel size of the soft glow behind the countdown. Set to zero to preserve the RectTransform's authored size.")]
        [SerializeField] private Vector2 timerFreezeGlowSize = new Vector2(420f, 190f);
        [Min(.01f)] [SerializeField] private float timerFreezeGlowFadeInDuration = .25f;
        [Min(.01f)] [SerializeField] private float timerFreezeGlowFadeOutDuration = .35f;
        [SerializeField] private Ease timerFreezeGlowFadeInEase = Ease.OutSine;
        [SerializeField] private Ease timerFreezeGlowFadeOutEase = Ease.InSine;

        [Header("Clock Freeze Visual Animation")]
        [Tooltip("Base clock UI Image.")]
        [SerializeField] private Image baseClockImage;

        [Tooltip("Frozen clock overlay UI Image set to Image Type: Filled.")]
        [SerializeField] private Image frozenClockImage;

        [SerializeField, Tooltip("360 degree radial fill duration in seconds (increase value to make fill slower).")]
        private float freezeFillDuration = 1.2f;

        [SerializeField, Tooltip("Easing style for 360 degree radial fill animation.")]
        private Ease fillEase = Ease.Linear;

        [Header("Animation Speed & Easing Settings")]
        [SerializeField, Tooltip("Duration in seconds for timer_obj to travel from bottom off-screen to center.")]
        private float entranceDuration = 0.85f;

        [SerializeField, Tooltip("Pause duration in seconds at the pause position.")]
        private float centerPauseDuration = 1.0f;

        [SerializeField, Tooltip("Vertical Y offset from screen center for pause position (e.g. 0 = exact center, -150 = lower).")]
        private float centerOffsetY = 0f;

        [SerializeField, Tooltip("Duration in seconds to fly from pause position to timer_txt (matches entranceDuration).")]
        private float flyToTextDuration = 0.85f;

        [Header("Timer Freeze Settings")]
        [SerializeField, Tooltip("How many real-time seconds the countdown remains frozen after the animation completes.")]
        private float freezeDuration = 8.0f;

        private Vector3 originalScale = Vector3.one;
        private Sequence activeSequence;
        private Coroutine freezeRoutine;
        // The timer must stop on the button press, rather than after the
        // travelling-clock presentation has reached the countdown.
        private PrototypeBoard sequencePausedBoard;
        // Presentation has an explicit owner and lifetime. It must not disappear
        // merely because an optional legacy FreezeTimerBooster is unavailable.
        private Coroutine freezePresentationRoutine;
        private Tween urgencyFadeTween;
        private Tween timerFreezeGlowTween;
        private Sequence timerImpactSequence;
        private FreezeTimerBooster activeFreezeBooster;
        private ParticleSystemRenderer timerImpactParticlesRenderer;
        private ParticleSystemRenderer freezeSnowParticlesRenderer;
        private RectTransform blueParticleVfxRect;
        private ParticleSystemRenderer blueParticleVfxRenderer;
        private Canvas blueParticleVfxCanvas;
        // Cached renderers for FreezeFXRoot impact shards and ambient snow
        private ParticleSystemRenderer _impactShardLongRenderer;
        private ParticleSystemRenderer _impactShardDiamondRenderer;
        private ParticleSystemRenderer _impactShardChunkRenderer;
        private ParticleSystemRenderer _snowTopRenderer;
        private ParticleSystemRenderer _snowBottomRenderer;
        private ParticleSystemRenderer _snowLeftRenderer;
        private ParticleSystemRenderer _snowRightRenderer;

        // Freeze FX sequences and looping pulse tween
        private Sequence _freezeFXSequence;
        private Sequence _freezeCleanupSequence;
        private Tween _timerGlowPulseTween;
        private Tween _vaporTween;
        private Coroutine _sliderRoutine;
        private Tween _sliderFadeTween;

        private float EntranceDuration => tweenConfig != null
            ? tweenConfig.TimerEntranceDuration
            : entranceDuration;

        private Ease EntranceEase => tweenConfig != null
            ? tweenConfig.TimerEntranceEase
            : Ease.OutCubic;

        private float FreezeFillDuration => tweenConfig != null
            ? tweenConfig.TimerFreezeFillDuration
            : freezeFillDuration;

        private Ease FreezeFillEase => tweenConfig != null
            ? tweenConfig.TimerFreezeFillEase
            : fillEase;

        private float FlyToTargetDuration => tweenConfig != null
            ? tweenConfig.TimerFlyToTargetDuration
            : flyToTextDuration;

        private Ease FlyToTargetEase => tweenConfig != null
            ? tweenConfig.TimerFlyToTargetEase
            : Ease.InQuad;

        private float CenterPauseDuration => tweenConfig != null
            ? tweenConfig.TimerCenterPauseDuration
            : centerPauseDuration;

        private float UrgencyFadeInDuration => tweenConfig != null
            ? tweenConfig.TimerUrgencyFadeInDuration
            : urgencyFadeInDuration;

        private Ease UrgencyFadeInEase => tweenConfig != null
            ? tweenConfig.TimerUrgencyFadeInEase
            : urgencyFadeInEase;

        private float UrgencyFadeOutDuration => tweenConfig != null
            ? tweenConfig.TimerUrgencyFadeOutDuration
            : urgencyFadeOutDuration;

        private Ease UrgencyFadeOutEase => tweenConfig != null
            ? tweenConfig.TimerUrgencyFadeOutEase
            : urgencyFadeOutEase;

        private float ImpactDuration => tweenConfig != null
            ? tweenConfig.TimerImpactDuration
            : timerImpactDuration;

        private Ease ImpactScaleEase => tweenConfig != null
            ? tweenConfig.TimerImpactScaleEase
            : timerImpactScaleEase;

        private float ImpactStartScale => tweenConfig != null
            ? tweenConfig.TimerImpactStartScale
            : .22f;

        private float ImpactFadeInFraction => tweenConfig != null
            ? tweenConfig.TimerImpactFadeInFraction
            : .22f;

        private float ImpactScaleFraction => tweenConfig != null
            ? tweenConfig.TimerImpactScaleFraction
            : .55f;

        private float ImpactFadeOutFraction => tweenConfig != null
            ? tweenConfig.TimerImpactFadeOutFraction
            : .45f;

        private float FreezeGlowFadeInDuration => tweenConfig != null
            ? tweenConfig.TimerFreezeGlowFadeInDuration
            : timerFreezeGlowFadeInDuration;

        private Ease FreezeGlowFadeInEase => tweenConfig != null
            ? tweenConfig.TimerFreezeGlowFadeInEase
            : timerFreezeGlowFadeInEase;

        private float FreezeGlowFadeOutDuration => tweenConfig != null
            ? tweenConfig.TimerFreezeGlowFadeOutDuration
            : timerFreezeGlowFadeOutDuration;

        private Ease FreezeGlowFadeOutEase => tweenConfig != null
            ? tweenConfig.TimerFreezeGlowFadeOutEase
            : timerFreezeGlowFadeOutEase;

        // Freeze FX presentation timing shortcuts
        private float FlightScaleTarget       => tweenConfig != null ? tweenConfig.TimerFlightScaleTarget       : 0.60f;
        private float FreezeImpactStartSc     => tweenConfig != null ? tweenConfig.FreezeImpactStartScale       : 0.40f;
        private float FreezeImpactPeakSc      => tweenConfig != null ? tweenConfig.FreezeImpactPeakScale        : 1.40f;
        private float FreezeGlowPulseDur      => tweenConfig != null ? tweenConfig.FreezeGlowPulseDuration      : 1.50f;
        private float FreezeGlowPulseMinA     => tweenConfig != null ? tweenConfig.FreezeGlowPulseMinAlpha      : 0.65f;
        private float FreezeGlowPulseMaxA     => tweenConfig != null ? tweenConfig.FreezeGlowPulseMaxAlpha      : 0.85f;
        private float FreezeIndicatorFadeDur  => tweenConfig != null ? tweenConfig.FreezeIndicatorFadeDuration  : 0.25f;
        private float FreezeAtmoFadeInDur     => tweenConfig != null ? tweenConfig.FreezeAtmoFadeInDuration     : 0.28f;
        private float FreezeAtmoFadeOutDur    => tweenConfig != null ? tweenConfig.FreezeAtmoFadeOutDuration    : 0.70f;

        private void Awake()
        {
            if (boosterButtonRef == null)
            {
                boosterButtonRef = boosterButton != null
                    ? boosterButton.GetComponent<BoosterButton>()
                    : GetComponent<BoosterButton>();
            }

            if (timer_obj != null)
            {
                if (timerPresentationCanvas == null)
                    timerPresentationCanvas = timer_obj.GetComponent<Canvas>();

                originalScale = timer_obj.transform.localScale;
                if (originalScale.sqrMagnitude < 0.0001f)
                {
                    originalScale = Vector3.one;
                }

                timer_obj.SetActive(false);
            }

            if (frozenClockImage != null)
            {
                frozenClockImage.fillAmount = 0f;
            }

            if (freezeTimerBooster == null)
            {
                freezeTimerBooster = GetComponent<FreezeTimerBooster>();
            }

            if (timerUrgencyPresentation != null && timerUrgencyCanvasGroup == null)
                timerUrgencyCanvasGroup = timerUrgencyPresentation.GetComponent<CanvasGroup>();

            if (timerFreezeGlow != null && timerFreezeGlowCanvasGroup == null)
                timerFreezeGlowCanvasGroup = timerFreezeGlow.GetComponent<CanvasGroup>();

            if (timerImpactParticles != null)
                timerImpactParticlesRenderer = timerImpactParticles.GetComponent<ParticleSystemRenderer>();

            if (blueParticleVFX != null)
            {
                blueParticleVfxRect = blueParticleVFX.GetComponent<RectTransform>();
                blueParticleVfxRenderer = blueParticleVFX.GetComponent<ParticleSystemRenderer>();
                blueParticleVfxCanvas = blueParticleVFX.GetComponent<Canvas>();
            }

            if (timerImpactImage != null && timerImpactCanvasGroup == null)
                timerImpactCanvasGroup = timerImpactImage.GetComponent<CanvasGroup>();

            if (freezeSnowParticles != null)
                freezeSnowParticlesRenderer = freezeSnowParticles.GetComponent<ParticleSystemRenderer>();

            // Cache impact shard and ambient snow renderers for sorting
            if (impactShardLong    != null) _impactShardLongRenderer    = impactShardLong.GetComponent<ParticleSystemRenderer>();
            if (impactShardDiamond != null) _impactShardDiamondRenderer = impactShardDiamond.GetComponent<ParticleSystemRenderer>();
            if (impactShardChunk   != null) _impactShardChunkRenderer   = impactShardChunk.GetComponent<ParticleSystemRenderer>();
            if (snowTop    != null) _snowTopRenderer    = snowTop.GetComponent<ParticleSystemRenderer>();
            if (snowBottom != null) _snowBottomRenderer = snowBottom.GetComponent<ParticleSystemRenderer>();
            if (snowLeft   != null) _snowLeftRenderer   = snowLeft.GetComponent<ParticleSystemRenderer>();
            if (snowRight  != null) _snowRightRenderer  = snowRight.GetComponent<ParticleSystemRenderer>();

            ApplyFreezeVfxSorting();

            SetUrgencyPresentationVisible(false, true);
            SetTimerFreezeGlowVisible(false, true);
            SetTimerImpactImageVisible(false);

            ResetFreezeFXState();
        }

        private void Start()
        {
            if (presentationCamera == null)
                presentationCamera = PrototypeBootstrap.SceneCamera;

            if (presentationCamera == null)
                Debug.LogError("[TimerBooster] No gameplay camera is configured on Runtime Piece Factory Bootstrap.", this);

            GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
            if (level != null)
                GetTimerBoosterButton()?.ConfigureLevelUseCount(level.timerBoosterCount);
        }

        private void OnEnable()
        {
            if (boosterButton != null)
            {
                if (freezeTimerBooster != null)
                {
                    boosterButton.onClick.RemoveListener(freezeTimerBooster.ActivateFreezeBooster);
                }
                boosterButton.onClick.RemoveListener(PlayTimerBoosterSequence);
                boosterButton.onClick.AddListener(PlayTimerBoosterSequence);
            }
        }

        private void OnDisable()
        {
            if (boosterButton != null)
            {
                boosterButton.onClick.RemoveListener(PlayTimerBoosterSequence);
            }

            if (activeSequence != null && activeSequence.IsActive())
            {
                activeSequence.Kill();
            }

            if (freezeRoutine != null)
            {
                StopCoroutine(freezeRoutine);
                freezeRoutine = null;
            }

            ReleaseSequenceTimerPause();

            if (freezePresentationRoutine != null)
            {
                StopCoroutine(freezePresentationRoutine);
                freezePresentationRoutine = null;
            }

            UnsubscribeFromFreezeCompletion();
            StopFreezeAmbientPresentation(true);
            timerImpactSequence?.Kill();
            SetTimerImpactImageVisible(false);

            // Kill Freeze FX sequences and pulse loop; particles are stopped by StopFreezeAmbientPresentation
            _freezeFXSequence?.Kill();
            _freezeCleanupSequence?.Kill();
            KillTimerGlowPulse();
            KillVaporTween(immediate: true);
            StopFreezeSlider(immediate: true);
            StopNewAmbientSnow(true);
            StopNewImpactParticles();

            if (frozenClockImage != null)
            {
                frozenClockImage.fillAmount = 0f;
            }

            SetUrgencyPresentationVisible(false, true);
        }

        private void Update()
        {
            RefreshButtonInteractable();
        }

        private void RefreshButtonInteractable()
        {
            if (boosterButton == null)
                return;

            if (freezeTimerBooster != null)
                return;

            PrototypeBoard board = PrototypeBoard.Active;
            bool isSequenceRunning = activeSequence != null && activeSequence.IsActive();
            bool isFreezeActive = activeFreezeBooster != null && activeFreezeBooster.IsFreezeActive;

            boosterButton.interactable =
                board != null &&
                board.IsTimerActive &&
                board.IsTimerStarted &&
                board.TimeRemaining > 0f &&
                !LevelTimerUI.IsGameOver &&
                !isSequenceRunning &&
                !isFreezeActive;
        }

        /// <summary>
        /// Public entry point to trigger the Timer Booster animation sequence.
        /// </summary>
        public void PlayTimerBoosterSequence()
        {
            PrototypeBoard activeBoard = PrototypeBoard.Active;
            if (activeBoard == null || !activeBoard.IsTimerActive || !activeBoard.IsTimerStarted || activeBoard.TimeRemaining <= 0f || LevelTimerUI.IsGameOver)
            {
                Debug.LogWarning("[TimerBooster] Cannot play sequence: timer has not started or is inactive.");
                return;
            }

            if (timer_obj == null || timer_txt == null)
            {
                Debug.LogWarning("[TimerBooster] Cannot play sequence: timer_obj or timer_txt reference is missing in Inspector.");
                return;
            }

            // Reserve an owner-specific pause before starting presentation.
            // The animation can take several seconds, and it must not consume
            // any of the player's remaining level time.
            if (!activeBoard.TryPauseTimer(this))
            {
                Debug.LogWarning("[TimerBooster] Cannot play sequence: timer could not be paused.");
                return;
            }

            sequencePausedBoard = activeBoard;

            if (activeSequence != null && activeSequence.IsActive())
            {
                activeSequence.Kill();
            }

            Canvas.ForceUpdateCanvases();

            // Bring timer_obj to front of Canvas hierarchy so it is rendered on top of all panels
            timer_obj.transform.SetAsLastSibling();

            // The presentation Canvas is authored on the timer prefab/scene object. Gameplay must never add
            // UI components at runtime: the visual can still use its parent Canvas when no dedicated overlay is needed.
            if (timerPresentationCanvas != null)
            {
                timerPresentationCanvas.overrideSorting = true;
                timerPresentationCanvas.sortingOrder = GetSafeTimerPresentationSortingOrder();
            }

            // Keep SpriteRenderer-based clock variants above board presentation.
            SpriteRenderer[] srs = timer_obj.GetComponentsInChildren<SpriteRenderer>(true);
            foreach (var sr in srs)
            {
                if (sr != null)
                {
                    sr.sortingOrder = GetSafeTimerPresentationSortingOrder();
                }
            }

            if (originalScale.sqrMagnitude < 0.0001f)
            {
                originalScale = Vector3.one;
            }

            // Ensure timer_obj and ALL its child Images (like baseClockImage) are fully active, enabled, and visible (opacity = 1)
            timer_obj.SetActive(true);

            Image[] allImages = timer_obj.GetComponentsInChildren<Image>(true);
            foreach (var img in allImages)
            {
                if (img == null) continue;
                img.gameObject.SetActive(true);
                img.enabled = true;

                // Restore alpha opacity if transparent
                Color c = img.color;
                if (c.a < 0.05f)
                {
                    c.a = 1f;
                    img.color = c;
                }

                // Make sure non-frozen base images are 100% filled and visible
                if (img != frozenClockImage && img.type == Image.Type.Filled)
                {
                    img.fillAmount = 1f;
                }
            }

            // Reset only frozenClockImage overlay to fillAmount = 0
            if (frozenClockImage != null)
            {
                frozenClockImage.type = Image.Type.Filled;
                frozenClockImage.fillAmount = 0f;
                frozenClockImage.gameObject.SetActive(true);
            }

            Camera cam = presentationCamera;
            Vector3 centerPos = Vector3.zero;
            float offscreenOffsetY = 8f;

            RectTransform rectTransform = timer_obj.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                // UI Canvas Space: Screen Center at (0, centerOffsetY)
                Canvas canvas = timer_obj.GetComponentInParent<Canvas>();
                float canvasHeight = canvas != null ? canvas.GetComponent<RectTransform>().rect.height : Screen.height;
                if (canvasHeight <= 0) canvasHeight = 1920f;

                offscreenOffsetY = canvasHeight * 0.7f;
                centerPos = new Vector3(0f, centerOffsetY, 0f);
            }
            else
            {
                // World Space: Screen Center at (0, centerOffsetY)
                float worldOffsetY = centerOffsetY * 0.01f;
                if (cam != null)
                {
                    centerPos = new Vector3(0f, cam.transform.position.y + worldOffsetY, 0f);
                    offscreenOffsetY = cam.orthographicSize + 4f;
                }
                else
                {
                    centerPos = new Vector3(0f, worldOffsetY, 0f);
                    offscreenOffsetY = 10f;
                }
            }

            Vector3 startPos = centerPos + new Vector3(0f, -offscreenOffsetY, 0f);

            // Step 1: Initial Reset (Reset rotation & scale)
            timer_obj.transform.rotation = Quaternion.identity;
            timer_obj.transform.localScale = originalScale;
            if (rectTransform != null)
            {
                rectTransform.anchoredPosition = startPos;
            }
            else
            {
                timer_obj.transform.position = startPos;
            }

            Sequence seq = DOTween.Sequence()
                .SetLink(timer_obj, LinkBehaviour.KillOnDisable)
                .SetAutoKill(true);
            activeSequence = seq;

            // Step 1: Move from bottom off-screen to screen center position smoothly
            if (rectTransform != null)
            {
                seq.Append(rectTransform.DOAnchorPos(centerPos, EntranceDuration).SetEase(EntranceEase));
            }
            else
            {
                seq.Append(timer_obj.transform.DOMove(centerPos, EntranceDuration).SetEase(EntranceEase));
            }

            // Step 2: Pause at center & animate 360 degree radial fill over freezeFillDuration
            if (frozenClockImage != null)
            {
                frozenClockImage.type = Image.Type.Filled;
                seq.Append(frozenClockImage.DOFillAmount(1f, FreezeFillDuration).SetEase(FreezeFillEase));
                float remainingPause = Mathf.Max(0f, CenterPauseDuration - FreezeFillDuration);
                if (remainingPause > 0f)
                {
                    seq.AppendInterval(remainingPause);
                }
            }
            else
            {
                seq.AppendInterval(CenterPauseDuration);
            }

            // Step 3: Fly to timer_txt; simultaneously shrink scale for a receding look
            seq.Append(timer_obj.transform.DOMove(timer_txt.position, FlyToTargetDuration).SetEase(FlyToTargetEase));
            seq.Join(timer_obj.transform
                .DOScale(originalScale * FlightScaleTarget, FlyToTargetDuration)
                .SetEase(FlyToTargetEase));

            // Step 4: Arrival Event (Particle Burst + Deactivate)
            seq.AppendCallback(() =>
            {
                // The visual freeze begins at the exact impact moment, not on
                // button press. This keeps the vignette synchronized with the
                // clock reaching the timer display.
                SetUrgencyPresentationVisible(true, false);
                PlayTimerImpactParticles();
                StartFreezeAmbientPresentation();
                BeginFreezePresentationLifetime();
                PlayFreezeFXImpact();          // FAZ 4+5: new FreezeFXRoot sequence

                // FreezeSnowParticles used to be assigned to this legacy impact
                // slot as well. Do not move/restart the ambient snow as a burst.
                if (blueParticleVFX != null &&
                    blueParticleVFX != freezeSnowParticles &&
                    blueParticleVFX != timerImpactParticles)
                {
                    if (!blueParticleVFX.gameObject.scene.IsValid())
                    {
                        Debug.LogWarning("[TimerBooster] Blue Particle VFX must reference a scene-authored ParticleSystem; prefab assets are not instantiated at runtime.", this);
                    }
                    else
                    {
                        if (blueParticleVfxRect != null)
                            blueParticleVfxRect.anchoredPosition = GetTargetAnchoredPosition(blueParticleVfxRect, timer_txt);
                        else
                            blueParticleVFX.transform.position = GetTargetWorldPosition(timer_txt, timer_obj.transform);

                        if (blueParticleVFX.transform.localScale.sqrMagnitude < 0.0001f)
                            blueParticleVFX.transform.localScale = Vector3.one;

                        blueParticleVFX.gameObject.SetActive(true);
                        if (blueParticleVfxRenderer != null)
                            blueParticleVfxRenderer.sortingOrder = GetSafeTimerPresentationSortingOrder();
                        if (blueParticleVfxCanvas != null)
                        {
                            blueParticleVfxCanvas.overrideSorting = true;
                            blueParticleVfxCanvas.sortingOrder = GetSafeTimerPresentationSortingOrder();
                        }

                        blueParticleVFX.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                        blueParticleVFX.Play(true);
                    }
                }
            });

            seq.OnComplete(() =>
            {
                timer_obj.SetActive(false);
                if (frozenClockImage != null)
                {
                    frozenClockImage.fillAmount = 0f;
                }
                OnSequenceCompleted();
            });
        }

        private Vector3 GetTargetWorldPosition(Transform target, Transform mover)
        {
            if (target == null) return Vector3.zero;

            Canvas targetCanvas = target.GetComponentInParent<Canvas>();
            Camera cam = presentationCamera;

            if (targetCanvas != null && targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay && cam != null)
            {
                Vector3 screenPos = target.position;
                screenPos.z = Mathf.Abs(mover.position.z - cam.transform.position.z);
                if (screenPos.z < 0.1f) screenPos.z = 10f;
                return cam.ScreenToWorldPoint(screenPos);
            }

            return target.position;
        }

        private Vector3 GetTargetAnchoredPosition(RectTransform mover, Transform target)
        {
            if (mover == null || target == null)
                return Vector3.zero;

            RectTransform parentRect = mover.parent as RectTransform;
            if (parentRect == null)
                return mover.anchoredPosition;

            Canvas moverCanvas = mover.GetComponentInParent<Canvas>();
            Camera moverCam = (moverCanvas != null && moverCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
                ? moverCanvas.worldCamera
                : null;

            Canvas targetCanvas = target.GetComponentInParent<Canvas>();
            Camera targetCam = (targetCanvas != null && targetCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
                ? targetCanvas.worldCamera
                : null;

            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(targetCam, target.position);

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parentRect, screenPoint, moverCam, out Vector2 localPoint))
                return mover.anchoredPosition;

            Vector2 anchorCenter = (mover.anchorMin + mover.anchorMax) * 0.5f;
            Rect pRect = parentRect.rect;
            Vector2 anchorRefPos = new Vector2(
                Mathf.Lerp(pRect.xMin, pRect.xMax, anchorCenter.x),
                Mathf.Lerp(pRect.yMin, pRect.yMax, anchorCenter.y));

            return localPoint - anchorRefPos;
        }

        private void OnSequenceCompleted()
        {
            GetTimerBoosterButton()?.TryConsumeUse();

            float effectiveDuration = GetEffectiveFreezeDuration();

            // Freeze timer for authoritative duration
            FreezeTimerBooster freeze = freezeTimerBooster;
            if (freeze != null)
            {
                freeze.freezeDuration = effectiveDuration;
                SubscribeToFreezeCompletion(freeze);
                freeze.ActivateFreezeBooster();
                if (freeze.IsFreezeActive)
                {
                    // FreezeTimerBooster now owns the post-impact pause. Drop
                    // this sequence's owner without creating a timer gap.
                    ReleaseSequenceTimerPause();
                    // Begin visual progression only after the authoritative
                    // pause has actually started, so both windows share the
                    // same first frame and duration.
                    StartFreezeSlider();
                }
                else
                {
                    UnsubscribeFromFreezeCompletion();
                    ReleaseSequenceTimerPause();
                }
            }
            else
            {
                PrototypeBoard activeBoard = PrototypeBoard.Active;
                if (activeBoard != null)
                {
                    if (freezeRoutine != null) StopCoroutine(freezeRoutine);
                    freezeRoutine = StartCoroutine(FreezeTimerRoutine(activeBoard, effectiveDuration));
                    StartFreezeSlider();
                }
            }
        }

        private BoosterButton GetTimerBoosterButton()
        {
            return boosterButtonRef;
        }

        private IEnumerator FreezeTimerRoutine(PrototypeBoard targetBoard, float duration)
        {
            if (targetBoard == null || targetBoard != sequencePausedBoard)
            {
                SetUrgencyPresentationVisible(false, false);
                StopFreezeAmbientPresentation(false);
                yield break;
            }

            Debug.Log($"[TimerBooster] Timer frozen for {duration} seconds!");

            float elapsed = 0f;
            while (elapsed < duration && targetBoard != null &&
                   targetBoard == PrototypeBoard.Active &&
                   targetBoard.IsTimerActive && !LevelTimerUI.IsGameOver)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (targetBoard != null)
            {
                ReleaseSequenceTimerPause();
                Debug.Log("[TimerBooster] Timer resumed after 8s freeze!");
            }

            freezeRoutine = null;
            PlayFreezeExpirationSequence();    // FAZ 7: staggered new FX cleanup
            SetUrgencyPresentationVisible(false, false);
            StopFreezeAmbientPresentation(false);
        }

        private void ReleaseSequenceTimerPause()
        {
            if (sequencePausedBoard == null)
                return;

            sequencePausedBoard.ResumeTimer(this);
            sequencePausedBoard = null;
        }

        private void PlayTimerImpactParticles()
        {
            PlayTimerImpactImage();

            if (timerImpactParticles == null)
                return;

            ApplyFreezeVfxSorting();

            EnsureParticleSystemHierarchyVisible(timerImpactParticles);

            if (timer_txt != null)
                timerImpactParticles.transform.position = timer_txt.position;

            timerImpactParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            timerImpactParticles.Play(true);
        }

        private void BeginFreezePresentationLifetime()
        {
            if (freezePresentationRoutine != null)
                StopCoroutine(freezePresentationRoutine);

            freezePresentationRoutine = StartCoroutine(FreezePresentationLifetime());
        }

        private IEnumerator FreezePresentationLifetime()
        {
            yield return new WaitForSecondsRealtime(GetEffectiveFreezeDuration());
            freezePresentationRoutine = null;
            SetUrgencyPresentationVisible(false, false);
            StopFreezeAmbientPresentation(false);
        }

        private void PlayTimerImpactImage()
        {
            if (timerImpactImage == null || timerImpactCanvasGroup == null)
                return;

            RectTransform impactRect = timerImpactImage.rectTransform;
            if (timer_txt != null)
                impactRect.anchoredPosition = GetTargetAnchoredPosition(impactRect, timer_txt);

            if (timerImpactImageSize.x > 0f && timerImpactImageSize.y > 0f)
                impactRect.sizeDelta = timerImpactImageSize;

            for (Transform current = timerImpactImage.transform;
                 current != null;
                 current = current.parent)
            {
                if (!current.gameObject.activeSelf)
                    current.gameObject.SetActive(true);
            }

            timerImpactSequence?.Kill();
            timerImpactCanvasGroup.alpha = 0f;
            impactRect.localScale = Vector3.one * ImpactStartScale;
            timerImpactSequence = DOTween.Sequence()
                .SetLink(timerImpactImage.gameObject, LinkBehaviour.KillOnDisable)
                .SetAutoKill(true);
            timerImpactSequence.Append(timerImpactCanvasGroup.DOFade(1f, ImpactDuration * ImpactFadeInFraction));
            timerImpactSequence.Join(impactRect.DOScale(1f, ImpactDuration * ImpactScaleFraction).SetEase(ImpactScaleEase));
            timerImpactSequence.Append(timerImpactCanvasGroup.DOFade(0f, ImpactDuration * ImpactFadeOutFraction));
            timerImpactSequence.OnComplete(() => SetTimerImpactImageVisible(false));
        }

        private void SetTimerImpactImageVisible(bool visible)
        {
            if (timerImpactImage == null)
                return;

            if (timerImpactCanvasGroup != null)
                timerImpactCanvasGroup.alpha = visible ? 1f : 0f;

            if (timerImpactImage.gameObject.activeSelf != visible)
                timerImpactImage.gameObject.SetActive(visible);
        }

        private void StartFreezeAmbientPresentation()
        {
            ApplyFreezeVfxSorting();

            if (freezeSnowParticles != null)
            {
                EnsureParticleSystemHierarchyVisible(freezeSnowParticles);
                freezeSnowParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                freezeSnowParticles.Play(true);
            }

            SetTimerFreezeGlowVisible(true, false);
        }

        private void StopFreezeAmbientPresentation(bool immediate)
        {
            if (freezeSnowParticles != null)
            {
                freezeSnowParticles.Stop(true, immediate
                    ? ParticleSystemStopBehavior.StopEmittingAndClear
                    : ParticleSystemStopBehavior.StopEmitting);
            }

            SetTimerFreezeGlowVisible(false, immediate);
        }

        private void ApplyFreezeVfxSorting()
        {
            if (timerImpactParticlesRenderer != null)
                timerImpactParticlesRenderer.sortingOrder = freezeVfxSortingOrder;

            if (freezeSnowParticlesRenderer != null)
                freezeSnowParticlesRenderer.sortingOrder = freezeVfxSortingOrder;

            // FreezeFXRoot impact shards
            if (_impactShardLongRenderer    != null) _impactShardLongRenderer.sortingOrder    = freezeVfxSortingOrder;
            if (_impactShardDiamondRenderer != null) _impactShardDiamondRenderer.sortingOrder = freezeVfxSortingOrder;
            if (_impactShardChunkRenderer   != null) _impactShardChunkRenderer.sortingOrder   = freezeVfxSortingOrder;

            // Ambient snow
            if (_snowTopRenderer    != null) _snowTopRenderer.sortingOrder    = freezeVfxSortingOrder;
            if (_snowBottomRenderer != null) _snowBottomRenderer.sortingOrder = freezeVfxSortingOrder;
            if (_snowLeftRenderer   != null) _snowLeftRenderer.sortingOrder   = freezeVfxSortingOrder;
            if (_snowRightRenderer  != null) _snowRightRenderer.sortingOrder  = freezeVfxSortingOrder;
        }

        private void SubscribeToFreezeCompletion(FreezeTimerBooster freeze)
        {
            if (activeFreezeBooster == freeze)
                return;

            UnsubscribeFromFreezeCompletion();
            activeFreezeBooster = freeze;
            activeFreezeBooster.FreezeEnded += HandleFreezeEnded;
            activeFreezeBooster.FreezeProgressChanged += HandleFreezeProgressChanged;
        }

        private void UnsubscribeFromFreezeCompletion()
        {
            if (activeFreezeBooster == null)
                return;

            activeFreezeBooster.FreezeEnded -= HandleFreezeEnded;
            activeFreezeBooster.FreezeProgressChanged -= HandleFreezeProgressChanged;
            activeFreezeBooster = null;
        }

        private void HandleFreezeProgressChanged(FreezeTimerBooster source, float normalizedProgress)
        {
            if (source != activeFreezeBooster)
                return;

            SetFreezeSliderValue(normalizedProgress);
        }

        private void HandleFreezeEnded(FreezeTimerBooster source)
        {
            if (source != activeFreezeBooster)
                return;

            if (freezePresentationRoutine != null)
            {
                StopCoroutine(freezePresentationRoutine);
                freezePresentationRoutine = null;
            }

            SetUrgencyPresentationVisible(false, false);
            PlayFreezeExpirationSequence();    // FAZ 7: staggered new FX cleanup
            StopFreezeAmbientPresentation(false);
            UnsubscribeFromFreezeCompletion();
        }

        private static void EnsureParticleSystemHierarchyVisible(ParticleSystem particleSystem)
        {
            for (Transform current = particleSystem.transform;
                 current != null;
                 current = current.parent)
            {
                if (!current.gameObject.activeSelf)
                    current.gameObject.SetActive(true);
            }
        }

        private void SetTimerFreezeGlowVisible(bool visible, bool immediate)
        {
            if (timerFreezeGlow == null)
                return;

            timerFreezeGlowTween?.Kill();

            if (visible)
            {
                RectTransform glowRect = timerFreezeGlow.transform as RectTransform;
                if (timer_txt != null)
                {
                    if (glowRect != null)
                    {
                        glowRect.anchoredPosition =
                            (Vector2)GetTargetAnchoredPosition(glowRect, timer_txt) + freezeTimerGlowOffset;

                        if (timerFreezeGlowSize.x > 0f && timerFreezeGlowSize.y > 0f)
                            glowRect.sizeDelta = timerFreezeGlowSize;
                    }
                    else
                    {
                        timerFreezeGlow.transform.position = timer_txt.position;
                    }
                }

                for (Transform current = timerFreezeGlow.transform;
                     current != null;
                     current = current.parent)
                {
                    if (!current.gameObject.activeSelf)
                        current.gameObject.SetActive(true);
                }

                if (timerFreezeGlowCanvasGroup == null)
                    return;

                timerFreezeGlowCanvasGroup.alpha = 0f;
                timerFreezeGlowTween = timerFreezeGlowCanvasGroup
                    .DOFade(1f, FreezeGlowFadeInDuration)
                    .SetEase(FreezeGlowFadeInEase)
                    .SetLink(timerFreezeGlow, LinkBehaviour.KillOnDisable)
                    .SetAutoKill(true);
                return;
            }

            if (immediate || timerFreezeGlowCanvasGroup == null)
            {
                if (timerFreezeGlowCanvasGroup != null)
                    timerFreezeGlowCanvasGroup.alpha = 0f;

                if (timerFreezeGlow.activeSelf)
                    timerFreezeGlow.SetActive(false);

                return;
            }

            timerFreezeGlowTween = timerFreezeGlowCanvasGroup
                .DOFade(0f, FreezeGlowFadeOutDuration)
                .SetEase(FreezeGlowFadeOutEase)
                .SetLink(timerFreezeGlow, LinkBehaviour.KillOnDisable)
                .SetAutoKill(true)
                .OnComplete(() =>
                {
                    if (timerFreezeGlow != null)
                        timerFreezeGlow.SetActive(false);
                });
        }

        private void SetUrgencyPresentationVisible(bool visible, bool immediate)
        {
            if (timerUrgencyPresentation == null)
                return;

            urgencyFadeTween?.Kill();

            if (visible)
            {
                // The assigned reference is normally the inactive Timer_effect
                // root.  Also restore inactive ancestors so this works when a
                // nested vignette object is assigned instead.
                for (Transform current = timerUrgencyPresentation.transform;
                     current != null;
                     current = current.parent)
                {
                    if (!current.gameObject.activeSelf)
                        current.gameObject.SetActive(true);
                }

                if (timerUrgencyCanvasGroup == null)
                    return;

                timerUrgencyCanvasGroup.alpha = 0f;
                urgencyFadeTween = timerUrgencyCanvasGroup
                    .DOFade(1f, UrgencyFadeInDuration)
                    .SetEase(UrgencyFadeInEase)
                    .SetLink(timerUrgencyPresentation, LinkBehaviour.KillOnDisable)
                    .SetAutoKill(true);

                return;
            }

            if (immediate || timerUrgencyCanvasGroup == null)
            {
                if (timerUrgencyCanvasGroup != null)
                    timerUrgencyCanvasGroup.alpha = 0f;

                if (timerUrgencyPresentation.activeSelf)
                    timerUrgencyPresentation.SetActive(false);

                return;
            }

            urgencyFadeTween = timerUrgencyCanvasGroup
                .DOFade(0f, UrgencyFadeOutDuration)
                .SetEase(UrgencyFadeOutEase)
                .SetLink(timerUrgencyPresentation, LinkBehaviour.KillOnDisable)
                .SetAutoKill(true)
                .OnComplete(() =>
                {
                    if (timerUrgencyPresentation != null)
                        timerUrgencyPresentation.SetActive(false);
                });
        }

        // ── Freeze FX Helpers (FreezeFXRoot) ────────────────────────────────

        private int GetSafeTimerPresentationSortingOrder()
        {
            return Mathf.Clamp(timerPresentationSortingOrder, 0, short.MaxValue);
        }

        /// <summary>
        /// Resets all FreezeFXRoot visual objects to their pre-activation hidden state.
        /// Safe to call from Awake or before re-triggering the sequence.
        /// </summary>
        private void ResetFreezeFXState()
        {
            _freezeFXSequence?.Kill();
            _freezeCleanupSequence?.Kill();
            KillTimerGlowPulse();

            // Impact image
            if (freezeImpactImageCG != null)   freezeImpactImageCG.alpha = 0f;
            if (freezeImpactImageRect != null)  freezeImpactImageRect.localScale = Vector3.one * FreezeImpactStartSc;

            // Vapor UI Image
            KillVaporTween(immediate: true);

            // Freeze Slider
            StopFreezeSlider(immediate: true);

            // Atmosphere
            if (freezeTimerGlow != null)        freezeTimerGlow.alpha = 0f;
            if (freezeFrostBorder != null)      freezeFrostBorder.alpha = 0f;
            if (freezeEdgeGlow != null)         freezeEdgeGlow.alpha = 0f;
            if (freezeDarkVignette != null)     freezeDarkVignette.alpha = 0f;

            // Indicator
            if (freezeTimerIndicator != null)     freezeTimerIndicator.alpha = 0f;
            if (freezeTimerIndicatorRect != null) freezeTimerIndicatorRect.localScale = Vector3.one * 0.8f;

            StopNewAmbientSnow(true);
            StopNewImpactParticles();
        }

        /// <summary>
        /// Builds and plays the FAZ 4 impact + FAZ 5 atmosphere DOTween Sequence for the
        /// FreezeFXRoot hierarchy. Called at the exact moment the clock reaches the timer display.
        /// Uses SetUpdate(true) so it is immune to timeScale changes.
        /// </summary>
        private void PlayFreezeFXImpact()
        {
            _freezeFXSequence?.Kill();

            bool hasAnyFX = freezeImpactImageCG != null || freezeTimerGlow != null ||
                            freezeFrostBorder != null || freezeEdgeGlow != null || freezeDarkVignette != null;
            if (!hasAnyFX)
                return;

            // Align timer-local effects to the actual timer target
            if (timer_txt != null)
            {
                if (freezeTimerGlowRect != null)
                    freezeTimerGlowRect.anchoredPosition =
                        (Vector2)GetTargetAnchoredPosition(freezeTimerGlowRect, timer_txt) + freezeTimerGlowOffset;

                if (freezeTimerIndicatorRect != null)
                    freezeTimerIndicatorRect.anchoredPosition = GetTargetAnchoredPosition(freezeTimerIndicatorRect, timer_txt);

                if (freezeImpactImageRect != null)
                    freezeImpactImageRect.anchoredPosition = GetTargetAnchoredPosition(freezeImpactImageRect, timer_txt);
            }

            // Ensure a clean starting state before building the sequence
            if (freezeImpactImageCG != null)    freezeImpactImageCG.alpha = 0f;
            if (freezeImpactImageRect != null)  freezeImpactImageRect.localScale = Vector3.one * FreezeImpactStartSc;
            if (freezeTimerGlow != null)        freezeTimerGlow.alpha = 0f;
            if (freezeFrostBorder != null)      freezeFrostBorder.alpha = 0f;
            if (freezeEdgeGlow != null)         freezeEdgeGlow.alpha = 0f;
            if (freezeDarkVignette != null)     freezeDarkVignette.alpha = 0f;
            if (freezeTimerIndicator != null)     freezeTimerIndicator.alpha = 0f;
            if (freezeTimerIndicatorRect != null) freezeTimerIndicatorRect.localScale = Vector3.one * 0.8f;

            _freezeFXSequence = DOTween.Sequence()
                .SetUpdate(true)
                .SetLink(gameObject, LinkBehaviour.KillOnDisable)
                .SetAutoKill(true);

            // ── FAZ 4: IMPACT ───────────────────────────────────────────────
            // Phase 1 (0.00 - 0.40s): Burst expansion + fade in
            if (freezeImpactImageCG != null)
                _freezeFXSequence.Insert(0f, freezeImpactImageCG.DOFade(1f, 0.10f).SetEase(Ease.OutQuad));
            if (freezeImpactImageRect != null)
                _freezeFXSequence.Insert(0f, freezeImpactImageRect
                    .DOScale(Vector3.one * 1.25f, 0.40f).SetEase(Ease.OutCubic));

            // t=0.02  Impact shard particles
            _freezeFXSequence.InsertCallback(0.02f, () =>
            {
                impactShardLong?.Play();
                impactShardDiamond?.Play();
                impactShardChunk?.Play();
            });

            // t=0.03  Cold vapor: UI Image DOTween — fade in, expand in place, then fade out
            _freezeFXSequence.InsertCallback(0.03f, () => PlayVaporAnimation());

            // Phase 2 (0.40 - 0.55s): Gentle hold / swell (tam görünür kalır)
            if (freezeImpactImageRect != null)
                _freezeFXSequence.Insert(0.40f, freezeImpactImageRect
                    .DOScale(Vector3.one * 1.32f, 0.15f).SetEase(Ease.InOutSine));

            // Phase 3 (0.55 - 0.95s): Slow expand & soft fade out
            if (freezeImpactImageRect != null)
                _freezeFXSequence.Insert(0.55f, freezeImpactImageRect
                    .DOScale(Vector3.one * 1.45f, 0.40f).SetEase(Ease.OutQuad));
            if (freezeImpactImageCG != null)
                _freezeFXSequence.Insert(0.55f, freezeImpactImageCG
                    .DOFade(0f, 0.40f).SetEase(Ease.OutQuad));

            // ── FAZ 5: ATMOSPHERE (staggered wave) ──────────────────────────
            float atmoIn = FreezeAtmoFadeInDur;

            // t=0.06  FreezeTimerGlow
            if (freezeTimerGlow != null)
                _freezeFXSequence.Insert(0.06f, freezeTimerGlow.DOFade(1f, atmoIn));

            // t=0.10  FreezeFrostBorder
            if (freezeFrostBorder != null)
                _freezeFXSequence.Insert(0.10f, freezeFrostBorder.DOFade(1f, atmoIn));

            // t=0.12  FreezeEdgeGlow
            if (freezeEdgeGlow != null)
                _freezeFXSequence.Insert(0.12f, freezeEdgeGlow.DOFade(1f, atmoIn));

            // t=0.14  FreezeDarkVignette
            if (freezeDarkVignette != null)
                _freezeFXSequence.Insert(0.14f, freezeDarkVignette.DOFade(1f, atmoIn));

            // t=0.18  Ambient snow (looping PSystems)
            _freezeFXSequence.InsertCallback(0.18f, () =>
            {
                snowTop?.Play();
                snowBottom?.Play();
                snowLeft?.Play();
                snowRight?.Play();
            });

            // t=0.24  FreezeTimerIndicator — fully null-safe, OutCubic with scale
            if (freezeTimerIndicator != null)
                _freezeFXSequence.Insert(0.24f, freezeTimerIndicator
                    .DOFade(1f, FreezeIndicatorFadeDur).SetEase(Ease.OutCubic));
            if (freezeTimerIndicatorRect != null)
                _freezeFXSequence.Insert(0.24f, freezeTimerIndicatorRect
                    .DOScale(Vector3.one, FreezeIndicatorFadeDur).SetEase(Ease.OutCubic));

            // When all atmosphere elements have faded in, start the ambient pulse
            _freezeFXSequence.OnComplete(() => StartTimerGlowPulse());
        }

        /// <summary>
        /// Starts the looping TimerGlow alpha + scale pulse that runs throughout the freeze duration.
        /// Uses SetAutoKill(false) so the loop survives sequence completion.
        /// </summary>
        private void StartTimerGlowPulse()
        {
            KillTimerGlowPulse();
            if (freezeTimerGlow == null)
                return;

            float halfDur = FreezeGlowPulseDur * 0.5f;

            Sequence pulseSeq = DOTween.Sequence()
                .SetLoops(-1, LoopType.Restart)
                .SetUpdate(true)
                .SetAutoKill(false)
                .SetLink(gameObject, LinkBehaviour.KillOnDisable);

            // Alpha: min → max → (loop) min
            pulseSeq.Append(freezeTimerGlow.DOFade(FreezeGlowPulseMaxA, halfDur).SetEase(Ease.InOutSine));
            pulseSeq.Append(freezeTimerGlow.DOFade(FreezeGlowPulseMinA, halfDur).SetEase(Ease.InOutSine));

            // Scale pulse joined in the same sequence
            if (freezeTimerGlowRect != null)
            {
                pulseSeq.Insert(0f,      freezeTimerGlowRect.DOScale(Vector3.one * 1.05f, halfDur).SetEase(Ease.InOutSine));
                pulseSeq.Insert(halfDur, freezeTimerGlowRect.DOScale(Vector3.one,         halfDur).SetEase(Ease.InOutSine));
            }

            _timerGlowPulseTween = pulseSeq;
        }

        /// <summary>Kills the TimerGlow pulse loop and resets its scale to Vector3.one.</summary>
        private void KillTimerGlowPulse()
        {
            if (_timerGlowPulseTween != null && _timerGlowPulseTween.IsActive())
                _timerGlowPulseTween.Kill();
            _timerGlowPulseTween = null;

            // Reset scale so the glow doesn't stay mid-pulse after kill
            if (freezeTimerGlowRect != null)
                freezeTimerGlowRect.localScale = Vector3.one;
        }

        /// <summary>
        /// FAZ 7: staggered DOTween cleanup sequence for all FreezeFXRoot objects.
        /// Called when the freeze timer expires (via FreezeTimerBooster.FreezeEnded or FreezeTimerRoutine).
        /// Snow particles are stopped immediately (StopEmitting) so existing particles finish naturally.
        /// </summary>
        private void PlayFreezeExpirationSequence()
        {
            _freezeCleanupSequence?.Kill();

            // Stop snow emitters; existing particles finish their lifetime
            StopNewAmbientSnow(false);

            // Stop freeze slider with a fade out
            StopFreezeSlider(immediate: false);

            bool hasAnyNewFX = freezeTimerGlow != null || freezeFrostBorder != null ||
                               freezeEdgeGlow != null || freezeDarkVignette != null ||
                               freezeTimerIndicator != null;
            if (!hasAnyNewFX)
                return;

            float fadeDur = FreezeAtmoFadeOutDur;

            _freezeCleanupSequence = DOTween.Sequence()
                .SetUpdate(true)
                .SetLink(gameObject, LinkBehaviour.KillOnDisable)
                .SetAutoKill(true);

            // t=0.04  FreezeTimerIndicator fade out
            if (freezeTimerIndicator != null)
                _freezeCleanupSequence.Insert(0.04f, freezeTimerIndicator.DOFade(0f, fadeDur * 0.7f).SetEase(Ease.OutQuad));

            // t=0.08  Kill glow pulse, then fade FreezeTimerGlow out
            _freezeCleanupSequence.InsertCallback(0.08f, KillTimerGlowPulse);
            if (freezeTimerGlow != null)
                _freezeCleanupSequence.Insert(0.08f, freezeTimerGlow.DOFade(0f, fadeDur).SetEase(Ease.OutQuad));

            // t=0.12  FreezeEdgeGlow fade out
            if (freezeEdgeGlow != null)
                _freezeCleanupSequence.Insert(0.12f, freezeEdgeGlow.DOFade(0f, fadeDur).SetEase(Ease.OutQuad));

            // t=0.16  FreezeFrostBorder fade out (köşe buz bordürü)
            if (freezeFrostBorder != null)
                _freezeCleanupSequence.Insert(0.16f, freezeFrostBorder.DOFade(0f, fadeDur).SetEase(Ease.OutQuad));

            // t=0.20  FreezeDarkVignette fade out (karanlık vinyet en son yumuşakça kaybolur)
            if (freezeDarkVignette != null)
                _freezeCleanupSequence.Insert(0.20f, freezeDarkVignette.DOFade(0f, fadeDur + 0.10f).SetEase(Ease.OutQuad));
        }

        /// <summary>Stops the four ambient snow PSystems used during the freeze atmosphere.</summary>
        private void StopNewAmbientSnow(bool immediate)
        {
            var behavior = immediate
                ? ParticleSystemStopBehavior.StopEmittingAndClear
                : ParticleSystemStopBehavior.StopEmitting;

            snowTop?.Stop(true, behavior);
            snowBottom?.Stop(true, behavior);
            snowLeft?.Stop(true, behavior);
            snowRight?.Stop(true, behavior);
        }

        /// <summary>Stops and clears all one-shot impact PSystems.</summary>
        private void StopNewImpactParticles()
        {
            impactShardLong?.Stop(true,    ParticleSystemStopBehavior.StopEmittingAndClear);
            impactShardDiamond?.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            impactShardChunk?.Stop(true,   ParticleSystemStopBehavior.StopEmittingAndClear);
            // Vapor is now a UI Image; kill its tween and reset state
            KillVaporTween(immediate: true);
        }

        // ── Vapor UI Image helpers ───────────────────────────────────────────

        /// <summary>
        /// Positions the vapor UI Image at the same anchored position as the timer target,
        /// then plays a 3-phase DOTween animation: fade-in + expand -> hold -> fade-out + drift expand.
        /// </summary>
        private void PlayVaporAnimation()
        {
            if (freezeImpactVaporRect == null || freezeImpactVaporCG == null)
                return;

            // Ensure the vapor's hierarchy is visible
            for (Transform t = freezeImpactVaporRect.transform; t != null; t = t.parent)
            {
                if (!t.gameObject.activeSelf)
                    t.gameObject.SetActive(true);
            }

            // Align to timer target using the same coordinate system as the other impact elements
            if (timer_txt != null)
            {
                freezeImpactVaporRect.anchoredPosition = GetTargetAnchoredPosition(freezeImpactVaporRect, timer_txt);
            }

            // Reset to clean starting state (do NOT touch sizeDelta — Inspector controls base size)
            KillVaporTween(immediate: false);
            freezeImpactVaporCG.alpha        = 0f;
            freezeImpactVaporRect.localScale = Vector3.one * 0.55f;

            // Phase 1  (0.00 – 0.55s)  fade-in + expand
            //   alpha:  0 → 0.75  over 0.10s  OutQuad
            //   scale:  0.55 → 1.25  over 0.55s  OutCubic
            // Phase 2  (0.35 – 0.90s)  fade-out + gentle final expand (overlap with Phase 1 end)
            //   alpha:  0.75 → 0  over 0.55s  OutQuad
            //   scale:  1.25 → 1.40  over 0.55s
            Sequence vaporSeq = DOTween.Sequence()
                .SetUpdate(true)
                .SetAutoKill(true)
                .SetLink(gameObject, LinkBehaviour.KillOnDisable);

            // Phase 1 — simultaneous fade-in and scale-up
            vaporSeq.Insert(0.00f, freezeImpactVaporCG
                .DOFade(0.75f, 0.10f)
                .SetEase(Ease.OutQuad));
            vaporSeq.Insert(0.00f, freezeImpactVaporRect
                .DOScale(Vector3.one * 1.25f, 0.55f)
                .SetEase(Ease.OutCubic));

            // Phase 2 — fade-out + drift expansion, starts at t=0.35s
            vaporSeq.Insert(0.35f, freezeImpactVaporCG
                .DOFade(0f, 0.55f)
                .SetEase(Ease.OutQuad));
            vaporSeq.Insert(0.35f, freezeImpactVaporRect
                .DOScale(Vector3.one * 1.40f, 0.55f)
                .SetEase(Ease.OutQuad));

            _vaporTween = vaporSeq;
        }

        /// <summary>
        /// Kills the active vapor tween and optionally resets the visual state immediately.
        /// </summary>
        private void KillVaporTween(bool immediate)
        {
            if (_vaporTween != null && _vaporTween.IsActive())
                _vaporTween.Kill();
            _vaporTween = null;

            if (immediate)
            {
                if (freezeImpactVaporCG != null)   freezeImpactVaporCG.alpha = 0f;
                if (freezeImpactVaporRect != null) freezeImpactVaporRect.localScale = Vector3.one * 0.55f;
            }
        }

        // ── Freeze Slider Helpers ────────────────────────────────────────────

        /// <summary>
        /// Gets the single authoritative freeze duration configured in either FreezeTimerBooster or TimerBooster.
        /// </summary>
        private float GetEffectiveFreezeDuration()
        {
            if (activeFreezeBooster != null && activeFreezeBooster.freezeDuration > 0.05f)
                return activeFreezeBooster.freezeDuration;

            if (freezeTimerBooster != null && freezeTimerBooster.freezeDuration > 0.05f)
                return freezeTimerBooster.freezeDuration;

            return Mathf.Max(0.1f, freezeDuration);
        }

        /// <summary>
        /// Starts the proportional freeze progress animation on freezeSlider and/or freezeFillImage.
        /// Runs a real-time unscaled coroutine so slider value is 100% synchronized with the actual freeze window.
        /// </summary>
        private void StartFreezeSlider()
        {
            StopFreezeSlider(immediate: true);

            bool hasSlider = freezeSlider != null || freezeFillImage != null;
            if (!hasSlider)
                return;

            float duration = GetEffectiveFreezeDuration();
            float startVal = freezeSliderDrains ? 1f : 0f;

            // Ensure GameObject hierarchy is active
            if (freezeSlider != null)
            {
                freezeSlider.gameObject.SetActive(true);
                freezeSlider.minValue = 0f;
                freezeSlider.maxValue = 1f;
                freezeSlider.value = startVal;
            }

            if (freezeFillImage != null)
            {
                freezeFillImage.gameObject.SetActive(true);
                freezeFillImage.fillAmount = startVal;
            }

            // Smooth fade-in
            if (freezeSliderCanvasGroup != null)
            {
                freezeSliderCanvasGroup.gameObject.SetActive(true);
                freezeSliderCanvasGroup.alpha = 0f;
                _sliderFadeTween = freezeSliderCanvasGroup
                    .DOFade(1f, 0.20f)
                    .SetUpdate(true)
                    .SetLink(gameObject, LinkBehaviour.KillOnDisable);
            }

            // The authored FreezeTimerBooster owns the authoritative unscaled
            // clock. Subscribe to its exact progress rather than running a
            // second timer that can drift from the real freeze lifetime.
            if (activeFreezeBooster != null && activeFreezeBooster.IsFreezeActive)
                return;

            _sliderRoutine = StartCoroutine(FreezeSliderProgressRoutine(duration));
        }

        private IEnumerator FreezeSliderProgressRoutine(float totalDuration)
        {
            float elapsed = 0f;
            totalDuration = Mathf.Max(0.1f, totalDuration);

            while (elapsed < totalDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / totalDuration);
                SetFreezeSliderValue(progress);

                yield return null;
            }

            // Ensure it settles cleanly at final value
            SetFreezeSliderValue(1f);

            _sliderRoutine = null;
        }

        private void SetFreezeSliderValue(float normalizedProgress)
        {
            float progress = Mathf.Clamp01(normalizedProgress);
            float value = freezeSliderDrains ? 1f - progress : progress;

            if (freezeSlider != null)
                freezeSlider.value = value;

            if (freezeFillImage != null)
                freezeFillImage.fillAmount = value;
        }

        /// <summary>
        /// Stops the freeze slider progression and fades out / resets the UI elements.
        /// </summary>
        private void StopFreezeSlider(bool immediate)
        {
            if (_sliderRoutine != null)
            {
                StopCoroutine(_sliderRoutine);
                _sliderRoutine = null;
            }

            if (_sliderFadeTween != null && _sliderFadeTween.IsActive())
                _sliderFadeTween.Kill();
            _sliderFadeTween = null;

            if (immediate)
            {
                if (freezeSliderCanvasGroup != null)
                    freezeSliderCanvasGroup.alpha = 0f;

                if (freezeSlider != null)
                    freezeSlider.value = freezeSliderDrains ? 1f : 0f;

                if (freezeFillImage != null)
                    freezeFillImage.fillAmount = freezeSliderDrains ? 1f : 0f;

                return;
            }

            // Freeze completion can arrive one frame before the progress
            // coroutine writes its terminal sample. Snap to the completed
            // value before the presentation fades so a draining slider never
            // disappears while it still appears partially full.
            float completedValue = freezeSliderDrains ? 0f : 1f;
            if (freezeSlider != null)
                freezeSlider.value = completedValue;

            if (freezeFillImage != null)
                freezeFillImage.fillAmount = completedValue;

            if (freezeSliderCanvasGroup != null)
            {
                _sliderFadeTween = freezeSliderCanvasGroup
                    .DOFade(0f, FreezeAtmoFadeOutDur)
                    .SetEase(Ease.OutQuad)
                    .SetUpdate(true)
                    .SetLink(gameObject, LinkBehaviour.KillOnDisable);
            }
        }
    }
}
