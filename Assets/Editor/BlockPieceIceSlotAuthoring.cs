#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using TMPro;

namespace GravityPuzzle.Editor
{
    public static class BlockPieceIceSlotAuthoring
    {
        private const int SlotCount = 288;
        private const string CounterName = "Ice Counter Presentation";
        private const string PrefabPath = "Assets/Prefabs/BlockPiece.prefab";

        [MenuItem("Gravity Puzzle/Refactor/Add BlockPiece Ice Slots")]
        private static void AddIceSlots()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            Transform existing = root.transform.Find("Ice Presentation Slots");
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            GameObject container = new GameObject("Ice Presentation Slots");
            container.transform.SetParent(root.transform, false);
            for (int index = 0; index < SlotCount; index++)
            {
                GameObject slot = new GameObject("Ice Slot " + index);
                slot.transform.SetParent(container.transform, false);
                SpriteRenderer renderer = slot.AddComponent<SpriteRenderer>();
                renderer.enabled = false;
            }

            EnsureIceCounter(root);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            PrefabUtility.UnloadPrefabContents(root);
        }

        [MenuItem("Gravity Puzzle/Refactor/Create BlockPiece Ice Counter")]
        private static void CreateIceCounter()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            EnsureIceCounter(root);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            PrefabUtility.UnloadPrefabContents(root);
        }

        private static void EnsureIceCounter(GameObject root)
        {
            Transform counterTransform = root.transform.Find(CounterName);
            GameObject counter = counterTransform != null
                ? counterTransform.gameObject
                : new GameObject(CounterName);
            counter.transform.SetParent(root.transform, false);

            TextMeshPro counterText = counter.GetComponent<TextMeshPro>();
            if (counterText == null)
                counterText = counter.AddComponent<TextMeshPro>();

            counterText.font = TMP_Settings.defaultFontAsset;
            counterText.alignment = TextAlignmentOptions.Center;
            counterText.fontStyle = FontStyles.Bold;
            counterText.enabled = false;
        }
    }
}
#endif
