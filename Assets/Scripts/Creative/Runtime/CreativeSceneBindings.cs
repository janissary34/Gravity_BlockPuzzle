using System;
using GravityPuzzle.Creative.Config;
using UnityEngine;
using UnityEngine.UI;

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

            [Tooltip("UI graphics hidden by disabling the Graphic component (Image, TMP text, ...). Scripts, " +
                     "tweens and coroutines keep running, raycasts pass through, and a panel the game " +
                     "activates later stays invisible. Preferred for UI groups.")]
            public Graphic[] graphics;

            [Tooltip("Objects hidden by deactivating them. Use only for inert objects: deactivation stops " +
                     "their scripts, coroutines and linked tweens.")]
            public GameObject[] objects;
        }

        [SerializeField] private UIGroupBinding[] uiGroups = Array.Empty<UIGroupBinding>();

        [Tooltip("Backdrop renderers hidden in SolidColor background mode (board, frame and pieces stay visible).")]
        [SerializeField] private Renderer[] backdropRenderers = Array.Empty<Renderer>();

        [Tooltip("Progress bar moved by a CreativeFramingProfile during a take.")]
        [SerializeField] private RectTransform progressBar;

        [Tooltip("Canvas that holds the progress bar.")]
        [SerializeField] private Canvas progressBarCanvas;

        public RectTransform ProgressBar => progressBar;

        public Canvas ProgressBarCanvas => progressBarCanvas;

        public int UIGroupCount => uiGroups != null ? uiGroups.Length : 0;

        public UIGroupBinding GetUIGroup(int index) => uiGroups[index];

        public int BackdropRendererCount => backdropRenderers != null ? backdropRenderers.Length : 0;

        public Renderer GetBackdropRenderer(int index) => backdropRenderers[index];
    }
}
