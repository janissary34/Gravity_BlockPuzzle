using UnityEngine;
using UnityEngine.Serialization;

namespace GravityPuzzle.Config
{
    [CreateAssetMenu(fileName = "ShredderConfig", menuName = "Gravity Puzzle/Config/Shredder")]
    public sealed class ShredderConfig : ScriptableObject
    {
        [Header("Particle System Presentation")]
        [Tooltip("Number of flying particle voxels emitted per shredded block cell.")]
        [Range(1, 100)] [SerializeField] private int particlesPerShreddedCell = 24;
        [Tooltip("Distance below the shredder line where particles emerge from beneath the wheels.")]
        [Range(0f, 2f)] [SerializeField] private float exitSeamOffsetBelowShredder = 0.65f;

        [Header("Contact Occlusion Presentation")]
        [Tooltip("Keeps the clipped edge slightly inside the rotating teeth, so material is hidden by the mechanism instead of appearing to vanish on its top edge.")]
        [Range(0f, .5f)] [SerializeField] private float feedMaskMouthInset = .14f;
        [Tooltip("Emits small square debris continuously from the same hidden edge. This is independent from the optional dissolve shader.")]
        [SerializeField] private bool enableContactDebrisPresentation = true;
        [Tooltip("Maximum number of progress-voxel handoffs emitted from one atlas block while it crosses the cutter. Wider blocks use more origins along their active contact span.")]
        [Range(1, 16)] [SerializeField] private int maxContactProgressEmissions = 8;

        [Header("Grinding Debris Presentation")]
        [Tooltip("Opt-in gate for the experimental rough-cut shader and continuous debris. Keep disabled until it has been play-tested on target devices.")]
        [SerializeField] private bool enableAdvancedShreddingPresentation;
        [Tooltip("Small square fragments emitted per second for each world unit at the active cutter edge.")]
        [Range(0f, 80f)] [SerializeField] private float debrisPerSecondPerWorldUnit = 22f;
        [Tooltip("Extra fragments emitted where a rendered cell fully crosses the cutter edge.")]
        [Range(0, 16)] [SerializeField] private int debrisBurstPerCrossedCell = 3;
        [Tooltip("Hard upper bound on one feed's debris emission in a frame.")]
        [Range(1, 32)] [SerializeField] private int maxDebrisPerFrame = 10;
        [Tooltip("Lifetime of a square debris fragment in seconds.")]
        [Range(.05f, 2f)] [SerializeField] private float debrisLifetime = .48f;
        [Tooltip("World-space size range for debris fragments.")]
        [SerializeField] private Vector2 debrisSizeRange = new Vector2(.055f, .14f);
        [Tooltip("Initial downward pull that makes fragments read as being drawn into the wheels.")]
        [Range(0f, 8f)] [SerializeField] private float debrisDownwardSpeed = 2.8f;
        [Tooltip("Small lateral variation applied as fragments leave the cutter line.")]
        [Range(0f, 4f)] [SerializeField] private float debrisHorizontalSpeed = .75f;
        [Tooltip("Particle-system gravity multiplier for the debris after it leaves the wheels.")]
        [Range(0f, 2f)] [SerializeField] private float debrisGravityModifier = .35f;
        [Tooltip("Maximum absolute angular velocity, in degrees per second, for a debris fragment.")]
        [Range(0f, 1440f)] [SerializeField] private float debrisAngularVelocity = 540f;
        [Tooltip("Sorting order for debris. Keep it behind teeth and in front of the clipped block edge.")]
        [SerializeField] private int debrisSortingOrder = 24;

        [Header("Rough Cutter Edge")]
        [Tooltip("Maximum world-space variation in the dissolve edge. This prevents a rectangular mask cut.")]
        [Range(0f, .5f)] [SerializeField] private float cutterEdgeAmplitude = .075f;
        [Tooltip("World-space width of the soft, irregular dissolve band.")]
        [Range(.001f, .25f)] [SerializeField] private float cutterEdgeSoftness = .035f;
        [Tooltip("Horizontal frequency of the fixed mechanical tooth pattern.")]
        [Range(1f, 32f)] [SerializeField] private float cutterNoiseFrequency = 12f;

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
        [Tooltip("Maximum number of pieces that have reached one shredder mouth and may feed concurrently.")]
        [Min(1)] [SerializeField] private int feedQueueCapacity = 16;
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
        public int ParticlesPerShreddedCell => particlesPerShreddedCell;
        public float ExitSeamOffsetBelowShredder => exitSeamOffsetBelowShredder;
        public float FeedMaskMouthInset => feedMaskMouthInset;
        public bool EnableContactDebrisPresentation => enableContactDebrisPresentation;
        public int MaxContactProgressEmissions => maxContactProgressEmissions;
        public float DebrisPerSecondPerWorldUnit => debrisPerSecondPerWorldUnit;
        public bool EnableAdvancedShreddingPresentation => enableAdvancedShreddingPresentation;
        public int DebrisBurstPerCrossedCell => debrisBurstPerCrossedCell;
        public int MaxDebrisPerFrame => maxDebrisPerFrame;
        public float DebrisLifetime => debrisLifetime;
        public Vector2 DebrisSizeRange => debrisSizeRange;
        public float DebrisDownwardSpeed => debrisDownwardSpeed;
        public float DebrisHorizontalSpeed => debrisHorizontalSpeed;
        public float DebrisGravityModifier => debrisGravityModifier;
        public float DebrisAngularVelocity => debrisAngularVelocity;
        public int DebrisSortingOrder => debrisSortingOrder;
        public float CutterEdgeAmplitude => cutterEdgeAmplitude;
        public float CutterEdgeSoftness => cutterEdgeSoftness;
        public float CutterNoiseFrequency => cutterNoiseFrequency;
        public float FeedSpeed => feedSpeed;
        public int FeedQueueCapacity => feedQueueCapacity;
        public float TremorIntensity => tremorIntensity;
        public float TremorFrequency => tremorFrequency;
        public float FeedShakeAmplitude => feedShakeAmplitude;
        public float FeedAngularDrag => feedAngularDrag;
        public float TumbleTorque => tumbleTorque;
        public float MaxFeedTiltAngle => maxFeedTiltAngle;
        public float FinalPieceTimerGraceSeconds => finalPieceTimerGraceSeconds;
    }
}
