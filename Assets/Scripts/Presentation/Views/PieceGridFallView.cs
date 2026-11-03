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

        private Tween activeFallTween;

        // Grid pieces deliberately have a non-simulated Rigidbody2D. Grid
        // movement is therefore presentation-only and must never depend on a
        // Rigidbody tween being simulated by Physics2D.
        public bool CanPlay => tweenConfig != null;
        public bool IsAnimating => activeFallTween != null && activeFallTween.IsActive();
        public TweenConfig Config => tweenConfig;

        /// <summary>
        /// The most recent downward grid-presentation rate. This is visual
        /// continuity data only: the shredder may use it as the initial
        /// velocity for a later physics handoff, but grid rules never depend
        /// on it.
        /// </summary>
        public float LastDownwardPresentationSpeed { get; private set; }

        private void OnDisable()
        {
            activeFallTween?.Kill();
            activeFallTween = null;
            LastDownwardPresentationSpeed = 0f;
        }

        public bool PlayFallTo(Vector2 targetPosition, Action onComplete)
        {
            if (tweenConfig == null)
                return false;

            return PlayTo(
                targetPosition,
                tweenConfig.GetGridFallDuration(Vector2.Distance(transform.position, targetPosition)),
                onComplete);
        }

        public bool PlayReleaseTo(Vector2 targetPosition, Action onComplete)
        {
            if (tweenConfig == null)
                return false;

            return PlayTo(
                targetPosition,
                tweenConfig.GetGridReleaseDuration(Vector2.Distance(transform.position, targetPosition)),
                onComplete);
        }

        /// <summary>
        /// Extends an active gravity presentation after more legal board space
        /// became available below it. Gameplay commits the new target before
        /// this presentation-only retarget occurs.
        /// </summary>
        public bool RetargetReleaseTo(Vector2 targetPosition, Action onComplete)
        {
            if (!IsAnimating || tweenConfig == null)
                return false;

            return PlayTo(
                targetPosition,
                tweenConfig.GetGridReleaseDuration(Vector2.Distance(transform.position, targetPosition)),
                onComplete);
        }

        private bool PlayTo(Vector2 targetPosition, float duration, Action onComplete)
        {
            activeFallTween?.Kill();
            Vector3 start = transform.position;
            Vector3 target = new Vector3(targetPosition.x, targetPosition.y, transform.position.z);

            // Preserve the authored grid-fall tempo across the later
            // Grid-to-OpenFreefall ownership handoff. A non-downward move is
            // not a falling velocity and must not be reused at that boundary.
            LastDownwardPresentationSpeed = target.y < start.y
                ? (start.y - target.y) / Mathf.Max(duration, Mathf.Epsilon)
                : 0f;

            activeFallTween = transform.DOMove(target, duration)
                // This shares the same transform-based movement path as drag.
                // Physics only owns the later OpenFreefall/Shredding phases.
                .SetUpdate(UpdateType.Normal)
                .SetEase(tweenConfig.GridFallEase)
                .SetLink(gameObject, LinkBehaviour.KillOnDisable)
                .SetAutoKill(true)
                .OnComplete(() =>
                {
                    activeFallTween = null;
                    transform.position = target;
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
