using System.Reflection;
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
