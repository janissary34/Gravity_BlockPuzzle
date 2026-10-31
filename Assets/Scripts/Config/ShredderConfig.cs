using UnityEngine;
using UnityEngine.Serialization;
using GravityPuzzle.Presentation.Views;

namespace GravityPuzzle.Config
{
    [CreateAssetMenu(fileName = "ShredderConfig", menuName = "Gravity Puzzle/Config/Shredder")]
    public sealed class ShredderConfig : ScriptableObject
    {
        [Header("Contact Occlusion Presentation")]
        [Tooltip("Keeps the clipped edge slightly inside the rotating teeth, so material is hidden by the mechanism instead of appearing to vanish on its top edge.")]
        [Range(0f, .5f)] [SerializeField] private float feedMaskMouthInset = .14f;
        [Tooltip("Emits small square debris continuously from the same hidden edge. This is independent from the optional dissolve shader.")]
        [SerializeField] private bool enableContactDebrisPresentation = true;
        [Tooltip("Visible progress grains emitted for each authored board block. These use the shared particle flight buffer and all travel to the slider.")]
        [Range(1, 48)] [SerializeField] private int progressVoxelMultiplier = 24;
        [Header("Grinding Debris Presentation")]
        [Tooltip("Opt-in gate for the experimental rough-cut shader and continuous debris. Keep disabled until it has been play-tested on target devices.")]
        [SerializeField] private bool enableAdvancedShreddingPresentation;
        [Tooltip("Small square fragments emitted per second for each world unit at the active cutter edge.")]
        [Range(0f, 5000f)] [SerializeField] private float debrisPerSecondPerWorldUnit = 600f;
        [Tooltip("Dense square-fragment burst emitted across the block's contact width on the first shredder-contact frame.")]
        [Range(0f, 512f)] [SerializeField] private float debrisEntryBurstPerWorldUnit = 60f;
        [Tooltip("Shared maximum for initial contact bursts in one frame. This permits a dense first impact while bounding simultaneous feeds.")]
        [Range(1, 1024)] [SerializeField] private int maxDebrisEntryBurstPerFrame = 512;
        [Tooltip("Extra square-fragment burst emitted from the last active cutter contact when a piece has fully entered the wheels.")]
        [Range(0f, 512f)] [SerializeField] private float debrisFinalBurstPerWorldUnit = 36f;
        [Tooltip("Height of the material-conversion band above the cutter. Fragments are born inside this band instead of from a single flat line.")]
        [Range(.01f, 1f)] [SerializeField] private float debrisContactBandHeight = .22f;
        [Tooltip("Extra fragments emitted where a rendered cell fully crosses the cutter edge.")]
        [Range(0, 32)] [SerializeField] private int debrisBurstPerCrossedCell = 12;
        [Tooltip("Hard shared upper bound on cutter debris emitted by the single particle system in one frame.")]
        [Range(1, 512)] [SerializeField] private int maxDebrisPerFrame = 96;
        [Tooltip("Fixed particle capacity shared by all active shredder mouths. This is preallocated once and does not instantiate at runtime.")]
        [Range(128, 8192)] [SerializeField] private int debrisParticleCapacity = 1536;
        [Tooltip("Lifetime of a square debris fragment in seconds.")]
        [Range(.05f, 2f)] [SerializeField] private float debrisLifetime = .58f;
        [FormerlySerializedAs("debrisSizeRange")]
        [Tooltip("Layer C: World-space size range for chunky colored gravity debris chips.")]
        [SerializeField] private Vector2 debrisSize = new Vector2(0.12f, 0.22f);
        [Tooltip("Layer A: Brightness and alpha multiplier for the additive contact glow seam hider.")]
        [Range(0.1f, 5f)] [SerializeField] private float glowIntensity = 1.4f;
        [Tooltip("Layer B: Number of high-speed stretched friction sparks emitted during contact bursts.")]
        [Range(1, 60)] [SerializeField] private int sparkEmissionRate = 20;
        [FormerlySerializedAs("debrisUpwardSpeed")]
        [FormerlySerializedAs("debrisDownwardSpeed")]
        [Tooltip("Initial downward cutter-plume speed in world units per second. Gravity can add further downward acceleration.")]
        [Range(0f, 8f)] [SerializeField] private float debrisInitialDownwardSpeed = 2f;
        [Tooltip("Small lateral variation applied as fragments leave the cutter line.")]
        [Range(0f, 4f)] [SerializeField] private float debrisHorizontalSpeed = .75f;
        [Tooltip("Particle-system gravity multiplier for the debris after it leaves the wheels.")]
        [Range(0f, 2f)] [SerializeField] private float debrisGravityModifier = .35f;
        [Tooltip("Maximum absolute angular velocity, in degrees per second, for a debris fragment.")]
        [Range(0f, 1440f)] [SerializeField] private float debrisAngularVelocity = 540f;
        [Tooltip("Sorting order for debris. Keep it inside the wheel stack so teeth and hubs partially occlude the material being pulled in.")]
        [SerializeField] private int debrisSortingOrder = 28;
        [Tooltip("URP-compatible unlit material used by the pooled square cutter debris.")]
        [SerializeField] private Material debrisMaterial;

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
        [SerializeField] private ShredderPhysicsMouth physicsMouthPrefab;
        [Min(1)] [SerializeField] private int physicsMouthPoolCapacity = 1;
        [SerializeField] private ShredderFeedMask feedMaskPrefab;
        [SerializeField] private PhysicsMaterial2D feedPhysicsMaterial;
        [SerializeField] private ShredderContactGlowView contactGlowPrefab;
        [Min(1)] [SerializeField] private int contactGlowPoolCapacity = 4;

        [Header("Temporary Physics Island")]
        [Tooltip("World-space clearance below the map bottom before an exiting piece leaves the grid reservation and enters freefall physics.")]
        [Min(0f)] [SerializeField] private float physicsHandoffDistanceBelowMap = 1.5f;
        [Tooltip("Lets only a contact-connected stack above a shredder feed use Unity physics briefly. Normal board gravity remains grid-owned.")]
        [SerializeField] private bool enablePhysicsIsland = true;
        [Tooltip("Gravity applied to a temporary dynamic support piece inside the shredder mouth.")]
        [Range(0f, 5f)] [SerializeField] private float physicsIslandGravityScale = 1f;
        [Tooltip("Angular damping used by a temporary dynamic support piece.")]
        [Min(0f)] [SerializeField] private float physicsIslandAngularDrag = 4f;
        [Tooltip("Distance above the cutter at which a dynamic island member is captured by the motorized feed.")]
        [Range(0.01f, 1f)] [SerializeField] private float physicsIslandCaptureDistance = .12f;
        [Tooltip("Extra horizontal room between the board edge and each invisible containment guide.")]
        [Range(0f, 2f)] [SerializeField] private float physicsIslandGuideClearance = .25f;
        [Tooltip("Thickness of each static containment guide.")]
        [Range(.01f, 1f)] [SerializeField] private float physicsIslandGuideThickness = .12f;
        [Tooltip("How far upward from the cutter the temporary containment guides extend.")]
        [Range(.5f, 20f)] [SerializeField] private float physicsIslandGuideDepth = 6f;

        [Header("Feed Mask Presentation")]
        [Tooltip("World-space offset from the shredder line to the center of the pooled feed clipping mask.")]
        [SerializeField] private float feedMaskVerticalOffset = -15f;
        [SerializeField] private Vector2 feedMaskScale = new Vector2(60f, 30f);

        [Header("Runtime Shredder Wheels")]
        [Tooltip("Idle rotation speed of shredder gears in degrees/second.")]
        [Min(0f)] [SerializeField] private float gearIdleSpeed = 220f;
        [Tooltip("Active rotation speed of shredder gears during block contact (2.5x - 3x idle).")]
        [Min(0f)] [SerializeField] private float gearShredSpeed = 600f;
        [Tooltip("Clipping height ratio relative to wheel radius (0 = top tangent, 0.45 = upper-middle line behind teeth).")]
        [Range(0.1f, 0.9f)] [SerializeField] private float gearClippingRatio = 0.45f;
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
        [Tooltip("Duration of the visual spring release when it reaches the cutter.")]
        [Min(0.01f)] [SerializeField] private float springReleaseDuration = .2f;
        [Tooltip("World-space sideways distance of the released spring's kick.")]
        [Min(0f)] [SerializeField] private float springReleaseTravel = .18f;
        [Tooltip("World-space upward lift of the released spring's kick.")]
        [Min(0f)] [SerializeField] private float springReleaseLift = .16f;
        [Tooltip("Maximum rotational flick of the released spring, in degrees.")]
        [Min(0f)] [SerializeField] private float springReleaseRotation = 55f;
        [Tooltip("Distance above the clipping seam at which the spring release begins.")]
        [Min(0f)] [SerializeField] private float springReleaseLead = .3f;
        [Min(0.01f)] [SerializeField] private float feedSpeed = 3.8f;
        [Min(0f)] [SerializeField] private float tremorIntensity = .045f;
        [Min(0f)] [SerializeField] private float tremorFrequency = 55f;
        [FormerlySerializedAs("tremorVelocityMultiplier")]
        [Min(0f)] [SerializeField] private float feedShakeAmplitude = 2.5f;
        [Min(0f)] [SerializeField] private float feedAngularDrag = 6f;
        [Tooltip("Angular speed range used by the kinematic feed's bounded physical tilt.")]
        [Min(0f)] [SerializeField] private float tumbleTorque = .35f;
        [Range(0f, 20f)] [SerializeField] private float maxFeedTiltAngle = 5f;

        [Header("Final Piece Timer Grace")]
        [Tooltip("If the final live piece enters a shredder within this many seconds of 00:00, its feed is allowed to finish and the result becomes a win.")]
        [Min(0f)] [SerializeField] private float finalPieceTimerGraceSeconds = 1f;

        public float WheelRadiusMultiplier => wheelRadiusMultiplier;
        public float SpringReleaseDuration => springReleaseDuration;
        public float SpringReleaseTravel => springReleaseTravel;
        public float SpringReleaseLift => springReleaseLift;
        public float SpringReleaseRotation => springReleaseRotation;
        public float SpringReleaseLead => springReleaseLead;
        public ShredderWheel WheelPrefab => wheelPrefab;
        public int WheelPoolCapacity => wheelPoolCapacity;
        public ShredderCatchZone CatchZonePrefab => catchZonePrefab;
        public int CatchZonePoolCapacity => catchZonePoolCapacity;
        public ShredderPhysicsMouth PhysicsMouthPrefab => physicsMouthPrefab;
        public int PhysicsMouthPoolCapacity => physicsMouthPoolCapacity;
        public ShredderFeedMask FeedMaskPrefab => feedMaskPrefab;
        public ShredderContactGlowView ContactGlowPrefab => contactGlowPrefab;
        public int ContactGlowPoolCapacity => contactGlowPoolCapacity;
        public float FeedMaskVerticalOffset => feedMaskVerticalOffset;
        public Vector2 FeedMaskScale => feedMaskScale;
        /// <summary>Authored low-friction material used only during a shredder feed.</summary>
        public PhysicsMaterial2D FeedPhysicsMaterial => feedPhysicsMaterial;
        public bool EnablePhysicsIsland => enablePhysicsIsland;
        public float PhysicsHandoffDistanceBelowMap => physicsHandoffDistanceBelowMap;
        public float PhysicsIslandGravityScale => physicsIslandGravityScale;
        public float PhysicsIslandAngularDrag => physicsIslandAngularDrag;
        public float PhysicsIslandCaptureDistance => physicsIslandCaptureDistance;
        public float PhysicsIslandGuideClearance => physicsIslandGuideClearance;
        public float PhysicsIslandGuideThickness => physicsIslandGuideThickness;
        public float PhysicsIslandGuideDepth => physicsIslandGuideDepth;
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
        public float FeedMaskMouthInset => feedMaskMouthInset;
        public bool EnableContactDebrisPresentation => enableContactDebrisPresentation;
        public int ProgressVoxelMultiplier => Mathf.Clamp(progressVoxelMultiplier, 1, 48);

        /// <summary>
        /// Calculates the complete presentation budget for one shredder feed.
        /// The configured multiplier is a direct grain-per-block value, rather
        /// than a multiplier for a hidden hard-coded burst size.
        /// </summary>
        public int GetProgressVoxelFlightCount(int blockCount)
        {
            return Mathf.Max(1, blockCount) * ProgressVoxelMultiplier;
        }
        public float DebrisPerSecondPerWorldUnit => debrisPerSecondPerWorldUnit;
        public float DebrisEntryBurstPerWorldUnit => debrisEntryBurstPerWorldUnit;
        public int MaxDebrisEntryBurstPerFrame => maxDebrisEntryBurstPerFrame;
        public float DebrisFinalBurstPerWorldUnit => debrisFinalBurstPerWorldUnit;
        public float DebrisContactBandHeight => debrisContactBandHeight;
        public bool EnableAdvancedShreddingPresentation => enableAdvancedShreddingPresentation;
        public int DebrisBurstPerCrossedCell => debrisBurstPerCrossedCell;
        public int MaxDebrisPerFrame => maxDebrisPerFrame;
        public int DebrisParticleCapacity => debrisParticleCapacity;
        public float DebrisLifetime => Mathf.Max(.05f, debrisLifetime);
        public Vector2 DebrisSize => debrisSize;
        public Vector2 DebrisSizeRange => debrisSize;
        public float GlowIntensity => glowIntensity;
        public int SparkEmissionRate => sparkEmissionRate;
        public float GearIdleSpeed => gearIdleSpeed;
        public float GearShredSpeed => gearShredSpeed;
        public float GearClippingRatio => Mathf.Clamp(gearClippingRatio, 0.1f, 0.9f);
        public float DebrisInitialDownwardSpeed => Mathf.Max(0f, debrisInitialDownwardSpeed);
        public float DebrisHorizontalSpeed => debrisHorizontalSpeed;
        public float DebrisGravityModifier => debrisGravityModifier;
        public float DebrisAngularVelocity => debrisAngularVelocity;
        public int DebrisSortingOrder => debrisSortingOrder;
        public Material DebrisMaterial => debrisMaterial;
        public float CutterEdgeAmplitude => cutterEdgeAmplitude;
        public float CutterEdgeSoftness => cutterEdgeSoftness;
        public float CutterNoiseFrequency => cutterNoiseFrequency;
        public float FeedSpeed => feedSpeed;
        public float TremorIntensity => tremorIntensity;
        public float TremorFrequency => tremorFrequency;
        public float FeedShakeAmplitude => feedShakeAmplitude;
        public float FeedAngularDrag => feedAngularDrag;
        public float TumbleTorque => tumbleTorque;
        public float MaxFeedTiltAngle => maxFeedTiltAngle;
        public float FinalPieceTimerGraceSeconds => finalPieceTimerGraceSeconds;
    }
}
