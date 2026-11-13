using GravityPuzzle.Infrastructure.Services;
using UnityEngine;
using UnityEngine.UI;

namespace GravityPuzzle.Presentation.Views
{
    /// <summary>
    /// Plays the shared button click cue from the Button's own onClick, so the cue still plays when a
    /// click handler disables the button in the same frame (e.g. the win panel's Claim button).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button))]
    public sealed class ButtonClickSound : MonoBehaviour
    {
        private Button button;

        private void Awake()
        {
            button = GetComponent<Button>();
        }

        private void OnEnable()
        {
            if (button != null)
                button.onClick.AddListener(SoundFeedbackRuntime.ButtonClicked);
        }

        private void OnDisable()
        {
            if (button != null)
                button.onClick.RemoveListener(SoundFeedbackRuntime.ButtonClicked);
        }
    }
}
