using UnityEngine;
using GravityPuzzle.Infrastructure.Pooling;

namespace GravityPuzzle
{
    public sealed class ShredderWheel : MonoBehaviour, IPoolable
    {
        private const int ToothCount = 12;
        private float rotationSpeed;

        // Runtime path: a prewarmed prefab instance is configured, never built.
        public void Configure(float radius, float speed)
        {
            rotationSpeed = speed;
            CircleCollider2D trigger = GetComponent<CircleCollider2D>();
            if (trigger == null || transform.childCount == 0)
            {
                Debug.LogError("[ShredderPool] ShredderWheel prefab is missing its authored trigger or visual children.", this);
                return;
            }

            transform.localScale = Vector3.one * radius;
            trigger.enabled = true;
            RestoreAuthoredVisuals();
        }

#if UNITY_EDITOR
        // Used only by the editor menu that authors ShredderWheel.prefab.
        public void BuildAuthoredPrefabHierarchy()
        {
            CircleCollider2D trigger = gameObject.AddComponent<CircleCollider2D>();
            trigger.radius = .92f;
            trigger.isTrigger = true;

            GameObject disc = new GameObject("Shredder Disc");
            disc.transform.SetParent(transform, false);
            disc.transform.localScale = Vector3.one * 1.65f;
            SpriteRenderer discRenderer = disc.AddComponent<SpriteRenderer>();
            discRenderer.sprite = PrototypeBootstrap.GetCircleSprite();
            discRenderer.color = new Color(.32f, .36f, .48f);
            discRenderer.sortingOrder = 25;

            GameObject hub = new GameObject("Shredder Hub");
            hub.transform.SetParent(transform, false);
            hub.transform.localScale = Vector3.one * .48f;
            SpriteRenderer hubRenderer = hub.AddComponent<SpriteRenderer>();
            hubRenderer.sprite = PrototypeBootstrap.GetCircleSprite();
            hubRenderer.color = new Color(.1f, .12f, .18f);
            hubRenderer.sortingOrder = 27;

            for (int index = 0; index < ToothCount; index++)
            {
                float angle = index * 360f / ToothCount;
                GameObject tooth = PrototypeBootstrap.CreateVisualBlock(
                    $"Tooth {index + 1}", Vector2.zero, new Vector2(.42f, .24f), new Color(.75f, .8f, .92f));
                tooth.transform.SetParent(transform, false);
                tooth.transform.localPosition = Quaternion.Euler(0f, 0f, angle) * Vector3.up * .86f;
                tooth.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
                tooth.GetComponent<SpriteRenderer>().sortingOrder = 26;
            }
        }
#endif

        private void Update() => transform.Rotate(0f, 0f, rotationSpeed * Time.deltaTime);

        private void RestoreAuthoredVisuals()
        {
            SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
            for (int index = 0; index < renderers.Length; index++)
                renderers[index].enabled = renderers[index].sprite != null;
        }

        public void OnSpawn()
        {
            rotationSpeed = 0f;
            transform.localRotation = Quaternion.identity;
        }

        public void OnDespawn()
        {
            rotationSpeed = 0f;
            transform.localRotation = Quaternion.identity;
        }

        private void OnTriggerEnter2D(Collider2D other) => TryShred(other, transform.position);

        internal static void TryShred(Collider2D other, Vector2 shredderCentre)
        {
            BlockShredder shredder = BlockShredder.Instance;
            if (shredder != null)
            {
                shredder.TryShredBlock(other, shredderCentre);
                return;
            }

            Debug.LogError("[ShredderPool] No BlockShredder is available to process a wheel trigger.");
        }
    }
}
