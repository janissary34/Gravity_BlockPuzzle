using System.Collections.Generic;
using System.Reflection;
using GravityPuzzle;
using NUnit.Framework;
using TMPro;
using UnityEngine;

public sealed class BombPresentationTests
{
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

    private static void SetField<T>(PuzzlePiece piece, string name, T value)
    {
        typeof(PuzzlePiece).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(piece, value);
    }

    private static T GetField<T>(PuzzlePiece piece, string name)
    {
        return (T)typeof(PuzzlePiece).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(piece);
    }
}
