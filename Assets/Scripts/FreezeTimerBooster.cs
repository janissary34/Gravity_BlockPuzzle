using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace GravityPuzzle
{
    /// <summary>
    /// One-use-per-level booster that pauses the authoritative board timer.
    /// Attach this component to a UI object and connect ActivateFreezeBooster
    /// to a Button OnClick event, or assign boosterButton for auto-wiring.
    /// </summary>
    public sealed class FreezeTimerBooster : MonoBehaviour
    {
        [Header("Freeze Booster")]
        [Tooltip("Optional. Assign a UI Button to wire its click automatically.")]
        public Button boosterButton;

        [Tooltip("How many real-time seconds the countdown remains frozen.")]
        [Min(.1f)]
        public float freezeDuration = 5f;

        [Header("Urgency Presentation")]
        [Tooltip("Full-screen UI overlay shown only while this timer-freeze booster is active. Assign the inactive TimerUrgencyVignette GameObject here.")]
        [SerializeField] private GameObject timerUrgencyVignette;
        [SerializeField] private CanvasGroup timerUrgencyCanvasGroup;
        [Min(.01f)] [SerializeField] private float urgencyFadeOutDuration = .45f;
        [SerializeField] private Ease urgencyFadeOutEase = Ease.InSine;

        public bool IsFreezeActive => freezeRoutine != null;
        public bool HasBeenUsedThisLevel => usedThisLevel;

        private PrototypeBoard boundBoard;
        private Coroutine freezeRoutine;
        private Tween urgencyFadeTween;
        private CanvasGroup buttonCanvasGroup;
        private bool usedThisLevel;

        private void OnEnable()
        {
            if (boosterButton != null)
                boosterButton.onClick.AddListener(ActivateFreezeBooster);

            SynchronizeLevel();
            CacheUrgencyCanvasGroup();
            SetUrgencyVignetteVisible(false, true);
            RefreshButtonState();
        }

        private void Update()
        {
            // Supports a persistent UI canvas: a newly created board represents
            // a new level and restores the booster's single use automatically.
            if (PrototypeBoard.Active != null && PrototypeBoard.Active != boundBoard)
                SynchronizeLevel();

            RefreshButtonState();
        }

        private void OnDisable()
        {
            if (boosterButton != null)
                boosterButton.onClick.RemoveListener(ActivateFreezeBooster);

            CancelOwnedFreeze();
            SetUrgencyVignetteVisible(false, true);
        }

        /// <summary>
        /// Public UI entry point. Link this method to a Button's OnClick event.
        /// Calls made while active, after use, or after game-over are ignored.
        /// </summary>
        public void ActivateFreezeBooster()
        {
            SynchronizeLevel();

            if (boundBoard == null || usedThisLevel || IsFreezeActive ||
                LevelTimerUI.IsGameOver || !boundBoard.IsTimerActive ||
                boundBoard.TimeRemaining <= 0f)
            {
                RefreshButtonState();
                return;
            }

            // Owner-based pausing means another system may already have paused
            // the timer. Releasing this boost later will not cancel that pause.
            if (!boundBoard.TryPauseTimer(this))
                return;

            usedThisLevel = true;
            SetUrgencyVignetteVisible(true, false);
            freezeRoutine = StartCoroutine(FreezeTimerRoutine(boundBoard));
            RefreshButtonState();
        }

        private IEnumerator FreezeTimerRoutine(PrototypeBoard targetBoard)
        {
            float elapsed = 0f;
            float duration = Mathf.Max(.1f, freezeDuration);

            // Unscaled time makes the five-second window reliable even if a menu
            // or another feature changes Time.timeScale while the boost is active.
            while (elapsed < duration && targetBoard != null &&
                   targetBoard == PrototypeBoard.Active &&
                   targetBoard.IsTimerActive && !LevelTimerUI.IsGameOver)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (targetBoard != null)
                targetBoard.ResumeTimer(this);

            freezeRoutine = null;
            SetUrgencyVignetteVisible(false, false);
            RefreshButtonState();
        }

        private void SynchronizeLevel()
        {
            PrototypeBoard activeBoard = PrototypeBoard.Active;
            if (activeBoard == null || activeBoard == boundBoard)
                return;

            CancelOwnedFreeze();
            boundBoard = activeBoard;
            usedThisLevel = false;
            RefreshButtonState();
        }

        private void CancelOwnedFreeze()
        {
            if (freezeRoutine != null)
            {
                StopCoroutine(freezeRoutine);
                freezeRoutine = null;
            }

            if (boundBoard != null)
                boundBoard.ResumeTimer(this);

            SetUrgencyVignetteVisible(false, true);
        }

        private void CacheUrgencyCanvasGroup()
        {
            if (timerUrgencyVignette != null && timerUrgencyCanvasGroup == null)
                timerUrgencyCanvasGroup = timerUrgencyVignette.GetComponent<CanvasGroup>();
        }

        private void SetUrgencyVignetteVisible(bool visible, bool immediate)
        {
            if (timerUrgencyVignette == null)
                return;

            CacheUrgencyCanvasGroup();
            urgencyFadeTween?.Kill();

            if (visible)
            {
                // The presentation can be assigned either as its root or as
                // the vignette child. Make its hierarchy visible so an
                // inactive effects parent cannot hide the configured overlay.
                Transform current = timerUrgencyVignette.transform;
                while (current != null)
                {
                    if (!current.gameObject.activeSelf)
                        current.gameObject.SetActive(true);

                    current = current.parent;
                }

                if (timerUrgencyCanvasGroup != null)
                {
                    urgencyFadeTween = timerUrgencyCanvasGroup
                        .DOFade(1f, .35f)
                        .SetEase(Ease.OutSine)
                        .SetLink(timerUrgencyVignette, LinkBehaviour.KillOnDisable)
                        .SetAutoKill(true);
                }

                return;
            }

            if (immediate || timerUrgencyCanvasGroup == null)
            {
                if (timerUrgencyCanvasGroup != null)
                    timerUrgencyCanvasGroup.alpha = 0f;

                if (timerUrgencyVignette.activeSelf)
                    timerUrgencyVignette.SetActive(false);

                return;
            }

            urgencyFadeTween = timerUrgencyCanvasGroup
                .DOFade(0f, urgencyFadeOutDuration)
                .SetEase(urgencyFadeOutEase)
                .SetLink(timerUrgencyVignette, LinkBehaviour.KillOnDisable)
                .SetAutoKill(true)
                .OnComplete(() =>
                {
                    if (timerUrgencyVignette != null)
                        timerUrgencyVignette.SetActive(false);
                });
        }

        private void RefreshButtonState()
        {
            if (boosterButton == null)
                return;

            // Keep the GameObject active so this coroutine continues even when
            // the booster component lives directly on the Button. CanvasGroup
            // hides the whole visual hierarchy without disabling the component.
            if (buttonCanvasGroup == null)
            {
                buttonCanvasGroup = boosterButton.GetComponent<CanvasGroup>();
                if (buttonCanvasGroup == null)
                    buttonCanvasGroup = boosterButton.gameObject.AddComponent<CanvasGroup>();
            }

            bool visible = !usedThisLevel;
            buttonCanvasGroup.alpha = visible ? 1f : 0f;
            buttonCanvasGroup.interactable = visible;
            buttonCanvasGroup.blocksRaycasts = visible;

            boosterButton.interactable =
                visible &&
                boundBoard != null &&
                !IsFreezeActive &&
                !LevelTimerUI.IsGameOver &&
                boundBoard.IsTimerActive &&
                boundBoard.TimeRemaining > 0f;
        }
    }
}
