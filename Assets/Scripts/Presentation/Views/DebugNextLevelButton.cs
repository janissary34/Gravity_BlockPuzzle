using GravityPuzzle;
using UnityEngine;
using UnityEngine.UI;

namespace GravityPuzzle.Presentation.Views
{
    /// <summary>
    /// Build-time test control that advances the active campaign level without
    /// completing the board or granting its completion rewards.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DebugNextLevelButton : MonoBehaviour
    {
        [SerializeField] private Button button;

        private void Awake()
        {
            if (button == null)
                button = GetComponent<Button>();

            if (button == null)
            {
                Debug.LogWarning("[DebugNextLevel] Button reference is missing; control is disabled.", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (button == null)
                return;

            button.onClick.AddListener(AdvanceToNextLevel);
        }

        private void Start()
        {
            RefreshAvailability();
        }

        private void OnDisable()
        {
            if (button != null)
                button.onClick.RemoveListener(AdvanceToNextLevel);
        }

        private void AdvanceToNextLevel()
        {
            PrototypeBoard activeBoard = PrototypeBoard.Active;
            if (activeBoard == null || !activeBoard.TryAdvanceToNextLevelForDebug())
            {
                RefreshAvailability();
                return;
            }

            button.interactable = false;
        }

        private void RefreshAvailability()
        {
            if (button != null)
                button.interactable = PrototypeBoard.Active != null && GravityLevelRuntime.HasNextLevel;
        }
    }
}
