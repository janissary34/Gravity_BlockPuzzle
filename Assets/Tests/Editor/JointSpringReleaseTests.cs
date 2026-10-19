using System.Reflection;
using DG.Tweening;
using GravityPuzzle.Infrastructure.Pooling;
using GravityPuzzle.Presentation.Views;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class JointSpringReleaseTests
{
    [TestCase(0, false, -1f)]
    [TestCase(1, true, 1f)]
    public void Release_DetachesFlicksFadesAndResetsForReuse(int key, bool vertical, float direction)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<JointSpringView>("Assets/Prefabs/JointSpringView.prefab");
        Assert.That(prefab, Is.Not.Null);
        var poolRoot = new GameObject("Spring test pool");
        var piece = new GameObject("Spring test piece");
        var pool = new GameObjectPool<JointSpringView>(prefab, poolRoot.transform, 1);
        try
        {
            pool.Prewarm();
            Assert.That(pool.TryRent(out var view), Is.True);
            view.Initialize(key, 1, 2, Vector3.zero, vertical);
            view.transform.SetParent(piece.transform, true);
            var renderer = view.GetComponent<SpriteRenderer>();
            Assert.That(renderer, Is.Not.Null);
            float startAngle = view.transform.eulerAngles.z;
            int completed = 0;
            view.PlayShredderRelease(poolRoot.transform, .2f, .2f, .16f, 55f, _ => completed++);
            Assert.That(view.transform.parent, Is.EqualTo(poolRoot.transform));
            var field = typeof(JointSpringView).GetField("releaseTween", BindingFlags.Instance | BindingFlags.NonPublic);
            var tween = (Sequence)field.GetValue(view);
            tween.Goto(.1f, false);
            Assert.That(view.transform.position.x * direction, Is.GreaterThan(0f));
            Assert.That(view.transform.position.y, Is.GreaterThan(0f));
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(startAngle, view.transform.eulerAngles.z)), Is.GreaterThan(1f));
            Assert.That(renderer.color.a, Is.LessThan(1f));
            tween.Goto(.2f, false);
            Assert.That(completed, Is.EqualTo(1));
            Assert.That(pool.AvailableCount, Is.EqualTo(1));
            Assert.That(pool.TryRent(out var reused), Is.True);
            Assert.That(reused, Is.SameAs(view));
            Assert.That(renderer.color.a, Is.EqualTo(1f));
            Assert.That(renderer.enabled, Is.True);
            pool.Return(reused);
        }
        finally
        {
            Object.DestroyImmediate(piece);
            Object.DestroyImmediate(poolRoot);
        }
    }
}
