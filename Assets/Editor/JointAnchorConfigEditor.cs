using GravityPuzzle.Config;
using UnityEditor;
using UnityEngine;

namespace GravityPuzzle.Editor
{
    [CustomEditor(typeof(JointAnchorConfig))]
    public sealed class JointAnchorConfigEditor : UnityEditor.Editor
    {
        private int selectedLayout;
        private bool editSpring;
        private float zoom = 1f;
        private Vector2 previewScroll;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.HelpBox("Add a layout and assign its block sprite. Click the sprite preview to add stud centres; right-click a marker to remove it. For the spring, click its two black ends in order and enable Spring Ends Authored.", MessageType.Info);
            DrawDefaultInspector();
            Vector2 endA = serializedObject.FindProperty("springEndA").vector2Value;
            Vector2 endB = serializedObject.FindProperty("springEndB").vector2Value;
            if (!JointAnchorConfig.IsValidPoint(endA) || !JointAnchorConfig.IsValidPoint(endB))
                EditorGUILayout.HelpBox("Spring end coordinates must be between 0 and 1. Re-mark the ends in the preview; until then the legacy placement is used.", MessageType.Error);
            SerializedProperty layouts = serializedObject.FindProperty("layouts");
            editSpring = GUILayout.Toggle(editSpring, "Edit spring ends", "Button");
            SerializedProperty spriteProperty;
            SerializedProperty points = null;
            if (editSpring)
                spriteProperty = serializedObject.FindProperty("springSprite");
            else
            {
                if (layouts.arraySize == 0)
                {
                    serializedObject.ApplyModifiedProperties();
                    return;
                }
                selectedLayout = EditorGUILayout.IntSlider("Layout to edit", selectedLayout, 0, layouts.arraySize - 1);
                SerializedProperty layout = layouts.GetArrayElementAtIndex(selectedLayout);
                spriteProperty = layout.FindPropertyRelative("sprite");
                points = layout.FindPropertyRelative("studs");
                for (int i = 0; i < points.arraySize; i++)
                    if (!JointAnchorConfig.IsValidPoint(points.GetArrayElementAtIndex(i).vector2Value))
                    {
                        EditorGUILayout.HelpBox("A stud coordinate is outside 0–1. Correct it before this sprite can use anchor placement.", MessageType.Error);
                        break;
                    }
            }

            Sprite sprite = spriteProperty.objectReferenceValue as Sprite;
            if (sprite != null)
            {
                zoom = EditorGUILayout.Slider("Preview zoom", zoom, 1f, 4f);
                float availableWidth = Mathf.Max(100f, EditorGUIUtility.currentViewWidth - 55f);
                float width = Mathf.Min(availableWidth, 420f) * zoom;
                float previewHeight = width * sprite.rect.height / sprite.rect.width;
                previewScroll = EditorGUILayout.BeginScrollView(previewScroll, GUILayout.Height(Mathf.Min(500f, previewHeight + 22f)));
                Rect preview = GUILayoutUtility.GetRect(width, previewHeight, GUILayout.ExpandWidth(false));
                Rect rect = sprite.rect;
                Texture2D texture = sprite.texture;
                GUI.DrawTextureWithTexCoords(preview, texture, new Rect(rect.x / texture.width, rect.y / texture.height, rect.width / texture.width, rect.height / texture.height), true);
                if (editSpring)
                {
                    DrawMarker(preview, serializedObject.FindProperty("springEndA").vector2Value, Color.red);
                    DrawMarker(preview, serializedObject.FindProperty("springEndB").vector2Value, Color.cyan);
                }
                else
                    for (int i = 0; i < points.arraySize; i++)
                        DrawMarker(preview, points.GetArrayElementAtIndex(i).vector2Value, Color.yellow);

                Event e = Event.current;
                if (e.type == EventType.MouseDown && preview.Contains(e.mousePosition))
                {
                    Vector2 p = new Vector2((e.mousePosition.x - preview.x) / preview.width, 1f - (e.mousePosition.y - preview.y) / preview.height);
                    if (editSpring && e.button == 0)
                    {
                        string property = e.shift ? "springEndB" : "springEndA";
                        serializedObject.FindProperty(property).vector2Value = p;
                    }
                    else if (!editSpring && e.button == 0)
                    {
                        points.InsertArrayElementAtIndex(points.arraySize);
                        points.GetArrayElementAtIndex(points.arraySize - 1).vector2Value = p;
                    }
                    else if (!editSpring && e.button == 1 && points.arraySize > 0)
                    {
                        int nearest = 0;
                        float best = float.PositiveInfinity;
                        for (int i = 0; i < points.arraySize; i++)
                        {
                            float distance = (points.GetArrayElementAtIndex(i).vector2Value - p).sqrMagnitude;
                            if (distance < best) { best = distance; nearest = i; }
                        }
                        if (best < .01f) points.DeleteArrayElementAtIndex(nearest);
                    }
                    e.Use();
                }
                EditorGUILayout.EndScrollView();
                if (editSpring) EditorGUILayout.HelpBox("Click for end A (red); Shift-click for end B (cyan). Values are normalized, not pixels.", MessageType.None);
            }
            serializedObject.ApplyModifiedProperties();
        }

        private static void DrawMarker(Rect rect, Vector2 p, Color color)
        {
            Vector2 position = new Vector2(rect.x + p.x * rect.width, rect.y + (1f - p.y) * rect.height);
            EditorGUI.DrawRect(new Rect(position.x - 5, position.y - 1, 10, 2), color);
            EditorGUI.DrawRect(new Rect(position.x - 1, position.y - 5, 2, 10), color);
        }
    }
}
