using UnityEngine;
using GravityPuzzle.Config;
using GravityPuzzle.Infrastructure.Pooling;

namespace GravityPuzzle
{
    public sealed class ShredderWheel : MonoBehaviour, IPoolable
    {
        private float rotationSpeed;
        private CircleCollider2D trigger;
        private SpriteRenderer[] visualRenderers;
        private bool hasAuthoredHierarchy;

        private void Awake()
        {
            // Pool instances cache prefab-authored dependencies once. Configure
            // is called repeatedly by gameplay, so it must not search/build.
            trigger = GetComponent<CircleCollider2D>();
            visualRenderers = GetComponentsInChildren<SpriteRenderer>(true);
            hasAuthoredHierarchy = trigger != null && visualRenderers.Length > 0;
        }

        // Runtime path: a prewarmed prefab instance is configured, never built.
        public void Configure(float radius, float speed)
        {
            rotationSpeed = speed;
            if (!hasAuthoredHierarchy)
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
        public void BuildAuthoredPrefabHierarchy(ShredderConfig config)
        {
            if (config == null)
            {
                Debug.LogError("[ShredderPrefab] A ShredderConfig is required to author a wheel prefab.", this);
                return;
            }

            CircleCollider2D trigger = gameObject.AddComponent<CircleCollider2D>();
            trigger.radius = .92f;
            trigger.isTrigger = true;

            GameObject disc = new GameObject("Shredder Disc");
            disc.transform.SetParent(transform, false);
            disc.transform.localScale = Vector3.one * (config.DiscArtScale * config.WheelArtScale);
            SpriteRenderer discRenderer = disc.AddComponent<SpriteRenderer>();
            discRenderer.sprite = PrototypeBootstrap.GetCircleSprite();
            discRenderer.color = new Color(.32f, .36f, .48f);
            discRenderer.sortingOrder = config.DiscSortingOrder;

            GameObject hub = new GameObject("Shredder Hub");
            hub.transform.SetParent(transform, false);
            hub.transform.localScale = Vector3.one * (config.HubArtScale * config.WheelArtScale);
            SpriteRenderer hubRenderer = hub.AddComponent<SpriteRenderer>();
            hubRenderer.sprite = PrototypeBootstrap.GetCircleSprite();
            hubRenderer.color = new Color(.1f, .12f, .18f);
            hubRenderer.sortingOrder = config.HubSortingOrder;

            for (int index = 0; index < config.WheelToothCount; index++)
            {
                float angle = index * 360f / config.WheelToothCount;
                GameObject tooth = PrototypeBootstrap.CreateVisualBlock(
                    $"Tooth {index + 1}", Vector2.zero, config.ToothArtScale, new Color(.75f, .8f, .92f));
                tooth.transform.SetParent(transform, false);
                tooth.transform.localScale = Vector3.one * config.WheelArtScale;
                tooth.transform.localPosition = Quaternion.Euler(0f, 0f, angle) * Vector3.up * config.ToothRadialOffset;
                tooth.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
                tooth.GetComponent<SpriteRenderer>().sortingOrder = config.ToothSortingOrder;
            }
        }
#endif

        private void Update() => transform.Rotate(0f, 0f, rotationSpeed * Time.deltaTime);

        private void RestoreAuthoredVisuals()
        {
            for (int index = 0; index < visualRenderers.Length; index++)
                visualRenderers[index].enabled = visualRenderers[index].sprite != null;
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

    }
}
