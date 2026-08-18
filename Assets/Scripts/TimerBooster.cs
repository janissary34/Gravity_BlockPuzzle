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

        [SerializeField, Tooltip("Owns timer-booster tween timing and easing.")]
        private TweenConfig tweenConfig;

        [Header("Visual Effects (VFX)")]
        [Tooltip("Blue particle explosion VFX system triggered when clock arrives at timer_txt.")]
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

        [Tooltip("Soft UI glow placed behind the timer countdown.")]
        [SerializeField] private GameObject timerFreezeGlow;

        [SerializeField] private CanvasGroup timerFreezeGlowCanvasGroup;
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
        // Presentation has an explicit owner and lifetime. It must not disappear
        // merely because an optional legacy FreezeTimerBooster is unavailable.
        private Coroutine freezePresentationRoutine;
        private Tween urgencyFadeTween;
        private Tween timerFreezeGlowTween;
        private Sequence timerImpactSequence;
        private FreezeTimerBooster activeFreezeBooster;
        private ParticleSystemRenderer timerImpactParticlesRenderer;
        private ParticleSystemRenderer freezeSnowParticlesRenderer;

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

        private void Awake()
        {
            if (timer_obj != null)
            {
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

            if (timerUrgencyPresentation != null && timerUrgencyCanvasGroup == null)
                timerUrgencyCanvasGroup = timerUrgencyPresentation.GetComponent<CanvasGroup>();

            if (timerFreezeGlow != null && timerFreezeGlowCanvasGroup == null)
                timerFreezeGlowCanvasGroup = timerFreezeGlow.GetComponent<CanvasGroup>();

            if (timerImpactParticles != null)
                timerImpactParticlesRenderer = timerImpactParticles.GetComponent<ParticleSystemRenderer>();

            if (timerImpactImage != null && timerImpactCanvasGroup == null)
                timerImpactCanvasGroup = timerImpactImage.GetComponent<CanvasGroup>();

            if (freezeSnowParticles != null)
                freezeSnowParticlesRenderer = freezeSnowParticles.GetComponent<ParticleSystemRenderer>();

            ApplyFreezeVfxSorting();

            SetUrgencyPresentationVisible(false, true);
            SetTimerFreezeGlowVisible(false, true);
            SetTimerImpactImageVisible(false);
        }

        private void OnEnable()
        {
            if (boosterButton != null)
            {
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

            if (freezePresentationRoutine != null)
            {
                StopCoroutine(freezePresentationRoutine);
                freezePresentationRoutine = null;
            }

            UnsubscribeFromFreezeCompletion();
            StopFreezeAmbientPresentation(true);
            timerImpactSequence?.Kill();
            SetTimerImpactImageVisible(false);

            if (frozenClockImage != null)
            {
                frozenClockImage.fillAmount = 0f;
            }

            SetUrgencyPresentationVisible(false, true);
        }

        /// <summary>
        /// Public entry point to trigger the Timer Booster animation sequence.
        /// </summary>
        public void PlayTimerBoosterSequence()
        {
            if (timer_obj == null || timer_txt == null)
            {
                Debug.LogWarning("[TimerBooster] Cannot play sequence: timer_obj or timer_txt reference is missing in Inspector.");
                return;
            }

            if (activeSequence != null && activeSequence.IsActive())
            {
                activeSequence.Kill();
            }

            Canvas.ForceUpdateCanvases();

            // Bring timer_obj to front of Canvas hierarchy so it is rendered on top of all panels
            timer_obj.transform.SetAsLastSibling();

            // Force Canvas sorting order to 30000 so clock renders ON TOP of all puzzle blocks and boards
            Canvas objCanvas = timer_obj.GetComponent<Canvas>();
            if (objCanvas == null)
            {
                objCanvas = timer_obj.gameObject.AddComponent<Canvas>();
            }
            objCanvas.overrideSorting = true;
            objCanvas.sortingOrder = 30000;

            GraphicRaycaster raycaster = timer_obj.GetComponent<GraphicRaycaster>();
            if (raycaster == null && timer_obj.GetComponentInParent<Canvas>() != null)
            {
                timer_obj.gameObject.AddComponent<GraphicRaycaster>();
            }

            // Force SpriteRenderer sorting order to 30000 if timer_obj uses SpriteRenderers
            SpriteRenderer[] srs = timer_obj.GetComponentsInChildren<SpriteRenderer>(true);
            foreach (var sr in srs)
            {
                if (sr != null)
                {
                    sr.sortingOrder = 30000;
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

            Camera cam = Camera.main;
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

            // Step 1: Move from bottom off-screen to screen center position smoothly (0.85s)
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

            // Step 3: Move timer_obj directly to exact World position of timer_txt (0.5s Ease.InQuad)
            seq.Append(timer_obj.transform.DOMove(timer_txt.position, FlyToTargetDuration).SetEase(FlyToTargetEase));

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

                // FreezeSnowParticles used to be assigned to this legacy impact
                // slot as well. Do not move/restart the ambient snow as a burst.
                if (blueParticleVFX != null &&
                    blueParticleVFX != freezeSnowParticles &&
                    blueParticleVFX != timerImpactParticles)
                {
                    ParticleSystem vfxInstance = blueParticleVFX;
                    bool isPrefabAsset = !blueParticleVFX.gameObject.scene.IsValid();

                    if (isPrefabAsset)
                    {
                        // Dynamically instantiate prefab if assigned from Project window
                        vfxInstance = Instantiate(blueParticleVFX);
                    }

                    RectTransform vfxRect = vfxInstance.GetComponent<RectTransform>();
                    if (vfxRect != null)
                    {
                        // Position in UI Canvas Space
                        Vector3 targetAnchored = GetTargetAnchoredPosition(vfxRect, timer_txt);
                        vfxRect.anchoredPosition = targetAnchored;
                    }
                    else
                    {
                        // Position in Camera World Space
                        Vector3 vfxWorldPos = GetTargetWorldPosition(timer_txt, timer_obj.transform);
                        vfxInstance.transform.position = vfxWorldPos;
                    }

                    // Ensure scale is native Vector3.one
                    if (vfxInstance.transform.localScale.sqrMagnitude < 0.0001f)
                    {
                        vfxInstance.transform.localScale = Vector3.one;
                    }

                    // Force active and play
                    vfxInstance.gameObject.SetActive(true);

                    // Force sorting order so particles render ON TOP of UI Canvas elements
                    ParticleSystemRenderer psr = vfxInstance.GetComponent<ParticleSystemRenderer>();
                    if (psr != null)
                    {
                        psr.sortingOrder = 30000;
                    }

                    Canvas c = vfxInstance.GetComponent<Canvas>();
                    if (c != null)
                    {
                        c.overrideSorting = true;
                        c.sortingOrder = 30000;
                    }

                    vfxInstance.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    vfxInstance.Play(true);

                    if (isPrefabAsset)
                    {
                        Destroy(vfxInstance.gameObject, 3.5f);
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
            Camera cam = Camera.main;

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

            Canvas canvas = mover.GetComponentInParent<Canvas>();
            Camera uiCam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                ? canvas.worldCamera
                : null;

            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(uiCam, target.position);

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parentRect, screenPoint, uiCam, out Vector2 localPoint))
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

            // Freeze timer for 8 seconds
            FreezeTimerBooster freeze = GetComponent<FreezeTimerBooster>() ?? Object.FindObjectOfType<FreezeTimerBooster>();
            if (freeze != null)
            {
                freeze.freezeDuration = freezeDuration;
                SubscribeToFreezeCompletion(freeze);
                freeze.ActivateFreezeBooster();
                if (!freeze.IsFreezeActive)
                {
                    UnsubscribeFromFreezeCompletion();
                }
            }
            else
            {
                PrototypeBoard activeBoard = PrototypeBoard.Active ?? Object.FindObjectOfType<PrototypeBoard>();
                if (activeBoard != null)
                {
                    if (freezeRoutine != null) StopCoroutine(freezeRoutine);
                    freezeRoutine = StartCoroutine(FreezeTimerRoutine(activeBoard, freezeDuration));
                }
            }
        }

        private BoosterButton GetTimerBoosterButton()
        {
            if (boosterButton != null)
            {
                var b = boosterButton.GetComponent<BoosterButton>();
                if (b != null) return b;
            }

            BoosterButton[] allButtons = Object.FindObjectsOfType<BoosterButton>();
            foreach (var b in allButtons)
            {
                if (b != null && (b.gameObject.name.ToLower().Contains("time") || b.gameObject.name.ToLower().Contains("timer")))
                {
                    return b;
                }
            }
            return null;
        }

        private IEnumerator FreezeTimerRoutine(PrototypeBoard targetBoard, float duration)
        {
            if (targetBoard == null || !targetBoard.TryPauseTimer(this))
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
                targetBoard.ResumeTimer(this);
                Debug.Log("[TimerBooster] Timer resumed after 8s freeze!");
            }

            freezeRoutine = null;
            SetUrgencyPresentationVisible(false, false);
            StopFreezeAmbientPresentation(false);
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
            yield return new WaitForSecondsRealtime(Mathf.Max(.1f, freezeDuration));
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
        }

        private void SubscribeToFreezeCompletion(FreezeTimerBooster freeze)
        {
            if (activeFreezeBooster == freeze)
                return;

            UnsubscribeFromFreezeCompletion();
            activeFreezeBooster = freeze;
            activeFreezeBooster.FreezeEnded += HandleFreezeEnded;
        }

        private void UnsubscribeFromFreezeCompletion()
        {
            if (activeFreezeBooster == null)
                return;

            activeFreezeBooster.FreezeEnded -= HandleFreezeEnded;
            activeFreezeBooster = null;
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
                        glowRect.anchoredPosition = GetTargetAnchoredPosition(glowRect, timer_txt);

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
    }
}
