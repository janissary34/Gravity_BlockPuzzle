using UnityEngine;
using System.Collections;
using System;
using GravityPuzzle.Infrastructure.Pooling;

namespace GravityPuzzle
{
    [RequireComponent(typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(BoxCollider2D))]
    [RequireComponent(typeof(GemFlyToUI))]
    public sealed class VoxelShard : MonoBehaviour, IPoolable
    {
        private SpriteRenderer spriteRenderer;
        private Rigidbody2D rb;
        private BoxCollider2D col;
        private GemFlyToUI gemFly;

        // The owning PuzzlePiece captures this during configuration so the
        // shredder feed never needs to search the hierarchy for renderers.
        public SpriteRenderer Renderer => spriteRenderer;

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            rb = GetComponent<Rigidbody2D>();
            col = GetComponent<BoxCollider2D>();
            gemFly = GetComponent<GemFlyToUI>();
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

        public void TriggerShred(Vector2 impactPoint, Vector2 ejectionForce, bool isGem, Action<GemFlyToUI> onGemRecycle, RectTransform uiTargetRect, UnityEngine.UI.Slider uiTargetSlider, Camera cam, float flyDuration, DG.Tweening.Ease flyEase)
        {
            transform.SetParent(null); // Detach from parent block
            if (spriteRenderer != null)
                spriteRenderer.maskInteraction = SpriteMaskInteraction.None;

            col.enabled = true;
            col.size = Vector2.one; 
            
            if (isGem)
            {
                spriteRenderer.sortingOrder = 4; // Behind shredder disc
                gemFly.Launch(
                    transform.position,
                    ejectionForce,
                    uiTargetRect,
                    uiTargetSlider,
                    cam,
                    flyDuration,
                    flyEase,
                    (gem) => {
                        onGemRecycle?.Invoke(gem);
                        Recycle();
                    }
                );
            }
            else
            {
                spriteRenderer.sortingOrder = 4; // Behind shredder disc
                rb.simulated = true;
                rb.bodyType = RigidbodyType2D.Dynamic;
                rb.gravityScale = 0.35f;
                rb.velocity = ejectionForce * 0.5f;
                rb.angularVelocity = UnityEngine.Random.Range(-180f, 180f);
                StartCoroutine(FadeOutAndRecycle());
            }
        }

        private IEnumerator FadeOutAndRecycle()
        {
            float lifetime = UnityEngine.Random.Range(0.6f, 1.2f);
            yield return new WaitForSeconds(lifetime * 0.5f);
            
            float duration = lifetime * 0.5f;
            float time = 0f;
            Color startColor = spriteRenderer.color;
            Color endColor = new Color(startColor.r, startColor.g, startColor.b, 0f);

            while (time < duration)
            {
                time += Time.deltaTime;
                spriteRenderer.color = Color.Lerp(startColor, endColor, time / duration);
                yield return null;
            }

            Recycle();
        }

        public void Recycle()
        {
            VoxelBlockBuilder.ReturnVoxel(this);
        }
    }
}
