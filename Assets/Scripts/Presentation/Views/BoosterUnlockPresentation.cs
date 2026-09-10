using DG.Tweening;
using UnityEngine;

namespace GravityPuzzle.Presentation.Views
{
    /// <summary>
    /// Owns the visual-only reveal for one authored booster-reward content slot.
    /// It is activated by <see cref="NewBoosterPanelView"/> along with the slot.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoosterUnlockPresentation : MonoBehaviour
    {
        [Header("Authored UI References")]
        [SerializeField] private RectTransform raysTransform;

        [Header("Reveal Tuning")]
        [SerializeField, Min(.01f)] private float revealDuration = .30f;
        [SerializeField, Min(.01f)] private float settleDuration = .22f;
        [SerializeField] private float rayRevealRotationDegrees = 145f;
        [SerializeField, Min(.01f)] private float idleRotationDuration = 10f;

        private readonly Vector3 HiddenRayScale = new Vector3(.55f, .55f, 1f);
        private readonly Vector3 ExpandedRayScale = new Vector3(1.2f, 1.2f, 1f);
        private Sequence revealSequence;
        private Sequence idleSequence;
        private bool initialized;

        private void Awake()
        {
            if (raysTransform == null)
            {
                Debug.LogWarning("[Booster Unlock Presentation] Assign the rays UI reference.", this);
                enabled = false;
                return;
            }

            initialized = true;
        }

        private void OnEnable()
        {
            if (initialized)
                Play();
        }

        private void OnDisable()
        {
            StopAnimations();
        }

        /// <summary>Restarts the unlock reveal; useful for inspector-wired preview buttons.</summary>
        public void Play()
        {
            StopAnimations();
            ResetToRevealStart();

            revealSequence = DOTween.Sequence()
                .SetLink(gameObject, LinkBehaviour.KillOnDisable)
                .SetAutoKill(true);

            revealSequence.Join(raysTransform.DOScale(ExpandedRayScale, revealDuration).SetEase(Ease.OutQuad));
            revealSequence.Join(raysTransform.DORotate(
                new Vector3(0f, 0f, rayRevealRotationDegrees), revealDuration,
                RotateMode.FastBeyond360).SetEase(Ease.OutCubic));
            revealSequence.Append(raysTransform.DOScale(Vector3.one, settleDuration).SetEase(Ease.OutQuad));
            revealSequence.AppendCallback(StartIdleRotation);
        }

        private void StartIdleRotation()
        {
            idleSequence = DOTween.Sequence()
                .SetLink(gameObject, LinkBehaviour.KillOnDisable)
                .SetAutoKill(false)
                .SetLoops(-1, LoopType.Restart);

            idleSequence.Append(raysTransform.DORotate(
                new Vector3(0f, 0f, rayRevealRotationDegrees + 360f), idleRotationDuration,
                RotateMode.FastBeyond360).SetEase(Ease.Linear));
        }

        private void ResetToRevealStart()
        {
            raysTransform.localScale = HiddenRayScale;
            raysTransform.localRotation = Quaternion.Euler(0f, 0f, -25f);
        }

        private void StopAnimations()
        {
            if (revealSequence != null)
            {
                revealSequence.Kill();
                revealSequence = null;
            }

            if (idleSequence != null)
            {
                idleSequence.Kill();
                idleSequence = null;
            }
        }
    }
}
