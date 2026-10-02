#if UNITY_EDITOR
using System.Collections.Generic;
using GravityPuzzle.Config;
using UnityEditor;
using UnityEngine;

namespace GravityPuzzle.Editor
{
    /// <summary>
    /// Reads the already-sliced artist atlases and writes their normalized
    /// silhouettes into PieceVisualConfig. This is authoring only; the player
    /// never discovers sprites or touches TextureImporter settings at runtime.
    /// </summary>
    public static class PieceShapeVisualConfigAuthoring
    {
        private const string ConfigPath = "Assets/PieceVisualConfig.asset";
        private const string BrickAtlasPath = "Assets/Art/BrickBlocks.png";
        private const string IceAtlasPath = "Assets/Art/Blocks_Sprite_Ice.png";
        private const float PixelsPerModule = 155f;

        [MenuItem("Gravity Puzzle/Art/Build Piece Shape Visual Config")]
        private static void Build()
        {
            PieceVisualConfig config = AssetDatabase.LoadAssetAtPath<PieceVisualConfig>(ConfigPath);
            if (config == null)
            {
                Debug.LogError("[PieceShapeVisualConfig] PieceVisualConfig asset was not found.");
                return;
            }

            Dictionary<string, Sprite> normalSprites = ReadSpritesByShape(BrickAtlasPath);
            Dictionary<string, Sprite> iceSprites = ReadSpritesByShape(IceAtlasPath);
            SerializedObject serializedConfig = new SerializedObject(config);
            SerializedProperty normalFallback = serializedConfig.FindProperty("normalFallbackSprite");
            normalFallback.objectReferenceValue = FindSprite(BrickAtlasPath, "BB_1X1");
            // The ice atlas currently has no independently sliced 1x1 sprite.
            // Leave this empty rather than stretching an unrelated silhouette.
            // A missing ice sprite is valid: frozen pieces retain the normal
            // silhouette and receive the configured ice overlay instead.
            serializedConfig.FindProperty("iceFallbackSprite").objectReferenceValue = null;

            SerializedProperty definitions = serializedConfig.FindProperty("shapeDefinitions");
            definitions.ClearArray();
            int iceMatchedShapeCount = 0;
            foreach (KeyValuePair<string, Sprite> pair in normalSprites)
            {
                iceSprites.TryGetValue(pair.Key, out Sprite iceSprite);
                if (iceSprite != null)
                    iceMatchedShapeCount++;

                int index = definitions.arraySize;
                definitions.InsertArrayElementAtIndex(index);
                SerializedProperty definition = definitions.GetArrayElementAtIndex(index);
                definition.FindPropertyRelative("shapeKey").stringValue = pair.Key;
                definition.FindPropertyRelative("normalSprite").objectReferenceValue = pair.Value;
                definition.FindPropertyRelative("iceSprite").objectReferenceValue = iceSprite;
            }

            serializedConfig.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            Debug.Log(
                $"[PieceShapeVisualConfig] Configured {definitions.arraySize} normal artist silhouettes " +
                $"({iceMatchedShapeCount} with dedicated ice art). Missing ice art uses the configured ice overlay.");
        }

        private static Dictionary<string, Sprite> ReadSpritesByShape(string path)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            bool wasReadable = importer != null && importer.isReadable;
            if (importer != null && !wasReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
            }

            Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
            for (int index = 0; index < assets.Length; index++)
            {
                Sprite sprite = assets[index] as Sprite;
                if (sprite == null)
                    continue;

                string key = BuildShapeKey(sprite);
                if (!string.IsNullOrEmpty(key) && !sprites.ContainsKey(key))
                    sprites.Add(key, sprite);
            }

            if (importer != null && !wasReadable)
            {
                importer.isReadable = false;
                importer.SaveAndReimport();
            }

            return sprites;
        }

        private static string BuildShapeKey(Sprite sprite)
        {
            int width = Mathf.RoundToInt(sprite.rect.width / PixelsPerModule);
            int height = Mathf.RoundToInt(sprite.rect.height / PixelsPerModule);
            if (width < 1 || height < 1 ||
                !Mathf.Approximately(sprite.rect.width, width * PixelsPerModule) ||
                !Mathf.Approximately(sprite.rect.height, height * PixelsPerModule))
                return null;

            List<string> cells = new List<string>();
            Texture2D texture = sprite.texture;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int sampleX = Mathf.RoundToInt(sprite.rect.x + (x + .5f) * PixelsPerModule);
                    int sampleY = Mathf.RoundToInt(sprite.rect.y + (y + .5f) * PixelsPerModule);
                    if (texture.GetPixel(sampleX, sampleY).a > .1f)
                        cells.Add(x + "," + y);
                }
            }

            return cells.Count > 0 ? string.Join(";", cells) : null;
        }

        private static Sprite FindSprite(string path, string spriteName)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
            for (int index = 0; index < assets.Length; index++)
            {
                Sprite sprite = assets[index] as Sprite;
                if (sprite != null && sprite.name == spriteName)
                    return sprite;
            }

            return null;
        }
    }
}
#endif
