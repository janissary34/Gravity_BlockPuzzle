using UnityEngine;
using UnityEngine.SceneManagement;
using GravityPuzzle.Presentation.Views;

namespace GravityPuzzle.Infrastructure.Services
{
    /// <summary>
    /// Routes level transitions and reloads through the persistent loading cover
    /// without loading an intermediate Splash scene.
    /// </summary>
    public static class SplashSceneLoader
    {
        public static void LoadViaSplash(string destinationSceneName)
        {
            if (string.IsNullOrWhiteSpace(destinationSceneName))
            {
                Debug.LogWarning("[SplashSceneLoader] Destination scene name is empty; load ignored.");
                return;
            }

            if (SplashScreenController.Instance != null)
            {
                SplashScreenController.Instance.TransitionToScene(destinationSceneName);
                return;
            }

            SceneManager.LoadScene(destinationSceneName);
        }
    }
}
