using System;
using UnityEngine;

namespace GravityPuzzle.Presentation.Views
{
    /// <summary>
    /// Scene-authored storage for level presentation and static collision slots.
    /// Level data still owns topology; this component only reconfigures components
    /// that were created in Edit Mode so a level build does not instantiate objects
    /// or add components at runtime.
    /// </summary>
    public sealed class LevelRuntimeRoot : MonoBehaviour
    {
        [Header("Hierarchy")]
        [SerializeField] private Transform visualSlotRoot;
        [SerializeField] private Transform boardBackgroundRoot;
        [SerializeField] private Transform boardFrameRoot;
        [SerializeField] private Transform obstacleVisualRoot;
        [SerializeField] private Transform obstacleCollisionRoot;
        [SerializeField] private Transform mapCollisionRoot;

        [Header("Pre-authored Slots")]
        [SerializeField] private Sprite fallbackSprite;
        [SerializeField] private SpriteRenderer[] visualSlots = Array.Empty<SpriteRenderer>();
        [SerializeField] private BoxCollider2D[] obstacleCollisionSlots = Array.Empty<BoxCollider2D>();
        [SerializeField] private BoxCollider2D[] mapCollisionSlots = Array.Empty<BoxCollider2D>();
        [SerializeField] private Rigidbody2D mapCollisionBody;
        [SerializeField] private CompositeCollider2D mapCompositeCollider;

        private int visualSlotIndex;
        private int obstacleCollisionSlotIndex;
        private int mapCollisionSlotIndex;

        public Transform BoardBackgroundRoot => boardBackgroundRoot;
        public Transform BoardFrameRoot => boardFrameRoot;

#if UNITY_EDITOR
        public int UsedVisualSlotCount => visualSlotIndex;
        public int UsedObstacleCollisionSlotCount => obstacleCollisionSlotIndex;
        public int UsedMapCollisionSlotCount => mapCollisionSlotIndex;
#endif

        public void BeginBuild()
        {
            visualSlotIndex = 0;
            obstacleCollisionSlotIndex = 0;
            mapCollisionSlotIndex = 0;

            ResetVisualSlots();
            ResetColliderSlots(obstacleCollisionSlots, obstacleCollisionRoot);
            ResetColliderSlots(mapCollisionSlots, mapCollisionRoot);

            if (mapCollisionBody != null)
            {
                mapCollisionBody.bodyType = RigidbodyType2D.Static;
                mapCollisionBody.simulated = true;
            }

            if (mapCompositeCollider != null)
            {
                mapCompositeCollider.geometryType = CompositeCollider2D.GeometryType.Polygons;
                mapCompositeCollider.generationType = CompositeCollider2D.GenerationType.Synchronous;
                mapCompositeCollider.edgeRadius = 0f;
                mapCompositeCollider.enabled = true;
                PrototypeBootstrap.RegisterPhysicsIslandGridBlocker(mapCompositeCollider);
            }
        }

        public GameObject RentVisual(
            string objectName,
            Vector2 position,
            Vector2 size,
            Color color,
            Material presentationMaterial,
            int sortingOrder,
            Sprite presentationSprite,
            bool fitSpriteToSize,
            bool renderVisual,
            bool sliceSpriteToSize,
            bool flipX,
            bool flipY,
            Transform parent = null)
        {
            if (visualSlotIndex >= visualSlots.Length)
                throw new InvalidOperationException(
                    $"[LevelRuntimeRoot] Visual capacity {visualSlots.Length} is too small. " +
                    "Run Gravity Puzzle/Level Authoring/Rebuild Level Runtime Root.");

            SpriteRenderer renderer = visualSlots[visualSlotIndex++];
            if (renderer == null)
                throw new InvalidOperationException("[LevelRuntimeRoot] A configured visual slot is missing its SpriteRenderer.");

            Transform slotTransform = renderer.transform;
            slotTransform.SetParent(parent != null ? parent : obstacleVisualRoot, false);
            slotTransform.position = position;
            slotTransform.rotation = Quaternion.identity;
            slotTransform.localScale = new Vector3(size.x, size.y, 1f);
            slotTransform.gameObject.name = objectName;

            renderer.sprite = presentationSprite != null
                ? presentationSprite
                : fallbackSprite;
            if (renderer.sprite == null)
                throw new InvalidOperationException("[LevelRuntimeRoot] The authored fallback sprite is not assigned.");
            renderer.color = color;
            renderer.sharedMaterial = presentationMaterial;
            renderer.sortingOrder = sortingOrder;
            renderer.enabled = renderVisual;
            renderer.flipX = flipX;
            renderer.flipY = flipY;
            renderer.drawMode = SpriteDrawMode.Simple;
            renderer.size = Vector2.one;

            if (sliceSpriteToSize && presentationSprite != null)
            {
                slotTransform.localScale = Vector3.one;
                renderer.drawMode = SpriteDrawMode.Sliced;
                renderer.size = size;
            }
            else if (fitSpriteToSize && presentationSprite != null)
            {
                Vector2 spriteSize = presentationSprite.bounds.size;
                if (spriteSize.x > 0f && spriteSize.y > 0f)
                {
                    slotTransform.localScale = new Vector3(
                        size.x / spriteSize.x,
                        size.y / spriteSize.y,
                        1f);
                }
            }

            return renderer.gameObject;
        }

        public BoxCollider2D RentObstacleCollision(
            string objectName,
            Vector2 position,
            Vector2 size,
            bool roundCorners)
        {
            if (obstacleCollisionSlotIndex >= obstacleCollisionSlots.Length)
                throw new InvalidOperationException(
                    $"[LevelRuntimeRoot] Obstacle collision capacity {obstacleCollisionSlots.Length} is too small. " +
                    "Run Gravity Puzzle/Level Authoring/Rebuild Level Runtime Root.");

            BoxCollider2D collider = obstacleCollisionSlots[obstacleCollisionSlotIndex++];
            ConfigureBoxCollider(collider, objectName, obstacleCollisionRoot, position, size);
            collider.usedByComposite = false;
            collider.edgeRadius = 0f;
            if (roundCorners)
                PrototypeBootstrap.RoundColliderCorners(collider, size);

            PrototypeBootstrap.RegisterAuthoredObstacleCollider(collider);
            PrototypeBootstrap.RegisterPhysicsIslandGridBlocker(collider);
            return collider;
        }

        public void RentMapCollision(string objectName, Vector2 position, float fineCellSize)
        {
            if (mapCollisionSlotIndex >= mapCollisionSlots.Length)
                throw new InvalidOperationException(
                    $"[LevelRuntimeRoot] Map collision capacity {mapCollisionSlots.Length} is too small. " +
                    "Run Gravity Puzzle/Level Authoring/Rebuild Level Runtime Root.");

            BoxCollider2D collider = mapCollisionSlots[mapCollisionSlotIndex++];
            ConfigureBoxCollider(
                collider,
                objectName,
                mapCollisionRoot,
                position,
                Vector2.one * fineCellSize);
            collider.edgeRadius = 0f;
            collider.usedByComposite = true;
        }

        public void CompleteMapCollision()
        {
            if (mapCompositeCollider == null)
                throw new InvalidOperationException("[LevelRuntimeRoot] Map CompositeCollider2D is not assigned.");

            mapCompositeCollider.GenerateGeometry();
        }

        private void ResetVisualSlots()
        {
            for (int index = 0; index < visualSlots.Length; index++)
            {
                SpriteRenderer renderer = visualSlots[index];
                if (renderer == null)
                    continue;

                renderer.enabled = false;
                renderer.sprite = null;
                renderer.sharedMaterial = null;
                renderer.color = Color.white;
                renderer.flipX = false;
                renderer.flipY = false;
                renderer.drawMode = SpriteDrawMode.Simple;
                renderer.size = Vector2.one;
                renderer.transform.SetParent(visualSlotRoot, false);
                renderer.transform.localPosition = Vector3.zero;
                renderer.transform.localRotation = Quaternion.identity;
                renderer.transform.localScale = Vector3.one;
            }
        }

        private static void ResetColliderSlots(BoxCollider2D[] slots, Transform parent)
        {
            for (int index = 0; index < slots.Length; index++)
            {
                BoxCollider2D collider = slots[index];
                if (collider == null)
                    continue;

                collider.enabled = false;
                collider.usedByComposite = false;
                collider.edgeRadius = 0f;
                collider.transform.SetParent(parent, false);
                collider.transform.localPosition = Vector3.zero;
                collider.transform.localRotation = Quaternion.identity;
                collider.transform.localScale = Vector3.one;
            }
        }

        private static void ConfigureBoxCollider(
            BoxCollider2D collider,
            string objectName,
            Transform parent,
            Vector2 position,
            Vector2 size)
        {
            if (collider == null)
                throw new InvalidOperationException("[LevelRuntimeRoot] A configured collision slot is missing its BoxCollider2D.");

            Transform slotTransform = collider.transform;
            slotTransform.SetParent(parent, false);
            slotTransform.position = position;
            slotTransform.rotation = Quaternion.identity;
            slotTransform.localScale = new Vector3(size.x, size.y, 1f);
            slotTransform.gameObject.name = objectName;
            collider.offset = Vector2.zero;
            collider.size = Vector2.one;
            collider.enabled = true;
        }

#if UNITY_EDITOR
        public void ConfigureAuthoredSlots(
            Transform authoredVisualSlotRoot,
            Transform authoredBoardBackgroundRoot,
            Transform authoredBoardFrameRoot,
            Transform authoredObstacleVisualRoot,
            Transform authoredObstacleCollisionRoot,
            Transform authoredMapCollisionRoot,
            SpriteRenderer[] authoredVisualSlots,
            BoxCollider2D[] authoredObstacleCollisionSlots,
            BoxCollider2D[] authoredMapCollisionSlots,
            Rigidbody2D authoredMapCollisionBody,
            CompositeCollider2D authoredMapCompositeCollider)
        {
            visualSlotRoot = authoredVisualSlotRoot;
            boardBackgroundRoot = authoredBoardBackgroundRoot;
            boardFrameRoot = authoredBoardFrameRoot;
            obstacleVisualRoot = authoredObstacleVisualRoot;
            obstacleCollisionRoot = authoredObstacleCollisionRoot;
            mapCollisionRoot = authoredMapCollisionRoot;
            visualSlots = authoredVisualSlots ?? Array.Empty<SpriteRenderer>();
            obstacleCollisionSlots = authoredObstacleCollisionSlots ?? Array.Empty<BoxCollider2D>();
            mapCollisionSlots = authoredMapCollisionSlots ?? Array.Empty<BoxCollider2D>();
            mapCollisionBody = authoredMapCollisionBody;
            mapCompositeCollider = authoredMapCompositeCollider;
        }

        public void ConfigureFallbackSprite(Sprite authoredFallbackSprite)
        {
            fallbackSprite = authoredFallbackSprite;
        }
#endif
    }
}
