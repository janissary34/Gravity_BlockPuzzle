#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GravityPuzzle.Editor
{
    /// <summary>
    /// Authors the nested Canvas required by the travelling timer visual. This
    /// deliberately runs only in the editor so TimerBooster never creates UI
    /// components while the game is running.
    /// </summary>
    public static class TimerBoosterPresentationAuthoring
    {
        [MenuItem("Gravity Puzzle/Refactor/Configure Timer Booster Presentation Canvas")]
        private static void ConfigurePresentationCanvas()
        {
            TimerBooster[] boosters = Object.FindObjectsOfType<TimerBooster>(true);
            int configuredCount = 0;

            for (int i = 0; i < boosters.Length; i++)
            {
                TimerBooster booster = boosters[i];
                SerializedObject serializedBooster = new SerializedObject(booster);
                SerializedProperty timerObjectProperty = serializedBooster.FindProperty("timer_obj");
                if (timerObjectProperty.objectReferenceValue is not GameObject timerObject)
                    continue;

                Canvas parentCanvas = timerObject.transform.parent != null
                    ? timerObject.transform.parent.GetComponentInParent<Canvas>()
                    : null;
                Canvas canvas = timerObject.GetComponent<Canvas>();
                if (canvas == null)
                    canvas = Undo.AddComponent<Canvas>(timerObject);

                // A nested clock canvas must stay in the same canvas space as its
                // authored parent. Switching it to a new default overlay canvas
                // can put the animated clock behind world/board presentation.
                if (parentCanvas != null)
                {
                    canvas.renderMode = parentCanvas.renderMode;
                    canvas.worldCamera = parentCanvas.worldCamera;
                    canvas.planeDistance = parentCanvas.planeDistance;
                }

                canvas.overrideSorting = true;
                SerializedProperty orderProperty = serializedBooster.FindProperty("timerPresentationSortingOrder");
                int safeSortingOrder = Mathf.Clamp(orderProperty.intValue, 0, short.MaxValue);
                orderProperty.intValue = safeSortingOrder;
                canvas.sortingOrder = safeSortingOrder;
                serializedBooster.FindProperty("timerPresentationCanvas").objectReferenceValue = canvas;
                serializedBooster.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(booster);
                EditorUtility.SetDirty(canvas);
                EditorSceneManager.MarkSceneDirty(booster.gameObject.scene);
                configuredCount++;
            }

            Debug.Log($"[TimerBooster] Configured presentation Canvas for {configuredCount} timer booster(s).");
        }
    }
}
#endif
