using System.Collections;
using DG.Tweening;
using TMPro;
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

        [Header("Visual Effects (VFX)")]
        [Tooltip("Blue particle explosion VFX system triggered when clock arrives at timer_txt.")]
        [SerializeField] private ParticleSystem blueParticleVFX;

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

            if (frozenClockImage != null)
            {
                frozenClockImage.fillAmount = 0f;
            }
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
                seq.Append(rectTransform.DOAnchorPos(centerPos, entranceDuration).SetEase(Ease.OutCubic));
            }
            else
            {
                seq.Append(timer_obj.transform.DOMove(centerPos, entranceDuration).SetEase(Ease.OutCubic));
            }

            // Step 2: Pause at center & animate 360 degree radial fill over freezeFillDuration
            if (frozenClockImage != null)
            {
                frozenClockImage.type = Image.Type.Filled;
                seq.Append(frozenClockImage.DOFillAmount(1f, freezeFillDuration).SetEase(fillEase));
                float remainingPause = Mathf.Max(0f, centerPauseDuration - freezeFillDuration);
                if (remainingPause > 0f)
                {
                    seq.AppendInterval(remainingPause);
                }
            }
            else
            {
                seq.AppendInterval(centerPauseDuration);
            }

            // Step 3: Move timer_obj directly to exact World position of timer_txt (0.5s Ease.InQuad)
            seq.Append(timer_obj.transform.DOMove(timer_txt.position, 0.5f).SetEase(Ease.InQuad));

            // Step 4: Arrival Event (Particle Burst + Deactivate)
            seq.AppendCallback(() =>
            {
                if (blueParticleVFX != null)
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
                freeze.ActivateFreezeBooster();
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
                yield break;

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
        }
    }
}
