using System;
using UnityEngine;

namespace Arda.GameFoundation.Events
{
    /// <summary>
    /// Scene-reference-friendly event channel. Subscribers must unsubscribe
    /// during their own lifecycle; this asset never owns mutable game state.
    /// </summary>
    [CreateAssetMenu(fileName = "VoidEventChannel", menuName = "Game Foundation/Events/Void")]
    public sealed class VoidEventChannel : ScriptableObject
    {
        public event Action Raised;

        public void Raise()
        {
            Raised?.Invoke();
        }
    }
}
