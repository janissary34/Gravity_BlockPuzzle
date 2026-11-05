using System.Reflection;
using GravityPuzzle;
using GravityPuzzle.Creative.Config;
using GravityPuzzle.Creative.Runtime;
using GravityPuzzle.Gameplay.Input;
using NUnit.Framework;
using UnityEngine;

public sealed class CreativePointerLogTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private GravityLevelDefinition level;
    private CreativeScript script;
    private GameObject cameraObject;
    private Camera camera;

    [SetUp]
    public void SetUp()
    {
        level = ScriptableObject.CreateInstance<GravityLevelDefinition>();
        level.boardColumns = 7;
        level.boardRows = 7;
        script = ScriptableObject.CreateInstance<CreativeScript>();
        var drag = new CreativeScript.Drag { startFrame = 0, moveFrames = 4, fromCell = new Vector2(1f, 3f), toCell = new Vector2(0f, 3f) };
        typeof(CreativeScript).GetField("drags", Private).SetValue(script, new[] { drag });
        cameraObject = new GameObject("Pointer Log Camera");
        camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(level);
        Object.DestroyImmediate(script);
        Object.DestroyImmediate(cameraObject);
    }

    private BoardPointerSample[] Run(CreativeEventLog log)
    {
        var pointer = new CreativePointerScript(script, level);
        pointer.Start(camera, log);
        var samples = new BoardPointerSample[5];
        for (int frame = 0; frame < samples.Length; frame++)
            samples[frame] = pointer.GetSample(100 + frame);
        return samples;
    }

    [Test]
    public void Logging_DoesNotChangeWhatTheDragControllerReceives()
    {
        BoardPointerSample[] withoutLog = Run(null);
        BoardPointerSample[] withLog = Run(new CreativeEventLog(16));

        CollectionAssert.AreEqual(withoutLog, withLog);
    }

    [Test]
    public void OneSampleIsLoggedPerFrameOfTheDrag()
    {
        var log = new CreativeEventLog(16);

        Run(log);

        Assert.That(log.Count, Is.EqualTo(5));
    }

    [Test]
    public void ASecondReadOfAFrameIsNotLoggedAgain()
    {
        var log = new CreativeEventLog(16);
        var pointer = new CreativePointerScript(script, level);
        pointer.Start(camera, log);

        pointer.GetSample(100);
        pointer.GetSample(100);

        Assert.That(log.Count, Is.EqualTo(1));
    }
}
