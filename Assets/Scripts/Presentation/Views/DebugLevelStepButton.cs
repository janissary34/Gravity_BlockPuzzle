using GravityPuzzle;
using UnityEngine;
using UnityEngine.UI;

namespace GravityPuzzle.Presentation.Views
{
    /// <summary>
    /// Build-time test control that moves the active campaign to the next or
    /// previous level without completing the board or granting its rewards.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DebugLevelStepButton : MonoBehaviour
    {
        [SerializeField] private Button button;
        [SerializeField] private GravityLevelRuntime.LevelStep step = GravityLevelRuntime.LevelStep.Next;
        private bool transitionStarted;

        private void Awake()
        {
            if (button == null)
                button = GetComponent<Button>();

            if (button == null)
            {
                Debug.LogWarning("[DebugLevelStep] Button reference is missing; control is disabled.", this);
                enabled = false;
                return;
            }

            // Ensure the button has an enabled, raycastable graphic so user clicks are received
            Image image = GetComponent<Image>();
            if (image != null)
            {
                image.enabled = true;
                image.raycastTarget = true;
                button.targetGraphic = image;
            }

            Graphic[] childGraphics = GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < childGraphics.Length; i++)
            {
                if (childGraphics[i] != null)
                    childGraphics[i].raycastTarget = true;
            }

            button.interactable = true;
        }

        private void OnEnable()
        {
            if (button == null)
                return;

            button.onClick.AddListener(StepLevel);
            RefreshAvailability();
        }

        private void Start()
        {
            RefreshAvailability();
        }

        private void Update()
        {
            if (button != null && !button.interactable && !transitionStarted)
                RefreshAvailability();
        }

        private void OnDisable()
        {
            if (button != null)
                button.onClick.RemoveListener(StepLevel);
        }

        private void StepLevel()
        {
            if (transitionStarted)
                return;

            transitionStarted = true;
            if (button != null)
                button.interactable = false;

            PrototypeBoard activeBoard = PrototypeBoard.Active;
            if (activeBoard != null)
            {
                if (!activeBoard.TryStepLevelForDebug(step))
                {
                    transitionStarted = false;
                    RefreshAvailability();
                    return;
                }
            }
            else
            {
                if (!GravityLevelRuntime.TryStepLevel(step))
                {
                    transitionStarted = false;
                    RefreshAvailability();
                    return;
                }

                UnityEngine.SceneManagement.Scene activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                Infrastructure.Services.SplashSceneLoader.LoadViaSplash(activeScene.name);
            }
        }

        private void RefreshAvailability()
        {
            if (button != null && !transitionStarted)
                button.interactable = GravityLevelRuntime.HasNextLevel;
        }
    }
}
