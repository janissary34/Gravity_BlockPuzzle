using DG.Tweening;
using UnityEngine;
using GravityPuzzle;
using GravityPuzzle.Config;
using GravityPuzzle.Gameplay.Tutorial;

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
        [SerializeField] private Vector2 approachOffset = new Vector2(0f, -72f);
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

        public bool IsPlaying => loop != null && loop.IsActive();

        private void Awake()
        {
            Active = this;
            if (handRoot == null)
                handRoot = transform as RectTransform;
            authoredScale = (handRoot != null && handRoot.localScale.sqrMagnitude > 0.001f)
                ? handRoot.localScale
                : Vector3.one;
            SetEffectsVisible(false);
            SetVisible(false);
        }

        private void Start()
        {
            RefreshActiveTutorial();
        }

        private void Update()
        {
            IBoosterFirstUseTutorialGate gate = BoosterFirstUseTutorialRuntime.Gate;
            if (gate != null &&
                (gate.IsBoosterHighlighted(BoosterRewardType.FreezeTimer) ||
                 gate.IsBoosterHighlighted(BoosterRewardType.Hammer) ||
                 gate.IsBoosterHighlighted(BoosterRewardType.Rocket)))
            {
                if (handRoot != null && (!handRoot.gameObject.activeSelf || !IsPlaying))
                {
                    RefreshActiveTutorial();
                }
            }
        }

        public void RefreshActiveTutorial()
        {
            IBoosterFirstUseTutorialGate gate = BoosterFirstUseTutorialRuntime.Gate;
            if (gate == null)
                return;

            BoosterRewardType? targetType = null;
            if (gate.IsBoosterHighlighted(BoosterRewardType.FreezeTimer))
                targetType = BoosterRewardType.FreezeTimer;
            else if (gate.IsBoosterHighlighted(BoosterRewardType.Hammer))
                targetType = BoosterRewardType.Hammer;
            else if (gate.IsBoosterHighlighted(BoosterRewardType.Rocket))
                targetType = BoosterRewardType.Rocket;

            if (targetType.HasValue)
            {
                BoosterButton btn = BoosterFirstUseTutorialRuntime.GetButton(targetType.Value);
                if (btn != null)
                {
                    btn.SetSelectionGlowVisible(true);
                    ShowForButton(btn);
                }
            }
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
            if (Active == null)
                Active = Object.FindFirstObjectByType<TutorialHandView>(FindObjectsInactive.Include);
            if (Active == null || button == null)
                return;

            RectTransform targetRect = button.ButtonComponent != null
                ? button.ButtonComponent.transform as RectTransform
                : button.transform as RectTransform;

            if (targetRect == null)
                targetRect = button.transform as RectTransform;

            Active.ShowForUiTarget(targetRect);
        }

        public static void ShowForBoardTarget(Vector2 worldPosition, Camera gameplayCamera)
        {
            if (Active == null)
                Active = Object.FindFirstObjectByType<TutorialHandView>(FindObjectsInactive.Include);
            if (gameplayCamera == null)
                gameplayCamera = PrototypeBootstrap.SceneCamera != null ? PrototypeBootstrap.SceneCamera : Camera.main;
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

            if (handRoot == null)
                handRoot = transform as RectTransform;

            RectTransform parent = handRoot != null ? handRoot.parent as RectTransform : null;
            if (parent == null)
                return;

            SetVisible(true);
            handRoot.SetAsLastSibling();

            Canvas handCanvas = handRoot.GetComponentInParent<Canvas>();
            Camera handCam = handCanvas != null && handCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? (handCanvas.worldCamera != null ? handCanvas.worldCamera : (PrototypeBootstrap.SceneCamera != null ? PrototypeBootstrap.SceneCamera : Camera.main))
                : null;

            if (handCanvas != null && handCanvas.renderMode == RenderMode.ScreenSpaceCamera && handCanvas.worldCamera == null && handCam != null)
                handCanvas.worldCamera = handCam;

            Canvas targetCanvas = target.GetComponentInParent<Canvas>();
            Camera targetCam = targetCanvas != null && targetCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? (targetCanvas.worldCamera != null ? targetCanvas.worldCamera : (PrototypeBootstrap.SceneCamera != null ? PrototypeBootstrap.SceneCamera : Camera.main))
                : null;

            if (targetCanvas != null && targetCanvas.renderMode == RenderMode.ScreenSpaceCamera && targetCanvas.worldCamera == null && targetCam != null)
                targetCanvas.worldCamera = targetCam;

            Vector3 worldCenter = target.TransformPoint(target.rect.center);
            Vector2 localTarget;
            if (targetCanvas != null && targetCanvas == handCanvas)
            {
                Vector3 localTarget3D = parent.InverseTransformPoint(worldCenter);
                localTarget = new Vector2(localTarget3D.x, localTarget3D.y);
            }
            else
            {
                Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(targetCam, worldCenter);
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPoint, handCam, out localTarget))
                {
                    Vector3 localTarget3D = parent.InverseTransformPoint(worldCenter);
                    localTarget = new Vector2(localTarget3D.x, localTarget3D.y);
                }
            }

            Vector2 anchor = parent != null ? parent.pivot : new Vector2(0.5f, 0.5f);
            handRoot.anchorMin = anchor;
            handRoot.anchorMax = anchor;
            handRoot.pivot = new Vector2(0.5f, 0.5f);

            // Buttons are in the bottom HUD: the gauntlet should stay above the button and point down
            handRoot.localRotation = Quaternion.identity;
            if (rippleRoot != null)
                rippleRoot.anchoredPosition = new Vector2(0f, -60f);

            // Hand center offset +60f puts the bottom fingertip directly over the button center
            Vector2 handCenter = localTarget + new Vector2(0f, 60f);
            PlayLoop(handCenter, new Vector2(0f, 50f), Vector2.down * 14f);
        }

        private void ShowAtScreenPosition(Vector2 screenPosition)
        {
            if (handRoot == null)
                handRoot = transform as RectTransform;

            RectTransform parent = handRoot != null ? handRoot.parent as RectTransform : null;
            if (parent == null)
                return;

            Canvas canvas = handRoot.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? (canvas.worldCamera != null ? canvas.worldCamera : (PrototypeBootstrap.SceneCamera != null ? PrototypeBootstrap.SceneCamera : Camera.main))
                : null;

            if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera == null && camera != null)
                canvas.worldCamera = camera;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPosition, camera, out Vector2 localTarget))
                return;

            SetVisible(true);
            handRoot.SetAsLastSibling();

            Vector2 anchor = parent != null ? parent.pivot : new Vector2(0.5f, 0.5f);
            handRoot.anchorMin = anchor;
            handRoot.anchorMax = anchor;
            handRoot.pivot = new Vector2(0.5f, 0.5f);

            // Board targets are above: the gauntlet points up from below
            handRoot.localRotation = Quaternion.Euler(0f, 0f, 180f);
            if (rippleRoot != null)
                rippleRoot.anchoredPosition = new Vector2(0f, 60f);

            Vector2 handCenter = localTarget + new Vector2(0f, -60f);
            PlayLoop(handCenter, new Vector2(0f, -50f), Vector2.up * 14f);
        }

        private void PlayLoop(Vector2 targetPosition, Vector2 offset, Vector2 pressDelta)
        {
            StopLoop();
            if (handRoot == null)
                return;

            Vector3 scale = authoredScale;
            if (scale.sqrMagnitude < 0.001f)
                scale = Vector3.one;

            handRoot.localScale = scale;
            Vector2 approachPos = targetPosition + offset;
            Vector2 pressedPos = targetPosition + pressDelta;

            handRoot.anchoredPosition = approachPos;
            loop = DOTween.Sequence()
                .SetLink(gameObject, LinkBehaviour.KillOnDisable)
                .SetAutoKill(true);
            loop.Append(handRoot.DOAnchorPos(targetPosition, approachDuration).SetEase(approachEase));
            loop.Append(handRoot.DOAnchorPos(pressedPos, pressDuration).SetEase(pressEase));
            loop.AppendCallback(PlayTapEffects);
            loop.Append(handRoot.DOAnchorPos(targetPosition, releaseDuration).SetEase(releaseEase));
            loop.AppendInterval(loopWaitDuration);
            loop.AppendCallback(() => handRoot.anchoredPosition = approachPos);
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
            {
                if (visible)
                    handRoot.SetAsLastSibling();
                handRoot.gameObject.SetActive(visible);
            }
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
