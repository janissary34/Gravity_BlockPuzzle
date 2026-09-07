using GravityPuzzle.Config;
using UnityEngine;

namespace GravityPuzzle.Infrastructure.Services
{
    public static class BoosterRewardUnlockState
    {
        private const string PresentedKeyPrefix = "GravityPuzzle.BoosterRewardPresented.";

        public static bool HasBeenPresented(BoosterRewardConfig config)
        {
            return config != null && PlayerPrefs.GetInt(GetPresentedKey(config), 0) == 1;
        }

        public static void MarkPresented(BoosterRewardConfig config)
        {
            if (config == null)
                return;

            PlayerPrefs.SetInt(GetPresentedKey(config), 1);
            PlayerPrefs.Save();
        }

        public static void ClearPresentation(BoosterRewardConfig config)
        {
            if (config == null)
                return;

            PlayerPrefs.DeleteKey(GetPresentedKey(config));
            PlayerPrefs.Save();
        }

        private static string GetPresentedKey(BoosterRewardConfig config)
        {
            // Changing a reward's presentation milestone deliberately creates a
            // new acknowledgement state for that campaign configuration.
            return PresentedKeyPrefix + config.BoosterType + ".Level" + config.PresentationLevel;
        }
    }
}
