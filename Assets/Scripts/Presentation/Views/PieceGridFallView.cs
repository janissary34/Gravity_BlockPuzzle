using System;
using DG.Tweening;
using DG.Tweening.Core;
using GravityPuzzle.Config;
using UnityEngine;

namespace GravityPuzzle.Presentation.Views
{
    [DisallowMultipleComponent]
    public sealed class PieceGridFallView : MonoBehaviour
    {
        [SerializeField] private TweenConfig tweenConfig;

        private Tween activeFallTween;
        private Action pendingOnComplete;
        private Vector3 fallStart;
        private Vector3 fallRunOrigin;
        private Vector3 fallTarget;
        private Vector3 fallDirection;
        private float fallDistance;
        private float fallStartSpeed;
        private float fallElapsed;
        private float arrivalSpeed;
        private int arrivalFrame = -1;

        private DOGetter<float> elapsedGetter;
        private DOSetter<float> elapsedSetter;
        private TweenCallback completeCallback;

        // Grid pieces deliberately have a non-simulated Rigidbody2D. Grid
        // movement is therefore presentation-only and must never depend on a
        // Rigidbody tween being simulated by Physics2D.
        public bool CanPlay => tweenConfig != null;
        public bool IsAnimating => activeFallTween != null && activeFallTween.IsActive();
        public TweenConfig Config => tweenConfig;

        /// <summary>
        /// Straight-line distance of the current fall run, known when the fall
        /// starts and grown by retargets. Lets presentation decide up front
        /// whether a fall is long enough for effects such as the fall trail.
        /// </summary>
        public float PlannedFallDistance => IsAnimating ? Vector3.Distance(fallRunOrigin, fallTarget) : 0f;

        /// <summary>
        /// Downward speed with which the most recent fall reached its target,
        /// valid only in the frame it arrived. Lets an immediate shredder
        /// handoff continue the fall without a visible speed drop.
        /// </summary>
        public float ArrivalSpeedThisFrame => arrivalFrame == Time.frameCount ? arrivalSpeed : 0f;

        private void Awake()
        {
            elapsedGetter = () => fallElapsed;
            elapsedSetter = ApplyElapsed;
            completeCallback = CompleteFall;
        }

        private void OnDisable()
        {
            Cancel();
            arrivalFrame = -1;
        }

        public bool PlayFallTo(Vector2 targetPosition, Action onComplete)
        {
            if (tweenConfig == null)
                return false;

            fallRunOrigin = transform.position;
            return PlayTo(targetPosition, tweenConfig.GridFallStartSpeed, onComplete);
        }

        public bool PlayReleaseTo(Vector2 targetPosition, Action onComplete)
        {
            if (tweenConfig == null)
                return false;

            fallRunOrigin = transform.position;
            return PlayTo(targetPosition, tweenConfig.GridReleaseStartSpeed, onComplete);
        }

        /// <summary>
        /// Extends an active gravity presentation after more legal board space
        /// became available below it. Gameplay commits the new target before
        /// this presentation-only retarget occurs. The current fall speed is
        /// preserved so the piece keeps accelerating.
        /// </summary>
        public bool RetargetReleaseTo(Vector2 targetPosition, Action onComplete)
        {
            if (!IsAnimating || tweenConfig == null)
                return false;

            return PlayTo(
                targetPosition,
                tweenConfig.GetGridFallSpeed(fallElapsed, fallStartSpeed),
                onComplete);
        }

        private bool PlayTo(Vector2 targetPosition, float startSpeed, Action onComplete)
        {
            activeFallTween?.Kill();
            fallStart = transform.position;
            fallTarget = new Vector3(targetPosition.x, targetPosition.y, fallStart.z);
            Vector3 delta = fallTarget - fallStart;
            fallDistance = delta.magnitude;
            fallDirection = fallDistance > 0f ? delta / fallDistance : Vector3.zero;
            fallStartSpeed = startSpeed;
            fallElapsed = 0f;
            pendingOnComplete = onComplete;
            arrivalFrame = -1;

            float duration = tweenConfig.GetGridFallDuration(fallDistance, startSpeed);
            activeFallTween = DOTween.To(elapsedGetter, elapsedSetter, duration, duration)
                // This shares the same transform-based movement path as drag.
                // Physics only owns the later OpenFreefall/Shredding phases.
                .SetUpdate(UpdateType.Normal)
                .SetEase(Ease.Linear)
                .SetLink(gameObject, LinkBehaviour.KillOnDisable)
                .SetAutoKill(true)
                .OnComplete(completeCallback);
            return true;
        }

        private void ApplyElapsed(float elapsed)
        {
            fallElapsed = elapsed;
            float travelled = Mathf.Min(
                fallDistance,
                tweenConfig.GetGridFallDistance(elapsed, fallStartSpeed));
            transform.position = fallStart + fallDirection * travelled;
        }

        private void CompleteFall()
        {
            arrivalSpeed = tweenConfig.GetGridFallSpeed(fallElapsed, fallStartSpeed) * Mathf.Max(0f, -fallDirection.y);
            arrivalFrame = Time.frameCount;
            activeFallTween = null;
            transform.position = fallTarget;
            Action onComplete = pendingOnComplete;
            pendingOnComplete = null;
            onComplete?.Invoke();
        }

        /// <summary>
        /// Stops a grid-fall presentation when another lifecycle owner takes
        /// control of the piece, such as the shredder handoff.
        /// </summary>
        public void Cancel()
        {
            activeFallTween?.Kill();
            activeFallTween = null;
            pendingOnComplete = null;
        }
    }
}
