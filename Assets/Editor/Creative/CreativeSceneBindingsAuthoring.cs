using System.Collections.Generic;
using System.Text;
using GravityPuzzle.Bootstrap;
using GravityPuzzle.Creative.Config;
using GravityPuzzle.Creative.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GravityPuzzle.Creative.EditorTools
{
    /// <summary>
    /// Authors the Creative Mode UI bindings for Scene1 (Edit Mode, run on demand).
    ///
    /// Maps every <see cref="CreativeUIGroup"/> to explicit hierarchy paths under the scene's
    /// InGameUI root, collects the UI Graphic components below those paths, and writes them to a
    /// <see cref="CreativeSceneBindings"/> component that is assigned to the composition root.
    /// The result is ordinary serialized scene data: it can be reviewed and edited in the
    /// Inspector, and Creative Mode never searches the scene at runtime.
    /// Re-run after UI hierarchy changes. A path that no longer exists is reported as an error.
    /// </summary>
    public static class CreativeSceneBindingsAuthoring
    {
        private const string ScenePath = "Assets/Scenes/Scene1.unity";
        private const string UIRootName = "InGameUI";
        private const string BindingsObjectName = "CreativeSceneBindings";

        private struct GroupPaths
        {
            public CreativeUIGroup Group;
            public string[] Paths;

            public GroupPaths(CreativeUIGroup group, params string[] paths)
            {
                Group = group;
                Paths = paths;
            }
        }

        // Paths are relative to the InGameUI root in Scene1.
        private static readonly GroupPaths[] Map =
        {
            new GroupPaths(CreativeUIGroup.LevelHeader, "UI_Level/Level_Display_txt", "UI_Level/level_txt_bg"),
            new GroupPaths(CreativeUIGroup.DifficultyBadge, "UI_Level/level_txt_bg (1)", "UI_Level/Level_Display_txt (1)"),
            new GroupPaths(CreativeUIGroup.Timer, "UI_Progress_Slider/Time"),
            new GroupPaths(CreativeUIGroup.ProgressBar, "UI_Progress_Slider/Background", "UI_Progress_Slider/Fill"),
            new GroupPaths(CreativeUIGroup.Coins, "UI_Coin"),
            new GroupPaths(CreativeUIGroup.BoosterBar, "Boosters"),
            new GroupPaths(CreativeUIGroup.SettingsButton, "UI_Settings"),
            new GroupPaths(CreativeUIGroup.RetryButton, "UI_Restart"),
            new GroupPaths(CreativeUIGroup.TutorialAndRewardPopups, "Settings_Panel", "New_Booster_Panel", "NewFeatures"),
            new GroupPaths(CreativeUIGroup.WinPanel, "FullscreenSuccess"),
            new GroupPaths(CreativeUIGroup.FailAndRevivePanels, "Fail_Panel", "RevivePanel", "Retry_Panel", "OutOfLives_Panel"),
            new GroupPaths(CreativeUIGroup.DebugButtons, "Debug_Next_Level_Button"),
        };

        [MenuItem("Gravity Puzzle/Creative/Author Scene1 UI Bindings")]
        private static void Author()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                Debug.LogError($"[CreativeBindings] Open {ScenePath} first (active scene: '{scene.path}').");
                return;
            }

            GameObject uiRoot = null;
            RuntimePieceFactoryBootstrap bootstrap = null;
            GameObject bindingsObject = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == UIRootName)
                    uiRoot = root;
                if (root.name == BindingsObjectName)
                    bindingsObject = root;
                if (bootstrap == null)
                    bootstrap = root.GetComponentInChildren<RuntimePieceFactoryBootstrap>(true);
            }

            if (uiRoot == null || bootstrap == null)
            {
                Debug.LogError($"[CreativeBindings] Scene1 needs a root '{UIRootName}' and a RuntimePieceFactoryBootstrap; nothing was changed.");
                return;
            }

            var report = new StringBuilder();
            var resolved = new List<KeyValuePair<CreativeUIGroup, Graphic[]>>(Map.Length);
            bool missing = false;
            for (int g = 0; g < Map.Length; g++)
            {
                var graphics = new List<Graphic>();
                for (int p = 0; p < Map[g].Paths.Length; p++)
                {
                    Transform target = uiRoot.transform.Find(Map[g].Paths[p]);
                    if (target == null)
                    {
                        missing = true;
                        report.AppendLine($"  MISSING {Map[g].Group}: {UIRootName}/{Map[g].Paths[p]}");
                        continue;
                    }

                    graphics.AddRange(target.GetComponentsInChildren<Graphic>(true));
                }

                resolved.Add(new KeyValuePair<CreativeUIGroup, Graphic[]>(Map[g].Group, graphics.ToArray()));
                report.AppendLine($"  {Map[g].Group}: {graphics.Count} graphics");
            }

            if (missing)
            {
                Debug.LogError("[CreativeBindings] Some UI paths no longer exist; nothing was changed. Update the map in CreativeSceneBindingsAuthoring.\n" + report);
                return;
            }

            if (bindingsObject == null)
            {
                bindingsObject = new GameObject(BindingsObjectName);
                Undo.RegisterCreatedObjectUndo(bindingsObject, "Create Creative Scene Bindings");
                SceneManager.MoveGameObjectToScene(bindingsObject, scene);
            }

            CreativeSceneBindings bindings = bindingsObject.GetComponent<CreativeSceneBindings>();
            if (bindings == null)
                bindings = Undo.AddComponent<CreativeSceneBindings>(bindingsObject);

            var serializedBindings = new SerializedObject(bindings);
            SerializedProperty groups = serializedBindings.FindProperty("uiGroups");
            groups.arraySize = resolved.Count;
            for (int i = 0; i < resolved.Count; i++)
            {
                SerializedProperty element = groups.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("group").intValue = (int)resolved[i].Key;
                SerializedProperty graphicsProperty = element.FindPropertyRelative("graphics");
                graphicsProperty.arraySize = resolved[i].Value.Length;
                for (int k = 0; k < resolved[i].Value.Length; k++)
                    graphicsProperty.GetArrayElementAtIndex(k).objectReferenceValue = resolved[i].Value[k];
                element.FindPropertyRelative("canvases").arraySize = 0;
                element.FindPropertyRelative("objects").arraySize = 0;
            }
            serializedBindings.ApplyModifiedProperties();

            var serializedBootstrap = new SerializedObject(bootstrap);
            serializedBootstrap.FindProperty("creativeSceneBindings").objectReferenceValue = bindings;
            serializedBootstrap.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[CreativeBindings] Scene1 UI bindings authored and saved:\n" + report, bindings);
        }

        [MenuItem("Gravity Puzzle/Creative/Author Scene1 UI Bindings", true)]
        private static bool AuthorValidate() => !EditorApplication.isPlayingOrWillChangePlaymode;
    }
}
