using System.Collections.Generic;
using GravityPuzzle.Infrastructure.Services;
using NUnit.Framework;
using UnityEngine;

namespace GravityPuzzle.Tests.Editor
{
    public sealed class CampaignProgressTests
    {
        private sealed class MemoryCampaignProgressPersistence : ICampaignProgressPersistence
        {
            public bool HasValue { get; private set; }
            public int StoredLevelIndex { get; private set; }
            public int SaveCallCount { get; private set; }

            public bool TryLoadLevelIndex(out int levelIndex)
            {
                levelIndex = StoredLevelIndex;
                return HasValue && StoredLevelIndex >= 0;
            }

            public void SaveLevelIndex(int levelIndex)
            {
                if (levelIndex < 0)
                    return;

                HasValue = true;
                StoredLevelIndex = levelIndex;
                SaveCallCount++;
            }

            public void Clear()
            {
                HasValue = false;
                StoredLevelIndex = 0;
            }
        }

        private MemoryCampaignProgressPersistence persistence;
        private GravityLevelSequence sequence;
        private readonly List<GravityLevelDefinition> createdLevels = new List<GravityLevelDefinition>();

        [SetUp]
        public void SetUp()
        {
            persistence = new MemoryCampaignProgressPersistence();
            CampaignProgressRuntime.Configure(persistence);
            GravityLevelRuntime.ResetCampaignState();

            sequence = ScriptableObject.CreateInstance<GravityLevelSequence>();
            for (int index = 0; index < 5; index++)
            {
                GravityLevelDefinition level = ScriptableObject.CreateInstance<GravityLevelDefinition>();
                level.name = $"TestLevel_{index + 1}";
                createdLevels.Add(level);
                sequence.levels.Add(level);
            }

            GravityLevelRuntime.ConfigureLevelSequence(sequence);
        }

        [TearDown]
        public void TearDown()
        {
            GravityLevelRuntime.ResetCampaignState();
            CampaignProgressRuntime.ResetToDefault();

            for (int index = 0; index < createdLevels.Count; index++)
            {
                if (createdLevels[index] != null)
                    Object.DestroyImmediate(createdLevels[index]);
            }
            createdLevels.Clear();

            if (sequence != null)
            {
                Object.DestroyImmediate(sequence);
                sequence = null;
            }
        }

        [Test]
        public void FindLevelToPlay_DefaultsToLevel1_AndRestoresSavedIndexAfterProcessReset()
        {
            GravityLevelDefinition initial = GravityLevelRuntime.FindLevelToPlay();
            Assert.That(initial, Is.SameAs(createdLevels[0]));
            Assert.That(GravityLevelRuntime.CurrentLevelNumber, Is.EqualTo(1));

            Assert.That(GravityLevelRuntime.TryAdvanceToNextLevel(), Is.True);
            Assert.That(GravityLevelRuntime.TryAdvanceToNextLevel(), Is.True);
            Assert.That(GravityLevelRuntime.CurrentLevelNumber, Is.EqualTo(3));
            Assert.That(persistence.HasValue, Is.True);
            Assert.That(persistence.StoredLevelIndex, Is.EqualTo(2));

            // Simulate closing and reopening the game.
            GravityLevelRuntime.ResetCampaignState();
            GravityLevelRuntime.ConfigureLevelSequence(sequence);

            GravityLevelDefinition resumed = GravityLevelRuntime.FindLevelToPlay();
            Assert.That(resumed, Is.SameAs(createdLevels[2]));
            Assert.That(GravityLevelRuntime.CurrentLevelNumber, Is.EqualTo(3));
        }

        [Test]
        public void SaveCompletedLevelProgression_PersistsNextLevelWithoutMutatingWinScreenLevelNumber()
        {
            GravityLevelRuntime.FindLevelToPlay();
            GravityLevelRuntime.TryAdvanceToNextLevel(); // Level 2 (index 1)
            Assert.That(GravityLevelRuntime.CurrentLevelNumber, Is.EqualTo(2));

            GravityLevelRuntime.SaveCompletedLevelProgression();

            // Active WinPanel still sees Level 2 while persistence advances to Level 3 (index 2).
            Assert.That(GravityLevelRuntime.CurrentLevelNumber, Is.EqualTo(2));
            Assert.That(persistence.StoredLevelIndex, Is.EqualTo(2));

            GravityLevelRuntime.ResetCampaignState();
            GravityLevelRuntime.ConfigureLevelSequence(sequence);

            GravityLevelDefinition resumed = GravityLevelRuntime.FindLevelToPlay();
            Assert.That(resumed, Is.SameAs(createdLevels[2]));
            Assert.That(GravityLevelRuntime.CurrentLevelNumber, Is.EqualTo(3));
        }

        [Test]
        public void FindLevelToPlay_ClampsOutOfRangeSavedIndex()
        {
            persistence.SaveLevelIndex(99);

            GravityLevelDefinition resumed = GravityLevelRuntime.FindLevelToPlay();
            Assert.That(resumed, Is.SameAs(createdLevels[4]));
            Assert.That(GravityLevelRuntime.CurrentLevelNumber, Is.EqualTo(5));
        }

        [Test]
        public void TryStepLevel_BeforeFindLevelToPlay_LoadsSavedProgressBeforeStepping()
        {
            persistence.SaveLevelIndex(2); // Level 3

            Assert.That(GravityLevelRuntime.TryAdvanceToNextLevel(), Is.True);
            Assert.That(GravityLevelRuntime.CurrentLevelNumber, Is.EqualTo(4));
            Assert.That(persistence.StoredLevelIndex, Is.EqualTo(3));
        }

        [Test]
        public void RequestRestart_RealignsSavedProgressToCurrentLevel()
        {
            GravityLevelRuntime.FindLevelToPlay();
            GravityLevelRuntime.TryAdvanceToNextLevel(); // Level 2 (index 1)
            GravityLevelRuntime.SaveCompletedLevelProgression(); // Saved index 2 (Level 3)

            GravityLevelRuntime.RequestRestart(); // Retrying Level 2 restores saved index 1.

            Assert.That(persistence.StoredLevelIndex, Is.EqualTo(1));
        }
    }
}
