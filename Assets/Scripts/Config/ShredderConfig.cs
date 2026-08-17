using UnityEngine;
using UnityEngine.Serialization;

namespace GravityPuzzle.Config
{
    [CreateAssetMenu(fileName = "ShredderConfig", menuName = "Gravity Puzzle/Config/Shredder")]
    public sealed class ShredderConfig : ScriptableObject
    {
        [Header("Runtime Shredder Wheels")]
        [Min(0.01f)] [SerializeField] private float wheelRadiusMultiplier = 1f;
        [Min(0f)] [SerializeField] private float wheelRotationSpeedMultiplier = 1f;

        [Header("Piece Feed")]
        [Min(0.01f)] [SerializeField] private float feedSpeed = 2f;
        [Min(0f)] [SerializeField] private float tremorIntensity = .045f;
        [Min(0f)] [SerializeField] private float tremorFrequency = 55f;
        [FormerlySerializedAs("tremorVelocityMultiplier")]
        [Min(0f)] [SerializeField] private float feedShakeAmplitude = 2.5f;
        [Min(0f)] [SerializeField] private float tumbleTorque = .35f;
        [Range(0f, 20f)] [SerializeField] private float maxFeedTiltAngle = 5f;

        [Header("Physics Handoff")]
        [SerializeField] private float physicsHandoffY;
        [Min(0f)] [SerializeField] private float tremorDuration = .3f;
        [SerializeField] private Vector2 tremorPositionRange;
        [SerializeField] private Vector2 tremorRotationRange;
        [SerializeField] private Vector2 angularVelocityRange;

        public float WheelRadiusMultiplier => wheelRadiusMultiplier;
        public float WheelRotationSpeedMultiplier => wheelRotationSpeedMultiplier;
        public float FeedSpeed => feedSpeed;
        public float TremorIntensity => tremorIntensity;
        public float TremorFrequency => tremorFrequency;
        public float FeedShakeAmplitude => feedShakeAmplitude;
        public float TumbleTorque => tumbleTorque;
        public float MaxFeedTiltAngle => maxFeedTiltAngle;
        public float PhysicsHandoffY => physicsHandoffY;
        public float TremorDuration => tremorDuration;
        public Vector2 TremorPositionRange => tremorPositionRange;
        public Vector2 TremorRotationRange => tremorRotationRange;
        public Vector2 AngularVelocityRange => angularVelocityRange;
    }
}
