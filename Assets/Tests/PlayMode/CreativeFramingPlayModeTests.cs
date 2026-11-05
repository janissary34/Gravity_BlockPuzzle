using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GravityPuzzle.Tests.PlayMode
{
    /// <summary>
    /// A creative framing profile places the progress bar above the board frame for the take, the
    /// bar returns to its authored position when the take ends, and a bad layout fails validation.
    /// </summary>
    public sealed class CreativeFramingPlayModeTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const int BoardRows = 10;
        private const float Gap = .02f;

        private static readonly Type ConfigType = Type.GetType("GravityPuzzle.Creative.Config.CreativeTakeConfig, Assembly-CSharp");
        private static readonly Type FramingType = Type.GetType("GravityPuzzle.Creative.Config.CreativeFramingProfile, Assembly-CSharp");
        private static readonly Type LevelType = Type.GetType("GravityPuzzle.GravityLevelDefinition, Assembly-CSharp");
        private static readonly Type BindingsType = Type.GetType("GravityPuzzle.Creative.Runtime.CreativeSceneBindings, Assembly-CSharp");
        private static readonly Type LevelRootType = Type.GetType("GravityPuzzle.Presentation.Views.LevelRuntimeRoot, Assembly-CSharp");
        private static readonly Type RequestType = Type.GetType("GravityPuzzle.Creative.Runtime.CreativeTakeRequest, Assembly-CSharp");
        private static readonly Type SessionType = Type.GetType("GravityPuzzle.Creative.Runtime.CreativeSession, Assembly-CSharp");

        private GameObject root;
        private Camera camera;
        private Canvas canvas;
        private RectTransform bar;
        private RectTransform visual;
        private SpriteRenderer frame;
        private Component levelRoot;
        private Texture2D frameTexture;
        private MonoBehaviour bindings;
        private ScriptableObject config;
        private ScriptableObject framing;
        private ScriptableObject level;
        private string outputFolder;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Creative Framing Test");
            var cameraObject = new GameObject("Camera");
            cameraObject.transform.SetParent(root.transform);
            camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 10f;
            camera.transform.position = new Vector3(0f, 3f, -10f);

            var canvasObject = new GameObject("Canvas");
            canvasObject.transform.SetParent(root.transform);
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;

            bar = new GameObject("Bar", typeof(RectTransform)).GetComponent<RectTransform>();
            bar.SetParent(canvas.transform, false);
            bar.pivot = new Vector2(.5f, 1f);
            bar.sizeDelta = new Vector2(100f, 40f);

            visual = new GameObject("Visual", typeof(RectTransform)).GetComponent<RectTransform>();
            visual.SetParent(bar, false);
            visual.sizeDelta = new Vector2(80f, 20f);

            frameTexture = new Texture2D(4, 4);
            var frameObject = new GameObject("Frame Slot");
            frameObject.transform.SetParent(root.transform);
            frameObject.transform.position = new Vector3(0f, BoardRows * .5f, 0f);
            frame = frameObject.AddComponent<SpriteRenderer>();
            frame.sprite = Sprite.Create(frameTexture, new Rect(0f, 0f, 4f, 4f), new Vector2(.5f, .5f), 4f);

            Type poolType = Type.GetType("GravityPuzzle.Presentation.Views.BoardFrameModulePool, Assembly-CSharp");
            Array pools = Array.CreateInstance(poolType, 1);
            pools.SetValue(Activator.CreateInstance(poolType, Enum.ToObject(poolType.GetProperty("Role").PropertyType, 0), new[] { frame }), 0);
            levelRoot = root.AddComponent(LevelRootType);
            LevelRootType.GetField("frameModulePools", Private).SetValue(levelRoot, pools);

            bindings = (MonoBehaviour)root.AddComponent(BindingsType);
            BindingsType.GetField("progressBar", Private).SetValue(bindings, bar);
            BindingsType.GetField("progressBarVisual", Private).SetValue(bindings, visual);
            BindingsType.GetField("progressBarCanvas", Private).SetValue(bindings, canvas);

            level = ScriptableObject.CreateInstance(LevelType);
            LevelType.GetField("boardRows").SetValue(level, BoardRows);
            framing = ScriptableObject.CreateInstance(FramingType);
            config = ScriptableObject.CreateInstance(ConfigType);
            ConfigType.GetField("level", Private).SetValue(config, level);
            ConfigType.GetField("framing", Private).SetValue(config, framing);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.Destroy(root);
            UnityEngine.Object.Destroy(config);
            UnityEngine.Object.Destroy(framing);
            UnityEngine.Object.Destroy(level);
            UnityEngine.Object.Destroy(frameTexture);
            if (outputFolder != null && Directory.Exists(outputFolder))
                Directory.Delete(outputFolder, true);
        }

        [UnityTest]
        public IEnumerator Gap_PlacesTheBarAboveTheFrame_WhenTheCameraMovedInTheSameFrame_AndEndRestoresIt()
        {
            FramingType.GetField("progressBarBoardGap", Private).SetValue(framing, Gap);
            yield return null;
            Vector3 authored = bar.localPosition;

            // The level build moves the camera in the frame the session begins, before the canvas follows.
            camera.transform.position = new Vector3(0f, 1f, -10f);
            object session = CreateSession();
            SessionType.GetMethod("Begin").Invoke(session, null);
            for (int frame = 0; frame < 5; frame++)
                yield return null;

            float expected = camera.WorldToScreenPoint(new Vector3(0f, FrameTop, 0f)).y + Gap * camera.pixelHeight;
            Assert.That(ScreenBottom(), Is.EqualTo(expected).Within(1f));

            SessionType.GetMethod("End").Invoke(session, null);
            Assert.That(Vector3.Distance(bar.localPosition, authored), Is.LessThan(.0001f));
        }

        [UnityTest]
        public IEnumerator NoGap_LeavesTheBarInPlace()
        {
            bar.anchoredPosition = new Vector2(0f, 200f);
            yield return null;
            Vector3 authored = bar.position;
            object session = CreateSession();

            Assert.That(Prepare(session, out string failure), Is.True, failure);

            Assert.That(bar.position, Is.EqualTo(authored));
        }

        [UnityTest]
        public IEnumerator BarOverlappingTheBoard_FailsValidation()
        {
            yield return null;
            bar.position = new Vector3(bar.position.x, FrameTop - 1f, bar.position.z);
            object session = CreateSession();

            Assert.That(Prepare(session, out string failure), Is.False);
            StringAssert.Contains("overlaps the board", failure);
        }

        [UnityTest]
        public IEnumerator BarPushedOffTheScreen_FailsValidation()
        {
            FramingType.GetField("progressBarBoardGap", Private).SetValue(framing, .2f);
            FrameRenderer.transform.position = new Vector3(0f, 9.5f, 0f);
            yield return null;
            object session = CreateSession();

            Assert.That(Prepare(session, out string failure), Is.False);
            StringAssert.Contains("outside the safe screen area", failure);
        }

        [UnityTest]
        public IEnumerator FailedPlacement_RestoresTheBar()
        {
            FramingType.GetField("progressBarBoardGap", Private).SetValue(framing, .2f);
            FrameRenderer.transform.position = new Vector3(0f, 9.5f, 0f);
            yield return null;
            Vector3 authored = bar.localPosition;
            object session = CreateSession();

            Assert.That(Prepare(session, out _), Is.False);

            Assert.That(Vector3.Distance(bar.localPosition, authored), Is.LessThan(.0001f));
        }

        [UnityTest]
        public IEnumerator VisibleTimer_IsKeptClearOfTheFrame()
        {
            FramingType.GetField("progressBarBoardGap", Private).SetValue(framing, Gap);
            RectTransform timer = AddTimer();
            yield return null;
            object session = CreateSession();

            Assert.That(Prepare(session, out string failure), Is.True, failure);

            var corners = new Vector3[4];
            timer.GetWorldCorners(corners);
            float expected = camera.WorldToScreenPoint(new Vector3(0f, FrameTop, 0f)).y + Gap * camera.pixelHeight;
            Assert.That(RectTransformUtility.WorldToScreenPoint(camera, corners[0]).y, Is.EqualTo(expected).Within(1f));
        }

        [UnityTest]
        public IEnumerator FramedTakeWithoutFrame_FailsValidation()
        {
            FramingType.GetField("progressBarBoardGap", Private).SetValue(framing, Gap);
            FrameRenderer.enabled = false;
            yield return null;
            object session = CreateSession();

            Assert.That(Prepare(session, out string failure), Is.False);
            StringAssert.Contains("board frame top", failure);
        }

        // An inactive graphic counts: the game activates the timer only when play starts.
        private RectTransform AddTimer()
        {
            var timerObject = new GameObject("Timer", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            timerObject.transform.SetParent(bar, false);
            var timer = timerObject.GetComponent<RectTransform>();
            timer.sizeDelta = new Vector2(60f, 30f);
            timer.anchoredPosition = new Vector2(0f, -60f);
            var graphic = timerObject.GetComponent<UnityEngine.UI.Image>();
            timerObject.SetActive(false);

            Type groupType = Type.GetType("GravityPuzzle.Creative.Config.CreativeUIGroup, Assembly-CSharp");
            Type bindingType = BindingsType.GetNestedType("UIGroupBinding");
            object binding = Activator.CreateInstance(bindingType);
            bindingType.GetField("group").SetValue(binding, Enum.Parse(groupType, "Timer"));
            bindingType.GetField("graphics").SetValue(binding, new UnityEngine.UI.Graphic[] { graphic });
            Array groups = Array.CreateInstance(bindingType, 1);
            groups.SetValue(binding, 0);
            BindingsType.GetField("uiGroups", Private).SetValue(bindings, groups);
            return timer;
        }

        private SpriteRenderer FrameRenderer => frame;

        private float FrameTop => frame.bounds.max.y;

        private float ScreenBottom()
        {
            var corners = new Vector3[4];
            visual.GetWorldCorners(corners);
            return RectTransformUtility.WorldToScreenPoint(camera, corners[0]).y;
        }

        private static bool Prepare(object session, out string failure)
        {
            object[] arguments = { null };
            bool ok = (bool)SessionType.GetMethod("PrepareProgressBar", Private).Invoke(session, arguments);
            failure = arguments[0] as string;
            return ok;
        }

        private object CreateSession()
        {
            outputFolder = Path.Combine(Path.GetTempPath(), "gbp-creative-framing-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outputFolder);
            object request = Activator.CreateInstance(
                RequestType, Private, null, new object[] { config, "framing-test", outputFolder }, null);

            ConstructorInfo constructor = SessionType.GetConstructors()[0];
            object[] arguments = new object[constructor.GetParameters().Length];
            arguments[0] = config;
            arguments[1] = request;
            arguments[4] = camera;
            arguments[5] = bindings;
            arguments[7] = levelRoot;
            arguments[arguments.Length - 1] = bindings; // coroutine host
            return constructor.Invoke(arguments);
        }
    }
}
