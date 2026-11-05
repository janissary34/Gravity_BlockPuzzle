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
    /// A path is a list of exact object names, one per hierarchy level, because Scene1 has names
    /// containing '/' (which Transform.Find cannot reach) and duplicate siblings (of which it
    /// returns only the first). Every object matching a path is bound.
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
            public string[][] Paths;

            public GroupPaths(CreativeUIGroup group, params string[][] paths)
            {
                Group = group;
                Paths = paths;
            }
        }

        private static string[] Path(params string[] objectNames) => objectNames;

        // Paths are relative to the InGameUI root in Scene1. Each string is one object's full name.
        private static readonly GroupPaths[] Map =
        {
            new GroupPaths(CreativeUIGroup.LevelHeader, Path("UI_Level", "Level_Display_txt"), Path("UI_Level", "level_txt_bg")),
            new GroupPaths(CreativeUIGroup.DifficultyBadge, Path("UI_Level", "level_txt_bg (1)"), Path("UI_Level", "Level_Display_txt (1)")),
            new GroupPaths(CreativeUIGroup.Timer, Path("UI_Progress_Slider", "Time")),
            new GroupPaths(CreativeUIGroup.ProgressBar, Path("UI_Progress_Slider", "Background"), Path("UI_Progress_Slider", "Fill")),
            new GroupPaths(CreativeUIGroup.Coins, Path("UI_Coin")),
            new GroupPaths(CreativeUIGroup.BoosterBar, Path("Boosters")),
            new GroupPaths(CreativeUIGroup.SettingsButton, Path("UI_Settings")),
            new GroupPaths(CreativeUIGroup.RetryButton, Path("UI_Restart")),
            // Scene1 renames the root of the nested New_Booster_Panel prefab instance to this name.
            // Settings, fail and revive panels exist twice, directly and under New_Panels; both are bound.
            new GroupPaths(CreativeUIGroup.TutorialAndRewardPopups,
                Path("Settings_Panel"), Path("New_Panels", "Settings_Panel"),
                Path("New_Booster/feature_Panel_Dark_bg"), Path("NewFeatures")),
            // Two siblings have this name; PrototypeBoard.winPanel uses the scene-added one.
            new GroupPaths(CreativeUIGroup.WinPanel, Path("FullscreenSuccess")),
            new GroupPaths(CreativeUIGroup.FailAndRevivePanels,
                Path("Fail_Panel"), Path("New_Panels", "Fail_Panel"),
                Path("RevivePanel"), Path("New_Panels", "RevivePanel"),
                Path("Retry_Panel"), Path("New_Panels", "Retry_Panel"),
                Path("OutOfLives_Panel")),
            new GroupPaths(CreativeUIGroup.DebugButtons, Path("Debug_Next_Level_Button")),
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

            // The tool saves the scene, which must not persist unrelated edits.
            if (scene.isDirty)
            {
                Debug.LogError($"[CreativeBindings] {ScenePath} has unsaved changes. Save or revert them, then run this again; nothing was changed.");
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
                var seen = new HashSet<Graphic>();
                for (int p = 0; p < Map[g].Paths.Length; p++)
                {
                    string[] path = Map[g].Paths[p];
                    string pathLabel = string.Join(" > ", path);
                    List<Transform> targets = FindAll(uiRoot.transform, path);
                    if (targets.Count == 0)
                    {
                        missing = true;
                        report.AppendLine($"  MISSING {Map[g].Group}: {UIRootName} > {pathLabel}");
                        continue;
                    }

                    if (targets.Count > 1)
                        report.AppendLine($"  {Map[g].Group}: '{pathLabel}' matches {targets.Count} objects; all are bound");

                    for (int t = 0; t < targets.Count; t++)
                    {
                        Graphic[] found = targets[t].GetComponentsInChildren<Graphic>(true);
                        for (int f = 0; f < found.Length; f++)
                        {
                            if (seen.Add(found[f]))
                                graphics.Add(found[f]);
                        }
                    }
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

        /// <summary>
        /// Every object below <paramref name="root"/> at the given path. Each entry is compared
        /// with one object's whole name, in sibling order, so '/' has no special meaning.
        /// </summary>
        internal static List<Transform> FindAll(Transform root, string[] objectNames)
        {
            var matches = new List<Transform> { root };
            for (int step = 0; step < objectNames.Length; step++)
            {
                var next = new List<Transform>();
                for (int m = 0; m < matches.Count; m++)
                {
                    for (int c = 0; c < matches[m].childCount; c++)
                    {
                        Transform child = matches[m].GetChild(c);
                        if (child.name == objectNames[step])
                            next.Add(child);
                    }
                }

                matches = next;
            }

            return matches;
        }

        [MenuItem("Gravity Puzzle/Creative/Author Scene1 UI Bindings", true)]
        private static bool AuthorValidate() => !EditorApplication.isPlayingOrWillChangePlaymode;
    }
}
