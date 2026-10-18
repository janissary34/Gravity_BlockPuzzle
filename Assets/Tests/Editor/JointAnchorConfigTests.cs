using GravityPuzzle.Config;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class JointAnchorConfigTests
{
    [Test]
    public void BrickBlockAtlas_AllSpritesHaveValidStudLayouts()
    {
        var config = AssetDatabase.LoadAssetAtPath<JointAnchorConfig>("Assets/Config/Presentation/JointAnchorConfig.asset");
        Assert.That(config, Is.Not.Null);
        var assets = AssetDatabase.LoadAllAssetsAtPath("Assets/Art/BrickBlocks.png");
        int count = 0;
        foreach (var asset in assets)
        {
            if (!(asset is Sprite sprite) || !sprite.name.StartsWith("BB_"))
                continue;
            Assert.That(config.TryGetStuds(sprite, out var studs), Is.True, sprite.name);
            Assert.That(studs.Count, Is.GreaterThanOrEqualTo(4), sprite.name);
            count++;
        }
        Assert.That(count, Is.EqualTo(28));
    }

    [Test]
    public void ReferenceFit_IsIndependentOfTheMarkedBlackEnds()
    {
        var config = ScriptableObject.CreateInstance<JointAnchorConfig>();
        var texture = new Texture2D(2, 2);
        var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * .5f);
        try
        {
            var serialized = new SerializedObject(config);
            serialized.FindProperty("springSprite").objectReferenceValue = sprite;
            serialized.FindProperty("springEndA").vector2Value = new Vector2(.05f, .75f);
            serialized.FindProperty("springEndB").vector2Value = new Vector2(.95f, .75f);
            serialized.FindProperty("springEndsAuthored").boolValue = true;
            serialized.FindProperty("referenceFitA").vector2Value = new Vector2(.17f, .67f);
            serialized.FindProperty("referenceFitB").vector2Value = new Vector2(.83f, .68f);
            serialized.FindProperty("useReferenceFit").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(config.TryGetSpringFitPoints(sprite, out Vector2 fitA, out Vector2 fitB), Is.True);
            Assert.That(fitA, Is.EqualTo(new Vector2(.17f, .67f)));
            Assert.That(fitB, Is.EqualTo(new Vector2(.83f, .68f)));
            Assert.That(config.TryGetSpringEnds(sprite, out Vector2 endA, out _), Is.True);
            Assert.That(endA, Is.EqualTo(new Vector2(.05f, .75f)));
            serialized.FindProperty("useReferenceFit").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(config.TryGetSpringFitPoints(sprite, out fitA, out _), Is.True);
            Assert.That(fitA, Is.EqualTo(endA));
        }
        finally
        {
            Object.DestroyImmediate(sprite);
            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(config);
        }
    }

    [TestCase(0f, 0f, true)]
    [TestCase(1f, 1f, true)]
    [TestCase(1.47f, .72f, false)]
    [TestCase(.87f, 16.87f, false)]
    [TestCase(-.01f, .5f, false)]
    public void CoordinatesMustBeNormalized(float x, float y, bool valid)
    {
        Assert.That(JointAnchorConfig.IsValidPoint(new Vector2(x, y)), Is.EqualTo(valid));
    }

    [Test]
    public void SpriteLocalPoint_UsesSpriteRectPivotAndPixelsPerUnit()
    {
        var texture = new Texture2D(100, 100);
        var sprite = Sprite.Create(texture, new Rect(10, 20, 40, 20), new Vector2(.25f, .75f), 20f);
        try
        {
            Vector2 point = JointAnchorConfig.SpriteLocalPoint(sprite, new Vector2(.75f, .25f));
            Assert.That(point.x, Is.EqualTo(1f).Within(.0001f));
            Assert.That(point.y, Is.EqualTo(-.5f).Within(.0001f));
        }
        finally
        {
            Object.DestroyImmediate(sprite);
            Object.DestroyImmediate(texture);
        }
    }
}
