using UnityEngine;

namespace GravityPuzzle.Config
{
    [CreateAssetMenu(fileName = "PoolConfig", menuName = "Gravity Puzzle/Config/Pool")]
    public sealed class PoolConfig : ScriptableObject
    {
        [Min(0)] [SerializeField] private int blockPieceCapacity = 32;
        [Min(0)] [SerializeField] private int shredVoxelCapacity = 128;

        [Header("Voxel Presentation")]
        [Tooltip("Voxel grid resolution per authored block cell. 3 creates 3x3 = 9 voxels per cell.")]
        [Range(1, 6)] [SerializeField] private int voxelSubdivisions = 3;

        public int BlockPieceCapacity => blockPieceCapacity;
        public int ShredVoxelCapacity => shredVoxelCapacity;
        public int VoxelSubdivisions => voxelSubdivisions;
    }
}
