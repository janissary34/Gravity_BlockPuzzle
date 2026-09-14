using DG.Tweening;
using GravityPuzzle.Config;
using UnityEngine;

namespace GravityPuzzle.Presentation.Views
{
    /// <summary>
    /// Plays one authored modal's entry motion when its owning panel is enabled.
    /// It controls presentation only; panel visibility remains owned by gameplay UI.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PanelEntrancePresentation : MonoBehaviour
    {
        public enum EntranceStyle
        {
            CenterScale,
            SlideFromTop,
            SequentialScale
        }

        [SerializeField] private TweenConfig tweenConfig;
        [SerializeField] private EntranceStyle entranceStyle;
        [Tooltip("A direct child backdrop that is visible immediately and excluded from the scale reveal.")]
        [SerializeField] private RectTransform immediateBackdrop;
        [Tooltip("Ordered panel elements used only by Sequential Scale.")]
        [SerializeField] private RectTransform[] sequentialContent;
        [Tooltip("Pulse components that start only after this panel finishes its entrance.")]
        [SerializeField] private MonoBehaviour[] pulseComponentsAfterEntrance;

        private RectTransform panelRect;
        private Vector3 restingScale;
        private Vector2 restingAnchoredPosition;
        private Transform[] revealedChildren;
        private Vector3[] revealedChildRestingScales;
        private Vector2[] revealedChildRestingAnchoredPositions;
        private Vector3[] revealedChildRestingLocalPositions;
        private Vector3[] sequentialContentRestingScales;
        private Tween entranceTween;

        private void Awake()
        {
            panelRect = transform as RectTransform;
            if (panelRect == null)
            {
                Debug.LogWarning("[Panel Entrance] A RectTransform is required.", this);
                enabled = false;
                return;
            }

            restingScale = panelRect.localScale;
            restingAnchoredPosition = panelRect.anchoredPosition;
            CacheRevealedChildren();
            CacheSequentialContentScales();
        }

        private void OnEnable()
        {
            PlayEntrance();
        }

        private void OnDisable()
        {
            entranceTween?.Kill();
            entranceTween = null;
            RestoreRestingTransform();
        }

        private void OnDestroy()
        {
            entranceTween?.Kill();
        }

        private void PlayEntrance()
        {
            if (panelRect == null || tweenConfig == null)
                return;

            entranceTween?.Kill();

            if (entranceStyle == EntranceStyle.CenterScale)
            {
                PlayCenterScaleReveal();
                return;
            }

            if (entranceStyle == EntranceStyle.SequentialScale)
            {
                PlaySequentialScaleReveal();
                return;
            }

            PlaySlideFromTop();
        }

        private void CacheRevealedChildren()
        {
            int childCount = panelRect.childCount;
            int revealCount = 0;
            for (int childIndex = 0; childIndex < childCount; childIndex++)
            {
                if (panelRect.GetChild(childIndex) != immediateBackdrop)
                    revealCount++;
            }

            revealedChildren = new Transform[revealCount];
            revealedChildRestingScales = new Vector3[revealCount];
            revealedChildRestingAnchoredPositions = new Vector2[revealCount];
            revealedChildRestingLocalPositions = new Vector3[revealCount];
            int revealIndex = 0;
            for (int childIndex = 0; childIndex < childCount; childIndex++)
            {
                Transform child = panelRect.GetChild(childIndex);
                if (child == immediateBackdrop)
                    continue;

                revealedChildren[revealIndex] = child;
                revealedChildRestingScales[revealIndex] = child.localScale;
                revealedChildRestingLocalPositions[revealIndex] = child.localPosition;
                if (child is RectTransform childRect)
                    revealedChildRestingAnchoredPositions[revealIndex] = childRect.anchoredPosition;
                revealIndex++;
            }
        }

        private void PlaySlideFromTop()
        {
            if (revealedChildren == null || revealedChildren.Length == 0)
                return;

            Sequence slideSequence = DOTween.Sequence()
                .SetUpdate(true)
                .SetLink(gameObject, LinkBehaviour.KillOnDisable)
                .SetAutoKill(true);

            for (int index = 0; index < revealedChildren.Length; index++)
            {
                Transform child = revealedChildren[index];
                if (child == null)
                    continue;

                if (child is RectTransform childRect)
                {
                    Vector2 restingAnchoredPosition = revealedChildRestingAnchoredPositions[index];
                    childRect.anchoredPosition = restingAnchoredPosition +
                        Vector2.up * tweenConfig.KeepOnPlayingTopOffset;
                    slideSequence.Join(childRect
                        .DOAnchorPos(restingAnchoredPosition, tweenConfig.KeepOnPlayingSlideDuration)
                        .SetEase(tweenConfig.KeepOnPlayingSlideEase));
                    continue;
                }

                Vector3 restingLocalPosition = revealedChildRestingLocalPositions[index];
                child.localPosition = restingLocalPosition + Vector3.up * tweenConfig.KeepOnPlayingTopOffset;
                slideSequence.Join(child
                    .DOLocalMove(restingLocalPosition, tweenConfig.KeepOnPlayingSlideDuration)
                    .SetEase(tweenConfig.KeepOnPlayingSlideEase));
            }

            entranceTween = slideSequence;
        }

        private void PlayCenterScaleReveal()
        {
            if (revealedChildren == null || revealedChildren.Length == 0)
                return;

            StopPulseComponents();

            Sequence revealSequence = DOTween.Sequence()
                .SetUpdate(true)
                .SetLink(gameObject, LinkBehaviour.KillOnDisable)
                .SetAutoKill(true);

            for (int index = 0; index < revealedChildren.Length; index++)
            {
                Transform child = revealedChildren[index];
                if (child == null)
                    continue;

                Vector3 restingChildScale = revealedChildRestingScales[index];
                child.localScale = restingChildScale * tweenConfig.ModalPanelRevealStartScale;
                revealSequence.Join(child
                    .DOScale(restingChildScale, tweenConfig.ModalPanelRevealDuration)
                    .SetEase(tweenConfig.ModalPanelRevealEase));
            }

            entranceTween = revealSequence.OnComplete(StartPulseComponents);
        }

        private void PlaySequentialScaleReveal()
        {
            if (sequentialContent == null || sequentialContent.Length == 0)
                return;

            StopPulseComponents();

            Sequence revealSequence = DOTween.Sequence()
                .SetUpdate(true)
                .SetLink(gameObject, LinkBehaviour.KillOnDisable)
                .SetAutoKill(true);

            for (int index = 0; index < sequentialContent.Length; index++)
            {
                RectTransform content = sequentialContent[index];
                if (content == null)
                    continue;

                Vector3 restingContentScale = sequentialContentRestingScales[index];
                // Sequential entries must remain completely hidden until it is
                // their turn; unlike the general panel reveal, they do not use
                // a partially visible starting scale.
                content.localScale = Vector3.zero;
                revealSequence.Append(content
                    .DOScale(restingContentScale, tweenConfig.ModalPanelRevealDuration)
                    .SetEase(tweenConfig.ModalPanelRevealEase));

                if (index < sequentialContent.Length - 1)
                    revealSequence.AppendInterval(tweenConfig.ModalPanelSequenceDelay);
            }

            entranceTween = revealSequence.OnComplete(StartPulseComponents);
        }

        private void RestoreRestingTransform()
        {
            if (panelRect == null)
                return;

            panelRect.localScale = restingScale;
            panelRect.anchoredPosition = restingAnchoredPosition;

            if (revealedChildren == null)
                return;

            for (int index = 0; index < revealedChildren.Length; index++)
            {
                if (revealedChildren[index] != null)
                {
                    revealedChildren[index].localScale = revealedChildRestingScales[index];
                    if (revealedChildren[index] is RectTransform childRect)
                        childRect.anchoredPosition = revealedChildRestingAnchoredPositions[index];
                    else
                        revealedChildren[index].localPosition = revealedChildRestingLocalPositions[index];
                }
            }

            if (sequentialContent == null)
                return;

            for (int index = 0; index < sequentialContent.Length; index++)
            {
                if (sequentialContent[index] != null)
                    sequentialContent[index].localScale = sequentialContentRestingScales[index];
            }
        }

        private void CacheSequentialContentScales()
        {
            if (sequentialContent == null)
                return;

            sequentialContentRestingScales = new Vector3[sequentialContent.Length];
            for (int index = 0; index < sequentialContent.Length; index++)
            {
                if (sequentialContent[index] != null)
                    sequentialContentRestingScales[index] = sequentialContent[index].localScale;
            }
        }

        private void StopPulseComponents()
        {
            if (pulseComponentsAfterEntrance == null)
                return;

            for (int index = 0; index < pulseComponentsAfterEntrance.Length; index++)
            {
                MonoBehaviour pulseComponent = pulseComponentsAfterEntrance[index];
                if (pulseComponent != null)
                    pulseComponent.enabled = false;
            }
        }

        private void StartPulseComponents()
        {
            if (pulseComponentsAfterEntrance == null)
                return;

            for (int index = 0; index < pulseComponentsAfterEntrance.Length; index++)
            {
                MonoBehaviour pulseComponent = pulseComponentsAfterEntrance[index];
                if (pulseComponent != null)
                    pulseComponent.enabled = true;
            }
        }
    }
}
