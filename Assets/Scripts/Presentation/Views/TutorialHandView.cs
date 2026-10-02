using DG.Tweening;
using UnityEngine;

namespace GravityPuzzle.Presentation.Views
{
    /// <summary>
    /// Scene-authored tutorial pointer. Art may replace its sprite hierarchy
    /// freely as long as the root and optional effect references remain bound.
    /// It never interprets input or changes gameplay state.
    /// </summary>
    public sealed class TutorialHandView : MonoBehaviour
    {
        public static TutorialHandView Active { get; private set; }

        [Header("Authored References")]
        [SerializeField] private RectTransform handRoot;
        [SerializeField] private RectTransform rippleRoot;
        [SerializeField] private CanvasGroup rippleCanvasGroup;
        [SerializeField] private GameObject sparkleRoot;
        [SerializeField] private AudioSource tapAudioSource;
        [SerializeField] private AudioClip tapClip;

        [Header("Loop Tuning")]
        [SerializeField] private Vector2 approachOffset = new Vector2(42f, -58f);
        [SerializeField, Min(.01f)] private float approachDuration = .32f;
        [SerializeField, Min(.01f)] private float pressDuration = .1f;
        [SerializeField, Min(.01f)] private float releaseDuration = .16f;
        [SerializeField, Min(0f)] private float loopWaitDuration = .32f;
        [SerializeField, Min(0f)] private float pressDistance = 16f;
        [SerializeField] private Ease approachEase = Ease.OutSine;
        [SerializeField] private Ease pressEase = Ease.InQuad;
        [SerializeField] private Ease releaseEase = Ease.OutSine;

        private Sequence loop;
        private Vector3 authoredScale;

        private void Awake()
        {
            Active = this;
            if (handRoot == null)
                handRoot = transform as RectTransform;
            authoredScale = handRoot != null ? handRoot.localScale : Vector3.one;
            SetEffectsVisible(false);
            SetVisible(false);
        }

        private void OnDisable()
        {
            StopLoop();
        }

        private void OnDestroy()
        {
            StopLoop();
            if (Active == this)
                Active = null;
        }

        public static void ShowForButton(BoosterButton button)
        {
            if (Active == null || button == null || button.ButtonComponent == null)
                return;

            Active.ShowForUiTarget(button.ButtonComponent.transform as RectTransform);
        }

        public static void ShowForBoardTarget(Vector2 worldPosition, Camera gameplayCamera)
        {
            if (Active == null || gameplayCamera == null)
                return;

            Active.ShowAtScreenPosition(gameplayCamera.WorldToScreenPoint(worldPosition));
        }

        public static void Hide()
        {
            if (Active != null)
                Active.SetVisible(false);
        }

        private void ShowForUiTarget(RectTransform target)
        {
            if (target == null)
                return;

            Canvas canvas = target.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            ShowAtScreenPosition(RectTransformUtility.WorldToScreenPoint(camera, target.position));
        }

        private void ShowAtScreenPosition(Vector2 screenPosition)
        {
            RectTransform parent = handRoot != null ? handRoot.parent as RectTransform : null;
            if (parent == null)
                return;

            Canvas canvas = handRoot.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPosition, camera, out Vector2 localTarget))
                return;

            SetVisible(true);
            PlayLoop(localTarget);
        }

        private void PlayLoop(Vector2 targetPosition)
        {
            StopLoop();
            if (handRoot == null)
                return;

            handRoot.anchoredPosition = targetPosition + approachOffset;
            handRoot.localScale = authoredScale;
            Vector2 pressedPosition = targetPosition + Vector2.down * pressDistance;
            loop = DOTween.Sequence()
                .SetLink(gameObject, LinkBehaviour.KillOnDisable)
                .SetAutoKill(true);
            loop.Append(handRoot.DOAnchorPos(targetPosition, approachDuration).SetEase(approachEase));
            loop.Append(handRoot.DOAnchorPos(pressedPosition, pressDuration).SetEase(pressEase));
            loop.AppendCallback(PlayTapEffects);
            loop.Append(handRoot.DOAnchorPos(targetPosition, releaseDuration).SetEase(releaseEase));
            loop.AppendInterval(loopWaitDuration);
            loop.AppendCallback(() => handRoot.anchoredPosition = targetPosition + approachOffset);
            loop.SetLoops(-1, LoopType.Restart);
        }

        private void PlayTapEffects()
        {
            SetEffectsVisible(true);
            if (rippleRoot != null)
            {
                rippleRoot.DOKill();
                rippleRoot.localScale = Vector3.one * .55f;
                rippleRoot.DOScale(Vector3.one * 1.25f, releaseDuration)
                    .SetLink(gameObject, LinkBehaviour.KillOnDisable)
                    .SetAutoKill(true);
            }

            if (rippleCanvasGroup != null)
            {
                rippleCanvasGroup.DOKill();
                rippleCanvasGroup.alpha = 1f;
                rippleCanvasGroup.DOFade(0f, releaseDuration)
                    .SetLink(gameObject, LinkBehaviour.KillOnDisable)
                    .SetAutoKill(true)
                    .OnComplete(() => SetEffectsVisible(false));
            }

            if (tapAudioSource != null && tapClip != null)
                tapAudioSource.PlayOneShot(tapClip);
        }

        private void SetVisible(bool visible)
        {
            if (!visible)
            {
                StopLoop();
                SetEffectsVisible(false);
            }

            if (handRoot != null)
                handRoot.gameObject.SetActive(visible);
        }

        private void SetEffectsVisible(bool visible)
        {
            if (sparkleRoot != null)
                sparkleRoot.SetActive(visible);
            if (rippleRoot != null)
                rippleRoot.gameObject.SetActive(visible);
        }

        private void StopLoop()
        {
            loop?.Kill();
            loop = null;
            if (handRoot != null)
                handRoot.DOKill();
            if (rippleRoot != null)
                rippleRoot.DOKill();
            if (rippleCanvasGroup != null)
                rippleCanvasGroup.DOKill();
        }
    }
}
