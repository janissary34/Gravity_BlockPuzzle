using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using GravityPuzzle.Config;

namespace GravityPuzzle.Tests.Editor
{
    public sealed class ShredderFeedFootprintTests
    {
        [Test]
        public void FeedCeiling_LimitsDropToGapAboveFeedCells()
        {
            var feed = new List<Bounds> { new Bounds(new Vector3(0f, 1f, 0f), Vector3.one) };
            var falling = new List<Bounds> { new Bounds(new Vector3(0f, 3f, 0f), Vector3.one) };
            Assert.That(BlockShredder.ComputeFeedCeilingDrop(falling, feed, 0f), Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void FeedCeiling_DetectsPenetrationAsNegativeGap()
        {
            var feed = new List<Bounds> { new Bounds(new Vector3(0f, 1f, 0f), Vector3.one) };
            // feed top is 1.5. If falling center is 1.8, min is 1.3, so gap is 1.3 - 1.5 = -0.2
            var penetrated = new List<Bounds> { new Bounds(new Vector3(0f, 1.8f, 0f), Vector3.one) };
            Assert.That(BlockShredder.ComputeFeedCeilingDrop(penetrated, feed, 0f), Is.EqualTo(-0.2f).Within(1e-4f));
        }

        [Test]
        public void FeedCeiling_IgnoresConsumedCellsAndNonOverlappingColumns()
        {
            var consumed = new List<Bounds> { new Bounds(new Vector3(0f, 0f, 0f), Vector3.one) };
            var side = new List<Bounds> { new Bounds(new Vector3(5f, 1f, 0f), Vector3.one) };
            var falling = new List<Bounds> { new Bounds(new Vector3(0f, 3f, 0f), Vector3.one) };
            Assert.That(float.IsPositiveInfinity(BlockShredder.ComputeFeedCeilingDrop(falling, consumed, 0.6f)));
            Assert.That(float.IsPositiveInfinity(BlockShredder.ComputeFeedCeilingDrop(falling, side, 0f)));
        }

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

        [Test]
        public void FeedAdmission_DoesNotContainALogicalColumnGate()
        {
            Assert.That(typeof(BlockShredder).GetMethod(
                "TryAcquireFeedGate",
                BindingFlags.NonPublic | BindingFlags.Instance), Is.Null);
        }

        private static void SetPrivateField<T>(ShredderConfig config, string name, T value)
        {
            FieldInfo field = typeof(ShredderConfig).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(config, value);
        }
    }
}
