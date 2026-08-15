using UnityEngine;
using System.Collections.Generic;

namespace GravityPuzzle
{
    public static class VoxelBlockBuilder
    {
        private static Queue<VoxelShard> voxelPool = new Queue<VoxelShard>();
        private static Transform poolContainer;
        private static Sprite defaultVoxelSprite;
        
        public const int Subdivisions = 3; // 3x3 grid

        private static VoxelShard CreateNewVoxel()
        {
            GameObject go = new GameObject("VoxelShard");
            if (poolContainer == null) poolContainer = new GameObject("Voxel Shard Pool").transform;
            go.transform.SetParent(poolContainer, false);
            return go.AddComponent<VoxelShard>();
        }

        public static VoxelShard GetVoxel()
        {
            return voxelPool.Count > 0 ? voxelPool.Dequeue() : CreateNewVoxel();
        }

        public static void ReturnVoxel(VoxelShard shard)
        {
            shard.gameObject.SetActive(false);
            voxelPool.Enqueue(shard);
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

        public static void BuildVoxelGrid(Transform parent, string namePrefix, Vector2 totalSize, Color color)
        {
            float voxelWidth = totalSize.x / Subdivisions;
            float voxelHeight = totalSize.y / Subdivisions;
            Vector2 voxelSize = new Vector2(voxelWidth, voxelHeight);
            
            Sprite sprite = GetDefaultVoxelSprite();

            float startX = -totalSize.x * 0.5f + voxelWidth * 0.5f;
            float startY = -totalSize.y * 0.5f + voxelHeight * 0.5f;
            for (int x = 0; x < Subdivisions; x++)
            {
                for (int y = 0; y < Subdivisions; y++)
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
