using System;
using System.Collections.Generic;
using GravityPuzzle.Config;
using GravityPuzzle.Infrastructure.Services;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GravityPuzzle.Tests.Editor
{
    public sealed class BoosterEconomyTests
    {
        private BoosterEconomyConfig LoadAuthoredConfig()
        {
            BoosterEconomyConfig config = AssetDatabase.LoadAssetAtPath<BoosterEconomyConfig>(
                "Assets/Config/Boosters/BoosterEconomyConfig.asset");
            Assert.That(config, Is.Not.Null);
            return config;
        }

        [Test]
        public void AuthoredConfig_HasExpectedUnlockLevelsAndInitialGrants()
        {
            BoosterEconomyConfig config = LoadAuthoredConfig();

            Assert.That(config.GetUnlockLevel(BoosterRewardType.FreezeTimer), Is.EqualTo(4));
            Assert.That(config.GetUnlockLevel(BoosterRewardType.Hammer), Is.EqualTo(6));
            Assert.That(config.GetUnlockLevel(BoosterRewardType.Rocket), Is.EqualTo(10));

            Assert.That(config.GetInitialGrantAmount(BoosterRewardType.FreezeTimer), Is.EqualTo(2));
            Assert.That(config.GetInitialGrantAmount(BoosterRewardType.Hammer), Is.EqualTo(2));
            Assert.That(config.GetInitialGrantAmount(BoosterRewardType.Rocket), Is.EqualTo(2));

            Assert.That(config.IsBoosterUnlockedAtLevel(BoosterRewardType.FreezeTimer, 3), Is.False);
            Assert.That(config.IsBoosterUnlockedAtLevel(BoosterRewardType.FreezeTimer, 4), Is.True);
            Assert.That(config.IsBoosterUnlockedAtLevel(BoosterRewardType.Hammer, 5), Is.False);
            Assert.That(config.IsBoosterUnlockedAtLevel(BoosterRewardType.Hammer, 6), Is.True);
            Assert.That(config.IsBoosterUnlockedAtLevel(BoosterRewardType.Rocket, 9), Is.False);
            Assert.That(config.IsBoosterUnlockedAtLevel(BoosterRewardType.Rocket, 10), Is.True);
        }

        [Test]
        public void FirstClearPeriodicRewards_FollowConfiguredBandsAcrossLevels1To68()
        {
            BoosterEconomyConfig config = LoadAuthoredConfig();
            var inventory = new MemoryInventory();
            var persistence = new MemoryPersistence();
            var service = new BoosterEconomyService(config, inventory, persistence);

            // Levels 1-13: no periodic completion rewards.
            for (int level = 1; level <= 13; level++)
                Assert.That(service.ProcessFirstTimeLevelCompletion(level).Count, Is.EqualTo(0));

            // Band 14-25 (12 levels): Freeze every 5 (L18, L23 -> +2), Hammer every 6 (L19, L25 -> +2), Rocket 0.
            for (int level = 14; level <= 25; level++)
                service.ProcessFirstTimeLevelCompletion(level);

            Assert.That(inventory.GetCount(BoosterRewardType.FreezeTimer), Is.EqualTo(2));
            Assert.That(inventory.GetCount(BoosterRewardType.Hammer), Is.EqualTo(2));
            Assert.That(inventory.GetCount(BoosterRewardType.Rocket), Is.EqualTo(0));

            // Band 26-45 (20 levels): Freeze every 4 (+5), Hammer every 5 (+4), Rocket every 7 (+2).
            for (int level = 26; level <= 45; level++)
                service.ProcessFirstTimeLevelCompletion(level);

            Assert.That(inventory.GetCount(BoosterRewardType.FreezeTimer), Is.EqualTo(7));
            Assert.That(inventory.GetCount(BoosterRewardType.Hammer), Is.EqualTo(6));
            Assert.That(inventory.GetCount(BoosterRewardType.Rocket), Is.EqualTo(2));

            // Band 46-68 (23 levels): Freeze every 3 (+7), Hammer every 4 (+5), Rocket every 6 (+3).
            for (int level = 46; level <= 68; level++)
                service.ProcessFirstTimeLevelCompletion(level);

            Assert.That(inventory.GetCount(BoosterRewardType.FreezeTimer), Is.EqualTo(14));
            Assert.That(inventory.GetCount(BoosterRewardType.Hammer), Is.EqualTo(11));
            Assert.That(inventory.GetCount(BoosterRewardType.Rocket), Is.EqualTo(5));
        }

        [Test]
        public void FullCampaignSimulation_CombinesInitialUnlockGiftsAndPeriodicRewards()
        {
            BoosterEconomyConfig config = LoadAuthoredConfig();
            var inventory = new MemoryInventory();
            var persistence = new MemoryPersistence();
            var service = new BoosterEconomyService(config, inventory, persistence);

            for (int level = 1; level <= 68; level++)
            {
                service.EnsureInitialUnlockGrantsForLevel(level);
                service.ProcessFirstTimeLevelCompletion(level);
            }

            // Initial unlocks (+2 each at L4, L6, L10) + periodic completions (+14 Freeze, +11 Hammer, +5 Rocket).
            Assert.That(inventory.GetCount(BoosterRewardType.FreezeTimer), Is.EqualTo(16));
            Assert.That(inventory.GetCount(BoosterRewardType.Hammer), Is.EqualTo(13));
            Assert.That(inventory.GetCount(BoosterRewardType.Rocket), Is.EqualTo(7));
        }

        [Test]
        public void ReplayingCompletedLevel_DoesNotGrantDuplicateRewards()
        {
            BoosterEconomyConfig config = LoadAuthoredConfig();
            var inventory = new MemoryInventory();
            var persistence = new MemoryPersistence();
            var service = new BoosterEconomyService(config, inventory, persistence);

            // Level 18 is the 5th level in band 14-25 -> +1 FreezeTimer on first clear.
            IReadOnlyList<BoosterEconomyGrant> first = service.ProcessFirstTimeLevelCompletion(18);
            Assert.That(first.Count, Is.EqualTo(1));
            Assert.That(inventory.GetCount(BoosterRewardType.FreezeTimer), Is.EqualTo(1));

            // Replay must grant nothing.
            IReadOnlyList<BoosterEconomyGrant> replay = service.ProcessFirstTimeLevelCompletion(18);
            Assert.That(replay.Count, Is.EqualTo(0));
            Assert.That(inventory.GetCount(BoosterRewardType.FreezeTimer), Is.EqualTo(1));
        }

        [Test]
        public void InitialUnlockGrant_AddsTwoOnceAndNotifiesInventory()
        {
            BoosterEconomyConfig config = LoadAuthoredConfig();
            var inventory = new MemoryInventory();
            var persistence = new MemoryPersistence();
            var service = new BoosterEconomyService(config, inventory, persistence);

            int countChangedCalls = 0;
            inventory.CountChanged += (_, _) => countChangedCalls++;

            IReadOnlyList<BoosterEconomyGrant> first = service.EnsureInitialUnlockGrantsForLevel(4);
            Assert.That(first.Count, Is.EqualTo(1));
            Assert.That(inventory.GetCount(BoosterRewardType.FreezeTimer), Is.EqualTo(2));
            Assert.That(inventory.IsUnlocked(BoosterRewardType.FreezeTimer), Is.True);
            Assert.That(countChangedCalls, Is.EqualTo(1));

            IReadOnlyList<BoosterEconomyGrant> second = service.EnsureInitialUnlockGrantsForLevel(4);
            Assert.That(second.Count, Is.EqualTo(0));
            Assert.That(inventory.GetCount(BoosterRewardType.FreezeTimer), Is.EqualTo(2));
            Assert.That(countChangedCalls, Is.EqualTo(1));
        }

        [Test]
        public void CustomConfigValues_DriveUnlockAndPeriodicRulesWithoutCodeChanges()
        {
            BoosterEconomyConfig custom = ScriptableObject.CreateInstance<BoosterEconomyConfig>();
            try
            {
                custom.ConfigureForTesting(
                    new[]
                    {
                        new BoosterEconomyConfig.BoosterUnlockRule
                        {
                            boosterType = BoosterRewardType.FreezeTimer,
                            unlockLevel = 5,
                            initialGrantAmount = 3
                        }
                    },
                    new[]
                    {
                        new BoosterEconomyConfig.PeriodicRewardBand
                        {
                            bandName = "Custom",
                            minLevel = 10,
                            maxLevel = 12,
                            grants = new[]
                            {
                                new BoosterEconomyConfig.PeriodicBoosterGrant
                                {
                                    boosterType = BoosterRewardType.Rocket,
                                    everyNLevels = 2,
                                    grantAmount = 2
                                }
                            }
                        }
                    });

                var inventory = new MemoryInventory();
                var persistence = new MemoryPersistence();
                var service = new BoosterEconomyService(custom, inventory, persistence);

                Assert.That(service.GetUnlockLevel(BoosterRewardType.FreezeTimer), Is.EqualTo(5));
                Assert.That(service.IsBoosterUnlockedAtLevel(BoosterRewardType.FreezeTimer, 4), Is.False);
                Assert.That(service.IsBoosterUnlockedAtLevel(BoosterRewardType.FreezeTimer, 5), Is.True);

                service.EnsureInitialUnlockGrantsForLevel(5);
                Assert.That(inventory.GetCount(BoosterRewardType.FreezeTimer), Is.EqualTo(3));

                service.ProcessFirstTimeLevelCompletion(10);
                Assert.That(inventory.GetCount(BoosterRewardType.Rocket), Is.EqualTo(0));

                service.ProcessFirstTimeLevelCompletion(11);
                Assert.That(inventory.GetCount(BoosterRewardType.Rocket), Is.EqualTo(2));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(custom);
            }
        }

        private sealed class MemoryInventory : IPlayerBoosterInventory
        {
            private readonly Dictionary<BoosterRewardType, int> counts = new Dictionary<BoosterRewardType, int>();
            private readonly HashSet<BoosterRewardType> unlocked = new HashSet<BoosterRewardType>();

            public event Action<BoosterRewardType, int> CountChanged;

            public int GetCount(BoosterRewardType boosterType) =>
                counts.TryGetValue(boosterType, out int count) ? count : 0;

            public bool IsUnlocked(BoosterRewardType boosterType) => unlocked.Contains(boosterType);

            public void Grant(BoosterRewardType boosterType, int amount)
            {
                if (amount <= 0)
                    return;

                int next = GetCount(boosterType) + amount;
                counts[boosterType] = next;
                unlocked.Add(boosterType);
                CountChanged?.Invoke(boosterType, next);
            }

            public bool TryConsume(BoosterRewardType boosterType)
            {
                int current = GetCount(boosterType);
                if (current <= 0)
                    return false;

                counts[boosterType] = current - 1;
                CountChanged?.Invoke(boosterType, current - 1);
                return true;
            }
        }

        private sealed class MemoryPersistence : IBoosterEconomyPersistence
        {
            private readonly HashSet<BoosterRewardType> unlocks = new HashSet<BoosterRewardType>();
            private readonly HashSet<int> levels = new HashSet<int>();

            public bool HasClaimedInitialUnlock(BoosterRewardType boosterType) => unlocks.Contains(boosterType);
            public void MarkInitialUnlockClaimed(BoosterRewardType boosterType) => unlocks.Add(boosterType);
            public bool HasClaimedLevelCompletion(int levelNumber) => levels.Contains(levelNumber);
            public void MarkLevelCompletionClaimed(int levelNumber) => levels.Add(levelNumber);
        }
    }
}
