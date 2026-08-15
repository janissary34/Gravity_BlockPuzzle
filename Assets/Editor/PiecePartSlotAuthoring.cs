using UnityEditor;
using UnityEngine;

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
                if (prefabRoot.transform.Find("Piece Part Slots") != null)
                {
                    Debug.Log("[PiecePartSlots] BlockPiece already contains authored part slots.");
                    return;
                }

                Transform visualRoot = new GameObject("Piece Part Slots").transform;
                visualRoot.SetParent(prefabRoot.transform, false);
                Transform colliderRoot = new GameObject("Piece Part Collision Slots").transform;
                colliderRoot.SetParent(prefabRoot.transform, false);

                for (int index = 0; index < SlotCount; index++)
                {
                    GameObject visual = new GameObject($"Part Slot {index + 1}");
                    visual.transform.SetParent(visualRoot, false);
                    visual.AddComponent<SpriteRenderer>().enabled = false;

                    GameObject collision = new GameObject($"Part Collision Slot {index + 1}");
                    collision.transform.SetParent(colliderRoot, false);
                    BoxCollider2D collider = collision.AddComponent<BoxCollider2D>();
                    collider.usedByComposite = true;
                    collider.enabled = false;
                }

                PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
                Debug.Log("[PiecePartSlots] Added 128 reusable visual and collision slots to BlockPiece.prefab.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }
    }
}
