using GravityPuzzle.Bootstrap;
using GravityPuzzle.Config;
using GravityPuzzle.Infrastructure.Services;
using GravityPuzzle.Presentation.Views;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GravityPuzzle.Tests.Editor
{
    public sealed class FeatureMilestoneTests
    {
        [TestCase(4, "Freeze_Timer", true)]
        [TestCase(5, "CombinedBlock", false)]
        [TestCase(6, "Hammer", true)]
        [TestCase(8, "Ice_block", false)]
        [TestCase(10, "Rocket", true)]
        [TestCase(16, "BombBlock", false)]
        [TestCase(23, "Box", false)]
        [TestCase(32, "Elevator", false)]
        [TestCase(53, "KeyLock", false)]
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
        [TestCase(24)]
        [TestCase(25)]
        [TestCase(33)]
        [TestCase(54)]
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
            Assert.That(sequence.levels.Count, Is.GreaterThanOrEqualTo(53));

            Assert.That(sequence.levels[4]?.name, Is.EqualTo("L5"));
            Assert.That(sequence.levels[9]?.name, Is.EqualTo("rocket_intrp"));
            Assert.That(sequence.levels[15]?.name, Is.EqualTo("L16"));
            Assert.That(sequence.levels[22]?.name, Is.EqualTo("box_level"));
            Assert.That(sequence.levels[31]?.name, Is.EqualTo("elevator_level"));
            Assert.That(sequence.levels[52]?.name, Is.EqualTo("anahtar_kilit"));
        }

        [Test]
        public void RocketIntroLevel_HasAuthoredRocketTutorialTargetPiece()
        {
            GravityLevelDefinition level = AssetDatabase.LoadAssetAtPath<GravityLevelDefinition>(
                "Assets/rocket_intrp.asset");
            BoosterRewardConfig rocketConfig = AssetDatabase.LoadAssetAtPath<BoosterRewardConfig>(
                "Assets/Config/Boosters/RocketBoosterRewardConfig.asset");

            Assert.That(level, Is.Not.Null);
            Assert.That(rocketConfig, Is.Not.Null);
            Assert.That(rocketConfig.PresentationLevel, Is.EqualTo(10));
            Assert.That(string.IsNullOrWhiteSpace(rocketConfig.TutorialTargetPieceId), Is.False);

            bool foundTarget = false;
            for (int i = 0; i < level.pieces.Count; i++)
            {
                if (level.pieces[i] != null &&
                    level.pieces[i].tutorialTargetId == rocketConfig.TutorialTargetPieceId)
                {
                    foundTarget = true;
                    break;
                }
            }

            Assert.That(foundTarget, Is.True, "Level 10 (rocket_intrp) must tag a piece with rocket_intro_target.");
        }

        [Test]
        public void BoosterInventoryRuntime_PreservesCountChangedListenersAcrossConfigure()
        {
            int observedCount = -1;
            BoosterRewardType observedType = default;
            void OnCountChanged(BoosterRewardType type, int count)
            {
                observedType = type;
                observedCount = count;
            }

            BoosterInventoryRuntime.Current.CountChanged += OnCountChanged;
            try
            {
                var replacement = new StubBoosterInventory();
                BoosterInventoryRuntime.Configure(replacement);
                BoosterInventoryRuntime.Current.Grant(BoosterRewardType.FreezeTimer, 1);

                Assert.That(observedType, Is.EqualTo(BoosterRewardType.FreezeTimer));
                Assert.That(observedCount, Is.EqualTo(1));
            }
            finally
            {
                BoosterInventoryRuntime.Current.CountChanged -= OnCountChanged;
                BoosterInventoryRuntime.Configure(new PlayerPrefsBoosterInventory());
            }
        }

        [TestCase(LevelDifficulty.Normal, "Normal")]
        [TestCase(LevelDifficulty.Hard, "Hard")]
        [TestCase(LevelDifficulty.VeryHard, "Very Hard")]
        public void FormatDifficultyLabel_MapsAllEnumValues(LevelDifficulty difficulty, string expected)
        {
            Assert.That(
                RuntimePieceFactoryBootstrap.FormatDifficultyLabel(difficulty),
                Is.EqualTo(expected));
        }

        [Test]
        public void TimeBoosterButtonPrefab_UsesTenSecondFreezeDuration()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/MainPrefabs/UI_Booster/Time_Booster_Button.prefab");
            Assert.That(prefab, Is.Not.Null);

            FreezeTimerBooster[] freezeBoosters = prefab.GetComponentsInChildren<FreezeTimerBooster>(true);
            Assert.That(freezeBoosters.Length, Is.GreaterThan(0));
            for (int i = 0; i < freezeBoosters.Length; i++)
                Assert.That(freezeBoosters[i].freezeDuration, Is.EqualTo(10f));
        }

        [Test]
        public void SoundFeedbackConfig_HasAllGameplaySoundCuesAssigned()
        {
            SoundFeedbackConfig config = AssetDatabase.LoadAssetAtPath<SoundFeedbackConfig>(
                "Assets/Config/Presentation/SoundFeedbackConfig.asset");
            Assert.That(config, Is.Not.Null);
            Assert.That(config.BarFillClips, Is.Not.Null.And.Not.Empty);
            Assert.That(config.BarFillClips[0], Is.Not.Null);
            Assert.That(config.ClockTickingClip, Is.Not.Null);
            Assert.That(config.FreezeTimerImpactClips, Is.Not.Null.And.Not.Empty);
            Assert.That(config.FreezeTimerImpactClips[0], Is.Not.Null);
            Assert.That(config.RocketLaunchClips, Is.Not.Null.And.Not.Empty);
            Assert.That(config.RocketLaunchClips[0], Is.Not.Null);
            Assert.That(config.ElevatorOpenClips, Is.Not.Null.And.Not.Empty);
            Assert.That(config.ElevatorOpenClips[0], Is.Not.Null);
            Assert.That(config.BoxCounterStepClips, Is.Not.Null.And.Not.Empty);
            Assert.That(config.BoxCounterStepClips[0], Is.Not.Null);
            Assert.That(config.BoxBreakClips, Is.Not.Null.And.Not.Empty);
            Assert.That(config.BoxBreakClips[0], Is.Not.Null);
        }

        private sealed class StubBoosterInventory : IPlayerBoosterInventory
        {
            private int freezeCount;
            public event System.Action<BoosterRewardType, int> CountChanged;

            public int GetCount(BoosterRewardType boosterType) =>
                boosterType == BoosterRewardType.FreezeTimer ? freezeCount : 0;

            public bool IsUnlocked(BoosterRewardType boosterType) => freezeCount > 0;

            public void Grant(BoosterRewardType boosterType, int amount)
            {
                if (boosterType == BoosterRewardType.FreezeTimer)
                    freezeCount += amount;
                CountChanged?.Invoke(boosterType, GetCount(boosterType));
            }

            public bool TryConsume(BoosterRewardType boosterType) => false;
        }
    }
}
