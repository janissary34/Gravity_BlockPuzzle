using UnityEngine;

namespace GravityPuzzle.Config
{
    [CreateAssetMenu(fileName = "ShredderConfig", menuName = "Gravity Puzzle/Config/Shredder")]
    public sealed class ShredderConfig : ScriptableObject
    {
        [SerializeField] private float physicsHandoffY;
        [Min(0f)] [SerializeField] private float tremorDuration = .3f;
        [SerializeField] private Vector2 tremorPositionRange;
        [SerializeField] private Vector2 tremorRotationRange;
        [SerializeField] private Vector2 angularVelocityRange;

        public float PhysicsHandoffY => physicsHandoffY;
        public float TremorDuration => tremorDuration;
        public Vector2 TremorPositionRange => tremorPositionRange;
        public Vector2 TremorRotationRange => tremorRotationRange;
        public Vector2 AngularVelocityRange => angularVelocityRange;
    }
}
