using UnityEngine;

namespace GravityPuzzle.Infrastructure.Services
{
    /// <summary>
    /// Persistence boundary for the player's active campaign level position.
    /// Kept behind an interface so automated tests can swap an in-memory store
    /// without mutating live PlayerPrefs.
    /// </summary>
    public interface ICampaignProgressPersistence
    {
        bool TryLoadLevelIndex(out int levelIndex);
        void SaveLevelIndex(int levelIndex);
        void Clear();
    }

    public sealed class PlayerPrefsCampaignProgressPersistence : ICampaignProgressPersistence
    {
        public const string CurrentLevelIndexKey = "GravityPuzzle.Campaign.CurrentLevelIndex";

        public bool TryLoadLevelIndex(out int levelIndex)
        {
            if (!PlayerPrefs.HasKey(CurrentLevelIndexKey))
            {
                levelIndex = 0;
                return false;
            }

            int storedIndex = PlayerPrefs.GetInt(CurrentLevelIndexKey, -1);
            if (storedIndex < 0)
            {
                levelIndex = 0;
                return false;
            }

            levelIndex = storedIndex;
            return true;
        }

        public void SaveLevelIndex(int levelIndex)
        {
            if (levelIndex < 0)
                return;

            PlayerPrefs.SetInt(CurrentLevelIndexKey, levelIndex);
            PlayerPrefs.Save();
        }

        public void Clear()
        {
            ClearSavedProgress();
        }

        public static void ClearSavedProgress()
        {
            PlayerPrefs.DeleteKey(CurrentLevelIndexKey);
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// Composition-root access point for campaign level persistence.
    /// </summary>
    public static class CampaignProgressRuntime
    {
        private sealed class RuntimePersistenceProxy : ICampaignProgressPersistence
        {
            private ICampaignProgressPersistence backing;

            public RuntimePersistenceProxy(ICampaignProgressPersistence initial)
            {
                backing = initial;
            }

            public void SetBacking(ICampaignProgressPersistence next)
            {
                backing = next;
            }

            public bool TryLoadLevelIndex(out int levelIndex) => backing.TryLoadLevelIndex(out levelIndex);
            public void SaveLevelIndex(int levelIndex) => backing.SaveLevelIndex(levelIndex);
            public void Clear() => backing.Clear();
        }

        private static readonly RuntimePersistenceProxy proxy =
            new RuntimePersistenceProxy(new PlayerPrefsCampaignProgressPersistence());

        public static ICampaignProgressPersistence Current => proxy;

        public static void Configure(ICampaignProgressPersistence persistence)
        {
            if (persistence == null)
            {
                Debug.LogError("[CampaignProgress] Bootstrap supplied a null campaign progress persistence.");
                return;
            }

            if (ReferenceEquals(persistence, proxy))
                return;

            proxy.SetBacking(persistence);
        }

        public static void ResetToDefault()
        {
            proxy.SetBacking(new PlayerPrefsCampaignProgressPersistence());
        }
    }
}
