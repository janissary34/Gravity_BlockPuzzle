using UnityEngine;

namespace GravityPuzzle.Config
{
    [CreateAssetMenu(fileName = "PoolConfig", menuName = "Gravity Puzzle/Config/Pool")]
    public sealed class PoolConfig : ScriptableObject
    {
        [Min(0)] [SerializeField] private int blockPieceCapacity = 32;
        [Min(0)] [SerializeField] private int shredVoxelCapacity = 128;
        [Min(0)] [SerializeField] private int progressVoxelCapacity = 96;

        [Header("Voxel Presentation Mode")]
        [Tooltip("If true, pieces are composed of subdivided VoxelShards (legacy). If false (default), pieces use clean solid cells with Particle System effects.")]
        [SerializeField] private bool useVoxelShardGrid = true;
        [Range(1, 20)] [SerializeField] private int voxelSubdivisions = 3;

        public int BlockPieceCapacity => blockPieceCapacity;
        public int ShredVoxelCapacity => shredVoxelCapacity;
        public int ProgressVoxelCapacity => progressVoxelCapacity;
        public int VoxelSubdivisions => voxelSubdivisions;
        public bool UseVoxelShardGrid => useVoxelShardGrid;
    }
}
