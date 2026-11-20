using System;
using System.Collections.Generic;
using UnityEngine;

namespace GravityPuzzle.Config
{
    /// <summary>
    /// Immutable value describing a single booster grant evaluated from <see cref="BoosterEconomyConfig"/>.
    /// </summary>
    public readonly struct BoosterEconomyGrant
    {
        public readonly BoosterRewardType BoosterType;
        public readonly int Amount;

        public BoosterEconomyGrant(BoosterRewardType boosterType, int amount)
        {
            BoosterType = boosterType;
            Amount = amount;
        }
    }

    /// <summary>
    /// Designer-owned configuration for booster unlock milestones, initial unlock gift
    /// quantities, and periodic first-clear level completion rewards across the campaign.
    /// Runtime session and persistence state live in <c>BoosterEconomyService</c>.
    /// </summary>
    [CreateAssetMenu(fileName = "BoosterEconomyConfig", menuName = "Gravity Puzzle/Boosters/Booster Economy Config")]
    public sealed class BoosterEconomyConfig : ScriptableObject
    {
        [Serializable]
        public struct BoosterUnlockRule
        {
            public BoosterRewardType boosterType;
            [Min(1)] public int unlockLevel;
            [Min(0)] public int initialGrantAmount;
            [Tooltip("Optional effect duration reference in seconds (e.g. Freeze Timer = 10s).")]
            [Min(0f)] public float effectDurationSeconds;
        }

        [Serializable]
        public struct PeriodicBoosterGrant
        {
            public BoosterRewardType boosterType;
            [Tooltip("Grants reward on every Nth level completed within this band (1-based offset from minLevel).")]
            [Min(1)] public int everyNLevels;
            [Min(1)] public int grantAmount;
        }

        [Serializable]
        public struct PeriodicRewardBand
        {
            public string bandName;
            [Min(1)] public int minLevel;
            [Min(1)] public int maxLevel;
            public PeriodicBoosterGrant[] grants;
        }

        [Header("Unlock Milestones & Initial Grants")]
        [SerializeField] private BoosterUnlockRule[] unlockRules = new[]
        {
            new BoosterUnlockRule
            {
                boosterType = BoosterRewardType.FreezeTimer,
                unlockLevel = 4,
                initialGrantAmount = 2,
                effectDurationSeconds = 10f
            },
            new BoosterUnlockRule
            {
                boosterType = BoosterRewardType.Hammer,
                unlockLevel = 6,
                initialGrantAmount = 2,
                effectDurationSeconds = 0f
            },
            new BoosterUnlockRule
            {
                boosterType = BoosterRewardType.Rocket,
                unlockLevel = 10,
                initialGrantAmount = 2,
                effectDurationSeconds = 0f
            }
        };

        [Header("First-Clear Periodic Completion Rewards")]
        [SerializeField] private PeriodicRewardBand[] periodicRewardBands = new[]
        {
            new PeriodicRewardBand
            {
                bandName = "Levels 14-25",
                minLevel = 14,
                maxLevel = 25,
                grants = new[]
                {
                    new PeriodicBoosterGrant { boosterType = BoosterRewardType.FreezeTimer, everyNLevels = 5, grantAmount = 1 },
                    new PeriodicBoosterGrant { boosterType = BoosterRewardType.Hammer, everyNLevels = 6, grantAmount = 1 }
                }
            },
            new PeriodicRewardBand
            {
                bandName = "Levels 26-45",
                minLevel = 26,
                maxLevel = 45,
                grants = new[]
                {
                    new PeriodicBoosterGrant { boosterType = BoosterRewardType.FreezeTimer, everyNLevels = 4, grantAmount = 1 },
                    new PeriodicBoosterGrant { boosterType = BoosterRewardType.Hammer, everyNLevels = 5, grantAmount = 1 },
                    new PeriodicBoosterGrant { boosterType = BoosterRewardType.Rocket, everyNLevels = 7, grantAmount = 1 }
                }
            },
            new PeriodicRewardBand
            {
                bandName = "Levels 46-68",
                minLevel = 46,
                maxLevel = 68,
                grants = new[]
                {
                    new PeriodicBoosterGrant { boosterType = BoosterRewardType.FreezeTimer, everyNLevels = 3, grantAmount = 1 },
                    new PeriodicBoosterGrant { boosterType = BoosterRewardType.Hammer, everyNLevels = 4, grantAmount = 1 },
                    new PeriodicBoosterGrant { boosterType = BoosterRewardType.Rocket, everyNLevels = 6, grantAmount = 1 }
                }
            }
        };

        public IReadOnlyList<BoosterUnlockRule> UnlockRules => unlockRules;
        public IReadOnlyList<PeriodicRewardBand> PeriodicRewardBands => periodicRewardBands;

        public bool TryGetUnlockRule(BoosterRewardType boosterType, out BoosterUnlockRule rule)
        {
            if (unlockRules != null)
            {
                for (int i = 0; i < unlockRules.Length; i++)
                {
                    if (unlockRules[i].boosterType == boosterType)
                    {
                        rule = unlockRules[i];
                        return true;
                    }
                }
            }

            rule = default;
            return false;
        }

        public int GetUnlockLevel(BoosterRewardType boosterType)
        {
            if (TryGetUnlockRule(boosterType, out BoosterUnlockRule rule))
                return Mathf.Max(1, rule.unlockLevel);

            return GetFallbackUnlockLevel(boosterType);
        }

        public int GetInitialGrantAmount(BoosterRewardType boosterType)
        {
            if (TryGetUnlockRule(boosterType, out BoosterUnlockRule rule))
                return Mathf.Max(0, rule.initialGrantAmount);

            return GetFallbackInitialGrantAmount(boosterType);
        }

        public bool IsBoosterUnlockedAtLevel(BoosterRewardType boosterType, int levelNumber)
        {
            return levelNumber >= GetUnlockLevel(boosterType);
        }

        /// <summary>
        /// Populates <paramref name="results"/> with any booster unlock rule whose
        /// <see cref="BoosterUnlockRule.unlockLevel"/> equals <paramref name="levelNumber"/>.
        /// </summary>
        public void CollectUnlockGrantsForLevel(int levelNumber, List<BoosterEconomyGrant> results)
        {
            if (results == null)
                return;

            results.Clear();
            if (levelNumber < 1 || unlockRules == null)
                return;

            for (int i = 0; i < unlockRules.Length; i++)
            {
                BoosterUnlockRule rule = unlockRules[i];
                if (rule.unlockLevel == levelNumber && rule.initialGrantAmount > 0)
                    results.Add(new BoosterEconomyGrant(rule.boosterType, rule.initialGrantAmount));
            }
        }

        /// <summary>
        /// Evaluates periodic completion rewards earned when completing <paramref name="completedLevel"/>
        /// for the first time. Within each configured band [<c>minLevel..maxLevel</c>], a grant triggers
        /// whenever <c>(completedLevel - minLevel + 1) % everyNLevels == 0</c>.
        /// </summary>
        public void EvaluatePeriodicRewardsForLevel(int completedLevel, List<BoosterEconomyGrant> results)
        {
            if (results == null)
                return;

            results.Clear();
            if (completedLevel < 1 || periodicRewardBands == null)
                return;

            for (int bandIndex = 0; bandIndex < periodicRewardBands.Length; bandIndex++)
            {
                PeriodicRewardBand band = periodicRewardBands[bandIndex];
                if (completedLevel < band.minLevel || completedLevel > band.maxLevel || band.grants == null)
                    continue;

                int stepInBand = completedLevel - band.minLevel + 1;
                for (int grantIndex = 0; grantIndex < band.grants.Length; grantIndex++)
                {
                    PeriodicBoosterGrant grant = band.grants[grantIndex];
                    if (grant.everyNLevels <= 0 || grant.grantAmount <= 0)
                        continue;

                    if (stepInBand % grant.everyNLevels == 0)
                        results.Add(new BoosterEconomyGrant(grant.boosterType, grant.grantAmount));
                }
            }
        }

        public static int GetFallbackUnlockLevel(BoosterRewardType boosterType)
        {
            return boosterType switch
            {
                BoosterRewardType.FreezeTimer => 4,
                BoosterRewardType.Hammer => 6,
                BoosterRewardType.Rocket => 10,
                _ => 1
            };
        }

        public static int GetFallbackInitialGrantAmount(BoosterRewardType boosterType)
        {
            return boosterType switch
            {
                BoosterRewardType.FreezeTimer => 2,
                BoosterRewardType.Hammer => 2,
                BoosterRewardType.Rocket => 2,
                _ => 1
            };
        }

        /// <summary>Test and editor factory for authoring custom rules without mutating shared assets.</summary>
        public void ConfigureForTesting(BoosterUnlockRule[] rules, PeriodicRewardBand[] bands)
        {
            unlockRules = rules ?? Array.Empty<BoosterUnlockRule>();
            periodicRewardBands = bands ?? Array.Empty<PeriodicRewardBand>();
        }
    }
}
