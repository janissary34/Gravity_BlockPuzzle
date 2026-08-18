using UnityEngine;
using UnityEngine.EventSystems;
using DG.Tweening;
using GravityPuzzle.Config;

namespace GravityPuzzle
{
    /// <summary>
    /// Reusable component that adds smooth, juicy press and click animations to UI buttons using DOTween.
    /// Attach directly to any UI Button or interactive GameObject in the Unity Inspector.
    /// </summary>
    [DisallowMultipleComponent]
    public class JuicyButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        [Header("Presentation Config")]
        [SerializeField] private TweenConfig tweenConfig;

        [Header("Press Animation Settings")]
        [SerializeField, Tooltip("Target scale multiplier when the pointer presses down (e.g. 0.9 for 90%).")]
        private float pressScale = 0.9f;

        [SerializeField, Tooltip("Duration of the press-down scaling animation in seconds.")]
        private float pressDuration = 0.1f;

        [SerializeField, Tooltip("Easing type for the press-down animation.")]
        private Ease pressEase = Ease.OutQuad;

        [Header("Release & Bounce Settings")]
        [SerializeField, Tooltip("If true, uses DOPunchScale for an elastic click bounce; otherwise uses DOScale with releaseEase.")]
        private bool usePunchBounce = true;

        [SerializeField, Tooltip("Punch scale vector added to original scale on release/click.")]
        private Vector3 punchScaleStrength = new Vector3(0.15f, 0.15f, 0f);

        [SerializeField, Tooltip("Duration of the release/click bounce animation in seconds.")]
        private float releaseDuration = 0.25f;

        [SerializeField, Tooltip("Easing type for release animation if punch bounce is disabled.")]
        private Ease releaseEase = Ease.OutBack;

        [SerializeField, Tooltip("Vibrato count for scale punch bounce.")]
        private int punchVibrato = 10;

        [SerializeField, Tooltip("Elasticity factor for scale punch bounce.")]
        private float punchElasticity = 1f;

        [Header("Rotation Punch Settings")]
        [SerializeField, Tooltip("Add a subtle, random rotational punch on click/release.")]
        private bool useRotationPunch = true;

        [SerializeField, Tooltip("Maximum random rotation angle (in degrees) for Z-axis punch.")]
        private float maxRotationAngle = 5f;

        [SerializeField, Tooltip("Vibrato count for rotation punch.")]
        private int rotationVibrato = 8;

        [Header("Time & Safety Settings")]
        [SerializeField, Tooltip("If true, animations continue playing smoothly even when Time.timeScale == 0 (e.g. during pause menus).")]
        private bool ignoreTimeScale = true;

        // Baseline transform parameters recorded on Awake
        private Vector3 originalScale;
        private Quaternion originalRotation;
        private bool isPressed;

        private void Awake()
        {
            // Store native initial transform state
            originalScale = transform.localScale;
            originalRotation = transform.localRotation;
        }

        private void OnDisable()
        {
            // Spam protection / cleanup: kill running tweens and restore native baseline
            KillActiveTweens(complete: false);
            ResetTransform();
            isPressed = false;
        }

        private void OnDestroy()
        {
            KillActiveTweens(complete: false);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            isPressed = true;

            // Spam protection: kill active tweens before starting a new one
            KillActiveTweens(complete: true);

            Vector3 targetScale = Vector3.Scale(originalScale, Vector3.one * pressScale);

            transform.DOScale(targetScale, PressDuration)
                .SetEase(PressEase)
                .SetUpdate(ignoreTimeScale)
                .SetLink(gameObject, LinkBehaviour.KillOnDisable)
                .SetAutoKill(true);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!isPressed) return;
            isPressed = false;

            AnimateRelease();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            // Backup handling if OnPointerClick fires without OnPointerUp state (e.g. key navigation)
            if (isPressed)
            {
                isPressed = false;
                AnimateRelease();
            }
        }

        private void AnimateRelease()
        {
            // Spam protection: clear previous scaling/rotation tweens instantly
            KillActiveTweens(complete: true);

            // 1. Scale Bounce Animation
            if (usePunchBounce)
            {
                transform.localScale = originalScale;
                transform.DOPunchScale(punchScaleStrength, ReleaseDuration, punchVibrato, punchElasticity)
                    .SetUpdate(ignoreTimeScale)
                    .SetLink(gameObject, LinkBehaviour.KillOnDisable)
                    .SetAutoKill(true);
            }
            else
            {
                transform.DOScale(originalScale, ReleaseDuration)
                    .SetEase(ReleaseEase)
                    .SetUpdate(ignoreTimeScale)
                    .SetLink(gameObject, LinkBehaviour.KillOnDisable)
                    .SetAutoKill(true);
            }

            // 2. Subtle Random Rotational Punch Animation
            if (useRotationPunch && maxRotationAngle > 0f)
            {
                transform.localRotation = originalRotation;

                float randomZAngle = Random.Range(-maxRotationAngle, maxRotationAngle);
                if (Mathf.Abs(randomZAngle) < 1f)
                    randomZAngle = Mathf.Sign(randomZAngle == 0 ? 1f : randomZAngle) * 2.5f;

                Vector3 rotationPunchVector = new Vector3(0f, 0f, randomZAngle);

                transform.DOPunchRotation(rotationPunchVector, ReleaseDuration, rotationVibrato)
                    .SetUpdate(ignoreTimeScale)
                    .SetLink(gameObject, LinkBehaviour.KillOnDisable)
                    .SetAutoKill(true);
            }
        }

        /// <summary>
        /// Kills any running DOTween animations on this transform safely.
        /// </summary>
        private void KillActiveTweens(bool complete)
        {
            transform.DOKill(complete);
        }

        private float PressDuration => tweenConfig != null ? tweenConfig.ButtonPressDuration : pressDuration;
        private Ease PressEase => tweenConfig != null ? tweenConfig.ButtonPressEase : pressEase;
        private float ReleaseDuration => tweenConfig != null ? tweenConfig.ButtonReleaseDuration : releaseDuration;
        private Ease ReleaseEase => tweenConfig != null ? tweenConfig.ButtonReleaseEase : releaseEase;

        /// <summary>
        /// Instantly restores the transform to its baseline scale and rotation.
        /// </summary>
        private void ResetTransform()
        {
            transform.localScale = originalScale;
            transform.localRotation = originalRotation;
        }
    }
}
