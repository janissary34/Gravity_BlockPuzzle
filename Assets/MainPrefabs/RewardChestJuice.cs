using UnityEngine;
using UnityEngine.UI;
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

        [Header("Optional Glow Alpha Pulse")]
        [Tooltip("When enabled alongside an alpha target, fades the graphic alpha in lockstep with the scale pulse.")]
        [SerializeField] private bool _pulseAlpha;

        [Tooltip("Optional UI Graphic whose alpha pulses synchronously with the scale beat.")]
        [SerializeField] private Graphic _alphaTarget;

        [Tooltip("Target alpha at the peak of each beat.")]
        [Range(0f, 1f)]
        [SerializeField] private float _peakAlpha = 0.95f;

        private Vector3 _originalScale;
        private float _originalAlpha;
        private Tween _pulseTween;
        private Tween _alphaTween;
        private bool _hasOriginalScale;
        private bool _hasOriginalAlpha;

        private void Awake()
        {
            CacheRestState();
        }

        private void OnEnable()
        {
            StopPulse();
            CacheRestState();

            transform.localScale = _originalScale;

            _pulseTween = transform.DOScale(_originalScale * _pulseScale, _duration)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(true)
                .SetLink(gameObject, LinkBehaviour.KillOnDisable)
                .SetAutoKill(true);

            if (_pulseAlpha && _alphaTarget != null)
            {
                SetGraphicAlpha(_alphaTarget, _originalAlpha);
                _alphaTween = _alphaTarget.DOFade(_peakAlpha, _duration)
                    .SetEase(Ease.InOutSine)
                    .SetLoops(-1, LoopType.Yoyo)
                    .SetUpdate(true)
                    .SetLink(gameObject, LinkBehaviour.KillOnDisable)
                    .SetAutoKill(true);
            }
        }

        private void OnDisable()
        {
            StopPulse();
        }

        private void OnDestroy()
        {
            StopPulse();
        }

        private void CacheRestState()
        {
            if (!_hasOriginalScale)
            {
                _originalScale = transform.localScale;
                _hasOriginalScale = true;
            }

            if (!_hasOriginalAlpha && _alphaTarget != null)
            {
                _originalAlpha = _alphaTarget.color.a;
                _hasOriginalAlpha = true;
            }
        }

        private void StopPulse()
        {
            if (_pulseTween != null && _pulseTween.IsActive())
                _pulseTween.Kill();

            _pulseTween = null;

            if (_alphaTween != null && _alphaTween.IsActive())
                _alphaTween.Kill();

            _alphaTween = null;

            if (_hasOriginalScale)
                transform.localScale = _originalScale;

            if (_hasOriginalAlpha && _alphaTarget != null)
                SetGraphicAlpha(_alphaTarget, _originalAlpha);
        }

        private static void SetGraphicAlpha(Graphic graphic, float alpha)
        {
            Color color = graphic.color;
            color.a = alpha;
            graphic.color = color;
        }
    }
}
