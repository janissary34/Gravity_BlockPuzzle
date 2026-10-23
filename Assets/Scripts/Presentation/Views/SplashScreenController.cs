using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GravityPuzzle.Presentation.Views
{
    /// <summary>
    /// Owns the splash screen's presentation and the one-time asynchronous
    /// handoff to the authored gameplay scene. It never initializes gameplay.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SplashScreenController : MonoBehaviour
    {
        [Header("Presentation")]
        [SerializeField] private Image loadingBarFill;

        [Header("Scene Handoff")]
        [SerializeField] private string destinationSceneName = "Scene1";
        [SerializeField, Min(0f)] private float minimumDisplaySeconds = 0.6f;

        private void Awake()
        {
            SetProgress(0f);
        }

        private IEnumerator Start()
        {
            // Waiting one rendered frame guarantees the splash artwork is
            // visible before the target scene starts consuming load time.
            yield return null;

            if (string.IsNullOrWhiteSpace(destinationSceneName) ||
                !Application.CanStreamedLevelBeLoaded(destinationSceneName))
            {
                Debug.LogError(
                    $"[Splash] Destination scene '{destinationSceneName}' is not available in Build Settings.",
                    this);
                yield break;
            }

            AsyncOperation loadOperation = SceneManager.LoadSceneAsync(destinationSceneName);
            if (loadOperation == null)
            {
                Debug.LogError($"[Splash] Unable to begin loading '{destinationSceneName}'.", this);
                yield break;
            }

            loadOperation.allowSceneActivation = false;
            float elapsedSeconds = 0f;

            while (!loadOperation.isDone)
            {
                elapsedSeconds += Time.unscaledDeltaTime;
                float loadProgress = Mathf.Clamp01(loadOperation.progress / 0.9f);
                float displayProgress = minimumDisplaySeconds <= 0f
                    ? loadProgress
                    : Mathf.Min(loadProgress, elapsedSeconds / minimumDisplaySeconds);
                SetProgress(displayProgress);

                if (loadProgress >= 1f && elapsedSeconds >= minimumDisplaySeconds)
                {
                    SetProgress(1f);
                    loadOperation.allowSceneActivation = true;
                }

                yield return null;
            }
        }

        private void SetProgress(float progress)
        {
            if (loadingBarFill != null)
                loadingBarFill.fillAmount = Mathf.Clamp01(progress);
        }
    }
}
