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
        private Vector3 authoredScale;

        private void Awake()
        {
            authoredScale = transform.localScale.sqrMagnitude > 0.0001f
                ? transform.localScale
                : Vector3.one;
        }

        public Vector3 AuthoredScale => authoredScale;

        public void OnSpawn()
        {
            transform.DOKill();
            transform.localScale = authoredScale;
            transform.localRotation = Quaternion.identity;
        }

        public void OnDespawn()
        {
            transform.DOKill();
            transform.localScale = authoredScale;
            transform.localRotation = Quaternion.identity;
        }
    }
}
