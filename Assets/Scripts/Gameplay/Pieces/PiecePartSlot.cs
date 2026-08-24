using System.Collections.Generic;
using UnityEngine;

namespace GravityPuzzle.Gameplay.Pieces
{
    public sealed class PiecePartSlot : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer visual;
        [SerializeField] private BoxCollider2D collision;
        private readonly List<VoxelShard> voxelShards = new List<VoxelShard>();

        public SpriteRenderer Visual => visual;
        public BoxCollider2D Collision => collision;
        public IReadOnlyList<VoxelShard> VoxelShards => voxelShards;

        /// <summary>
        /// RuntimePieceFactory registers each pooled voxel as it configures this
        /// authored slot. The slot can therefore return its own presentation
        /// without searching the transform hierarchy during a rebuild.
        /// </summary>
        public void RegisterVoxel(VoxelShard shard)
        {
            if (shard != null)
                voxelShards.Add(shard);
        }

        public void ReturnVoxels()
        {
            for (int index = 0; index < voxelShards.Count; index++)
                VoxelBlockBuilder.ReturnVoxel(voxelShards[index]);

            voxelShards.Clear();
        }

        public void ResetSlot()
        {
            if (visual != null)
                visual.enabled = false;
            if (collision != null)
                collision.enabled = false;
            transform.localPosition = Vector3.zero;
            transform.localScale = Vector3.one;
            if (collision != null)
            {
                collision.transform.localPosition = Vector3.zero;
                collision.transform.localScale = Vector3.one;
            }
        }

#if UNITY_EDITOR
        public void ConfigureForAuthoring(SpriteRenderer slotVisual, BoxCollider2D slotCollision)
        {
            visual = slotVisual;
            collision = slotCollision;
        }
#endif
    }
}
