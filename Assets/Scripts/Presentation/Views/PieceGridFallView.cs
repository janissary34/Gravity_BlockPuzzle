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
        public TweenConfig Config => tweenConfig;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
        }

        private void OnDisable()
        {
            activeFallTween?.Kill();
            activeFallTween = null;
        }

        public bool PlayFallTo(Vector2 targetPosition, Action onComplete)
        {
            if (body == null || tweenConfig == null)
                return false;

            return PlayTo(
                targetPosition,
                tweenConfig.GetGridFallDuration(Vector2.Distance(body.position, targetPosition)),
                onComplete);
        }

        public bool PlayReleaseTo(Vector2 targetPosition, Action onComplete)
        {
            if (body == null || tweenConfig == null)
                return false;

            return PlayTo(
                targetPosition,
                tweenConfig.GetGridReleaseDuration(Vector2.Distance(body.position, targetPosition)),
                onComplete);
        }

        /// <summary>
        /// Presents a normal, already-committed grid fall into the final legal
        /// row above a shredder. It never alters board occupancy; it simply
        /// spends the shredder clearance interval in the incoming motion rather
        /// than as a stationary wait at the destination.
        /// </summary>
        public bool PlayShredderApproachTo(Vector2 targetPosition, Action onComplete)
        {
            if (body == null || tweenConfig == null)
                return false;

            return PlayTo(
                targetPosition,
                tweenConfig.GetShredderApproachDuration(
                    Vector2.Distance(body.position, targetPosition)),
                onComplete);
        }

        private bool PlayTo(Vector2 targetPosition, float duration, Action onComplete)
        {
            activeFallTween?.Kill();
            activeFallTween = body.DOMove(targetPosition, duration)
                .SetUpdate(UpdateType.Fixed)
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

        /// <summary>
        /// Stops a grid-fall presentation when another lifecycle owner takes
        /// control of the piece, such as the shredder handoff.
        /// </summary>
        public void Cancel()
        {
            activeFallTween?.Kill();
            activeFallTween = null;
        }
    }
}
