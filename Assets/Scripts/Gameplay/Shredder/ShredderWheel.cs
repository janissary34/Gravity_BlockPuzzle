using System.Collections.Generic;
using UnityEngine;
using GravityPuzzle.Config;
using GravityPuzzle.Infrastructure.Pooling;

namespace GravityPuzzle
{
    public sealed class ShredderWheel : MonoBehaviour, IPoolable
    {
        private static readonly List<ShredderWheel> activeWheels = new List<ShredderWheel>();
        public static IReadOnlyList<ShredderWheel> ActiveWheels => activeWheels;

        private float baseDirection = 1f;
        private float idleSpeed = 220f;
        private float shredSpeed = 550f;
        private float currentSpeed = 220f;
        private bool isShredding;
        private bool isContactVibrating;
        private Vector3 initialLocalScale = Vector3.one;
        private Vector3 basePosition;
        private CircleCollider2D trigger;
        private SpriteRenderer[] visualRenderers;
        private bool hasAuthoredHierarchy;

        public float WorldX => transform.position.x;
        public float Radius { get; private set; }

        private void Awake()
        {
            // Pool instances cache prefab-authored dependencies once. Configure
            // is called repeatedly by gameplay, so it must not search/build.
            trigger = GetComponent<CircleCollider2D>();
            visualRenderers = GetComponentsInChildren<SpriteRenderer>(true);
            hasAuthoredHierarchy = trigger != null && visualRenderers.Length > 0;
            basePosition = transform.position;
            initialLocalScale = transform.localScale;
        }

        private void OnEnable()
        {
            if (!activeWheels.Contains(this))
                activeWheels.Add(this);
        }

        private void OnDisable()
        {
            activeWheels.Remove(this);
        }

        // Runtime path: a prewarmed prefab instance is configured, never built.
        public void Configure(float radius, float speed)
        {
            Radius = radius;
            baseDirection = speed >= 0f ? 1f : -1f;
            idleSpeed = Mathf.Max(10f, Mathf.Abs(speed));
            shredSpeed = idleSpeed * 2.8f;
            currentSpeed = idleSpeed;
            isShredding = false;
            isContactVibrating = false;

            if (!hasAuthoredHierarchy)
            {
                Debug.LogError("[ShredderPool] ShredderWheel prefab is missing its authored trigger or visual children.", this);
                return;
            }

            transform.localScale = Vector3.one * radius;
            initialLocalScale = transform.localScale;
            basePosition = transform.position;
            trigger.enabled = true;
            RestoreAuthoredVisuals();
        }

        public void ConfigureSpeeds(float idle, float shred)
        {
            idleSpeed = Mathf.Max(10f, idle);
            shredSpeed = Mathf.Max(idleSpeed, shred);
            currentSpeed = isShredding ? shredSpeed : idleSpeed;
        }

        public void SetShredding(bool active, bool contactVibrate = false)
        {
            isShredding = active;
            isContactVibrating = active && contactVibrate;
            if (!active)
            {
                transform.position = basePosition;
                transform.localScale = initialLocalScale;
            }
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

        private void Update()
        {
            float targetSpeed = isShredding ? shredSpeed : idleSpeed;
            currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, 1200f * Time.deltaTime);

            transform.Rotate(0f, 0f, currentSpeed * baseDirection * Time.deltaTime);

            if (isContactVibrating)
            {
                float time = Time.time * 65f;
                float vibX = Mathf.Sin(time) * 0.015f;
                float vibY = Mathf.Cos(time * 1.37f) * 0.010f;
                transform.position = basePosition + new Vector3(vibX, vibY, 0f);

                float scalePulse = 1f + Mathf.Sin(time * 0.85f) * 0.03f;
                transform.localScale = initialLocalScale * scalePulse;
            }
            else
            {
                transform.position = basePosition;
                transform.localScale = initialLocalScale;
            }
        }

        private void RestoreAuthoredVisuals()
        {
            for (int index = 0; index < visualRenderers.Length; index++)
                visualRenderers[index].enabled = visualRenderers[index].sprite != null;
        }

        public void OnSpawn()
        {
            currentSpeed = idleSpeed;
            isShredding = false;
            isContactVibrating = false;
            transform.localRotation = Quaternion.identity;
        }

        public void OnDespawn()
        {
            currentSpeed = idleSpeed;
            isShredding = false;
            isContactVibrating = false;
            transform.position = basePosition;
            transform.localScale = initialLocalScale;
            transform.localRotation = Quaternion.identity;
        }
    }
}
