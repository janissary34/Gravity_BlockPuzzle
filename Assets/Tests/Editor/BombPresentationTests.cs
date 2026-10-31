using System.Collections.Generic;
using System.Reflection;
using GravityPuzzle;
using GravityPuzzle.Presentation.Views;
using NUnit.Framework;
using TMPro;
using UnityEngine;

public sealed class BombPresentationTests
{
    [Test]
    public void BoosterForeground_RendersAboveDimmerAndRestoresAuthoredSorting()
    {
        var root = new GameObject("Booster foreground test");
        try
        {
            var piece = root.AddComponent<PuzzlePiece>();
            var visual = new GameObject("Visual").AddComponent<SpriteRenderer>();
            visual.transform.SetParent(root.transform);
            visual.sortingOrder = 7;

            piece.SetBoosterTargetingForeground(true, 102);
            Assert.That(visual.sortingOrder, Is.EqualTo(101));

            piece.SetBoosterTargetingForeground(false);
            Assert.That(visual.sortingOrder, Is.EqualTo(7));
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void BoosterRewardContent_ReactivatesItsAuthoredVisualWhenShown()
    {
        GameObject contentObject = new GameObject("Reward Content");
        GameObject visualObject = new GameObject("Rocket Visual");

        try
        {
            visualObject.transform.SetParent(contentObject.transform, false);
            visualObject.AddComponent<BoosterVisualView>();
            BoosterRewardContentView content = contentObject.AddComponent<BoosterRewardContentView>();

            visualObject.SetActive(false);
            content.SetVisible(true);

            Assert.That(visualObject.activeSelf, Is.True);
        }
        finally
        {
            Object.DestroyImmediate(contentObject);
        }
    }

    [Test]
    public void IceRefresh_ClearsIceWithoutClearingBombPresentation()
    {
        var root = new GameObject("Bomb test");
        try
        {
            var piece = root.AddComponent<PuzzlePiece>();
            var slots = new GameObject("Ice Presentation Slots").transform;
            slots.SetParent(root.transform);
            var bomb = new GameObject("Bomb").AddComponent<SpriteRenderer>();
            bomb.transform.SetParent(slots);
            var ice = new GameObject("Ice").AddComponent<SpriteRenderer>();
            ice.transform.SetParent(slots);
            var counter = new GameObject("Bomb counter").AddComponent<TextMeshPro>();
            counter.transform.SetParent(root.transform);
            counter.enabled = true;

            SetField(piece, "iceSlotsRoot", slots);
            SetField(piece, "bombOverlayRenderer", bomb);
            SetField(piece, "bombCounterText", counter);
            SetField(piece, "<IsBomb>k__BackingField", true);
            GetField<List<SpriteRenderer>>(piece, "iceRenderers").Add(ice);

            piece.RefreshFreezeState(1);

            Assert.That(bomb.enabled, Is.True);
            Assert.That(counter.enabled, Is.True);
            Assert.That(counter.gameObject.activeSelf, Is.True);
            Assert.That(ice.enabled, Is.False);
            Assert.That(GetField<SpriteRenderer>(piece, "bombOverlayRenderer"), Is.SameAs(bomb));
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [TestCase(true, true, 2, false)]
    [TestCase(true, true, 0, true)]
    [TestCase(false, true, 0, false)]
    [TestCase(true, false, 0, false)]
    public void HammerBombDefuse_RequiresFinalCellRemoval(
        bool hammerImpact,
        bool isBomb,
        int remainingCellCount,
        bool expected)
    {
        MethodInfo method = typeof(PuzzlePiece).GetMethod(
            "ShouldDefuseBombAfterCellRemoval",
            BindingFlags.Static | BindingFlags.NonPublic);

        bool actual = (bool)method.Invoke(null, new object[]
        {
            hammerImpact,
            isBomb,
            remainingCellCount
        });

        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void BombPresentation_MultiBlockPieceUsesOneBlockFootprintAtPieceCenter()
    {
        var root = new GameObject("Bomb footprint test");
        var texture = new Texture2D(16, 16);
        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 16f, 16f), new Vector2(.5f, .5f), 16f);

        try
        {
            var piece = root.AddComponent<PuzzlePiece>();
            var slots = new GameObject("Ice Presentation Slots").transform;
            slots.SetParent(root.transform);
            var overlay = new GameObject("Bomb Overlay Slot").AddComponent<SpriteRenderer>();
            overlay.transform.SetParent(slots);

            SetField(piece, "iceSlotsRoot", slots);
            GetField<List<SpriteRenderer>>(piece, "iceSlots").Add(overlay);
            SetField(piece, "bombOverlaySprite", sprite);
            SetField(piece, "bombOverlayFill", .936f);
            SetField(piece, "<IsBomb>k__BackingField", true);

            var cellVisuals = new List<SpriteRenderer>();
            var cellSizes = new List<Vector2>();
            SetField(piece, "collisionCellVisuals", cellVisuals);
            SetField(piece, "fullCollisionCellSizes", cellSizes);
            Vector2[] centers =
            {
                new Vector2(-.5f, -.5f),
                new Vector2(.5f, -.5f),
                new Vector2(-.5f, .5f),
                new Vector2(.5f, .5f)
            };
            for (int index = 0; index < centers.Length; index++)
            {
                var cell = new GameObject($"Cell {index}").AddComponent<SpriteRenderer>();
                cell.transform.SetParent(root.transform);
                cell.transform.localPosition = centers[index];
                cell.sprite = sprite;
                cellVisuals.Add(cell);
                cellSizes.Add(Vector2.one);
            }

            piece.RefreshBombState();

            Assert.That(overlay.bounds.center.x, Is.EqualTo(root.transform.position.x).Within(.001f));
            Assert.That(overlay.bounds.center.y, Is.EqualTo(root.transform.position.y).Within(.001f));
            Assert.That(overlay.bounds.size.x, Is.EqualTo(.936f).Within(.001f));
            Assert.That(overlay.bounds.size.y, Is.EqualTo(.936f).Within(.001f));
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(sprite);
            Object.DestroyImmediate(texture);
        }
    }

    private static void SetField<T>(PuzzlePiece piece, string name, T value)
    {
        typeof(PuzzlePiece).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(piece, value);
    }

    private static T GetField<T>(PuzzlePiece piece, string name)
    {
        return (T)typeof(PuzzlePiece).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(piece);
    }
}
