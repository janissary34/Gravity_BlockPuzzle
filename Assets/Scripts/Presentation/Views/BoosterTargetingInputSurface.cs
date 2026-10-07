using UnityEngine;
using UnityEngine.EventSystems;

namespace GravityPuzzle.Presentation.Views
{
    /// <summary>
    /// UI-owned input surface that forwards a targeting tap to the currently
    /// armed board booster. It keeps targeting input in the EventSystem path,
    /// which is required by the Device Simulator and avoids treating a UI tap
    /// as a board drag.
    /// </summary>
    public sealed class BoosterTargetingInputSurface : MonoBehaviour, IPointerDownHandler
    {
        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
                return;

            if (HammerBooster.TryHandleTargetingPointer(eventData.position))
                return;

            RocketBooster.TryHandleTargetingPointer(eventData.position);
        }
    }
}
