using System;
using DG.Tweening;
using GravityPuzzle.Config;
using UnityEngine;

namespace GravityPuzzle.Presentation.Views
{
    [DisallowMultipleComponent]
    public sealed class PieceGridFallView : MonoBehaviour
    {
        [SerializeField] private TweenConfig tweenConfig;

        private Rigidbody2D body;
        private Tween activeFallTween;

        public bool CanPlay => body != null && tweenConfig != null;
        public bool IsAnimating => activeFallTween != null && activeFallTween.IsActive();

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
        }

        private void OnDisable()
        {
            activeFallTween?.Kill();
            activeFallTween = null;
        }

        public bool PlayTo(Vector2 targetPosition, Action onComplete)
        {
            if (body == null || tweenConfig == null)
                return false;

            activeFallTween?.Kill();
            activeFallTween = body.DOMove(targetPosition, tweenConfig.GridFallDuration)
                .SetEase(tweenConfig.GridFallEase)
                .SetLink(gameObject, LinkBehaviour.KillOnDisable)
                .SetAutoKill(true)
                .OnComplete(() =>
                {
                    activeFallTween = null;
                    onComplete?.Invoke();
                });
            return true;
        }
    }
}
