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
        [SerializeField] private ShredderFeedMask feedMaskPrefab;
        [SerializeField] private PhysicsMaterial2D feedPhysicsMaterial;

        [Header("Feed Mask Presentation")]
        [Tooltip("World-space offset from the shredder line to the center of the pooled feed clipping mask.")]
        [SerializeField] private float feedMaskVerticalOffset = -15f;
        [SerializeField] private Vector2 feedMaskScale = new Vector2(60f, 30f);

        [Header("Runtime Shredder Wheels")]
        [Min(0.01f)] [SerializeField] private float wheelRadiusMultiplier = 1f;
        [Min(0f)] [SerializeField] private float wheelRotationSpeedMultiplier = 1f;
        [Tooltip("Small board-space tolerance above the cutter line used to capture a piece on the final legal grid row.")]
        [Range(0f, .25f)] [SerializeField] private float captureApproachDistance = .04f;

        [Header("Authored Wheel Art")]
        [Tooltip("Uniform scale applied to the Disc, Hub and Tooth children of ShredderWheel.prefab. Change this before using the authoring command.")]
        [Min(.001f)] [SerializeField] private float wheelArtScale = .04f;
        [Min(1)] [SerializeField] private int wheelToothCount = 12;
        [Min(.001f)] [SerializeField] private float discArtScale = 1.65f;
        [Min(.001f)] [SerializeField] private float hubArtScale = .48f;
        [Min(.001f)] [SerializeField] private Vector2 toothArtScale = new Vector2(.42f, .24f);
        [Min(0f)] [SerializeField] private float toothRadialOffset = .86f;
        [SerializeField] private int discSortingOrder = 25;
        [SerializeField] private int toothSortingOrder = 26;
        [SerializeField] private int hubSortingOrder = 27;

        [Header("Voxel Ejection")]
        [Tooltip("Primary direction used by the shredder to eject pooled voxel shards.")]
        [SerializeField] private Vector2 voxelEjectionDirection = new Vector2(0f, -1.2f);
        [Tooltip("Random angular spread, in degrees, applied to voxel ejection.")]
        [Range(0f, 180f)] [SerializeField] private float voxelEjectionSpreadAngle = 40f;

        [Header("Piece Feed")]
        [Min(0.01f)] [SerializeField] private float feedSpeed = 2f;
        [Min(0f)] [SerializeField] private float tremorIntensity = .045f;
        [Min(0f)] [SerializeField] private float tremorFrequency = 55f;
        [FormerlySerializedAs("tremorVelocityMultiplier")]
        [Min(0f)] [SerializeField] private float feedShakeAmplitude = 2.5f;
        [Min(0f)] [SerializeField] private float feedAngularDrag = 6f;
        [Min(0f)] [SerializeField] private float tumbleTorque = .35f;
        [Range(0f, 20f)] [SerializeField] private float maxFeedTiltAngle = 5f;

        [Header("Voxel Reward Presentation")]
        [Tooltip("Fraction of a shredded piece's pooled voxel shards that use the gem-progress presentation path.")]
        [Range(0f, 1f)] [SerializeField] private float gemVoxelRatio = .25f;

        [Header("Final Piece Timer Grace")]
        [Tooltip("If the final live piece enters a shredder within this many seconds of 00:00, its feed is allowed to finish and the result becomes a win.")]
        [Min(0f)] [SerializeField] private float finalPieceTimerGraceSeconds = 1f;

        public float WheelRadiusMultiplier => wheelRadiusMultiplier;
        public ShredderWheel WheelPrefab => wheelPrefab;
        public int WheelPoolCapacity => wheelPoolCapacity;
        public ShredderCatchZone CatchZonePrefab => catchZonePrefab;
        public int CatchZonePoolCapacity => catchZonePoolCapacity;
        public ShredderFeedMask FeedMaskPrefab => feedMaskPrefab;
        public float FeedMaskVerticalOffset => feedMaskVerticalOffset;
        public Vector2 FeedMaskScale => feedMaskScale;
        /// <summary>Authored low-friction material used only during a shredder feed.</summary>
        public PhysicsMaterial2D FeedPhysicsMaterial => feedPhysicsMaterial;
        public float WheelRotationSpeedMultiplier => wheelRotationSpeedMultiplier;
        public float CaptureApproachDistance => captureApproachDistance;
        public float WheelArtScale => wheelArtScale;
        public int WheelToothCount => wheelToothCount;
        public float DiscArtScale => discArtScale;
        public float HubArtScale => hubArtScale;
        public Vector2 ToothArtScale => toothArtScale;
        public float ToothRadialOffset => toothRadialOffset;
        public int DiscSortingOrder => discSortingOrder;
        public int ToothSortingOrder => toothSortingOrder;
        public int HubSortingOrder => hubSortingOrder;
        public Vector2 VoxelEjectionDirection => voxelEjectionDirection;
        public float VoxelEjectionSpreadAngle => voxelEjectionSpreadAngle;
        public float FeedSpeed => feedSpeed;
        public float TremorIntensity => tremorIntensity;
        public float TremorFrequency => tremorFrequency;
        public float FeedShakeAmplitude => feedShakeAmplitude;
        public float FeedAngularDrag => feedAngularDrag;
        public float TumbleTorque => tumbleTorque;
        public float MaxFeedTiltAngle => maxFeedTiltAngle;
        public float GemVoxelRatio => gemVoxelRatio;
        public float FinalPieceTimerGraceSeconds => finalPieceTimerGraceSeconds;
    }
}
