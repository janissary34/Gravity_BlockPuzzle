using System.Collections.Generic;
using GravityPuzzle.Infrastructure.Pooling;
using UnityEngine;

namespace GravityPuzzle
{
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class ShredderCatchZone : MonoBehaviour, IPoolable
    {
        private static readonly List<ShredderCatchZone> activeZones = new List<ShredderCatchZone>();

        private BoxCollider2D trigger;
        public static IReadOnlyList<ShredderCatchZone> ActiveZones => activeZones;
        public float ShredY { get; private set; }

        private void Awake()
        {
            trigger = GetComponent<BoxCollider2D>();
        }

        public void Configure(Vector2 position, Vector2 size, float shredY)
        {
            transform.position = position;
            trigger.size = size;
            trigger.isTrigger = true;
            trigger.enabled = true;
            ShredY = shredY;
            gameObject.name = "Shredder Catch Zone";
        }

        public void OnSpawn()
        {
            ShredY = 0f;
        }

        public void OnDespawn()
        {
            trigger.enabled = false;
            ShredY = 0f;
        }

        private void OnEnable()
        {
            if (!activeZones.Contains(this))
                activeZones.Add(this);
        }

        private void OnDisable()
        {
            activeZones.Remove(this);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            ShredderWheel.TryShred(other, new Vector2(other.transform.position.x, ShredY));
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            ShredderWheel.TryShred(other, new Vector2(other.transform.position.x, ShredY));
        }
    }
}
