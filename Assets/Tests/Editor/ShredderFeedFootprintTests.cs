using System.Collections.Generic;
using System.Reflection;
using GravityPuzzle.Core.Grid;
using GravityPuzzle.Gameplay.Pieces;
using NUnit.Framework;
using UnityEngine;
using GravityPuzzle.Config;

namespace GravityPuzzle.Tests.Editor
{
    public sealed class ShredderFeedFootprintTests
    {
        [Test]
        public void ProgressVoxelBudget_UsesConfiguredGrainsPerBlockWithoutHiddenBurstMultiplier()
        {
            ShredderConfig config = ScriptableObject.CreateInstance<ShredderConfig>();
            try
            {
                SetPrivateField(config, "progressVoxelMultiplier", 4);

                Assert.That(config.GetProgressVoxelFlightCount(3), Is.EqualTo(12));
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        private static readonly MethodInfo Overlap = typeof(BlockShredder).GetMethod(
            "HaveOverlappingHorizontalFootprints", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo GridOverlap = typeof(BlockShredder).GetMethod(
            "HaveOverlappingGridColumns", BindingFlags.NonPublic | BindingFlags.Static);

        [Test]
        public void AdjacentGridColumnsDoNotConflict()
        {
            Assert.That(GridConflicts(Model(8, 0, 1, 2, 3), Model(12, 0, 1, 2, 3)), Is.False);
        }

        [Test]
        public void SharedGridColumnsConflictRegardlessOfHeight()
        {
            Assert.That(GridConflicts(Model(8, 0, 1, 2, 3), Model(11, 0, 1)), Is.True);
        }

        [Test]
        public void IrregularGridGapRemainsAvailable()
        {
            Assert.That(GridConflicts(Model(0, 0, 2), Model(1, 0)), Is.False);
        }

        [Test]
        public void AdjacentCellsDoNotConflict()
        {
            Assert.That(Conflicts(Cells(0f), Cells(1f)), Is.False);
        }

        [Test]
        public void OverlappingCellsConflict()
        {
            Assert.That(Conflicts(Cells(0f), Cells(.75f)), Is.True);
        }

        [Test]
        public void GapBetweenSeparateCellsDoesNotConflict()
        {
            Assert.That(Conflicts(Cells(0f, 2f), Cells(1f)), Is.False);
        }

        private static List<Bounds> Cells(params float[] centers)
        {
            var bounds = new List<Bounds>(centers.Length);
            foreach (float x in centers)
                bounds.Add(new Bounds(new Vector3(x, 0f, 0f), Vector3.one));
            return bounds;
        }

        private static bool Conflicts(List<Bounds> first, List<Bounds> second)
        {
            Assert.That(Overlap, Is.Not.Null);
            return (bool)Overlap.Invoke(null, new object[] { first, second });
        }

        private static PieceModel Model(int anchorX, params int[] localColumns)
        {
            var cells = new List<GridCoordinate>(localColumns.Length);
            foreach (int x in localColumns)
                cells.Add(new GridCoordinate(x, 0));
            return new PieceModel(1, new GridCoordinate(anchorX, 0), new GridCoordinate(0, 0), cells);
        }

        private static bool GridConflicts(PieceModel first, PieceModel second)
        {
            Assert.That(GridOverlap, Is.Not.Null);
            return (bool)GridOverlap.Invoke(null, new object[] { first, second });
        }

        private static void SetPrivateField<T>(ShredderConfig config, string name, T value)
        {
            FieldInfo field = typeof(ShredderConfig).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(config, value);
        }
    }
}
