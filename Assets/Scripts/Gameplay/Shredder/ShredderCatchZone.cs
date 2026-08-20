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
        private float leftX;
        private float rightX;
        private float captureTopY;

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
            leftX = position.x - size.x * .5f;
            rightX = position.x + size.x * .5f;
            captureTopY = position.y + size.y * .5f;
            gameObject.name = "Shredder Catch Zone";
        }

        /// <summary>
        /// Checks the configured board-space catch volume. Trigger callbacks are
        /// deliberately not used as gameplay authority: a piece is captured only
        /// when its current footprint reaches this explicit coordinate boundary.
        /// </summary>
        public bool ContainsCaptureFootprint(PuzzlePiece piece)
        {
            if (piece == null || piece.IsBeingShredded)
                return false;

            Bounds bounds = piece.CollisionBounds;
            return bounds.max.x >= leftX &&
                   bounds.min.x <= rightX &&
                   bounds.min.y <= captureTopY;
        }

        public void OnSpawn()
        {
            ShredY = 0f;
            leftX = 0f;
            rightX = 0f;
            captureTopY = 0f;
        }

        public void OnDespawn()
        {
            trigger.enabled = false;
            ShredY = 0f;
            leftX = 0f;
            rightX = 0f;
            captureTopY = 0f;
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
    }
}
