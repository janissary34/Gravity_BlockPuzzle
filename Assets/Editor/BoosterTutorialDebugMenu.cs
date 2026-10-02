using GravityPuzzle.Config;
using GravityPuzzle.Gameplay.Tutorial;
using GravityPuzzle.Infrastructure.Services;
using UnityEditor;
using UnityEngine;

namespace GravityPuzzle.Editor
{
    /// <summary>Local editor-only controls for replaying first-use booster lessons.</summary>
    internal static class BoosterTutorialDebugMenu
    {
        private const string InventoryKeyPrefix = "GravityPuzzle.BoosterInventory.";

        [MenuItem("Gravity Puzzle/Debug/Reset Booster Tutorial Test Data")]
        private static void ResetBoosterTutorialTestData()
        {
            string[] configGuids = AssetDatabase.FindAssets("t:BoosterRewardConfig");
            for (int index = 0; index < configGuids.Length; index++)
            {
                string path = AssetDatabase.GUIDToAssetPath(configGuids[index]);
                BoosterRewardConfig config = AssetDatabase.LoadAssetAtPath<BoosterRewardConfig>(path);
                BoosterFirstUseTutorialProgress.ClearState(config);
                BoosterRewardUnlockState.ClearPresentation(config);
            }

            foreach (BoosterRewardType boosterType in System.Enum.GetValues(typeof(BoosterRewardType)))
            {
                string prefix = InventoryKeyPrefix + boosterType + ".";
                PlayerPrefs.DeleteKey(prefix + "Count");
                PlayerPrefs.DeleteKey(prefix + "Unlocked");
            }

            PlayerPrefs.Save();
            Debug.Log("[BoosterTutorial] Cleared booster inventory and first-use tutorial progress for local testing.");
        }
    }
}
