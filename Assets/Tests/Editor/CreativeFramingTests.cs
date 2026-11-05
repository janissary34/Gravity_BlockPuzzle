using System.Reflection;
using GravityPuzzle;
using NUnit.Framework;
using UnityEngine;

public sealed class CreativeFramingTests
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Static;
    private const float CameraSize = 10f;

    private GravityLevelDefinition level;

    [SetUp]
    public void SetUp()
    {
        level = ScriptableObject.CreateInstance<GravityLevelDefinition>();
        level.boardRows = 10;
    }

    [TearDown]
    public void TearDown()
    {
        GravityLevelRuntime.ConfigureBoardTopViewportInset(0f);
        Object.DestroyImmediate(level);
    }

    private float CameraY() => (float)typeof(GravityLevelRuntime)
        .GetMethod("ResolveCameraVerticalPosition", Private)
        .Invoke(null, new object[] { level, CameraSize });

    [Test]
    public void BoardTopInset_MovesTheCameraByThatShareOfTheView()
    {
        float gameFraming = CameraY();

        GravityLevelRuntime.ConfigureBoardTopViewportInset(.16f);

        Assert.That(CameraY() - gameFraming, Is.EqualTo(-.08f * CameraSize * 2f).Within(.0001f));
    }

    [Test]
    public void BoardTopInset_ZeroKeepsTheGameFraming()
    {
        float gameFraming = CameraY();

        GravityLevelRuntime.ConfigureBoardTopViewportInset(.16f);
        GravityLevelRuntime.ConfigureBoardTopViewportInset(0f);

        Assert.That(CameraY(), Is.EqualTo(gameFraming));
    }

    [Test]
    public void BoardTopInset_IsClearedWithTheLevelSequence()
    {
        float gameFraming = CameraY();

        GravityLevelRuntime.ConfigureBoardTopViewportInset(.16f);
        typeof(GravityLevelRuntime).GetMethod("ResetLevelSequence", Private).Invoke(null, null);

        Assert.That(CameraY(), Is.EqualTo(gameFraming));
    }
}
