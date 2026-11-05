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
    /// A creative framing profile places the progress bar above the board for the take, and the
    /// bar returns to its authored position when the take ends.
    /// </summary>
    public sealed class CreativeFramingPlayModeTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const int BoardRows = 10;
        private const float Gap = .05f;

        private static readonly Type ConfigType = Type.GetType("GravityPuzzle.Creative.Config.CreativeTakeConfig, Assembly-CSharp");
        private static readonly Type FramingType = Type.GetType("GravityPuzzle.Creative.Config.CreativeFramingProfile, Assembly-CSharp");
        private static readonly Type LevelType = Type.GetType("GravityPuzzle.GravityLevelDefinition, Assembly-CSharp");
        private static readonly Type BindingsType = Type.GetType("GravityPuzzle.Creative.Runtime.CreativeSceneBindings, Assembly-CSharp");
        private static readonly Type RequestType = Type.GetType("GravityPuzzle.Creative.Runtime.CreativeTakeRequest, Assembly-CSharp");
        private static readonly Type SessionType = Type.GetType("GravityPuzzle.Creative.Runtime.CreativeSession, Assembly-CSharp");

        private GameObject root;
        private Camera camera;
        private Canvas canvas;
        private RectTransform bar;
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
            camera.transform.position = new Vector3(0f, -2f, -10f);

            var canvasObject = new GameObject("Canvas");
            canvasObject.transform.SetParent(root.transform);
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;

            bar = new GameObject("Bar", typeof(RectTransform)).GetComponent<RectTransform>();
            bar.SetParent(canvas.transform, false);
            bar.pivot = new Vector2(.5f, 1f);
            bar.sizeDelta = new Vector2(100f, 40f);

            bindings = (MonoBehaviour)root.AddComponent(BindingsType);
            BindingsType.GetField("progressBar", Private).SetValue(bindings, bar);
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
            if (outputFolder != null && Directory.Exists(outputFolder))
                Directory.Delete(outputFolder, true);
        }

        [UnityTest]
        public IEnumerator Gap_PlacesTheBarAboveTheBoard_AndEndRestoresIt()
        {
            FramingType.GetField("progressBarBoardGap", Private).SetValue(framing, Gap);
            yield return null;
            Vector3 authored = bar.position;
            object session = CreateSession();

            SessionType.GetMethod("Begin").Invoke(session, null);

            float boardTop = camera.WorldToScreenPoint(new Vector3(0f, BoardRows * .5f, 0f)).y;
            float expected = boardTop + Gap * camera.pixelHeight;
            float actual = camera.WorldToScreenPoint(bar.TransformPoint(bar.rect.center)).y;
            Assert.That(actual, Is.EqualTo(expected).Within(1f));
            Assert.That(bar.position.x, Is.EqualTo(authored.x).Within(.0001f));

            SessionType.GetMethod("End").Invoke(session, null);
            Assert.That(Vector3.Distance(bar.position, authored), Is.LessThan(.0001f));
        }

        [UnityTest]
        public IEnumerator NoGap_LeavesTheBarInPlace()
        {
            yield return null;
            Vector3 authored = bar.position;
            object session = CreateSession();

            SessionType.GetMethod("Begin").Invoke(session, null);

            Assert.That(bar.position, Is.EqualTo(authored));
            SessionType.GetMethod("End").Invoke(session, null);
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
            arguments[arguments.Length - 1] = bindings; // coroutine host
            return constructor.Invoke(arguments);
        }
    }
}
