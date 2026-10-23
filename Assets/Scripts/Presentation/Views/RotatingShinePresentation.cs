using DG.Tweening;
using UnityEngine;

namespace GravityPuzzle.Presentation.Views
{
    /// <summary>
    /// Rotates authored shine layers around their shared visual center while the
    /// owning presentation is visible.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RotatingShinePresentation : MonoBehaviour
    {
        [SerializeField] private RectTransform[] shineTransforms;
        [SerializeField, Min(.01f)] private float rotationDuration = 10f;

        private Quaternion[] authoredRotations;
        private Sequence rotationSequence;

        private void Awake()
        {
            CacheAuthoredRotations();
        }

        private void OnEnable()
        {
            Play();
        }

        private void OnDisable()
        {
            StopAndRestore();
        }

        private void OnDestroy()
        {
            rotationSequence?.Kill();
        }

        private void Play()
        {
            if (shineTransforms == null || shineTransforms.Length == 0)
                return;

            if (authoredRotations == null || authoredRotations.Length != shineTransforms.Length)
                CacheAuthoredRotations();

            rotationSequence?.Kill();
            RestoreAuthoredRotations();

            rotationSequence = DOTween.Sequence()
                .SetLink(gameObject, LinkBehaviour.KillOnDisable)
                .SetAutoKill(false)
                .SetLoops(-1, LoopType.Restart);

            for (int index = 0; index < shineTransforms.Length; index++)
            {
                RectTransform shineTransform = shineTransforms[index];
                if (shineTransform == null)
                    continue;

                Vector3 targetRotation = authoredRotations[index].eulerAngles + Vector3.forward * 360f;
                rotationSequence.Join(shineTransform
                    .DOLocalRotate(targetRotation, rotationDuration, RotateMode.FastBeyond360)
                    .SetEase(Ease.Linear));
            }
        }

        private void StopAndRestore()
        {
            rotationSequence?.Kill();
            rotationSequence = null;
            RestoreAuthoredRotations();
        }

        private void CacheAuthoredRotations()
        {
            if (shineTransforms == null)
                return;

            authoredRotations = new Quaternion[shineTransforms.Length];
            for (int index = 0; index < shineTransforms.Length; index++)
            {
                if (shineTransforms[index] != null)
                    authoredRotations[index] = shineTransforms[index].localRotation;
            }
        }

        private void RestoreAuthoredRotations()
        {
            if (shineTransforms == null || authoredRotations == null)
                return;

            for (int index = 0; index < shineTransforms.Length; index++)
            {
                if (shineTransforms[index] != null)
                    shineTransforms[index].localRotation = authoredRotations[index];
            }
        }
    }
}
