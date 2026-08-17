using UnityEditor;
using UnityEngine;
using GravityPuzzle.Gameplay.Pieces;

namespace GravityPuzzle.EditorTools
{
    public static class PiecePartSlotAuthoring
    {
        private const string PrefabPath = "Assets/Prefabs/BlockPiece.prefab";
        private const int SlotCount = 128;

        [MenuItem("Gravity Puzzle/Refactor/Add BlockPiece Part Slots")]
        private static void AddSlots()
        {
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Transform visualRoot = prefabRoot.transform.Find("Piece Part Slots");
                if (visualRoot == null)
                {
                    visualRoot = new GameObject("Piece Part Slots").transform;
                    visualRoot.SetParent(prefabRoot.transform, false);
                }

                Transform colliderRoot = prefabRoot.transform.Find("Piece Part Collision Slots");
                if (colliderRoot == null)
                {
                    colliderRoot = new GameObject("Piece Part Collision Slots").transform;
                    colliderRoot.SetParent(prefabRoot.transform, false);
                }

                for (int index = 0; index < SlotCount; index++)
                {
                    Transform visualTransform = visualRoot.Find($"Part Slot {index + 1}");
                    GameObject visual = visualTransform != null ? visualTransform.gameObject : new GameObject($"Part Slot {index + 1}");
                    if (visualTransform == null) visual.transform.SetParent(visualRoot, false);
                    SpriteRenderer renderer = visual.GetComponent<SpriteRenderer>();
                    if (renderer == null) renderer = visual.AddComponent<SpriteRenderer>();
                    renderer.enabled = false;

                    Transform collisionTransform = colliderRoot.Find($"Part Collision Slot {index + 1}");
                    GameObject collision = collisionTransform != null ? collisionTransform.gameObject : new GameObject($"Part Collision Slot {index + 1}");
                    if (collisionTransform == null) collision.transform.SetParent(colliderRoot, false);
                    BoxCollider2D collider = collision.GetComponent<BoxCollider2D>();
                    if (collider == null) collider = collision.AddComponent<BoxCollider2D>();
                    collider.usedByComposite = true;
                    collider.enabled = false;

                    PiecePartSlot slot = visual.GetComponent<PiecePartSlot>();
                    if (slot == null) slot = visual.AddComponent<PiecePartSlot>();
                    slot.ConfigureForAuthoring(renderer, collider);
                }

                PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
                Debug.Log("[PiecePartSlots] Configured 128 reusable visual and collision slots on BlockPiece.prefab.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }
    }
}
