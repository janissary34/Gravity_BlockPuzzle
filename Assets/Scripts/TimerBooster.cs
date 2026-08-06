using System.Collections;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GravityPuzzle
{
    /// <summary>
    /// Timer Booster sequence controller.
    /// Animates timer_obj from screen bottom at native scale to a position lower than screen center,
    /// pauses for 1 second, and flies strictly vertically to timer_txt's location at the SAME speed,
    /// deactivating immediately via SetActive(false) upon arrival and freezing the timer for 8 seconds.
    /// </summary>
    public class TimerBooster : MonoBehaviour
    {
        [Header("UI & Animation References")]
        [Tooltip("The floating timer visual object that animates from bottom to center, then to timer_txt.")]
        [SerializeField] private GameObject timer_obj;

        [Tooltip("Target UI transform for timer text (e.g. timer_txt or time_txt element).")]
        [SerializeField] private Transform timer_txt;

        [Tooltip("Alternative reference name for target UI transform.")]
        [SerializeField] private Transform time_txt;

        [Tooltip("Optional UI Button to trigger the Timer Booster.")]
        [SerializeField] private Button boosterButton;

        [Header("Animation Speed & Easing Settings")]
        [SerializeField, Tooltip("Duration in seconds for timer_obj to travel from bottom off-screen to center.")]
        private float entranceDuration = 0.85f;

        [SerializeField, Tooltip("Pause duration in seconds at the pause position.")]
        private float centerPauseDuration = 1.0f;

        [SerializeField, Tooltip("Vertical Y offset for pause position (negative = lower down on screen).")]
        private float centerOffsetY = -150f;

        [SerializeField, Tooltip("Duration in seconds to fly from pause position to timer_txt (matches entranceDuration).")]
        private float flyToTextDuration = 0.85f;

        [Header("Timer Freeze Settings")]
        [SerializeField, Tooltip("How many real-time seconds the countdown remains frozen after the animation completes.")]
        private float freezeDuration = 8.0f;

        private Vector3 originalScale = Vector3.one;
        private Vector3 homeAnchoredPos;
        private Vector3 homeWorldPos;
        private bool homeCaptured;
        private Sequence activeSequence;
        private Coroutine freezeRoutine;

        private void Awake()
        {
            if (timer_obj != null)
            {
                originalScale = timer_obj.transform.localScale;

                RectTransform rt = timer_obj.GetComponent<RectTransform>();
                if (rt != null)
                {
                    homeAnchoredPos = rt.anchoredPosition;
                }
                homeWorldPos = timer_obj.transform.position;
                homeCaptured = true;

                timer_obj.SetActive(false);
            }

            FindAutoReferences();
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
        }

        private void FindAutoReferences()
        {
            if (timer_obj == null)
            {
                Transform tObj = transform.Find("timer_obj") ?? transform.Find("Timer_obj") ?? transform.Find("TimerObj");
                if (tObj != null)
                {
                    timer_obj = tObj.gameObject;
                    originalScale = timer_obj.transform.localScale;
                }
            }

            if (timer_txt == null && time_txt != null)
            {
                timer_txt = time_txt;
            }

            if (timer_txt == null)
            {
                GameObject timerTxtObj = GameObject.Find("timer_txt") ?? GameObject.Find("Timer_txt") ?? GameObject.Find("time_txt") ?? GameObject.Find("Time_txt") ?? GameObject.Find("Timer");
                if (timerTxtObj != null)
                {
                    timer_txt = timerTxtObj.transform;
                }
            }

            if (boosterButton == null)
            {
                boosterButton = GetComponent<Button>();
            }
        }

        /// <summary>
        /// Public entry point to trigger the Timer Booster animation sequence.
        /// </summary>
        public void PlayTimerBoosterSequence()
        {
            FindAutoReferences();

            Transform targetTextTransform = timer_txt ?? time_txt;

            if (timer_obj == null || targetTextTransform == null)
            {
                Debug.LogWarning("[TimerBooster] Cannot play sequence: timer_obj or timer_txt reference is missing.");
                return;
            }

            if (activeSequence != null && activeSequence.IsActive())
            {
                activeSequence.Kill();
            }

            Canvas.ForceUpdateCanvases();

            Camera cam = Camera.main;
            Vector3 centerPos = Vector3.zero;
            float offscreenOffsetY = 8f;

            RectTransform rectTransform = timer_obj.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                // UI Canvas Space: Lock X to home position, apply lower centerOffsetY
                float fixedX = homeCaptured ? homeAnchoredPos.x : rectTransform.anchoredPosition.x;
                Canvas canvas = timer_obj.GetComponentInParent<Canvas>();
                float canvasHeight = canvas != null ? canvas.GetComponent<RectTransform>().rect.height : Screen.height;
                offscreenOffsetY = canvasHeight > 0 ? canvasHeight * 0.8f : 800f;
                float baseY = homeCaptured ? homeAnchoredPos.y : rectTransform.anchoredPosition.y;
                centerPos = new Vector3(fixedX, baseY + centerOffsetY, 0f);
            }
            else
            {
                // World Space: Lock X to home position, apply lower centerOffsetY
                float fixedWorldX = homeCaptured ? homeWorldPos.x : timer_obj.transform.position.x;
                float worldOffsetY = centerOffsetY * 0.01f;
                if (cam != null)
                {
                    centerPos = new Vector3(fixedWorldX, cam.transform.position.y + worldOffsetY, 0f);
                    offscreenOffsetY = cam.orthographicSize + 4f;
                }
                else
                {
                    centerPos = new Vector3(fixedWorldX, worldOffsetY, 0f);
                    offscreenOffsetY = 10f;
                }
            }

            Vector3 startPos = centerPos + new Vector3(0f, -offscreenOffsetY, 0f);

            // Step 1: Bottom Entrance (Native Scale, Zero Rotation, Fixed X)
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
            timer_obj.SetActive(true);

            Sequence seq = DOTween.Sequence().SetLink(timer_obj);
            activeSequence = seq;

            // Step 1: Move from bottom off-screen to lower center position smoothly (0.85s)
            if (rectTransform != null)
            {
                seq.Append(rectTransform.DOAnchorPos(centerPos, entranceDuration).SetEase(Ease.OutCubic));
            }
            else
            {
                seq.Append(timer_obj.transform.DOMove(centerPos, entranceDuration).SetEase(Ease.OutCubic));
            }

            // Step 2: Pause at lower position (1.0s)
            seq.AppendInterval(centerPauseDuration);

            // Step 3: Move to timer_txt Y position keeping X strictly FIXED
            float halfHeight = 0f;
            if (rectTransform != null)
            {
                halfHeight = rectTransform.rect.height * 0.5f;
            }
            else
            {
                SpriteRenderer sr = timer_obj.GetComponentInChildren<SpriteRenderer>();
                if (sr != null)
                {
                    halfHeight = sr.bounds.extents.y;
                }
            }

            if (rectTransform != null)
            {
                float fixedX = homeCaptured ? homeAnchoredPos.x : rectTransform.anchoredPosition.x;
                Vector3 targetAnchored = GetTargetAnchoredPosition(rectTransform, targetTextTransform);
                targetAnchored.x = fixedX; // Keep X strictly fixed
                targetAnchored.y -= halfHeight;
                Debug.Log($"[TimerBooster] Vertical UI Flight: center={centerPos} -> topEdgeTargetAnchored={targetAnchored}");
                seq.Append(rectTransform.DOAnchorPos(targetAnchored, flyToTextDuration).SetEase(Ease.OutCubic));
            }
            else
            {
                float fixedWorldX = homeCaptured ? homeWorldPos.x : timer_obj.transform.position.x;
                Vector3 targetWorld = GetTargetWorldPosition(targetTextTransform, timer_obj.transform);
                targetWorld.x = fixedWorldX; // Keep X strictly fixed
                targetWorld.y -= halfHeight;
                Debug.Log($"[TimerBooster] Vertical World Flight: center={centerPos} -> topEdgeTargetWorld={targetWorld}");
                seq.Append(timer_obj.transform.DOMove(targetWorld, flyToTextDuration).SetEase(Ease.OutCubic));
            }

            // Immediately deactivate timer_obj when it reaches timer_txt
            seq.OnComplete(() =>
            {
                timer_obj.SetActive(false);
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
