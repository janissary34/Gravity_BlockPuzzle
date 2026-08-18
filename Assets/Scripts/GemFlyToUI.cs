using System;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using GravityPuzzle.Config;

namespace GravityPuzzle
{
    /// <summary>
    /// Controls the 3-stage gem attraction pipeline when a gem is liberated from a block:
    /// Stage 1: Physical pop-out jump at the shredder impact zone.
    /// Stage 2: Disables physics components for low mobile overhead.
    /// Stage 3: Smooth magnetic fly to top UI slider bar with world-to-screen conversion and UI bounce feedback.
    /// </summary>
    [DisallowMultipleComponent]
    public class GemFlyToUI : MonoBehaviour
    {
        [Header("Physics & Motion Setup")]
        [SerializeField, Tooltip("Rigidbody2D component attached to this gem voxel.")]
        private Rigidbody2D rb2D;

        [SerializeField, Tooltip("Collider2D component attached to this gem voxel.")]
        private Collider2D col2D;

        [SerializeField, Tooltip("SpriteRenderer component attached to this gem voxel.")]
        private SpriteRenderer spriteRenderer;

        [SerializeField, Tooltip("Owns the default UI-flight tween timing and easing.")]
        private TweenConfig tweenConfig;

        [Header("Stage Timings & Easing")]
        [SerializeField, Tooltip("Initial pop-out jump power/height.")]
        private float popPower = 1.2f;

        [SerializeField, Tooltip("Duration of initial physical pop jump before magnetic fly mode (0 = no jump).")]
        private float popDuration = 0.0f;

        [SerializeField, Tooltip("Duration of the flight from shredder to UI target.")]
        private float flyDuration = 0.75f;

        [SerializeField, Tooltip("Ease curve for magnetic flight to UI bar.")]
        private Ease flyEase = Ease.InBack;

        [Header("UI Feedback")]
        [SerializeField, Tooltip("Punch scale strength applied to target UI bar on arrival.")]
        private Vector3 uiPunchScale = new Vector3(0.18f, 0.18f, 0f);

        [SerializeField, Tooltip("Duration of UI punch bounce effect on collection.")]
        private float uiPunchDuration = 0.22f;

        [SerializeField, Tooltip("Amount by which target Slider value increases per collected gem voxel.")]
        private float sliderValueGain = 1f;

        private RectTransform targetRectTransform;
        private Slider targetSlider;
        private Camera targetCamera;
        private Action<GemFlyToUI> onRecycleCallback;

        private Tween activeJumpTween;
        private Tweener activeFlyTween;
        private Vector3 defaultScale = Vector3.one;
        private float activeFlightDuration;
        private Ease activeFlightEase;

        private float DefaultFlightDuration => tweenConfig != null
            ? tweenConfig.GemFlightDuration
            : flyDuration;

        private Ease DefaultFlightEase => tweenConfig != null
            ? tweenConfig.GemFlightEase
            : flyEase;

        private float UiPunchDuration => tweenConfig != null
            ? tweenConfig.GemUiPunchDuration
            : uiPunchDuration;

        private int UiPunchVibrato => tweenConfig != null
            ? tweenConfig.GemUiPunchVibrato
            : 5;

        private float UiPunchElasticity => tweenConfig != null
            ? tweenConfig.GemUiPunchElasticity
            : .5f;

        private void Awake()
        {
            if (rb2D == null) rb2D = GetComponent<Rigidbody2D>();
            if (col2D == null) col2D = GetComponent<Collider2D>();
            if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
            defaultScale = transform.localScale;
        }

        /// <summary>
        /// Initializes and starts the 3-stage gem attraction pipeline.
        /// </summary>
        public void Launch(
            Vector3 startWorldPos,
            Vector2 popVector,
            RectTransform uiTargetRect,
            Slider uiTargetSlider,
            Camera cam,
            float customFlyDuration,
            Ease customFlyEase,
            Action<GemFlyToUI> onRecycle)
        {
            // Kill existing tweens if recycled
            KillTweens();

            transform.position = startWorldPos;
            transform.localScale = defaultScale;
            if (spriteRenderer != null)
            {
                Color c = spriteRenderer.color;
                c.a = 1f;
                spriteRenderer.color = c;
            }

            targetRectTransform = uiTargetRect;
            targetSlider = uiTargetSlider;
            targetCamera = cam != null ? cam : Camera.main;
            onRecycleCallback = onRecycle;
            activeFlightDuration = customFlyDuration > 0f
                ? customFlyDuration
                : DefaultFlightDuration;
            activeFlightEase = customFlyEase;

            // STAGE 1: Physical Pop-Out Bounce
            EnablePhysics(true);
            if (rb2D != null)
            {
                rb2D.velocity = popVector;
                rb2D.angularVelocity = UnityEngine.Random.Range(-360f, 360f);
            }

            if (popDuration > 0f)
                activeJumpTween = DOVirtual.DelayedCall(popDuration, StartMagnetFlyToUI)
                    .SetLink(gameObject, LinkBehaviour.KillOnDisable)
                    .SetAutoKill(true);
            else
                StartMagnetFlyToUI();
        }

        // STAGE 2 & 3: Disable physics and fly to UI
        private void StartMagnetFlyToUI()
        {
            // Low-overhead optimization: Disable physics components while flying
            EnablePhysics(false);
            
            if (spriteRenderer != null)
                spriteRenderer.sortingOrder = 30; // Bring to front for UI flight

            if (targetRectTransform == null)
            {
                RecycleSelf();
                return;
            }

            // STAGE 3: World-to-UI Screen Conversion
            Vector3 targetWorldPos = GetTargetWorldPosition();

            // Animate flight to target UI position using DOMove with Ease.InBack
            activeFlyTween = transform.DOMove(targetWorldPos, activeFlightDuration)
                .SetEase(activeFlightEase)
                .SetLink(gameObject, LinkBehaviour.KillOnDisable)
                .SetAutoKill(true)
                .OnUpdate(() =>
                {
                    // Dynamically update target position during flight in case canvas scales or UI moves
                    if (targetRectTransform != null && activeFlyTween != null && activeFlyTween.IsActive())
                    {
                        Vector3 currentTargetWorld = GetTargetWorldPosition();
                        activeFlyTween.ChangeEndValue(currentTargetWorld, true);
                    }
                })
                .OnComplete(OnReachedUI);
        }

        private Vector3 GetTargetWorldPosition()
        {
            if (targetRectTransform == null)
                return transform.position;

            Canvas rootCanvas = targetRectTransform.GetComponentInParent<Canvas>();
            Camera cam = (rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
                ? (rootCanvas.worldCamera != null ? rootCanvas.worldCamera : targetCamera)
                : targetCamera;

            if (cam == null) cam = Camera.main;

            Vector3 uiScreenPoint = RectTransformUtility.WorldToScreenPoint(cam, targetRectTransform.position);
            Vector3 worldPoint = cam != null
                ? cam.ScreenToWorldPoint(new Vector3(uiScreenPoint.x, uiScreenPoint.y, Mathf.Abs(cam.transform.position.z - transform.position.z)))
                : targetRectTransform.position;

            worldPoint.z = transform.position.z;
            return worldPoint;
        }

        private void OnReachedUI()
        {
            // Trigger UI progress and bounce feedback
            if (targetSlider != null)
            {
                targetSlider.value += sliderValueGain;
            }

            if (targetRectTransform != null)
            {
                targetRectTransform.DOPunchScale(uiPunchScale, UiPunchDuration, UiPunchVibrato, UiPunchElasticity)
                    .SetLink(targetRectTransform.gameObject, LinkBehaviour.KillOnDisable)
                    .SetAutoKill(true);
            }

            RecycleSelf();
        }

        private void EnablePhysics(bool enable)
        {
            if (rb2D != null)
            {
                rb2D.simulated = enable;
                if (!enable) rb2D.velocity = Vector2.zero;
            }
            if (col2D != null)
            {
                col2D.enabled = enable;
            }
        }

        private void RecycleSelf()
        {
            KillTweens();
            EnablePhysics(false);
            gameObject.SetActive(false);
            onRecycleCallback?.Invoke(this);
        }

        private void KillTweens()
        {
            if (activeJumpTween != null && activeJumpTween.IsActive())
            {
                activeJumpTween.Kill();
                activeJumpTween = null;
            }
            if (activeFlyTween != null && activeFlyTween.IsActive())
            {
                activeFlyTween.Kill();
                activeFlyTween = null;
            }
        }

        private void OnDisable()
        {
            KillTweens();
        }
    }
}
