using DG.Tweening;
using GravityPuzzle.Config;
using UnityEngine;

namespace GravityPuzzle.Bootstrap
{
    [DefaultExecutionOrder(-1000)]
    public sealed class TweenBootstrap : MonoBehaviour
    {
        [SerializeField] private TweenConfig tweenConfig;

        private void Awake()
        {
            if (tweenConfig == null)
            {
                Debug.LogError("[TweenBootstrap] TweenConfig is required.", this);
                return;
            }

            DOTween.SetTweensCapacity(
                tweenConfig.TweenCapacity,
                tweenConfig.SequenceCapacity);
        }
    }
}
