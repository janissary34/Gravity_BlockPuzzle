using UnityEngine;

namespace GravityPuzzle.Config
{
    [CreateAssetMenu(fileName = "GravityConfig", menuName = "Gravity Puzzle/Config/Gravity")]
    public sealed class GravityConfig : ScriptableObject
    {
        [Min(.001f)] [SerializeField] private float tickInterval = .02f;
        [Min(.001f)] [SerializeField] private float stepDuration = .12f;
        [SerializeField] private AnimationCurve speedCurve = AnimationCurve.Linear(0f, 1f, 1f, 1f);

        public float TickInterval => tickInterval;
        public float StepDuration => stepDuration;
        public AnimationCurve SpeedCurve => speedCurve;
    }
}
