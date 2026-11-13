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
        private Tween _pulseTween;
        private bool _hasOriginalScale;

        private void OnEnable()
        {
            _originalScale = transform.localScale;
            _hasOriginalScale = true;
            _pulseTween = transform.DOScale(_originalScale * _pulseScale, _duration)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetLink(gameObject, LinkBehaviour.KillOnDisable);
        }

        private void OnDisable()
        {
            StopPulse();
        }

        private void OnDestroy()
        {
            StopPulse();
        }

        private void StopPulse()
        {
            if (_pulseTween != null && _pulseTween.IsActive())
                _pulseTween.Kill();

            _pulseTween = null;
            if (_hasOriginalScale)
                transform.localScale = _originalScale;
        }
    }
}
