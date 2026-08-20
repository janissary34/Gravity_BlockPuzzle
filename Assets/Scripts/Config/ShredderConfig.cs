using UnityEngine;
using UnityEngine.Serialization;

namespace GravityPuzzle.Config
{
    [CreateAssetMenu(fileName = "ShredderConfig", menuName = "Gravity Puzzle/Config/Shredder")]
    public sealed class ShredderConfig : ScriptableObject
    {
        [Header("Prefab")]
        [SerializeField] private ShredderWheel wheelPrefab;
        [Min(1)] [SerializeField] private int wheelPoolCapacity = 16;
        [SerializeField] private ShredderCatchZone catchZonePrefab;
        [Min(1)] [SerializeField] private int catchZonePoolCapacity = 1;

        [Header("Runtime Shredder Wheels")]
        [Min(0.01f)] [SerializeField] private float wheelRadiusMultiplier = 1f;
        [Min(0f)] [SerializeField] private float wheelRotationSpeedMultiplier = 1f;
        [Tooltip("Small board-space tolerance above the cutter line used to capture a piece on the final legal grid row.")]
        [Range(0f, .25f)] [SerializeField] private float captureApproachDistance = .04f;

        [Header("Piece Feed")]
        [Min(0.01f)] [SerializeField] private float feedSpeed = 2f;
        [Min(0f)] [SerializeField] private float tremorIntensity = .045f;
        [Min(0f)] [SerializeField] private float tremorFrequency = 55f;
        [FormerlySerializedAs("tremorVelocityMultiplier")]
        [Min(0f)] [SerializeField] private float feedShakeAmplitude = 2.5f;
        [Min(0f)] [SerializeField] private float feedAngularDrag = 6f;
        [Min(0f)] [SerializeField] private float tumbleTorque = .35f;
        [Range(0f, 20f)] [SerializeField] private float maxFeedTiltAngle = 5f;

        [Header("Final Piece Timer Grace")]
        [Tooltip("If the final live piece enters a shredder within this many seconds of 00:00, its feed is allowed to finish and the result becomes a win.")]
        [Min(0f)] [SerializeField] private float finalPieceTimerGraceSeconds = 1f;

        [Header("Physics Handoff")]
        [SerializeField] private float physicsHandoffY;
        [Min(0f)] [SerializeField] private float tremorDuration = .3f;
        [SerializeField] private Vector2 tremorPositionRange;
        [SerializeField] private Vector2 tremorRotationRange;
        [SerializeField] private Vector2 angularVelocityRange;

        public float WheelRadiusMultiplier => wheelRadiusMultiplier;
        public ShredderWheel WheelPrefab => wheelPrefab;
        public int WheelPoolCapacity => wheelPoolCapacity;
        public ShredderCatchZone CatchZonePrefab => catchZonePrefab;
        public int CatchZonePoolCapacity => catchZonePoolCapacity;
        public float WheelRotationSpeedMultiplier => wheelRotationSpeedMultiplier;
        public float CaptureApproachDistance => captureApproachDistance;
        public float FeedSpeed => feedSpeed;
        public float TremorIntensity => tremorIntensity;
        public float TremorFrequency => tremorFrequency;
        public float FeedShakeAmplitude => feedShakeAmplitude;
        public float FeedAngularDrag => feedAngularDrag;
        public float TumbleTorque => tumbleTorque;
        public float MaxFeedTiltAngle => maxFeedTiltAngle;
        public float FinalPieceTimerGraceSeconds => finalPieceTimerGraceSeconds;
        public float PhysicsHandoffY => physicsHandoffY;
        public float TremorDuration => tremorDuration;
        public Vector2 TremorPositionRange => tremorPositionRange;
        public Vector2 TremorRotationRange => tremorRotationRange;
        public Vector2 AngularVelocityRange => angularVelocityRange;
    }
}
