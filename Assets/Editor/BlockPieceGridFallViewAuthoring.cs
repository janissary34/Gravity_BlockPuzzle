#if UNITY_EDITOR
using GravityPuzzle.Config;
using GravityPuzzle.Presentation.Views;
using UnityEditor;
using UnityEngine;

namespace GravityPuzzle.Editor
{
    public static class BlockPieceGridFallViewAuthoring
    {
        private const string PrefabPath = "Assets/Prefabs/BlockPiece.prefab";
        private const string TweenConfigPath = "Assets/TweenConfig.asset";

        [MenuItem("Gravity Puzzle/Refactor/Add BlockPiece Grid Fall View")]
        private static void AddGridFallView()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            PieceGridFallView view = root.GetComponent<PieceGridFallView>();
            if (view == null)
                view = root.AddComponent<PieceGridFallView>();

            SerializedObject serializedView = new SerializedObject(view);
            serializedView.FindProperty("tweenConfig").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<TweenConfig>(TweenConfigPath);
            serializedView.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
#endif
