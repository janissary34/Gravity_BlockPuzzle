using DG.Tweening;
using GravityPuzzle.Infrastructure.Pooling;
using UnityEngine;

namespace GravityPuzzle.Presentation.Views
{
    /// <summary>
    /// Authored transient booster presentation root. Instances are owned by a
    /// small typed pool and never created or destroyed during gameplay.
    /// </summary>
    public sealed class BoosterVisualView : MonoBehaviour, IPoolable
    {
        private const string ImpactPointChildName = "HammerHeadImpactPoint";

        [Tooltip("Optional authored impact point used by a booster presentation.")]
        [SerializeField] private Transform impactPoint;
        private Vector3 authoredScale;
        private Quaternion authoredRotation;
        private Vector3 impactPointLocalPosition;

        private void Awake()
        {
            authoredScale = transform.localScale.sqrMagnitude > 0.0001f
                ? transform.localScale
                : Vector3.one;

            authoredRotation = transform.localRotation;

            if (impactPoint == null)
                impactPoint = transform.Find(ImpactPointChildName);

            if (impactPoint != null)
                impactPointLocalPosition = transform.InverseTransformPoint(impactPoint.position);
        }

        public Vector3 AuthoredScale => authoredScale;
        public Quaternion AuthoredRotation => authoredRotation;
        public bool HasImpactPoint => impactPoint != null;
        public Vector3 ImpactPointLocalPosition => impactPointLocalPosition;

        public void OnSpawn()
        {
            transform.DOKill();
            transform.localScale = authoredScale;
            transform.localRotation = authoredRotation;
        }

        public void OnDespawn()
        {
            transform.DOKill();
            transform.localScale = authoredScale;
            transform.localRotation = authoredRotation;
        }
    }
}
