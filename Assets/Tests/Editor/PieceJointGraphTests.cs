using System.Collections.Generic;
using GravityPuzzle.Core.Grid;
using GravityPuzzle.Gameplay.Pieces;
using NUnit.Framework;

namespace GravityPuzzle.Tests.Editor
{
    public sealed class PieceJointGraphTests
    {
        [Test]
        public void RemovingOneOfTwoJointsKeepsTheMembersInOneGroup()
        {
            PieceJointGraph graph = CreateGraph(
                new PieceJointGraph.Joint(1, 10, 20, new GridCoordinate(0, 0), new GridCoordinate(1, 0)),
                new PieceJointGraph.Joint(2, 10, 20, new GridCoordinate(0, 1), new GridCoordinate(1, 1)));

            graph.RemoveJointsAtEndpoint(10, new GridCoordinate(0, 0));

            Assert.That(graph.AreInSameGroup(10, 20), Is.True);
            Assert.That(graph.JointCount, Is.EqualTo(1));
        }

        [Test]
        public void RemovingTheLastJointSeparatesTheMembers()
        {
            PieceJointGraph graph = CreateGraph(
                new PieceJointGraph.Joint(1, 10, 20, new GridCoordinate(0, 0), new GridCoordinate(1, 0)));

            graph.RemoveJointsAtEndpoint(10, new GridCoordinate(0, 0));

            Assert.That(graph.AreInSameGroup(10, 20), Is.False);
            List<int> members = new List<int>();
            graph.CollectGroupMembers(10, members);
            Assert.That(members, Is.EqualTo(new[] { 10 }));
        }

        [Test]
        public void RemovingCapturedJointIdSeparatesMembersAfterEndpointIdentityChanges()
        {
            PieceJointGraph graph = CreateGraph(
                new PieceJointGraph.Joint(77, 10, 20, new GridCoordinate(0, 0), new GridCoordinate(1, 0)));

            // Hammer records stable joint ids before it rebuilds the struck
            // piece's topology. The endpoint may be transferred to a fragment
            // in between, but the original edge must still be removable.
            graph.TransferEndpoint(
                10,
                new GridCoordinate(0, 0),
                30,
                new GridCoordinate(0, 0),
                true);

            Assert.That(graph.RemoveJointsById(new[] { 77 }), Is.EqualTo(1));
            Assert.That(graph.AreInSameGroup(30, 20), Is.False);
            Assert.That(graph.JointCount, Is.EqualTo(0));
        }

        [Test]
        public void RemovingMiddleMemberJointsSplitsAChain()
        {
            PieceJointGraph graph = CreateGraph(
                new PieceJointGraph.Joint(1, 10, 20, new GridCoordinate(0, 0), new GridCoordinate(1, 0)),
                new PieceJointGraph.Joint(2, 20, 30, new GridCoordinate(2, 0), new GridCoordinate(3, 0)));

            graph.RemoveJointsForMember(20);

            Assert.That(graph.AreInSameGroup(10, 20), Is.False);
            Assert.That(graph.AreInSameGroup(20, 30), Is.False);
        }

        [Test]
        public void CycleRemainsConnectedAfterOneJointBreaks()
        {
            PieceJointGraph graph = CreateGraph(
                new PieceJointGraph.Joint(1, 10, 20, new GridCoordinate(0, 0), new GridCoordinate(1, 0)),
                new PieceJointGraph.Joint(2, 20, 30, new GridCoordinate(0, 0), new GridCoordinate(1, 0)),
                new PieceJointGraph.Joint(3, 30, 10, new GridCoordinate(0, 0), new GridCoordinate(1, 0)));

            graph.RemoveJointsAtEndpoint(10, new GridCoordinate(0, 0));

            Assert.That(graph.AreInSameGroup(10, 20), Is.True);
            Assert.That(graph.AreInSameGroup(10, 30), Is.True);
        }

        [Test]
        public void GroupMoveIsAtomicWhenAnyMemberIsBlocked()
        {
            GravityBoardGrid grid = new GravityBoardGrid(5, 3);
            PieceModel first = CreateSingleCellPiece(10, 0, 1);
            PieceModel second = CreateSingleCellPiece(20, 2, 1);
            PieceModel blocker = CreateSingleCellPiece(30, 3, 1);
            Assert.That(grid.TryPlace(first), Is.True);
            Assert.That(grid.TryPlace(second), Is.True);
            Assert.That(grid.TryPlace(blocker), Is.True);

            bool moved = grid.TryMoveGroupIgnoringMembers(
                new[] { first, second },
                new[] { new GridCoordinate(1, 1), new GridCoordinate(3, 1) },
                out _);

            Assert.That(moved, Is.False);
            Assert.That(first.Anchor, Is.EqualTo(new GridCoordinate(0, 1)));
            Assert.That(second.Anchor, Is.EqualTo(new GridCoordinate(2, 1)));
        }

        [Test]
        public void GroupMoveCanEnterAnotherMembersOldCell()
        {
            GravityBoardGrid grid = new GravityBoardGrid(4, 2);
            PieceModel first = CreateSingleCellPiece(10, 0, 1);
            PieceModel second = CreateSingleCellPiece(20, 1, 1);
            Assert.That(grid.TryPlace(first), Is.True);
            Assert.That(grid.TryPlace(second), Is.True);

            bool moved = grid.TryMoveGroupIgnoringMembers(
                new[] { first, second },
                new[] { new GridCoordinate(1, 1), new GridCoordinate(2, 1) },
                out _);

            Assert.That(moved, Is.True);
            Assert.That(first.Anchor, Is.EqualTo(new GridCoordinate(1, 1)));
            Assert.That(second.Anchor, Is.EqualTo(new GridCoordinate(2, 1)));
        }

        [Test]
        public void GroupGravityStopsWhenAnyMemberHasSupport()
        {
            GravityBoardGrid grid = new GravityBoardGrid(4, 4);
            PieceModel first = CreateSingleCellPiece(10, 0, 2);
            PieceModel second = CreateSingleCellPiece(20, 2, 2);
            PieceModel blocker = CreateSingleCellPiece(30, 0, 1);
            Assert.That(grid.TryPlace(first), Is.True);
            Assert.That(grid.TryPlace(second), Is.True);
            Assert.That(grid.TryPlace(blocker), Is.True);

            List<GridCoordinate> targets = new List<GridCoordinate>();

            Assert.That(grid.TryGetGroupFallTarget(new[] { first, second }, targets), Is.False);
            Assert.That(targets, Is.EqualTo(new[] { new GridCoordinate(0, 2), new GridCoordinate(2, 2) }));
        }

        [Test]
        public void GroupGravityUsesTheShortestFreeFallAcrossMembers()
        {
            GravityBoardGrid grid = new GravityBoardGrid(4, 5);
            PieceModel first = CreateSingleCellPiece(10, 0, 3);
            PieceModel second = CreateSingleCellPiece(20, 2, 3);
            PieceModel blocker = CreateSingleCellPiece(30, 2, 0);
            Assert.That(grid.TryPlace(first), Is.True);
            Assert.That(grid.TryPlace(second), Is.True);
            Assert.That(grid.TryPlace(blocker), Is.True);

            List<GridCoordinate> targets = new List<GridCoordinate>();

            Assert.That(grid.TryGetGroupFallTarget(new[] { first, second }, targets), Is.True);
            Assert.That(targets, Is.EqualTo(new[] { new GridCoordinate(0, 1), new GridCoordinate(2, 1) }));
        }

        private static PieceJointGraph CreateGraph(params PieceJointGraph.Joint[] joints)
        {
            PieceJointGraph graph = new PieceJointGraph();
            graph.Configure(new[] { 10, 20, 30 }, joints);
            return graph;
        }

        private static PieceModel CreateSingleCellPiece(int id, int x, int y)
        {
            return new PieceModel(
                id,
                new GridCoordinate(x, y),
                default,
                new List<GridCoordinate> { new GridCoordinate(0, 0) });
        }
    }
}
