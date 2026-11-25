using System;
using System.Collections.Generic;

namespace GravityPuzzle.Core.Grid
{
    /// <summary>
    /// Generates deterministic canonical coarse-block alignment candidates for
    /// piece release snapping without allocating or mutating board state.
    /// </summary>
    public static class GridReleaseSnapPlanner
    {
        public static bool IsCanonicalCoordinate(int value, int alignmentStride)
        {
            return alignmentStride <= 1 ||
                   ((value % alignmentStride) + alignmentStride) % alignmentStride == 0;
        }

        public static bool IsCanonicalAnchor(GridCoordinate anchor, int alignmentStride)
        {
            return IsCanonicalCoordinate(anchor.X, alignmentStride) &&
                   IsCanonicalCoordinate(anchor.Y, alignmentStride);
        }

        public static int FloorToMultiple(int value, int alignmentStride)
        {
            if (alignmentStride <= 1)
                return value;

            int quotient = value / alignmentStride;
            if ((value ^ alignmentStride) < 0 && value % alignmentStride != 0)
                quotient--;

            return quotient * alignmentStride;
        }

        public static int CeilToMultiple(int value, int alignmentStride)
        {
            if (alignmentStride <= 1)
                return value;

            int floor = FloorToMultiple(value, alignmentStride);
            return floor == value ? floor : floor + alignmentStride;
        }

        public static int RoundDeltaAligned(
            int targetValue,
            int canonicalReference,
            int alignmentStride)
        {
            if (alignmentStride <= 1)
                return targetValue;

            // Round relative to a canonicalized reference using ToEven midpoint parity
            // so odd coarse-block starts (e.g., X=4 dragged +6 -> +12) preserve symmetric
            // snap thresholds while still enforcing Target % alignmentStride == 0.
            int steps = (int)Math.Round(
                (double)(targetValue - canonicalReference) / alignmentStride,
                MidpointRounding.ToEven);
            return canonicalReference + steps * alignmentStride;
        }

        public static GridCoordinate RoundToCanonicalAnchor(
            GridCoordinate targetAnchor,
            GridCoordinate referenceAnchor,
            int alignmentStride)
        {
            if (alignmentStride <= 1)
                return targetAnchor;

            int canonicalRefX = RoundDeltaAligned(referenceAnchor.X, 0, alignmentStride);
            int canonicalRefY = RoundDeltaAligned(referenceAnchor.Y, 0, alignmentStride);
            return new GridCoordinate(
                RoundDeltaAligned(targetAnchor.X, canonicalRefX, alignmentStride),
                RoundDeltaAligned(targetAnchor.Y, canonicalRefY, alignmentStride));
        }

        /// <summary>
        /// Populates <paramref name="candidates"/> with unique canonical anchors ordered from
        /// the closest preferred snap target outward across surrounding canonical corners,
        /// current-anchor fallback corners, drag history, and a local 5x5 coarse lattice.
        /// </summary>
        public static void CollectCanonicalCandidates(
            GridCoordinate visualAnchor,
            GridCoordinate committedAnchor,
            bool hasLastCanonicalAnchor,
            GridCoordinate lastCanonicalAnchor,
            bool hasStartAnchor,
            GridCoordinate startAnchor,
            int alignmentStride,
            List<GridCoordinate> candidates)
        {
            if (candidates == null)
                return;

            candidates.Clear();
            int stride = alignmentStride <= 1 ? 1 : alignmentStride;
            if (stride == 1)
            {
                candidates.Add(visualAnchor);
                AddUnique(candidates, committedAnchor);
                if (hasLastCanonicalAnchor)
                    AddUnique(candidates, lastCanonicalAnchor);
                if (hasStartAnchor)
                    AddUnique(candidates, startAnchor);
                return;
            }

            GridCoordinate canonicalReference = hasStartAnchor
                ? RoundToCanonicalAnchor(startAnchor, default, stride)
                : RoundToCanonicalAnchor(committedAnchor, default, stride);

            GridCoordinate primary = RoundToCanonicalAnchor(visualAnchor, canonicalReference, stride);
            candidates.Add(primary);

            int cornerBatchStart = candidates.Count;
            AddEnclosingCanonicalCorners(visualAnchor, stride, candidates);
            SortSliceByDistanceSquared(candidates, cornerBatchStart, visualAnchor, committedAnchor);

            int committedBatchStart = candidates.Count;
            AddEnclosingCanonicalCorners(committedAnchor, stride, candidates);
            SortSliceByDistanceSquared(candidates, committedBatchStart, visualAnchor, committedAnchor);

            if (hasLastCanonicalAnchor && IsCanonicalAnchor(lastCanonicalAnchor, stride))
                AddUnique(candidates, lastCanonicalAnchor);

            if (hasStartAnchor)
                AddUnique(candidates, canonicalReference);

            int latticeBatchStart = candidates.Count;
            for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                GridCoordinate latticeCandidate = new GridCoordinate(
                    primary.X + dx * stride,
                    primary.Y + dy * stride);
                AddUnique(candidates, latticeCandidate);
            }

            SortSliceByDistanceSquared(candidates, latticeBatchStart, visualAnchor, committedAnchor);
        }

        private static void AddEnclosingCanonicalCorners(
            GridCoordinate anchor,
            int stride,
            List<GridCoordinate> candidates)
        {
            int x0 = FloorToMultiple(anchor.X, stride);
            int x1 = x0 + stride;
            int y0 = FloorToMultiple(anchor.Y, stride);
            int y1 = y0 + stride;

            AddUnique(candidates, new GridCoordinate(x0, y0));
            AddUnique(candidates, new GridCoordinate(x1, y0));
            AddUnique(candidates, new GridCoordinate(x0, y1));
            AddUnique(candidates, new GridCoordinate(x1, y1));
        }

        private static void AddUnique(List<GridCoordinate> candidates, GridCoordinate candidate)
        {
            for (int index = 0; index < candidates.Count; index++)
            {
                if (candidates[index].Equals(candidate))
                    return;
            }

            candidates.Add(candidate);
        }

        private static void SortSliceByDistanceSquared(
            List<GridCoordinate> candidates,
            int startIndex,
            GridCoordinate primaryTarget,
            GridCoordinate secondaryTieBreaker)
        {
            for (int i = startIndex + 1; i < candidates.Count; i++)
            {
                GridCoordinate key = candidates[i];
                long keyPrimaryDist = DistanceSquared(key, primaryTarget);
                long keySecondaryDist = DistanceSquared(key, secondaryTieBreaker);
                int j = i - 1;
                while (j >= startIndex)
                {
                    GridCoordinate current = candidates[j];
                    long currentPrimaryDist = DistanceSquared(current, primaryTarget);
                    bool shouldMove = keyPrimaryDist < currentPrimaryDist ||
                        (keyPrimaryDist == currentPrimaryDist &&
                         (keySecondaryDist < DistanceSquared(current, secondaryTieBreaker) ||
                          (keySecondaryDist == DistanceSquared(current, secondaryTieBreaker) &&
                           CompareCoordinate(key, current) < 0)));
                    if (!shouldMove)
                        break;

                    candidates[j + 1] = current;
                    j--;
                }

                candidates[j + 1] = key;
            }
        }

        private static long DistanceSquared(GridCoordinate a, GridCoordinate b)
        {
            long dx = a.X - b.X;
            long dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        private static int CompareCoordinate(GridCoordinate a, GridCoordinate b)
        {
            int yOrder = a.Y.CompareTo(b.Y);
            return yOrder != 0 ? yOrder : a.X.CompareTo(b.X);
        }
    }
}
