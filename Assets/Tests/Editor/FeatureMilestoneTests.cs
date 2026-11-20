using GravityPuzzle.Presentation.Views;
using NUnit.Framework;
using UnityEditor;

namespace GravityPuzzle.Tests.Editor
{
    public sealed class FeatureMilestoneTests
    {
        [TestCase(4, "Freeze_Timer", true)]
        [TestCase(6, "Hammer", true)]
        [TestCase(8, "Ice_block", false)]
        [TestCase(10, "Rocket", true)]
        [TestCase(16, "BombBlock", false)]
        [TestCase(24, "Box", false)]
        [TestCase(33, "Elevator", false)]
        [TestCase(54, "KeyLock", false)]
        public void GetFeatureTargetNameForLevel_ReturnsExpectedSlotAndKind(
            int levelNumber,
            string expectedTargetName,
            bool expectedIsBooster)
        {
            Assert.That(
                NewBoosterPanelView.GetFeatureTargetNameForLevel(levelNumber),
                Is.EqualTo(expectedTargetName));
            Assert.That(
                NewBoosterPanelView.IsBoosterMilestoneLevel(levelNumber),
                Is.EqualTo(expectedIsBooster));
        }

        [TestCase(1)]
        [TestCase(11)]
        [TestCase(15)]
        [TestCase(18)]
        [TestCase(21)]
        [TestCase(25)]
        [TestCase(26)]
        [TestCase(35)]
        [TestCase(40)]
        public void GetFeatureTargetNameForLevel_ReturnsNullForNonMilestoneLevels(int levelNumber)
        {
            Assert.That(NewBoosterPanelView.GetFeatureTargetNameForLevel(levelNumber), Is.Null);
        }

        [Test]
        public void LevelSequence_ContainsExpectedFeatureIntroductionLevels()
        {
            GravityLevelSequence sequence = AssetDatabase.LoadAssetAtPath<GravityLevelSequence>(
                "Assets/Resources/LevelSequence.asset");
            Assert.That(sequence, Is.Not.Null);
            Assert.That(sequence.levels, Is.Not.Null);
            Assert.That(sequence.levels.Count, Is.GreaterThanOrEqualTo(54));

            Assert.That(sequence.levels[15]?.name, Is.EqualTo("L15"));
            Assert.That(sequence.levels[23]?.name, Is.EqualTo("box_level"));
            Assert.That(sequence.levels[32]?.name, Is.EqualTo("elevator_level"));
            Assert.That(sequence.levels[53]?.name, Is.EqualTo("anahtar_kilit"));
        }
    }
}
