using System.Collections.Generic;
using GravityPuzzle.Creative.EditorTools;
using NUnit.Framework;
using UnityEngine;

public sealed class CreativeSceneBindingsAuthoringTests
{
    private GameObject root;

    [SetUp]
    public void SetUp() => root = new GameObject("Root");

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(root);

    private Transform AddChild(Transform parent, string name)
    {
        var child = new GameObject(name).transform;
        child.SetParent(parent);
        return child;
    }

    [Test]
    public void FindAll_TreatsSlashAsPartOfAName()
    {
        Transform slashName = AddChild(root.transform, "Panel/With_Slash");

        Assert.That(root.transform.Find("Panel/With_Slash"), Is.Null);
        CollectionAssert.AreEqual(new[] { slashName }, CreativeSceneBindingsAuthoring.FindAll(root.transform, new[] { "Panel/With_Slash" }));
    }

    [Test]
    public void FindAll_ReturnsDuplicateSiblingsInOrder()
    {
        Transform first = AddChild(root.transform, "Panel");
        AddChild(root.transform, "Other");
        Transform second = AddChild(root.transform, "Panel");

        CollectionAssert.AreEqual(new[] { first, second }, CreativeSceneBindingsAuthoring.FindAll(root.transform, new[] { "Panel" }));
    }

    [Test]
    public void FindAll_WalksEveryMatchingParent()
    {
        Transform first = AddChild(AddChild(root.transform, "Group"), "Leaf");
        Transform second = AddChild(AddChild(root.transform, "Group"), "Leaf");

        CollectionAssert.AreEqual(new[] { first, second }, CreativeSceneBindingsAuthoring.FindAll(root.transform, new[] { "Group", "Leaf" }));
    }

    [Test]
    public void FindAll_ReturnsNothingWhenAStepIsMissing()
    {
        AddChild(root.transform, "Group");

        List<Transform> matches = CreativeSceneBindingsAuthoring.FindAll(root.transform, new[] { "Group", "Leaf" });

        Assert.That(matches, Is.Empty);
    }
}
