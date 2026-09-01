using DG.Tweening;
using UnityEngine;

namespace GravityPuzzle.Presentation.Views
{
    /// <summary>
    /// Repeating shine sweep for a SpriteRenderer-based button. The shine must
    /// be a direct child of this object so local coordinates remain stable.
    /// </summary>
    public sealed class SpriteButtonShine : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform shineTransform;

        [Header("Timing")]
        [Min(.01f)] [SerializeField] private float shineDuration = .6f;
        [Min(0f)] [SerializeField] private float shineInterval = 3f;
        [Range(0f, .5f)] [SerializeField] private float offButtonPadding = .1f;

        private SpriteRenderer buttonRenderer;
        private SpriteRenderer shineSpriteRenderer;
        private RectTransform shineRectTransform;
        private Sequence shineSequence;
        private readonly Vector3[] shineWorldCorners = new Vector3[4];
        private Vector3 authoredWorldPosition;
        private float startWorldX;
        private float endWorldX;
        private bool started;

        private void Awake()
        {
            buttonRenderer = GetComponent<SpriteRenderer>();
            if (shineTransform != null)
            {
                shineSpriteRenderer = shineTransform.GetComponent<SpriteRenderer>();
                shineRectTransform = shineTransform as RectTransform;
            }
        }

        private void Start()
        {
            if (!TryInitialize())
                return;

            started = true;
            ResetShinePosition();
            PlayLoop();
        }

        private void OnEnable()
        {
            if (!started || !TryInitialize())
                return;

            ResetShinePosition();
            PlayLoop();
        }

        private void OnDisable()
        {
            shineSequence?.Kill();
            shineSequence = null;

            if (shineTransform != null)
                shineTransform.DOKill();
        }

        private bool TryInitialize()
        {
            if (buttonRenderer == null || buttonRenderer.sprite == null || shineTransform == null)
            {
                Debug.LogWarning(
                    "[SpriteButtonShine] Assign this button's SpriteRenderer and the Shine child Transform.",
                    this);
                return false;
            }

            Bounds buttonBounds = buttonRenderer.bounds;
            float shineHalfWidth = GetShineWorldHalfWidth();
            float padding = buttonBounds.size.x * offButtonPadding;
            authoredWorldPosition = shineTransform.position;
            startWorldX = buttonBounds.min.x - shineHalfWidth - padding;
            endWorldX = buttonBounds.max.x + shineHalfWidth + padding;
            return endWorldX > startWorldX;
        }

        private void ResetShinePosition()
        {
            Vector3 position = authoredWorldPosition;
            position.x = startWorldX;
            shineTransform.position = position;
        }

        private float GetShineWorldHalfWidth()
        {
            if (shineSpriteRenderer != null)
                return shineSpriteRenderer.bounds.extents.x;

            if (shineRectTransform != null)
            {
                shineRectTransform.GetWorldCorners(shineWorldCorners);
                return Mathf.Abs(shineWorldCorners[2].x - shineWorldCorners[0].x) * .5f;
            }

            return 0f;
        }

        private void PlayLoop()
        {
            shineSequence?.Kill();

            shineSequence = DOTween.Sequence()
                .SetLink(gameObject, LinkBehaviour.KillOnDisable)
                .SetAutoKill(true);
            shineSequence.AppendInterval(shineInterval);
            shineSequence.AppendCallback(ResetShinePosition);
            shineSequence.Append(
                shineTransform
                    .DOMoveX(endWorldX, shineDuration)
                    .SetEase(Ease.Linear));
            shineSequence.SetLoops(-1, LoopType.Restart);
        }
    }
}
