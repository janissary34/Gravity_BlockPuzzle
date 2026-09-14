using DG.Tweening;
using GravityPuzzle.Config;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

namespace GravityPuzzle
{
    /// <summary>
    /// Manages the level countdown timer and handles the game over (fail) flow.
    /// This script is designed to be fully modular so it can be dragged into any project.
    /// </summary>
    public class LevelTimerUI : MonoBehaviour
    {
        public static LevelTimerUI Active { get; private set; }

        [Header("Timer UI")]
        [Tooltip("The text component that displays the remaining time (e.g., 01:30)")]
        public TMP_Text timerText;

        [Tooltip("Slider that visually represents the remaining level time.")]
        [SerializeField] private Slider timerSlider;

        [Header("Timer Urgency Presentation")]
        [Tooltip("Full-screen red frame shown during the final seconds of an active countdown.")]
        [SerializeField] private GameObject timerUrgencyFrame;

        [Tooltip("Seconds remaining at which the urgency frame becomes visible.")]
        [Min(0.01f)] [SerializeField] private float timerUrgencyThresholdSeconds = 10f;

        [Tooltip("First campaign level allowed to show the red vignette.")]
        [Min(1)] [SerializeField] private int redVignetteFirstLevel = 1;

        [Tooltip("Shared presentation timing for the urgency-frame fade.")]
        [SerializeField] private TweenConfig timerUrgencyTweenConfig;

        [Tooltip("Canvas group on the urgency frame used for its fade.")]
        [SerializeField] private CanvasGroup timerUrgencyCanvasGroup;

        [Header("Fail Popup")]
        [Tooltip("The popup panel to show when the timer runs out")]
        public GameObject failPopupPanel;

        [Header("Continue Popup")]
        [Tooltip("Panel shown once when the level timer first expires.")]
        [SerializeField] private GameObject keepOnPlayingPanel;

        [Tooltip("Button that grants the one-time timer extension.")]
        [SerializeField] private Button liveAddsButton;

        [Tooltip("Non-ad continue button on the keep-on-playing panel.")]
        [SerializeField] private Button playOnButton;

        [Tooltip("Button that declines the continue offer and opens the fail panel.")]
        [SerializeField] private Button closeKeepOnPlayingButton;

        [Tooltip("Seconds granted after the player accepts the continue offer.")]
        [Min(0.01f)] [SerializeField] private float continueTimeSeconds = 20f;

        [Header("Buttons")]
        [Tooltip("Drag your Retry button here")]
        public Button retryButton;
        
        [Tooltip("Drag your Main Menu button here")]
        public Button mainMenuButton;

        [Header("Retry Confirmation Panel")]
        [Tooltip("Confirmation panel shown before the current level is restarted.")]
        [SerializeField] private GameObject retryConfirmationPanel;

        [Tooltip("HUD button that opens the retry confirmation panel.")]
        [SerializeField] private Button openRetryConfirmationButton;

        [Tooltip("Button in the confirmation panel that restarts the current level.")]
        [SerializeField] private Button confirmRetryButton;

        [Tooltip("Button in the confirmation panel that dismisses it.")]
        [SerializeField] private Button closeRetryConfirmationButton;

        [Tooltip("Legacy settings handler on the replay icon. It is closed before the retry panel opens.")]
        [SerializeField] private SettingsPanelButton retryButtonSettingsHandler;

        [Header("Scene Navigation")]
        [Tooltip("Name of your Main Menu scene. Leave blank to reload current level directly.")]
        public string mainMenuSceneName = "";

        // Global flag that other systems (like PuzzleDragController) can check to disable input
        public static bool IsGameOver { get; private set; }

        private PrototypeBoard board;
        private int lastDisplayedSecond = -1;
        private bool timerPresentationLocked;
        private bool timerUrgencyFrameVisible;
        private Tween timerUrgencyFadeTween;
        private Tween timerUrgencyScaleTween;
        private Tween timerTextUrgencyPulseTween;
        private RectTransform timerUrgencyFrameRect;
        private RectTransform timerTextRect;
        private Vector3 timerTextInitialScale;
        private bool timerTextUrgencyPulseVisible;
        private float configuredSliderTimeLimit = -1f;
        private bool continueOfferIsActive;
        private bool levelFailureHandled;

        private void Awake()
        {
            // Always ensure the game state is reset when this script wakes up (e.g. on scene reload)
            IsGameOver = false;

            // Keep the fail overlay out of the EventSystem raycast results as
            // soon as the scene is initialized. Waiting until Start leaves a
            // frame in which its full-panel Image can reject a booster target
            // tap, depending on Unity's component update order.
            if (failPopupPanel != null)
                failPopupPanel.SetActive(false);

            if (keepOnPlayingPanel != null)
                keepOnPlayingPanel.SetActive(false);

            if (timerUrgencyFrame != null && timerUrgencyCanvasGroup == null)
                timerUrgencyCanvasGroup = timerUrgencyFrame.GetComponent<CanvasGroup>();

            if (timerUrgencyFrame != null)
                timerUrgencyFrameRect = timerUrgencyFrame.GetComponent<RectTransform>();

            if (timerText != null)
            {
                timerTextRect = timerText.rectTransform;
                timerTextInitialScale = timerTextRect.localScale;
            }

            timerUrgencyFrameVisible = true;
            SetTimerUrgencyFrameVisible(false);

            if (retryConfirmationPanel != null)
                retryConfirmationPanel.SetActive(false);
        }

        private void OnEnable()
        {
            Active = this;
            BindBoard(PrototypeBoard.Active);
        }

        private void OnDisable()
        {
            timerUrgencyFadeTween?.Kill();
            timerUrgencyFadeTween = null;
            timerUrgencyScaleTween?.Kill();
            timerUrgencyScaleTween = null;
            timerTextUrgencyPulseTween?.Kill();
            timerTextUrgencyPulseTween = null;
            timerTextUrgencyPulseVisible = false;
            BindBoard(null);

            if (Active == this)
                Active = null;
        }

        private void Start()
        {
            if (retryButton != null)
                retryButton.onClick.AddListener(OnRetryClicked);
                
            if (mainMenuButton != null)
                mainMenuButton.onClick.AddListener(OnMainMenuClicked);

            if (liveAddsButton != null)
                liveAddsButton.onClick.AddListener(OnLiveAddsClicked);

            if (playOnButton != null)
                playOnButton.onClick.AddListener(OnKeepOnPlayingClosed);

            if (closeKeepOnPlayingButton != null)
                closeKeepOnPlayingButton.onClick.AddListener(OnKeepOnPlayingClosed);

            if (openRetryConfirmationButton != null)
                openRetryConfirmationButton.onClick.AddListener(OpenRetryConfirmation);

            if (confirmRetryButton != null)
                confirmRetryButton.onClick.AddListener(OnRetryClicked);

            if (closeRetryConfirmationButton != null)
                closeRetryConfirmationButton.onClick.AddListener(CloseRetryConfirmation);

            SetTimerVisible(false);
        }

        private void Update()
        {
            BindBoard(PrototypeBoard.Active);

            // LevelFailed is the primary notification path. This check covers
            // a failure that occurs before this UI has subscribed, such as a
            // timer expiring during scene initialization. Once the one-time
            // continue has been used, keep the fail overlay authoritative
            // while the board remains failed.
            if (board != null && board.HasFailed)
            {
                if (board.HasUsedTimerExpiryContinue)
                    ShowFailPopup();
                else if (!levelFailureHandled)
                    HandleLevelFailed();
            }

            bool hasTimeLimit = board != null && board.TimeLimit > 0f && !timerPresentationLocked;
            SetTimerVisible(hasTimeLimit);
            SetTimerUrgencyFrameVisible(
                hasTimeLimit &&
                board.IsTimerStarted &&
                board.TimeRemaining > 0f &&
                board.TimeRemaining <= timerUrgencyThresholdSeconds &&
                GravityLevelRuntime.CurrentLevelNumber >= redVignetteFirstLevel);
            SetTimerTextUrgencyPulseVisible(
                hasTimeLimit &&
                board.IsTimerStarted &&
                board.TimeRemaining > 0f &&
                board.TimeRemaining <= timerUrgencyThresholdSeconds);
            if (!hasTimeLimit)
                return;

            UpdateTimerDisplay(board.TimeRemaining);
            UpdateTimerSlider(board.TimeRemaining);
        }

        private void BindBoard(PrototypeBoard nextBoard)
        {
            if (board == nextBoard)
                return;

            if (board != null)
            {
                board.LevelFailed -= HandleLevelFailed;
                board.GameStateChanged -= HandleGameStateChanged;
            }

            board = nextBoard;
            lastDisplayedSecond = -1;
            configuredSliderTimeLimit = -1f;
            levelFailureHandled = false;
            timerPresentationLocked = board != null &&
                (board.GameState == GravityPuzzle.Core.StateMachine.GameState.LevelComplete ||
                 board.GameState == GravityPuzzle.Core.StateMachine.GameState.Result);

            if (board != null)
            {
                board.LevelFailed += HandleLevelFailed;
                board.GameStateChanged += HandleGameStateChanged;
            }
        }

        private void HandleLevelFailed()
        {
            if (levelFailureHandled)
                return;

            levelFailureHandled = true;

            if (board != null &&
                !board.HasUsedTimerExpiryContinue &&
                board.WasTimerExpiryFailure)
            {
                ShowKeepOnPlayingPopup();
                return;
            }

            ShowFailPopup();
        }

        private void HandleGameStateChanged(
            GravityPuzzle.Core.StateMachine.GameState previousState,
            GravityPuzzle.Core.StateMachine.GameState nextState)
        {
            timerPresentationLocked =
                nextState == GravityPuzzle.Core.StateMachine.GameState.LevelComplete ||
                nextState == GravityPuzzle.Core.StateMachine.GameState.Result;

            if (timerPresentationLocked)
            {
                SetTimerVisible(false);
                SetTimerUrgencyFrameVisible(false);
                SetTimerTextUrgencyPulseVisible(false);
            }
        }

        private void SetTimerVisible(bool visible)
        {
            if (timerText != null && timerText.gameObject.activeSelf != visible)
                timerText.gameObject.SetActive(visible);

            if (timerSlider != null && timerSlider.gameObject.activeSelf != visible)
                timerSlider.gameObject.SetActive(visible);
        }

        private void SetTimerUrgencyFrameVisible(bool visible)
        {
            if (timerUrgencyFrame == null || timerUrgencyFrameVisible == visible)
                return;

            timerUrgencyFrameVisible = visible;
            timerUrgencyFadeTween?.Kill();
            timerUrgencyScaleTween?.Kill();

            if (timerUrgencyCanvasGroup == null || timerUrgencyTweenConfig == null)
            {
                if (timerUrgencyCanvasGroup != null)
                    timerUrgencyCanvasGroup.alpha = visible ? 1f : 0f;

                if (timerUrgencyFrameRect != null)
                    timerUrgencyFrameRect.localScale = Vector3.one;

                timerUrgencyFrame.SetActive(visible);
                return;
            }

            if (visible)
            {
                if (!timerUrgencyFrame.activeSelf)
                    timerUrgencyFrame.SetActive(true);

                timerUrgencyCanvasGroup.alpha = 0f;
                if (timerUrgencyFrameRect != null)
                {
                    timerUrgencyFrameRect.localScale = Vector3.one * timerUrgencyTweenConfig.TimerUrgencyFrameStartScale;
                    timerUrgencyScaleTween = timerUrgencyFrameRect
                        .DOScale(Vector3.one, timerUrgencyTweenConfig.TimerUrgencyFrameRevealDuration)
                        .SetEase(timerUrgencyTweenConfig.TimerUrgencyFrameRevealEase)
                        .SetLink(timerUrgencyFrame, LinkBehaviour.KillOnDisable)
                        .SetAutoKill(true);
                }

                timerUrgencyFadeTween = timerUrgencyCanvasGroup
                    .DOFade(1f, timerUrgencyTweenConfig.TimerUrgencyFadeInDuration)
                    .SetEase(timerUrgencyTweenConfig.TimerUrgencyFadeInEase)
                    .SetLink(timerUrgencyFrame, LinkBehaviour.KillOnDisable)
                    .SetAutoKill(true);
                return;
            }

            if (!timerUrgencyFrame.activeSelf)
                return;

            if (timerUrgencyFrameRect != null)
                timerUrgencyFrameRect.localScale = Vector3.one;

            timerUrgencyFadeTween = timerUrgencyCanvasGroup
                .DOFade(0f, timerUrgencyTweenConfig.TimerUrgencyFadeOutDuration)
                .SetEase(timerUrgencyTweenConfig.TimerUrgencyFadeOutEase)
                .SetLink(timerUrgencyFrame, LinkBehaviour.KillOnDisable)
                .SetAutoKill(true)
                .OnComplete(() =>
                {
                    if (timerUrgencyFrame != null && !timerUrgencyFrameVisible)
                        timerUrgencyFrame.SetActive(false);
                });
        }

        private void SetTimerTextUrgencyPulseVisible(bool visible)
        {
            if (timerTextRect == null ||
                timerUrgencyTweenConfig == null ||
                timerTextUrgencyPulseVisible == visible)
                return;

            timerTextUrgencyPulseVisible = visible;
            timerTextUrgencyPulseTween?.Kill();

            if (!visible)
            {
                timerTextRect.localScale = timerTextInitialScale;
                return;
            }

            timerTextUrgencyPulseTween = DOTween.Sequence()
                .Append(
                    timerTextRect
                        .DOScale(
                            timerTextInitialScale * timerUrgencyTweenConfig.TimerUrgencyTextPulseScale,
                            timerUrgencyTweenConfig.TimerUrgencyTextPulseHalfDuration)
                        .SetEase(timerUrgencyTweenConfig.TimerUrgencyTextPulseEase))
                .Append(
                    timerTextRect
                        .DOScale(
                            timerTextInitialScale,
                            timerUrgencyTweenConfig.TimerUrgencyTextPulseHalfDuration)
                        .SetEase(timerUrgencyTweenConfig.TimerUrgencyTextPulseEase))
                .AppendInterval(timerUrgencyTweenConfig.TimerUrgencyTextPulseWaitDuration)
                .SetLoops(-1)
                .SetLink(timerText.gameObject, LinkBehaviour.KillOnDisable)
                .SetAutoKill(true);
        }

        private void UpdateTimerSlider(float timeRemaining)
        {
            if (timerSlider == null || board == null)
                return;

            float timeLimit = Mathf.Max(0f, board.TimeLimit);
            if (!Mathf.Approximately(configuredSliderTimeLimit, timeLimit))
            {
                configuredSliderTimeLimit = timeLimit;
                timerSlider.minValue = 0f;
                timerSlider.maxValue = timeLimit;
            }

            timerSlider.SetValueWithoutNotify(Mathf.Clamp(timeRemaining, 0f, timeLimit));
        }

        private void UpdateTimerDisplay(float timeRemaining)
        {
            if (timerText == null) return;

            int totalSeconds = Mathf.CeilToInt(timeRemaining);
            if (totalSeconds == lastDisplayedSecond)
                return;

            lastDisplayedSecond = totalSeconds;
            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;
            timerText.text = $"{minutes:00}:{seconds:00}";
            
            if (totalSeconds <= Mathf.CeilToInt(timerUrgencyThresholdSeconds) && totalSeconds > 0)
            {
                timerText.color = Color.red;
            }
            else
            {
                // Keep the authored black timer colour until the urgency window.
                timerText.color = Color.black;
            }
        }

        public void ShowFailPopup()
        {
            IsGameOver = true;
            continueOfferIsActive = false;

            if (keepOnPlayingPanel != null)
                keepOnPlayingPanel.SetActive(false);

            SetTimerUrgencyFrameVisible(false);
            SetTimerTextUrgencyPulseVisible(false);
            
            if (failPopupPanel != null)
                failPopupPanel.SetActive(true);
        }

        private void ShowKeepOnPlayingPopup()
        {
            IsGameOver = true;
            continueOfferIsActive = true;
            SetTimerUrgencyFrameVisible(false);
            SetTimerTextUrgencyPulseVisible(false);

            if (failPopupPanel != null)
                failPopupPanel.SetActive(false);

            if (keepOnPlayingPanel != null)
                keepOnPlayingPanel.SetActive(true);
        }

        /// <summary>
        /// Handles the keep-on-playing panel's live-adds button. Advertising
        /// completion can call this same method after its reward is granted.
        /// </summary>
        public void OnLiveAddsClicked()
        {
            if (!continueOfferIsActive || board == null)
                return;

            if (!board.TryResumeAfterTimerExpiry(continueTimeSeconds))
                return;

            continueOfferIsActive = false;
            levelFailureHandled = false;
            IsGameOver = false;
            lastDisplayedSecond = -1;
            SetTimerUrgencyFrameVisible(false);
            SetTimerTextUrgencyPulseVisible(false);

            if (keepOnPlayingPanel != null)
                keepOnPlayingPanel.SetActive(false);
        }

        private void OnKeepOnPlayingClosed()
        {
            if (continueOfferIsActive)
                ShowFailPopup();
        }

        public void OpenRetryConfirmation()
        {
            if (retryButtonSettingsHandler != null)
                retryButtonSettingsHandler.CloseSettingsPanel();

            if (retryConfirmationPanel != null)
                retryConfirmationPanel.SetActive(true);
        }

        public void CloseRetryConfirmation()
        {
            if (retryConfirmationPanel != null)
                retryConfirmationPanel.SetActive(false);
        }

        // Hook this up to your "Retry" button's OnClick event in the inspector
        public void OnRetryClicked()
        {
            IsGameOver = false;
            // Tell the runtime to skip the main menu and immediately start this level again
            GravityLevelRuntime.RequestRestart();
            // Reload the current level from scratch
            Scene activeScene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(activeScene.name);
        }

        // Hook this up to your "Main Menu" button's OnClick event in the inspector
        public void OnMainMenuClicked()
        {
            IsGameOver = false;
            GravityLevelRuntime.RequestRestart();
            if (!string.IsNullOrEmpty(mainMenuSceneName))
            {
                SceneManager.LoadScene(mainMenuSceneName);
            }
            else
            {
                Scene activeScene = SceneManager.GetActiveScene();
                SceneManager.LoadScene(activeScene.name);
            }
        }
    }
}
