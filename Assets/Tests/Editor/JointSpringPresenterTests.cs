using System.Collections.Generic;
using System.Reflection;
using GravityPuzzle;
using GravityPuzzle.Core.Grid;
using GravityPuzzle.Gameplay.Pieces;
using GravityPuzzle.Presentation.Views;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class JointSpringPresenterTests
{
    [Test]
    public void ResolveRequiredSpringCapacity_ReturnsTwoSpringsPerNonNullJoint()
    {
        Assert.That(JointSpringPresenter.ResolveRequiredSpringCapacity(null), Is.EqualTo(0));

        var level = ScriptableObject.CreateInstance<GravityLevelDefinition>();
        try
        {
            level.joints = new List<JointDefinition>
            {
                new JointDefinition { jointId = "a" },
                null,
                new JointDefinition { jointId = "b" }
            };

            Assert.That(JointSpringPresenter.ResolveRequiredSpringCapacity(level), Is.EqualTo(4));
        }
        finally
        {
            Object.DestroyImmediate(level);
        }
    }

    [Test]
    public void Level69_RequiredSpringCapacityExceedsLegacyFloorOfSixteen()
    {
        var level69 = AssetDatabase.LoadAssetAtPath<GravityLevelDefinition>("Assets/Level69.asset");
        Assert.That(level69, Is.Not.Null);
        Assert.That(JointSpringPresenter.ResolveRequiredSpringCapacity(level69), Is.EqualTo(level69.joints.Count * 2));
        Assert.That(JointSpringPresenter.ResolveRequiredSpringCapacity(level69), Is.GreaterThan(16));
    }

    [Test]
    public void Configure_ScalesPoolBeyondDefaultPrewarmCapacityForDenseLevels()
    {
        var springPrefab = AssetDatabase.LoadAssetAtPath<JointSpringView>("Assets/MainPrefabs/JointSpringView.prefab");
        Assert.That(springPrefab, Is.Not.Null);

        var root = new GameObject("SpringPresenterTestRoot");
        root.SetActive(false);
        var level = ScriptableObject.CreateInstance<GravityLevelDefinition>();
        level.boardColumns = 9;
        level.boardRows = 13;
        level.subdivisions = 4;
        try
        {
            var board = root.AddComponent<PrototypeBoard>();
            var presenter = root.AddComponent<JointSpringPresenter>();
            SetPrivateField(presenter, "springPrefab", springPrefab);

            const int jointCount = 12;
            var pieceIds = new List<int>(jointCount + 1);
            var initialJoints = new List<PieceJointGraph.Joint>(jointCount);
            var definitions = new List<PieceDefinition>(jointCount + 1);
            var snapshotPieces = new List<PieceModel>(jointCount + 1);
            var originCell = new GridCoordinate(0, 0);

            for (int i = 0; i <= jointCount; i++)
            {
                pieceIds.Add(i);
                var def = new PieceDefinition
                {
                    pieceId = $"p{i}",
                    origin = new Vector2Int(0, i * 4),
                    cells = new List<PieceCellDefinition>
                    {
                        new PieceCellDefinition(new Vector2Int(0, 0), PieceCellType.Block)
                    }
                };
                definitions.Add(def);
                snapshotPieces.Add(new PieceModel(
                    i,
                    new GridCoordinate(0, i * 4),
                    originCell,
                    new List<GridCoordinate> { originCell }));
            }

            level.pieces = definitions;
            level.joints = new List<JointDefinition>(jointCount);
            for (int i = 0; i < jointCount; i++)
            {
                level.joints.Add(new JointDefinition
                {
                    jointId = $"j{i}",
                    endpointA = new JointEndpointDefinition { pieceId = $"p{i}" },
                    endpointB = new JointEndpointDefinition { pieceId = $"p{i + 1}" }
                });
                initialJoints.Add(new PieceJointGraph.Joint(
                    i,
                    i,
                    i + 1,
                    originCell,
                    originCell));
            }

            var grid = new GravityBoardGrid(36, (jointCount + 1) * 4);
            board.InitializeBoardSnapshot(new LevelBoardSnapshot(grid, snapshotPieces, new List<LevelBoardSnapshotIssue>()));

            var graph = new PieceJointGraph();
            presenter.Configure(board, graph, level, root.transform);
            graph.Configure(pieceIds, initialJoints);

            var activeViews = (Dictionary<int, JointSpringView>)typeof(JointSpringPresenter)
                .GetField("activeSpringViews", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(presenter);

            Assert.That(activeViews.Count, Is.EqualTo(jointCount * 2));
        }
        finally
        {
            JointSpringPool.ResetPool();
            Object.DestroyImmediate(level);
            Object.DestroyImmediate(root);
        }
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, fieldName);
        field.SetValue(target, value);
    }
}
