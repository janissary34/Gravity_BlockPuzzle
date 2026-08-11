using UnityEngine;

namespace GravityPuzzle.Config
{
    [CreateAssetMenu(fileName = "PoolConfig", menuName = "Gravity Puzzle/Config/Pool")]
    public sealed class PoolConfig : ScriptableObject
    {
        [Min(0)] [SerializeField] private int blockPieceCapacity = 32;
        [Min(0)] [SerializeField] private int shredVoxelCapacity = 128;
        [Min(0)] [SerializeField] private int particleCapacity = 128;

        public int BlockPieceCapacity => blockPieceCapacity;
        public int ShredVoxelCapacity => shredVoxelCapacity;
        public int ParticleCapacity => particleCapacity;
    }
}
