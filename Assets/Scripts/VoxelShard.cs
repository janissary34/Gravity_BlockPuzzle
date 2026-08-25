using UnityEngine;
using System.Collections;
using System;
using GravityPuzzle.Infrastructure.Pooling;

namespace GravityPuzzle
{
    [RequireComponent(typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(BoxCollider2D))]
    public sealed class VoxelShard : MonoBehaviour, IPoolable
    {
        private SpriteRenderer spriteRenderer;
        private Rigidbody2D rb;
        private BoxCollider2D col;

        // The owning PuzzlePiece captures this during configuration so the
        // shredder feed never needs to search the hierarchy for renderers.
        public SpriteRenderer Renderer => spriteRenderer;

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            rb = GetComponent<Rigidbody2D>();
            col = GetComponent<BoxCollider2D>();
        }

        public void OnSpawn()
        {
            StopAllCoroutines();
            rb.simulated = false;
            rb.velocity = Vector2.zero;
            rb.angularVelocity = 0f;
            col.enabled = false;
            transform.localRotation = Quaternion.identity;
        }

        public void OnDespawn()
        {
            StopAllCoroutines();
            rb.simulated = false;
            rb.velocity = Vector2.zero;
            rb.angularVelocity = 0f;
            col.enabled = false;
        }

        public void InitializeIntact(Color color, Vector2 size, Sprite sprite)
        {
            spriteRenderer.color = color;
            spriteRenderer.sprite = sprite;
            spriteRenderer.sortingOrder = 5; 
            transform.localScale = new Vector3(size.x, size.y, 1f);
            transform.localRotation = Quaternion.identity;
            
            if (rb != null) rb.simulated = false;
            if (col != null) col.enabled = false;
            gameObject.SetActive(true);
        }

        /// <summary>
        /// Plays the short post-grinder presentation for this already pooled shard,
        /// then transfers its progress to the UI and returns it to the voxel pool.
        /// No temporary grain GameObject or runtime component is created here.
        /// </summary>
        public void BeginProgressHandoff(Vector2 grinderSeamPosition, Color color, float progressAmount)
        {
            StopAllCoroutines();
            transform.SetParent(null, true);
            transform.position = grinderSeamPosition + new Vector2(UnityEngine.Random.Range(-.13f, .13f), 0f);
            transform.localRotation = Quaternion.identity;

            spriteRenderer.enabled = true;
            spriteRenderer.color = new Color(color.r, color.g, color.b, 1f);
            spriteRenderer.maskInteraction = SpriteMaskInteraction.None;
            spriteRenderer.sortingOrder = 4;
            rb.simulated = false;
            col.enabled = false;

            StartCoroutine(PlayProgressHandoff(progressAmount));
        }

        private IEnumerator PlayProgressHandoff(float progressAmount)
        {
            float delay = UnityEngine.Random.Range(0f, .12f);
            if (delay > 0f)
                yield return new WaitForSeconds(delay);

            Vector3 start = transform.position;
            Vector3 end = start + new Vector3(UnityEngine.Random.Range(-.08f, .08f), UnityEngine.Random.Range(-1.2f, -.5f), 0f);
            const float dropDuration = .18f;
            float elapsed = 0f;
            while (elapsed < dropDuration)
            {
                elapsed += Time.deltaTime;
                transform.position = Vector3.Lerp(start, end, elapsed / dropDuration);
                yield return null;
            }

            spriteRenderer.enabled = false;
            LevelProgressManager manager = LevelProgressManager.Instance;
            if (manager != null)
            {
                manager.SpawnFlyingVoxel(transform.position, spriteRenderer.color, progressAmount, Recycle);
            }
            else
            {
                Recycle();
            }
        }

        public void Recycle()
        {
            VoxelBlockBuilder.ReturnVoxel(this);
        }
    }
}
