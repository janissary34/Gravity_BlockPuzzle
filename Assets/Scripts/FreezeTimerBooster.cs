using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using GravityPuzzle.Presentation.Views;

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

        [Tooltip("Optional BoosterButton component reference for multi-use tracking.")]
        [SerializeField] private BoosterButton boosterButtonRef;

        [Tooltip("How many real-time seconds the countdown remains frozen.")]
        [Min(.1f)]
        public float freezeDuration = 5f;

        public bool IsFreezeActive => freezeRoutine != null;
        public bool HasBeenUsedThisLevel => usedThisLevel;
        public bool HasUses => boosterButtonRef != null ? boosterButtonRef.HasUses : remainingCount > 0;
        public event Action<FreezeTimerBooster> FreezeEnded;
        public event Action<FreezeTimerBooster, float> FreezeProgressChanged;

        private PrototypeBoard boundBoard;
        private Coroutine freezeRoutine;
        private CanvasGroup buttonCanvasGroup;
        private bool usedThisLevel;
        private int remainingCount = 1;

        private void Awake()
        {
            EnsureReferences();
            buttonCanvasGroup = boosterButton != null ? boosterButton.GetComponent<CanvasGroup>() : null;
        }

        private void OnEnable()
        {
            EnsureReferences();

            if (boosterButton != null)
                boosterButton.onClick.AddListener(ActivateFreezeBooster);

            SynchronizeLevel();
            RefreshButtonState();
        }

        private void Start()
        {
            // The board can finish its startup after this UI object receives
            // OnEnable. Synchronize once more so the badge always uses the
            // active level's timerBoosterCount instead of its prefab value.
            SynchronizeLevel();
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
        }

        /// <summary>
        /// Public UI entry point. Link this method to a Button's OnClick event.
        /// Calls made while active, after use, or after game-over are ignored.
        /// </summary>
        public void ActivateFreezeBooster()
        {
            SynchronizeLevel();

            if (boundBoard == null || !HasUses || IsFreezeActive ||
                LevelTimerUI.IsGameOver || !boundBoard.IsTimerActive || !boundBoard.IsTimerStarted ||
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
            if (boosterButtonRef == null)
                remainingCount = Mathf.Max(0, remainingCount - 1);
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
                FreezeProgressChanged?.Invoke(this, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            if (targetBoard != null)
                targetBoard.ResumeTimer(this);

            FreezeProgressChanged?.Invoke(this, 1f);
            freezeRoutine = null;
            FreezeEnded?.Invoke(this);
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
            GravityLevelDefinition level = GravityLevelRuntime.FindLevelToPlay();
            int levelUseCount = level != null ? level.timerBoosterCount : 1;
            if (boosterButtonRef != null)
            {
                boosterButtonRef.ConfigureLevelUseCount(levelUseCount);
            }
            else
            {
                remainingCount = levelUseCount;
                if (remainingCount == 0)
                    gameObject.SetActive(false);
            }
            RefreshButtonState();
        }

        private void CancelOwnedFreeze()
        {
            bool wasFreezeActive = freezeRoutine != null;
            if (freezeRoutine != null)
            {
                StopCoroutine(freezeRoutine);
                freezeRoutine = null;
            }

            if (boundBoard != null)
                boundBoard.ResumeTimer(this);

            if (wasFreezeActive)
                FreezeEnded?.Invoke(this);
        }

        private void RefreshButtonState()
        {
            if (boosterButton == null)
                return;

            if (BoosterTargetingPresentation.IsBoosterButtonSuppressed(buttonCanvasGroup))
                return;

            // Keep the GameObject active so this coroutine continues even when
            // the booster component lives directly on the Button. CanvasGroup
            // hides the whole visual hierarchy without disabling the component.
            bool visible = HasUses;
            if (buttonCanvasGroup != null)
            {
                buttonCanvasGroup.alpha = visible ? 1f : 0f;
                buttonCanvasGroup.interactable = visible;
                buttonCanvasGroup.blocksRaycasts = visible;
            }

            boosterButton.interactable =
                visible &&
                boundBoard != null &&
                !IsFreezeActive &&
                !LevelTimerUI.IsGameOver &&
                boundBoard.IsTimerActive &&
                boundBoard.IsTimerStarted &&
                boundBoard.TimeRemaining > 0f;
        }

        private void EnsureReferences()
        {
            if (boosterButtonRef == null)
            {
                if (boosterButton != null)
                {
                    boosterButtonRef = boosterButton.GetComponent<BoosterButton>();
                }

                if (boosterButtonRef == null)
                {
                    boosterButtonRef = GetComponent<BoosterButton>();
                }
            }

            if (boosterButton == null && boosterButtonRef != null)
            {
                boosterButton = boosterButtonRef.ButtonComponent;
            }
        }
    }
}
