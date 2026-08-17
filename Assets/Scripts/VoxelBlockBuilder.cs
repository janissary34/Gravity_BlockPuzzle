using UnityEngine;
using GravityPuzzle.Infrastructure.Pooling;

namespace GravityPuzzle
{
    public static class VoxelBlockBuilder
    {
        private static PoolService poolService;
        private static Sprite defaultVoxelSprite;
        
        private static int subdivisions = 3;
        public static int Subdivisions => subdivisions;

        public static void SetPoolService(PoolService pools, int configuredSubdivisions)
        {
            poolService = pools ?? throw new System.ArgumentNullException(nameof(pools));
            subdivisions = Mathf.Clamp(configuredSubdivisions, 1, 6);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPool()
        {
            poolService = null;
            defaultVoxelSprite = null;
            subdivisions = 3;
        }

        public static VoxelShard GetVoxel()
        {
            if (poolService == null || !poolService.TryGet(out IPool<VoxelShard> voxelPool))
                throw new System.InvalidOperationException(
                    "[VoxelPool] VoxelBlockBuilder has not been configured by RuntimePieceFactoryBootstrap.");

            if (voxelPool.TryRent(out VoxelShard shard))
                return shard;

            throw new System.InvalidOperationException("[VoxelPool] Pool exhausted. Increase PoolConfig.ShredVoxelCapacity.");
        }

        public static void ReturnVoxel(VoxelShard shard)
        {
            if (shard != null && poolService != null &&
                poolService.TryGet(out IPool<VoxelShard> voxelPool))
                voxelPool.Return(shard);
        }

        public static int EstimateMaximumVoxelCount(GravityLevelDefinition level, int configuredSubdivisions)
        {
            if (level == null || level.pieces == null)
                return 0;

            int voxelsPerCell = configuredSubdivisions * configuredSubdivisions;
            int total = 0;
            for (int index = 0; index < level.pieces.Count; index++)
            {
                PieceDefinition piece = level.pieces[index];
                if (piece != null && piece.cells != null)
                    total += piece.cells.Count * voxelsPerCell;
            }

            return total;
        }

        public static Sprite GetDefaultVoxelSprite()
        {
            if (defaultVoxelSprite == null)
            {
                Texture2D tex = new Texture2D(16, 16);
                Color[] pixels = new Color[16 * 16];
                for (int y = 0; y < 16; y++)
                {
                    for (int x = 0; x < 16; x++)
                    {
                        bool border = (x == 0 || x == 15 || y == 0 || y == 15);
                        pixels[y * 16 + x] = border ? new Color(1f, 1f, 1f, 0.85f) : Color.white;
                    }
                }
                tex.SetPixels(pixels);
                tex.Apply();
                defaultVoxelSprite = Sprite.Create(tex, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 16);
            }
            return defaultVoxelSprite;
        }

        public static void BuildVoxelGrid(
            Transform parent,
            string namePrefix,
            Vector2 totalSize,
            Color color,
            Sprite voxelSprite = null)
        {
            float voxelWidth = totalSize.x / subdivisions;
            float voxelHeight = totalSize.y / subdivisions;
            Vector2 voxelSize = new Vector2(voxelWidth, voxelHeight);
            
            Sprite sprite = voxelSprite != null ? voxelSprite : GetDefaultVoxelSprite();

            float startX = -totalSize.x * 0.5f + voxelWidth * 0.5f;
            float startY = -totalSize.y * 0.5f + voxelHeight * 0.5f;
            for (int x = 0; x < subdivisions; x++)
            {
                for (int y = 0; y < subdivisions; y++)
                {
                    VoxelShard shard = GetVoxel();
                    shard.transform.SetParent(parent, false);
                    
                    Vector2 localPos = new Vector2(startX + x * voxelWidth, startY + y * voxelHeight);
                    shard.transform.localPosition = localPos;
                    shard.gameObject.name = $"{namePrefix} Voxel_{x}_{y}";
                    
                    shard.InitializeIntact(color, voxelSize, sprite);
                }
            }
        }
    }
}
