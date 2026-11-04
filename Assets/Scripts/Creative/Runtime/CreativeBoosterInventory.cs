using System;
using System.Collections.Generic;
using GravityPuzzle.Config;
using GravityPuzzle.Creative.Config;
using GravityPuzzle.Infrastructure.Services;

namespace GravityPuzzle.Creative.Runtime
{
    /// <summary>
    /// In-memory booster inventory for a creative take. Counts come from the take config;
    /// nothing is read from or written to the player's save (PlayerPrefs). Boosters listed in
    /// the config are treated as unlocked so their buttons can appear in the take.
    /// </summary>
    public sealed class CreativeBoosterInventory : IPlayerBoosterInventory
    {
        private readonly Dictionary<BoosterRewardType, int> counts = new Dictionary<BoosterRewardType, int>();

        public event Action<BoosterRewardType, int> CountChanged;

        public CreativeBoosterInventory(CreativeTakeConfig config)
        {
            if (config == null)
                return;

            for (int index = 0; index < config.BoosterEntryCount; index++)
            {
                CreativeTakeConfig.BoosterCount entry = config.GetBoosterEntry(index);
                counts[entry.booster] = Math.Max(0, entry.count);
            }
        }

        public int GetCount(BoosterRewardType boosterType)
        {
            return counts.TryGetValue(boosterType, out int count) ? count : 0;
        }

        public bool IsUnlocked(BoosterRewardType boosterType)
        {
            return counts.ContainsKey(boosterType);
        }

        public void Grant(BoosterRewardType boosterType, int amount)
        {
            if (amount <= 0)
                return;

            counts[boosterType] = GetCount(boosterType) + amount;
            CountChanged?.Invoke(boosterType, counts[boosterType]);
        }

        public bool TryConsume(BoosterRewardType boosterType)
        {
            int count = GetCount(boosterType);
            if (count <= 0)
                return false;

            counts[boosterType] = count - 1;
            CountChanged?.Invoke(boosterType, count - 1);
            return true;
        }
    }
}
