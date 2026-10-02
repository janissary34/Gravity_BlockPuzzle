using System;
using System.Collections.Generic;
using GravityPuzzle.Config;
using UnityEngine;

namespace GravityPuzzle.Infrastructure.Services
{
    /// <summary>
    /// The sole mutable owner of a player's reusable booster quantities.
    /// Level definitions may supply editor-only preview values, but production
    /// gameplay always reads and consumes this inventory.
    /// </summary>
    public interface IPlayerBoosterInventory
    {
        event Action<BoosterRewardType, int> CountChanged;

        int GetCount(BoosterRewardType boosterType);
        bool IsUnlocked(BoosterRewardType boosterType);
        void Grant(BoosterRewardType boosterType, int amount);
        bool TryConsume(BoosterRewardType boosterType);
    }

    public sealed class PlayerPrefsBoosterInventory : IPlayerBoosterInventory
    {
        private const string KeyPrefix = "GravityPuzzle.BoosterInventory.";
        private const string CountSuffix = ".Count";
        private const string UnlockedSuffix = ".Unlocked";
        private readonly Dictionary<BoosterRewardType, int> previewCounts =
            new Dictionary<BoosterRewardType, int>();

        public event Action<BoosterRewardType, int> CountChanged;

        public int GetCount(BoosterRewardType boosterType)
        {
            if (previewCounts.TryGetValue(boosterType, out int previewCount))
                return previewCount;

            return Mathf.Max(0, PlayerPrefs.GetInt(GetCountKey(boosterType), 0));
        }

        public bool IsUnlocked(BoosterRewardType boosterType)
        {
            return previewCounts.ContainsKey(boosterType) ||
                   PlayerPrefs.GetInt(GetUnlockedKey(boosterType), 0) == 1;
        }

        public void Grant(BoosterRewardType boosterType, int amount)
        {
            if (amount <= 0)
                return;

            int nextCount = Mathf.Max(0, PlayerPrefs.GetInt(GetCountKey(boosterType), 0)) + amount;
            PlayerPrefs.SetInt(GetCountKey(boosterType), nextCount);
            PlayerPrefs.SetInt(GetUnlockedKey(boosterType), 1);
            PlayerPrefs.Save();
            CountChanged?.Invoke(boosterType, GetCount(boosterType));
        }

        public bool TryConsume(BoosterRewardType boosterType)
        {
            if (previewCounts.TryGetValue(boosterType, out int previewCount))
            {
                if (previewCount <= 0)
                    return false;

                previewCounts[boosterType] = previewCount - 1;
                CountChanged?.Invoke(boosterType, previewCount - 1);
                return true;
            }

            int currentCount = Mathf.Max(0, PlayerPrefs.GetInt(GetCountKey(boosterType), 0));
            if (currentCount <= 0)
                return false;

            int nextCount = currentCount - 1;
            PlayerPrefs.SetInt(GetCountKey(boosterType), nextCount);
            PlayerPrefs.Save();
            CountChanged?.Invoke(boosterType, nextCount);
            return true;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Used exclusively by level-editor play previews. It never writes a
        /// player save and is discarded when the process reloads.
        /// </summary>
        public void SetEditorPreviewCount(BoosterRewardType boosterType, int amount)
        {
            previewCounts[boosterType] = Mathf.Max(0, amount);
            CountChanged?.Invoke(boosterType, previewCounts[boosterType]);
        }
#endif

        private static string GetCountKey(BoosterRewardType boosterType)
        {
            return KeyPrefix + boosterType + CountSuffix;
        }

        private static string GetUnlockedKey(BoosterRewardType boosterType)
        {
            return KeyPrefix + boosterType + UnlockedSuffix;
        }
    }

    /// <summary>
    /// Composition-root access point for the inventory contract. Runtime UI
    /// adapters use this contract rather than owning mirrored use counters.
    /// </summary>
    public static class BoosterInventoryRuntime
    {
        private static IPlayerBoosterInventory current;

        public static IPlayerBoosterInventory Current
        {
            get
            {
                if (current == null)
                    current = new PlayerPrefsBoosterInventory();
                return current;
            }
        }

        public static void Configure(IPlayerBoosterInventory inventory)
        {
            if (inventory == null)
            {
                Debug.LogError("[BoosterInventory] Bootstrap supplied a null inventory implementation.");
                return;
            }

            current = inventory;
        }

#if UNITY_EDITOR
        public static void SetEditorPreviewCount(BoosterRewardType boosterType, int amount)
        {
            if (Current is PlayerPrefsBoosterInventory inventory)
                inventory.SetEditorPreviewCount(boosterType, amount);
        }
#endif
    }
}
