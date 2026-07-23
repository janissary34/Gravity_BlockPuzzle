using UnityEngine;
using DG.Tweening;

namespace ThreadFever.UI
{
    /// <summary>
    /// Adds a smooth, breathing pulse animation to reward chests or any UI object.
    /// Just attach this script to the object and it will automatically animate.
    /// </summary>
    public class RewardChestJuice : MonoBehaviour
    {
        [Header("Animation Settings")]
        [Tooltip("The maximum scale multiplier for the pulse effect.")]
        [SerializeField] private float _pulseScale = 1.08f;
        
        [Tooltip("The duration of one half of the pulse (growing or shrinking).")]
        [SerializeField] private float _duration = 1.2f;

        private Vector3 _originalScale;

        private void Start()
        {
            // Store the original scale so it scales relative to its initial size in the scene
            _originalScale = transform.localScale;

            // Start the breathing animation
            transform.DOScale(_originalScale * _pulseScale, _duration)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo);
        }

        private void OnDestroy()
        {
            // Safely kill all tweens on this object to prevent memory leaks or orphan tweens
            // when the object is destroyed or the scene changes.
            transform.DOKill();
        }
    }
}
