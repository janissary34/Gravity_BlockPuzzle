using System.Collections.Generic;
using GravityPuzzle.Config;
using GravityPuzzle.Core.Grid;
using GravityPuzzle.Gameplay.Pieces;
using UnityEngine;

namespace GravityPuzzle
{
    /// <summary>
    /// Builds the smallest upward contact chain that loses its grid support
    /// when a root enters a shredder. It deliberately reads board occupancy,
    /// never Unity collision contacts, so the grid remains the rule authority.
    /// </summary>
    internal static class ShredderPhysicsIslandPlanner
    {
        private static readonly GridCoordinate Down = new GridCoordinate(0, -1);

        public static bool TryBuild(
            PrototypeBoard board,
            PuzzlePiece root,
            ShredderCatchZone zone,
            List<PuzzlePiece> members)
        {
            if (board == null || board.BoardSnapshot == null || root == null || zone == null ||
                members == null || !board.TryGetPieceModel(root, out PieceModel rootModel) ||
                root.IsShredderInteractionLocked || root.IsFrozen ||
                root.GridFallView != null && root.GridFallView.IsAnimating ||
                !CanBecomeDynamic(rootModel.State))
                return false;

            members.Clear();
            members.Add(root);
            HashSet<int> memberIds = new HashSet<int> { rootModel.Id };
            if (!zone.ContainsPhysicsIslandFootprint(board, rootModel) ||
                !HasClearVerticalRoute(board, rootModel, memberIds))
            {
                members.Clear();
                return false;
            }

            IReadOnlyList<PuzzlePiece> activePieces = PuzzlePiece.ActivePieces;

            bool added;
            do
            {
                added = false;
                for (int index = 0; index < activePieces.Count; index++)
                {
                    PuzzlePiece candidate = activePieces[index];
                    if (candidate == null || memberIds.Contains(candidate.SourcePieceId) ||
                        candidate.IsShredderInteractionLocked || candidate.IsFrozen ||
                        candidate.GridFallView != null && candidate.GridFallView.IsAnimating ||
                        !board.TryGetPieceModel(candidate, out PieceModel model) ||
                        !CanBecomeDynamic(model.State))
                        continue;

                    bool touchesIsland = false;
                    bool hasIndependentSupport = false;
                    for (int cellIndex = 0; cellIndex < model.LocalCells.Count; cellIndex++)
                    {
                        GridCoordinate below = model.GetWorldCell(cellIndex).Offset(Down);
                        if (board.BoardSnapshot.Grid.TryGetOccupantId(below, out int supportId))
                        {
                            if (supportId == model.Id)
                                continue;

                            if (memberIds.Contains(supportId))
                                touchesIsland = true;
                            else
                                hasIndependentSupport = true;
                        }
                        else if (board.BoardSnapshot.Grid.GetCellState(below) == GridCellState.Blocked)
                        {
                            hasIndependentSupport = true;
                        }
                    }

                    if (!touchesIsland || hasIndependentSupport)
                        continue;

                    // If even one dependent member cannot travel through the
                    // physical corridor, do not launch a partial island. A
                    // partial handoff would create an air gap while its still
                    // grid-owned neighbour remains reserved above it.
                    if (!zone.ContainsPhysicsIslandFootprint(board, model) ||
                        !HasClearVerticalRoute(board, model, memberIds))
                    {
                        members.Clear();
                        return false;
                    }

                    members.Add(candidate);
                    memberIds.Add(model.Id);
                    added = true;
                }
            }
            while (added);

            return true;
        }

        private static bool CanBecomeDynamic(PieceState state)
        {
            // A gravity target is committed before its visual tween finishes.
            // Starting a dynamic body from that projected model pose is what
            // made pieces begin a physics handoff already interpenetrating.
            return state == PieceState.Placed;
        }

        private static bool HasClearVerticalRoute(
            PrototypeBoard board,
            PieceModel model,
            HashSet<int> islandMemberIds)
        {
            for (int cellIndex = 0; cellIndex < model.LocalCells.Count; cellIndex++)
            {
                GridCoordinate source = model.GetWorldCell(cellIndex);
                for (int y = source.Y - 1; y >= 0; y--)
                {
                    GridCoordinate routeCell = new GridCoordinate(source.X, y);
                    if (board.BoardSnapshot.Grid.TryGetOccupantId(routeCell, out int occupantId))
                    {
                        if (occupantId != model.Id && !islandMemberIds.Contains(occupantId))
                            return false;

                        continue;
                    }

                    GridCellState state = board.BoardSnapshot.Grid.GetCellState(routeCell);
                    if (state == GridCellState.Blocked ||
                        state == GridCellState.Occupied ||
                        state == GridCellState.Reserved)
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Owns only temporary Unity physics for support-chain pieces. Board cells
    /// remain reserved until each member's coordinate-based cutter capture.
    /// </summary>
    internal sealed class ShredderPhysicsIsland
    {
        private readonly BlockShredder owner;
        private readonly ShredderConfig config;
        private readonly ShredderCatchZone zone;
        private readonly List<PuzzlePiece> dynamicMembers;
        private float elapsed;
        private bool stallWarningReported;

        public bool IsComplete => dynamicMembers.Count == 0;
        public ShredderCatchZone Zone => zone;

        public ShredderPhysicsIsland(
            BlockShredder owner,
            ShredderConfig config,
            ShredderCatchZone zone,
            List<PuzzlePiece> members)
        {
            this.owner = owner;
            this.config = config;
            this.zone = zone;
            dynamicMembers = new List<PuzzlePiece>(Mathf.Max(0, members.Count - 1));

            // Index zero is the kinematic root that the existing feed motor
            // owns. Every remaining member is a Dynamic body until it reaches
            // the cutter capture plane.
            for (int index = 1; index < members.Count; index++)
            {
                PuzzlePiece member = members[index];
                if (member == null)
                    continue;

                member.EnterShredderSupportPhysics(config);
                dynamicMembers.Add(member);
            }

            Physics2D.SyncTransforms();
        }

        public void FixedTick()
        {
            elapsed += Time.fixedDeltaTime;
            int captureIndex = -1;
            float lowestY = float.PositiveInfinity;
            int lowestId = int.MaxValue;

            for (int index = dynamicMembers.Count - 1; index >= 0; index--)
            {
                PuzzlePiece member = dynamicMembers[index];
                if (member == null || !member.gameObject.activeInHierarchy)
                {
                    dynamicMembers.RemoveAt(index);
                    continue;
                }

                if (zone == null || config == null ||
                    !zone.ContainsPhysicsIslandCaptureFootprint(
                        member,
                        config.PhysicsIslandCaptureDistance))
                    continue;

                if (!zone.TryGetPhysicsIslandCaptureFloorY(member, out float memberLowestY))
                    continue;

                bool isLower = memberLowestY < lowestY - .0001f;
                bool breaksTie = Mathf.Abs(memberLowestY - lowestY) <= .0001f &&
                                 member.SourcePieceId < lowestId;
                if (!isLower && !breaksTie)
                    continue;

                captureIndex = index;
                lowestY = memberLowestY;
                lowestId = member.SourcePieceId;
            }

            // A member may enter only when its real horizontal material
            // footprint has no active-feed conflict. Leave a conflicting
            // member on the real throat shelf/contacts; never force an
            // out-of-mouth member into Shredding.
            if (captureIndex >= 0 &&
                owner.TryCapturePhysicsIslandMember(dynamicMembers[captureIndex], zone))
            {
                dynamicMembers.RemoveAt(captureIndex);
            }

            if (!stallWarningReported && config != null &&
                elapsed >= config.PhysicsIslandMaximumDuration && dynamicMembers.Count > 0)
            {
                stallWarningReported = true;
                Debug.LogWarning(
                    "[ShredderPhysicsIsland] A support chain remains physically blocked after the configured observation window. " +
                    "It stays outside the shredder feed until it reaches the real mouth gate.");
            }
        }
    }
}
