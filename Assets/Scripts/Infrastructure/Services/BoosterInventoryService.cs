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
            // A level-editor quantity is balance/test data, not an unlock.
            // Otherwise a level that merely supplies a preview count would
            // reveal every booster in the player-facing HUD.
            return PlayerPrefs.GetInt(GetUnlockedKey(boosterType), 0) == 1;
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

        // HUD buttons query counts every frame; building these keys there
        // allocated a string (and boxed the enum) per call.
        private static readonly Dictionary<BoosterRewardType, string> countKeys = BuildKeys(CountSuffix);
        private static readonly Dictionary<BoosterRewardType, string> unlockedKeys = BuildKeys(UnlockedSuffix);

        private static Dictionary<BoosterRewardType, string> BuildKeys(string suffix)
        {
            Array values = Enum.GetValues(typeof(BoosterRewardType));
            var keys = new Dictionary<BoosterRewardType, string>(values.Length);
            foreach (BoosterRewardType value in values)
                keys[value] = KeyPrefix + value + suffix;
            return keys;
        }

        private static string GetCountKey(BoosterRewardType boosterType)
        {
            return countKeys.TryGetValue(boosterType, out string key)
                ? key
                : KeyPrefix + boosterType + CountSuffix;
        }

        private static string GetUnlockedKey(BoosterRewardType boosterType)
        {
            return unlockedKeys.TryGetValue(boosterType, out string key)
                ? key
                : KeyPrefix + boosterType + UnlockedSuffix;
        }
    }

    /// <summary>
    /// Composition-root access point for the inventory contract. Runtime UI
    /// adapters use this contract rather than owning mirrored use counters.
    /// The returned facade stays stable across scene reloads so listeners
    /// attached before bootstrap Awake never become orphaned on a replaced
    /// backing inventory.
    /// </summary>
    public static class BoosterInventoryRuntime
    {
        private sealed class RuntimeInventoryProxy : IPlayerBoosterInventory
        {
            private IPlayerBoosterInventory backing;

            public event Action<BoosterRewardType, int> CountChanged;

            public RuntimeInventoryProxy(IPlayerBoosterInventory initial)
            {
                SetBacking(initial);
            }

            public IPlayerBoosterInventory Backing => backing;

            public void SetBacking(IPlayerBoosterInventory next)
            {
                if (ReferenceEquals(backing, next))
                    return;

                if (backing != null)
                    backing.CountChanged -= ForwardCountChanged;

                backing = next;

                if (backing != null)
                    backing.CountChanged += ForwardCountChanged;
            }

            public int GetCount(BoosterRewardType boosterType) => backing.GetCount(boosterType);
            public bool IsUnlocked(BoosterRewardType boosterType) => backing.IsUnlocked(boosterType);
            public void Grant(BoosterRewardType boosterType, int amount) => backing.Grant(boosterType, amount);
            public bool TryConsume(BoosterRewardType boosterType) => backing.TryConsume(boosterType);

            private void ForwardCountChanged(BoosterRewardType boosterType, int count)
            {
                CountChanged?.Invoke(boosterType, count);
            }
        }

        private static readonly RuntimeInventoryProxy proxy =
            new RuntimeInventoryProxy(new PlayerPrefsBoosterInventory());

        public static IPlayerBoosterInventory Current => proxy;

        public static void Configure(IPlayerBoosterInventory inventory)
        {
            if (inventory == null)
            {
                Debug.LogError("[BoosterInventory] Bootstrap supplied a null inventory implementation.");
                return;
            }

            if (ReferenceEquals(inventory, proxy))
                return;

            proxy.SetBacking(inventory);
        }

#if UNITY_EDITOR
        public static void SetEditorPreviewCount(BoosterRewardType boosterType, int amount)
        {
            if (proxy.Backing is PlayerPrefsBoosterInventory inventory)
                inventory.SetEditorPreviewCount(boosterType, amount);
        }
#endif
    }
}
