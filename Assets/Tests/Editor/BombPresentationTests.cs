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

    [Test]
    public void HammerHitOnArmedBomb_DefusesWithoutRemovingItsBlock()
    {
        var root = new GameObject("Hammer bomb defuse test");
        var texture = new Texture2D(16, 16);
        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 16f, 16f), new Vector2(.5f, .5f), 16f);

        try
        {
            var piece = root.AddComponent<PuzzlePiece>();
            SpriteRenderer overlay = ConfigureBombPresentation(piece, root.transform, sprite, new[] { Vector2.zero });
            var counter = new GameObject("Bomb Counter Presentation").AddComponent<TextMeshPro>();
            counter.transform.SetParent(root.transform);
            SetField(piece, "bombCounterText", counter);
            piece.RefreshBombState();

            BoxCollider2D blockCollider = GetField<List<BoxCollider2D>>(piece, "collisionCells")[0];
            Assert.That(piece.TryRemoveCellWithHammer(blockCollider.bounds.center, out PuzzlePiece.RemovedCell result), Is.True);

            Assert.That(result.progressUnits, Is.Zero);
            Assert.That(piece.CollisionCellCount, Is.EqualTo(1));
            Assert.That(blockCollider.enabled, Is.True);
            Assert.That(piece.IsBombDefused, Is.True);
            Assert.That(overlay.enabled, Is.False);
            Assert.That(counter.enabled, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(sprite);
            Object.DestroyImmediate(texture);
        }
    }

    [Test]
    public void ArmedBomb_HammerVisualImpactUsesBombIconCenter()
    {
        var root = new GameObject("Hammer bomb impact point test");
        var texture = new Texture2D(16, 16);
        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 16f, 16f), new Vector2(.5f, .5f), 16f);

        try
        {
            var piece = root.AddComponent<PuzzlePiece>();
            SpriteRenderer overlay = ConfigureBombPresentation(piece, root.transform, sprite, new[] { new Vector2(2f, 3f) });
            piece.RefreshBombState();

            Assert.That(piece.TryGetBombVisualImpactPoint(out Vector2 impactPoint), Is.True);
            Assert.That(impactPoint, Is.EqualTo((Vector2)overlay.bounds.center));

            SetField(piece, "<IsBombDefused>k__BackingField", true);
            Assert.That(piece.TryGetBombVisualImpactPoint(out _), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(sprite);
            Object.DestroyImmediate(texture);
        }
    }

    [Test]
    public void ArmedBomb_ShredderPresentationReplacesBombIconWithVisualVoxels()
    {
        var root = new GameObject("Shredder bomb icon test");
        var texture = new Texture2D(16, 16);
        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 16f, 16f), new Vector2(.5f, .5f), 16f);
        Material fragmentMaterial = new Material(Shader.Find("Sprites/Default"));

        try
        {
            var piece = root.AddComponent<PuzzlePiece>();
            SpriteRenderer overlay = ConfigureBombPresentation(piece, root.transform, sprite, new[] { Vector2.zero });
            SetField(piece, "bombDefuseSplitMaterial", fragmentMaterial);
            piece.RefreshBombState();
            piece.PrepareModularShredderSurface();

            CollectionAssert.DoesNotContain(piece.ConfiguredShredderRenderers, overlay);
            Assert.That(
                piece.ConfiguredShredderRenderers.Length,
                Is.EqualTo(1 + VoxelBlockBuilder.Subdivisions * VoxelBlockBuilder.Subdivisions));
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(fragmentMaterial);
            Object.DestroyImmediate(sprite);
            Object.DestroyImmediate(texture);
        }
    }

    [TestCase(true, false, true)]
    [TestCase(true, true, false)]
    [TestCase(false, false, false)]
    public void HammerTargeting_UsesWholePieceOutlineForArmedBombsOnly(
        bool isBomb,
        bool isBombDefused,
        bool expected)
    {
        var root = new GameObject("Hammer targeting policy test");
        try
        {
            var piece = root.AddComponent<PuzzlePiece>();
            SetField(piece, "<IsBomb>k__BackingField", isBomb);
            SetField(piece, "<IsBombDefused>k__BackingField", isBombDefused);

            MethodInfo method = typeof(BoosterTargetingPresentation).GetMethod(
                "UsesWholePieceHammerHighlight",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);
            Assert.That((bool)method.Invoke(null, new object[] { piece }), Is.EqualTo(expected));
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
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
            SpriteRenderer overlay = ConfigureBombPresentation(piece, root.transform, sprite, new[]
            {
                new Vector2(-.5f, -.5f),
                new Vector2(.5f, -.5f),
                new Vector2(-.5f, .5f),
                new Vector2(.5f, .5f)
            });

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

    [Test]
    public void BombPresentation_ConcavePieceUsesCentralOccupiedBlock()
    {
        var root = new GameObject("Concave bomb footprint test");
        var texture = new Texture2D(16, 16);
        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 16f, 16f), new Vector2(.5f, .5f), 16f);

        try
        {
            var piece = root.AddComponent<PuzzlePiece>();
            SpriteRenderer overlay = ConfigureBombPresentation(piece, root.transform, sprite, new[]
            {
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(2f, 1f),
                new Vector2(3f, 1f),
                new Vector2(3f, 0f),
                new Vector2(3f, -1f)
            });

            piece.RefreshBombState();

            Assert.That(overlay.bounds.center.x, Is.EqualTo(2f).Within(.001f));
            Assert.That(overlay.bounds.center.y, Is.EqualTo(1f).Within(.001f));
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

    private static SpriteRenderer ConfigureBombPresentation(
        PuzzlePiece piece,
        Transform root,
        Sprite sprite,
        IReadOnlyList<Vector2> centers)
    {
        var slots = new GameObject("Ice Presentation Slots").transform;
        slots.SetParent(root);
        var overlay = new GameObject("Bomb Overlay Slot").AddComponent<SpriteRenderer>();
        overlay.transform.SetParent(slots);

        SetField(piece, "iceSlotsRoot", slots);
        GetField<List<SpriteRenderer>>(piece, "iceSlots").Add(overlay);
        SetField(piece, "bombOverlaySprite", sprite);
        SetField(piece, "bombOverlayFill", .936f);
        SetField(piece, "<IsBomb>k__BackingField", true);

        var cellVisuals = new List<SpriteRenderer>();
        var collisionCells = new List<BoxCollider2D>();
        var cellSizes = new List<Vector2>();
        SetField(piece, "collisionCellVisuals", cellVisuals);
        SetField(piece, "collisionCells", collisionCells);
        SetField(piece, "fullCollisionCellSizes", cellSizes);
        for (int index = 0; index < centers.Count; index++)
        {
            var cell = new GameObject($"Cell {index}").AddComponent<SpriteRenderer>();
            cell.transform.SetParent(root);
            cell.transform.localPosition = centers[index];
            cell.sprite = sprite;
            cellVisuals.Add(cell);
            collisionCells.Add(cell.gameObject.AddComponent<BoxCollider2D>());
            cellSizes.Add(Vector2.one);
        }

        return overlay;
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
