using UnityEngine;
using GravityPuzzle.Infrastructure.Pooling;

namespace GravityPuzzle
{
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class VoxelShard : MonoBehaviour, IPoolable
    {
        private SpriteRenderer spriteRenderer;

        public SpriteRenderer Renderer => spriteRenderer != null ? spriteRenderer : (spriteRenderer = GetComponent<SpriteRenderer>());

        private void Awake()
        {
            if (spriteRenderer == null)
                spriteRenderer = GetComponent<SpriteRenderer>();
        }

        public void OnSpawn()
        {
            transform.localRotation = Quaternion.identity;
        }

        public void OnDespawn()
        {
            transform.localRotation = Quaternion.identity;
        }

        public void InitializeIntact(Color color, Vector2 size, Sprite sprite)
        {
            if (spriteRenderer == null)
                spriteRenderer = GetComponent<SpriteRenderer>();

            spriteRenderer.color = color;
            spriteRenderer.sprite = sprite;
            spriteRenderer.sortingOrder = 5;
            transform.localScale = new Vector3(size.x, size.y, 1f);
            transform.localRotation = Quaternion.identity;
            gameObject.SetActive(true);
        }

        /// <summary>
        /// Hand off shredded shard to Particle System progress presentation and recycle immediately.
        /// </summary>
        public void BeginProgressHandoff(Vector2 grinderSeamPosition, Color color, float progressAmount, int particleCount = 1)
        {
            LevelProgressManager.Instance?.SpawnFlyingVoxel(grinderSeamPosition, color, progressAmount, null, particleCount);
            Recycle();
        }

        public void Recycle()
        {
            VoxelBlockBuilder.ReturnVoxel(this);
        }
    }
}
