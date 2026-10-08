using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using GravityPuzzle.Presentation.Views;
using GravityPuzzle.Config;
using GravityPuzzle.Gameplay.Tutorial;
using GravityPuzzle.Infrastructure.Services;

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
        public bool HasUses => boosterButtonRef != null
            ? boosterButtonRef.HasUses
            : BoosterInventoryRuntime.Current.GetCount(BoosterRewardType.FreezeTimer) > 0;
        public event Action<FreezeTimerBooster> FreezeEnded;
        public event Action<FreezeTimerBooster, float> FreezeProgressChanged;

        private PrototypeBoard boundBoard;
        private Coroutine freezeRoutine;
        private CanvasGroup buttonCanvasGroup;
        // When this component shares a HUD button with TimerBooster, it is the
        // authoritative timed effect behind that presentation sequence. It
        // must not subscribe to the click itself or it will freeze the clock
        // before the visual sequence reaches the countdown.
        private TimerBooster presentationSequenceOwner;
        private bool usedThisLevel;
        private int remainingCount = 1;

        private void Awake()
        {
            EnsureReferences();
            buttonCanvasGroup = boosterButton != null ? boosterButton.GetComponent<CanvasGroup>() : null;
            presentationSequenceOwner = GetComponent<TimerBooster>();
        }

        private void OnEnable()
        {
            EnsureReferences();

            if (boosterButton != null && presentationSequenceOwner == null)
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
            if (boosterButton != null && presentationSequenceOwner == null)
                boosterButton.onClick.RemoveListener(ActivateFreezeBooster);

            CancelOwnedFreeze();
        }

        /// <summary>
        /// Public UI entry point. Link this method to a Button's OnClick event.
        /// Calls made while active, after use, or after game-over are ignored.
        /// </summary>
        public void ActivateFreezeBooster()
        {
            TryActivateFreeze(consumeInventory: true);
        }

        /// <summary>
        /// Starts the authoritative freeze after TimerBooster has already
        /// consumed the shared inventory use. Keeping that transaction in the
        /// presentation owner prevents one button press from requiring two
        /// inventory uses.
        /// </summary>
        public bool ActivateFreezeFromPresentation(object presentationPauseOwner)
        {
            return TryActivateFreeze(consumeInventory: false, presentationPauseOwner);
        }

        private bool TryActivateFreeze(bool consumeInventory, object presentationPauseOwner = null)
        {
            SynchronizeLevel();

            bool isRequiredFirstUse = BoosterFirstUseTutorialRuntime.Gate != null &&
                                      BoosterFirstUseTutorialRuntime.Gate.IsBoosterHighlighted(
                                          BoosterRewardType.FreezeTimer);
            if (isRequiredFirstUse && boundBoard != null && !boundBoard.IsTimerStarted)
                boundBoard.StartTimer();

            if (boundBoard == null || (consumeInventory && !HasUses) || IsFreezeActive ||
                LevelTimerUI.IsGameOver || !boundBoard.IsTimerActive || !boundBoard.IsTimerStarted ||
                boundBoard.TimeRemaining <= 0f)
            {
                RefreshButtonState();
                return false;
            }

            // The timer presentation pauses on the input frame, then hands
            // that exact pause to this authoritative effect at impact. An
            // atomic transfer prevents a one-frame resume between owners.
            bool acquiredPause = presentationPauseOwner != null
                ? boundBoard.TryTransferTimerPause(presentationPauseOwner, this)
                : boundBoard.TryPauseTimer(this);
            if (!acquiredPause)
                return false;

            usedThisLevel = true;
            if (consumeInventory &&
                !TryConsumeInventoryUse())
            {
                boundBoard.ResumeTimer(this);
                return false;
            }
            freezeRoutine = StartCoroutine(FreezeTimerRoutine(boundBoard));
            RefreshButtonState();
            return true;
        }

        private bool TryConsumeInventoryUse()
        {
            return boosterButtonRef != null
                ? boosterButtonRef.TryConsumeUse()
                : BoosterInventoryRuntime.Current.TryConsume(BoosterRewardType.FreezeTimer);
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

            // Keep unavailable boosters visible as a subdued option instead of
            // removing them from the HUD. The whole button is also locked while
            // its timer-freeze sequence owns the board timer.
            bool hasUses = HasUses;
            bool isRequiredFirstUse = BoosterFirstUseTutorialRuntime.Gate != null &&
                                      BoosterFirstUseTutorialRuntime.Gate.IsBoosterHighlighted(
                                          BoosterRewardType.FreezeTimer);
            bool canInteract =
                hasUses &&
                !IsFreezeActive &&
                boundBoard != null &&
                !LevelTimerUI.IsGameOver &&
                boundBoard.IsTimerActive &&
                (boundBoard.IsTimerStarted || isRequiredFirstUse) &&
                boundBoard.TimeRemaining > 0f;

            if (buttonCanvasGroup != null)
            {
                buttonCanvasGroup.alpha = canInteract ? 1f : 0.55f;
                buttonCanvasGroup.interactable = canInteract;
                buttonCanvasGroup.blocksRaycasts = canInteract;
            }

            boosterButton.interactable = canInteract;
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
