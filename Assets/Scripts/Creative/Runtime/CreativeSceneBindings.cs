using System;
using GravityPuzzle.Creative.Config;
using UnityEngine;

namespace GravityPuzzle.Creative.Runtime
{
    /// <summary>
    /// Scene-authored references used by Creative Mode: which canvases or objects make up each
    /// UI group, and which renderers form the level backdrop. Explicit references only; Creative
    /// Mode never searches the scene. Has no behaviour of its own and does nothing when Creative
    /// Mode is off.
    /// </summary>
    public sealed class CreativeSceneBindings : MonoBehaviour
    {
        [Serializable]
        public struct UIGroupBinding
        {
            public CreativeUIGroup group;

            [Tooltip("Canvases hidden by disabling the Canvas component (children keep their own state).")]
            public Canvas[] canvases;

            [Tooltip("Objects hidden by deactivating them. Re-applied when the game shows panels.")]
            public GameObject[] objects;
        }

        [SerializeField] private UIGroupBinding[] uiGroups = Array.Empty<UIGroupBinding>();

        [Tooltip("Backdrop renderers hidden in SolidColor background mode (board, frame and pieces stay visible).")]
        [SerializeField] private Renderer[] backdropRenderers = Array.Empty<Renderer>();

        public int UIGroupCount => uiGroups != null ? uiGroups.Length : 0;

        public UIGroupBinding GetUIGroup(int index) => uiGroups[index];

        public int BackdropRendererCount => backdropRenderers != null ? backdropRenderers.Length : 0;

        public Renderer GetBackdropRenderer(int index) => backdropRenderers[index];
    }
}
