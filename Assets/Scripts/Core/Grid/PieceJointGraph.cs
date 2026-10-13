using System;
using System.Collections.Generic;

namespace GravityPuzzle.Core.Grid
{
    /// <summary>
    /// Runtime ownership of removable piece-to-piece edges. It contains no
    /// Unity presentation state; callers query a member's connected component
    /// to make drag and gravity decisions through the authoritative grid.
    /// </summary>
    public sealed class PieceJointGraph
    {
        public readonly struct Joint
        {
            public Joint(int id, int memberA, int memberB, GridCoordinate blockA, GridCoordinate blockB)
            {
                Id = id;
                MemberA = memberA;
                MemberB = memberB;
                BlockA = blockA;
                BlockB = blockB;
            }

            public int Id { get; }
            public int MemberA { get; }
            public int MemberB { get; }
            public GridCoordinate BlockA { get; }
            public GridCoordinate BlockB { get; }

            public bool ContainsMember(int memberId)
            {
                return MemberA == memberId || MemberB == memberId;
            }

            public bool ContainsEndpoint(int memberId, GridCoordinate block)
            {
                return MemberA == memberId && BlockA.Equals(block) ||
                       MemberB == memberId && BlockB.Equals(block);
            }
        }

        private readonly List<Joint> joints = new List<Joint>();
        private readonly List<int> registeredMembers = new List<int>();
        private readonly Dictionary<int, List<int>> membersByMember = new Dictionary<int, List<int>>();
        private readonly Dictionary<int, List<int>> neighborsByMember = new Dictionary<int, List<int>>();
        private readonly Queue<int> pendingMembers = new Queue<int>();
        private readonly HashSet<int> visitedMembers = new HashSet<int>();
        private bool groupsDirty = true;

        public int JointCount => joints.Count;

        public void RegisterMember(int memberId)
        {
            int countBefore = registeredMembers.Count;
            AddRegisteredMember(memberId);
            if (registeredMembers.Count != countBefore)
                groupsDirty = true;
        }

        public void Configure(IReadOnlyList<int> members, IReadOnlyList<Joint> initialJoints)
        {
            registeredMembers.Clear();
            joints.Clear();
            if (members != null)
            {
                for (int index = 0; index < members.Count; index++)
                    AddRegisteredMember(members[index]);
            }

            if (initialJoints != null)
            {
                for (int index = 0; index < initialJoints.Count; index++)
                    TryAddJoint(initialJoints[index]);
            }

            groupsDirty = true;
        }

        public bool TryAddJoint(Joint joint)
        {
            if (joint.MemberA == joint.MemberB ||
                !ContainsRegisteredMember(joint.MemberA) ||
                !ContainsRegisteredMember(joint.MemberB) ||
                ContainsJoint(joint.Id))
            {
                return false;
            }

            joints.Add(joint);
            groupsDirty = true;
            return true;
        }

        public int RemoveJointsForMember(int memberId)
        {
            int removed = 0;
            for (int index = joints.Count - 1; index >= 0; index--)
            {
                if (!joints[index].ContainsMember(memberId))
                    continue;

                joints.RemoveAt(index);
                removed++;
            }

            if (removed > 0)
                groupsDirty = true;
            return removed;
        }

        public int RemoveJointsAtEndpoint(int memberId, GridCoordinate block)
        {
            int removed = 0;
            for (int index = joints.Count - 1; index >= 0; index--)
            {
                if (!joints[index].ContainsEndpoint(memberId, block))
                    continue;

                joints.RemoveAt(index);
                removed++;
            }

            if (removed > 0)
                groupsDirty = true;
            return removed;
        }

        /// <summary>
        /// Removes previously resolved edges by their stable identity. Hammer
        /// captures these ids before it changes a piece's topology, then
        /// commits the removal only after that topology transaction succeeds.
        /// </summary>
        public int RemoveJointsById(IReadOnlyList<int> jointIds)
        {
            if (jointIds == null || jointIds.Count == 0)
                return 0;

            int removed = 0;
            for (int index = joints.Count - 1; index >= 0; index--)
            {
                int jointId = joints[index].Id;
                bool shouldRemove = false;
                for (int idIndex = 0; idIndex < jointIds.Count; idIndex++)
                {
                    if (jointIds[idIndex] != jointId)
                        continue;

                    shouldRemove = true;
                    break;
                }

                if (!shouldRemove)
                    continue;

                joints.RemoveAt(index);
                removed++;
            }

            if (removed > 0)
                groupsDirty = true;
            return removed;
        }

        /// <summary>
        /// Transfers a surviving endpoint to a hammer-created fragment. If no
        /// fragment still owns the endpoint's complete block, the edge is
        /// removed instead. This preserves graph topology without giving a
        /// runtime fragment an authored piece identity.
        /// </summary>
        public void TransferEndpoint(
            int previousMemberId,
            GridCoordinate previousBlock,
            int nextMemberId,
            GridCoordinate nextBlock,
            bool endpointSurvived)
        {
            bool changed = false;
            for (int index = joints.Count - 1; index >= 0; index--)
            {
                Joint joint = joints[index];
                bool matchesA = joint.MemberA == previousMemberId && joint.BlockA.Equals(previousBlock);
                bool matchesB = joint.MemberB == previousMemberId && joint.BlockB.Equals(previousBlock);
                if (!matchesA && !matchesB)
                    continue;

                if (!endpointSurvived)
                {
                    joints.RemoveAt(index);
                    changed = true;
                    continue;
                }

                AddRegisteredMember(nextMemberId);
                joints[index] = matchesA
                    ? new Joint(joint.Id, nextMemberId, joint.MemberB, nextBlock, joint.BlockB)
                    : new Joint(joint.Id, joint.MemberA, nextMemberId, joint.BlockA, nextBlock);
                changed = true;
            }

            if (changed)
                groupsDirty = true;
        }

        public void CollectGroupMembers(int memberId, List<int> results)
        {
            if (results == null)
                throw new ArgumentNullException(nameof(results));

            results.Clear();
            EnsureGroups();
            if (!membersByMember.TryGetValue(memberId, out List<int> members))
                return;

            results.AddRange(members);
        }

        public bool AreInSameGroup(int firstMemberId, int secondMemberId)
        {
            EnsureGroups();
            return membersByMember.TryGetValue(firstMemberId, out List<int> firstGroup) &&
                   membersByMember.TryGetValue(secondMemberId, out List<int> secondGroup) &&
                   ReferenceEquals(firstGroup, secondGroup);
        }

        public void CollectJoints(List<Joint> results)
        {
            if (results == null)
                throw new ArgumentNullException(nameof(results));

            results.Clear();
            results.AddRange(joints);
        }

        private void EnsureGroups()
        {
            if (!groupsDirty)
                return;

            membersByMember.Clear();
            neighborsByMember.Clear();
            for (int index = 0; index < registeredMembers.Count; index++)
                neighborsByMember.Add(registeredMembers[index], new List<int>());

            for (int index = 0; index < joints.Count; index++)
            {
                Joint joint = joints[index];
                neighborsByMember[joint.MemberA].Add(joint.MemberB);
                neighborsByMember[joint.MemberB].Add(joint.MemberA);
            }

            visitedMembers.Clear();
            for (int index = 0; index < registeredMembers.Count; index++)
            {
                int member = registeredMembers[index];
                if (!visitedMembers.Add(member))
                    continue;

                List<int> group = new List<int>();
                pendingMembers.Enqueue(member);
                while (pendingMembers.Count > 0)
                {
                    int current = pendingMembers.Dequeue();
                    group.Add(current);
                    List<int> neighbors = neighborsByMember[current];
                    for (int neighborIndex = 0; neighborIndex < neighbors.Count; neighborIndex++)
                    {
                        int neighbor = neighbors[neighborIndex];
                        if (visitedMembers.Add(neighbor))
                            pendingMembers.Enqueue(neighbor);
                    }
                }

                group.Sort();
                for (int memberIndex = 0; memberIndex < group.Count; memberIndex++)
                    membersByMember.Add(group[memberIndex], group);
            }

            groupsDirty = false;
        }

        private void AddRegisteredMember(int memberId)
        {
            if (!ContainsRegisteredMember(memberId))
                registeredMembers.Add(memberId);
        }

        private bool ContainsRegisteredMember(int memberId)
        {
            for (int index = 0; index < registeredMembers.Count; index++)
            {
                if (registeredMembers[index] == memberId)
                    return true;
            }

            return false;
        }

        private bool ContainsJoint(int jointId)
        {
            for (int index = 0; index < joints.Count; index++)
            {
                if (joints[index].Id == jointId)
                    return true;
            }

            return false;
        }
    }
}
