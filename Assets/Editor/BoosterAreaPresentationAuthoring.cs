using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GravityPuzzle.Editor
{
    /// <summary>
    /// Authors the fixed booster-area background in the InGameUI prefab. The panel
    /// covers board content below the divider while the existing booster buttons
    /// remain separate, interactive children above it.
    /// </summary>
    internal static class BoosterAreaPresentationAuthoring
    {
        private const string InGameUiPrefabPath = "Assets/Prefabs/UI/InGameUI.prefab";
        private const string SpriteSheetPath = "Assets/Art/CommonAssets_UI_LionPack (1).png";
        private const string PanelSpriteName = "Panel_Blue_Tall";
        private const string PanelObjectName = "Booster Area Background";
        private const string BoostersObjectName = "Boosters";

        // The canvas reference resolution is 1125x2436. This places the divider
        // above the authored booster buttons and hides the sprite's lower gold edge
        // just below the visible screen.
        private const float DividerHeightFromBottom = 430f;
        private const float HiddenLowerEdge = 128f;

        [MenuItem("Gravity Puzzle/UI/Author Booster Area Background")]
        private static void AuthorBoosterAreaBackground()
        {
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(InGameUiPrefabPath);
            try
            {
                Transform boosters = prefabRoot.transform.Find(BoostersObjectName);
                if (boosters == null)
                {
                    Debug.LogError($"[Booster Area] '{BoostersObjectName}' was not found in {InGameUiPrefabPath}.");
                    return;
                }

                Sprite panelSprite = LoadSprite(PanelSpriteName);
                if (panelSprite == null)
                {
                    Debug.LogError($"[Booster Area] Sprite '{PanelSpriteName}' was not found in {SpriteSheetPath}.");
                    return;
                }

                Transform existing = prefabRoot.transform.Find(PanelObjectName);
                GameObject panelObject;
                RectTransform panelRect;
                Image panelImage;

                if (existing == null)
                {
                    panelObject = new GameObject(PanelObjectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                    panelObject.layer = LayerMask.NameToLayer("UI");
                    panelObject.transform.SetParent(prefabRoot.transform, false);
                    panelRect = panelObject.GetComponent<RectTransform>();
                    panelImage = panelObject.GetComponent<Image>();
                }
                else
                {
                    panelObject = existing.gameObject;
                    panelRect = panelObject.GetComponent<RectTransform>();
                    panelImage = panelObject.GetComponent<Image>();
                    if (panelRect == null || panelImage == null)
                    {
                        Debug.LogError($"[Booster Area] Existing '{PanelObjectName}' must contain a RectTransform and Image.");
                        return;
                    }
                }

                panelObject.SetActive(true);
                panelObject.layer = LayerMask.NameToLayer("UI");

                panelRect.anchorMin = new Vector2(0f, 0f);
                panelRect.anchorMax = new Vector2(1f, 0f);
                panelRect.pivot = new Vector2(.5f, 0f);
                panelRect.anchoredPosition = new Vector2(0f, -HiddenLowerEdge);
                panelRect.sizeDelta = new Vector2(0f, DividerHeightFromBottom + HiddenLowerEdge);
                panelRect.localRotation = Quaternion.identity;
                panelRect.localScale = Vector3.one;

                panelImage.sprite = panelSprite;
                panelImage.type = Image.Type.Simple;
                panelImage.preserveAspect = false;
                panelImage.raycastTarget = false;
                panelImage.color = Color.white;

                int boostersIndex = boosters.GetSiblingIndex();
                if (panelRect.GetSiblingIndex() < boostersIndex)
                    boostersIndex--;

                panelRect.SetSiblingIndex(boostersIndex);
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, InGameUiPrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log("[Booster Area] Authored the fixed background and gold divider in InGameUI.prefab.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static Sprite LoadSprite(string spriteName)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(SpriteSheetPath);
            for (int index = 0; index < assets.Length; index++)
            {
                if (assets[index] is Sprite sprite && sprite.name == spriteName)
                    return sprite;
            }

            return null;
        }
    }
}
