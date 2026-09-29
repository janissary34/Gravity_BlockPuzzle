using DG.Tweening;
using UnityEngine;

namespace GravityPuzzle.Config
{
    [CreateAssetMenu(fileName = "RevealPresentationConfig", menuName = "Gravity Puzzle/Config/Reveal Presentation")]
    public sealed class RevealPresentationConfig : ScriptableObject
    {
        [Min(.01f)] public float boxOpenDuration = .45f;
        [Min(0f)] public float boxOpenDistance = 1.5f;
        public Ease boxOpenEase = Ease.OutCubic;
        [Min(.01f)] public float elevatorOpenDuration = .4f;
        [Min(0f)] public float elevatorDoorDistance = 1.2f;
        public Ease elevatorOpenEase = Ease.OutQuad;
        [Min(.01f)] public float elevatorFadeDuration = .2f;
        [Tooltip("Global presentation layer used by Elevator doors and contents.")]
        public int elevatorSortingOrder = 100;
        [Tooltip("Box cover layer. Keep this higher than Elevator so overlapping Boxes remain in front.")]
        public int boxSortingOrder = 200;
    }
}
