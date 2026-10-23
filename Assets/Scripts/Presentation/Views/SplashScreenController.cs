using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GravityPuzzle.Presentation.Views
{
    /// <summary>
    /// Owns the splash screen presentation and persistent level-transition overlay.
    /// It survives across scenes via DontDestroyOnLoad so level changes can display
    /// the loading cover without needing an extra intermediate scene load.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SplashScreenController : MonoBehaviour
    {
        public static SplashScreenController Instance { get; private set; }

        [Header("Presentation")]
        [SerializeField] private Image loadingBarFill;
        [SerializeField] private Canvas rootCanvas;

        [Header("Scene Handoff")]
        [SerializeField] private string destinationSceneName = "Scene1";
        [SerializeField, Min(0f)] private float minimumDisplaySeconds = 0f;

        private Coroutine activeTransitionRoutine;
        private bool isLevelReady;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (rootCanvas == null)
                rootCanvas = GetComponent<Canvas>();

            if (rootCanvas != null)
                rootCanvas.sortingOrder = 999;

            SetProgress(0f);
        }

        private IEnumerator Start()
        {
            // If already running a transition, do not duplicate
            if (activeTransitionRoutine != null)
                yield break;

            yield return null;

            if (string.IsNullOrWhiteSpace(destinationSceneName) ||
                !Application.CanStreamedLevelBeLoaded(destinationSceneName))
            {
                Debug.LogError(
                    $"[Splash] Destination scene '{destinationSceneName}' is not available in Build Settings.",
                    this);
                yield break;
            }

            activeTransitionRoutine = StartCoroutine(ExecuteTransition(destinationSceneName));
        }

        /// <summary>
        /// Transitions to the specified scene using the persistent loading cover.
        /// </summary>
        public void TransitionToScene(string targetScene)
        {
            if (string.IsNullOrWhiteSpace(targetScene))
                return;

            if (activeTransitionRoutine != null)
                StopCoroutine(activeTransitionRoutine);

            activeTransitionRoutine = StartCoroutine(ExecuteTransition(targetScene));
        }

        /// <summary>
        /// Called by the scene composition root when the board and level runtime
        /// have finished building and the level is ready for user interaction.
        /// </summary>
        public void NotifyLevelReady()
        {
            isLevelReady = true;
        }

        private IEnumerator ExecuteTransition(string targetScene)
        {
            isLevelReady = false;
            SetProgress(0f);

            if (rootCanvas != null)
            {
                rootCanvas.enabled = true;
                rootCanvas.sortingOrder = 999;
            }

            // Ensure the loading screen is drawn before the active scene is torn down
            yield return new WaitForEndOfFrame();

            AsyncOperation loadOperation = SceneManager.LoadSceneAsync(targetScene);
            if (loadOperation == null)
            {
                Debug.LogError($"[Splash] Unable to begin loading '{targetScene}'.", this);
                if (rootCanvas != null)
                    rootCanvas.enabled = false;
                activeTransitionRoutine = null;
                yield break;
            }

            float elapsedSeconds = 0f;

            while (!loadOperation.isDone)
            {
                elapsedSeconds += Time.unscaledDeltaTime;
                float loadProgress = Mathf.Clamp01(loadOperation.progress / 0.9f);
                float displayProgress = minimumDisplaySeconds <= 0f
                    ? loadProgress
                    : Mathf.Min(loadProgress, elapsedSeconds / minimumDisplaySeconds);
                SetProgress(displayProgress);
                yield return null;
            }

            SetProgress(1f);

            // Wait until the level runtime signals it is ready (or 0.5s safety timeout)
            float waitTimeout = 0.5f;
            while (!isLevelReady && waitTimeout > 0f)
            {
                waitTimeout -= Time.unscaledDeltaTime;
                yield return null;
            }

            // Brief frame buffer so the newly initialized board renders cleanly
            yield return null;

            if (rootCanvas != null)
                rootCanvas.enabled = false;

            activeTransitionRoutine = null;
        }

        private void SetProgress(float progress)
        {
            if (loadingBarFill != null)
                loadingBarFill.fillAmount = Mathf.Clamp01(progress);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }
    }
}
