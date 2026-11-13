using GravityPuzzle.Infrastructure.Services;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GravityPuzzle.Presentation.Views
{
    /// <summary>Plays the shared button click cue when its interactable Selectable is clicked.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Selectable))]
    public sealed class ButtonClickSound : MonoBehaviour, IPointerClickHandler
    {
        private Selectable selectable;

        private void Awake()
        {
            selectable = GetComponent<Selectable>();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
                return;

            if (selectable != null && selectable.IsInteractable())
                SoundFeedbackRuntime.ButtonClicked();
        }
    }
}
