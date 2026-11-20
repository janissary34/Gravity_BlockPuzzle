using System.Collections.Generic;
using GravityPuzzle.Config;
using UnityEngine;

namespace GravityPuzzle.Infrastructure.Services
{
    /// <summary>
    /// Persistence boundary for one-time booster economy claims (initial unlock gifts and
    /// first-clear level completion rewards). Kept behind an interface so unit tests can
    /// verify economy rules without mutating live PlayerPrefs.
    /// </summary>
    public interface IBoosterEconomyPersistence
    {
        bool HasClaimedInitialUnlock(BoosterRewardType boosterType);
        void MarkInitialUnlockClaimed(BoosterRewardType boosterType);
        bool HasClaimedLevelCompletion(int levelNumber);
        void MarkLevelCompletionClaimed(int levelNumber);
    }

    public sealed class PlayerPrefsBoosterEconomyPersistence : IBoosterEconomyPersistence
    {
        private const string InitialUnlockKeyPrefix = "GravityPuzzle.BoosterEconomy.InitialUnlock.";
        private const string LevelRewardKeyPrefix = "GravityPuzzle.BoosterEconomy.LevelReward.";

        public bool HasClaimedInitialUnlock(BoosterRewardType boosterType)
        {
            return PlayerPrefs.GetInt(InitialUnlockKeyPrefix + boosterType, 0) == 1;
        }

        public void MarkInitialUnlockClaimed(BoosterRewardType boosterType)
        {
            PlayerPrefs.SetInt(InitialUnlockKeyPrefix + boosterType, 1);
            PlayerPrefs.Save();
        }

        public bool HasClaimedLevelCompletion(int levelNumber)
        {
            return levelNumber >= 1 && PlayerPrefs.GetInt(LevelRewardKeyPrefix + levelNumber, 0) == 1;
        }

        public void MarkLevelCompletionClaimed(int levelNumber)
        {
            if (levelNumber < 1)
                return;

            PlayerPrefs.SetInt(LevelRewardKeyPrefix + levelNumber, 1);
            PlayerPrefs.Save();
        }

        public static void ClearAll(int maxCampaignLevel = 128)
        {
            foreach (BoosterRewardType boosterType in System.Enum.GetValues(typeof(BoosterRewardType)))
                PlayerPrefs.DeleteKey(InitialUnlockKeyPrefix + boosterType);

            int upperLevel = Mathf.Max(1, maxCampaignLevel);
            for (int level = 1; level <= upperLevel; level++)
                PlayerPrefs.DeleteKey(LevelRewardKeyPrefix + level);

            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// Authoritative coordinator for booster unlock milestones, initial unlock grants,
    /// and first-clear periodic level rewards. All quantity mutations flow through
    /// <see cref="IPlayerBoosterInventory"/> so HUD button counts and locked states
    /// refresh automatically via <see cref="IPlayerBoosterInventory.CountChanged"/>.
    /// </summary>
    public interface IBoosterEconomyService
    {
        BoosterEconomyConfig Config { get; }
        int GetUnlockLevel(BoosterRewardType boosterType);
        int GetInitialGrantAmount(BoosterRewardType boosterType);
        bool IsBoosterUnlockedAtLevel(BoosterRewardType boosterType, int currentLevel);
        bool TryGetUnlockedBoosterForLevel(int levelNumber, out BoosterRewardType boosterType);
        IReadOnlyList<BoosterEconomyGrant> EnsureInitialUnlockGrantsForLevel(int currentLevel);
        IReadOnlyList<BoosterEconomyGrant> ProcessFirstTimeLevelCompletion(int completedLevel);
    }

    public sealed class BoosterEconomyService : IBoosterEconomyService
    {
        private readonly BoosterEconomyConfig config;
        private readonly IPlayerBoosterInventory inventory;
        private readonly IBoosterEconomyPersistence persistence;
        private readonly List<BoosterEconomyGrant> evaluationBuffer = new List<BoosterEconomyGrant>(4);
        private readonly List<BoosterEconomyGrant> appliedBuffer = new List<BoosterEconomyGrant>(4);

        public BoosterEconomyConfig Config => config;

        public BoosterEconomyService(
            BoosterEconomyConfig config,
            IPlayerBoosterInventory inventory,
            IBoosterEconomyPersistence persistence)
        {
            this.config = config;
            this.inventory = inventory;
            this.persistence = persistence;
        }

        public int GetUnlockLevel(BoosterRewardType boosterType)
        {
            return config != null
                ? config.GetUnlockLevel(boosterType)
                : BoosterEconomyConfig.GetFallbackUnlockLevel(boosterType);
        }

        public int GetInitialGrantAmount(BoosterRewardType boosterType)
        {
            return config != null
                ? config.GetInitialGrantAmount(boosterType)
                : BoosterEconomyConfig.GetFallbackInitialGrantAmount(boosterType);
        }

        public bool IsBoosterUnlockedAtLevel(BoosterRewardType boosterType, int currentLevel)
        {
            return currentLevel >= GetUnlockLevel(boosterType);
        }

        public bool TryGetUnlockedBoosterForLevel(int levelNumber, out BoosterRewardType boosterType)
        {
            if (levelNumber >= 1)
            {
                if (config != null && config.UnlockRules != null)
                {
                    IReadOnlyList<BoosterEconomyConfig.BoosterUnlockRule> rules = config.UnlockRules;
                    for (int i = 0; i < rules.Count; i++)
                    {
                        if (rules[i].unlockLevel == levelNumber)
                        {
                            boosterType = rules[i].boosterType;
                            return true;
                        }
                    }

                    boosterType = default;
                    return false;
                }

                if (levelNumber == BoosterEconomyConfig.GetFallbackUnlockLevel(BoosterRewardType.FreezeTimer))
                {
                    boosterType = BoosterRewardType.FreezeTimer;
                    return true;
                }

                if (levelNumber == BoosterEconomyConfig.GetFallbackUnlockLevel(BoosterRewardType.Hammer))
                {
                    boosterType = BoosterRewardType.Hammer;
                    return true;
                }

                if (levelNumber == BoosterEconomyConfig.GetFallbackUnlockLevel(BoosterRewardType.Rocket))
                {
                    boosterType = BoosterRewardType.Rocket;
                    return true;
                }
            }

            boosterType = default;
            return false;
        }

        public IReadOnlyList<BoosterEconomyGrant> EnsureInitialUnlockGrantsForLevel(int currentLevel)
        {
            appliedBuffer.Clear();
            if (currentLevel < 1 || inventory == null || persistence == null)
                return appliedBuffer;

            evaluationBuffer.Clear();
            if (config != null)
            {
                config.CollectUnlockGrantsForLevel(currentLevel, evaluationBuffer);
            }
            else if (TryGetUnlockedBoosterForLevel(currentLevel, out BoosterRewardType fallbackType))
            {
                evaluationBuffer.Add(new BoosterEconomyGrant(
                    fallbackType,
                    BoosterEconomyConfig.GetFallbackInitialGrantAmount(fallbackType)));
            }

            for (int i = 0; i < evaluationBuffer.Count; i++)
            {
                BoosterEconomyGrant grant = evaluationBuffer[i];
                if (grant.Amount <= 0 || persistence.HasClaimedInitialUnlock(grant.BoosterType))
                    continue;

                inventory.Grant(grant.BoosterType, grant.Amount);
                persistence.MarkInitialUnlockClaimed(grant.BoosterType);
                appliedBuffer.Add(grant);

                Debug.Log(
                    $"[BoosterEconomy] Level {currentLevel}: Unlocked {grant.BoosterType} (+{grant.Amount}, Total: {inventory.GetCount(grant.BoosterType)}).");
            }

            return appliedBuffer;
        }

        public IReadOnlyList<BoosterEconomyGrant> ProcessFirstTimeLevelCompletion(int completedLevel)
        {
            appliedBuffer.Clear();
            if (completedLevel < 1 || inventory == null || persistence == null)
                return appliedBuffer;

            if (persistence.HasClaimedLevelCompletion(completedLevel))
                return appliedBuffer;

            persistence.MarkLevelCompletionClaimed(completedLevel);

            if (config == null)
            {
                Debug.LogWarning(
                    $"[BoosterEconomy] Level {completedLevel} completed without an assigned BoosterEconomyConfig; periodic rewards were skipped.");
            }
            else
            {
                config.EvaluatePeriodicRewardsForLevel(completedLevel, evaluationBuffer);
                for (int i = 0; i < evaluationBuffer.Count; i++)
                {
                    BoosterEconomyGrant grant = evaluationBuffer[i];
                    if (grant.Amount <= 0)
                        continue;

                    inventory.Grant(grant.BoosterType, grant.Amount);
                    appliedBuffer.Add(grant);

                    Debug.Log(
                        $"[BoosterEconomy] Level {completedLevel} first clear: Granted +{grant.Amount} {grant.BoosterType} (Total: {inventory.GetCount(grant.BoosterType)}).");
                }
            }

            if (TryGetUnlockedBoosterForLevel(completedLevel + 1, out BoosterRewardType nextUnlock))
            {
                int initialGift = GetInitialGrantAmount(nextUnlock);
                Debug.Log(
                    $"[BoosterEconomy] Level {completedLevel} completed: {nextUnlock} will unlock on Level {completedLevel + 1} (+{initialGift}).");
            }

            return appliedBuffer;
        }
    }

    /// <summary>
    /// Composition-root access point for the booster economy service, mirroring
    /// <see cref="BoosterInventoryRuntime"/>.
    /// </summary>
    public static class BoosterEconomyRuntime
    {
        private static IBoosterEconomyService current;

        public static IBoosterEconomyService Current
        {
            get
            {
                if (current == null)
                {
                    current = new BoosterEconomyService(
                        null,
                        BoosterInventoryRuntime.Current,
                        new PlayerPrefsBoosterEconomyPersistence());
                }

                return current;
            }
        }

        public static void Configure(IBoosterEconomyService service)
        {
            if (service == null)
            {
                Debug.LogError("[BoosterEconomy] Bootstrap supplied a null booster economy service.");
                return;
            }

            current = service;
        }
    }
}
