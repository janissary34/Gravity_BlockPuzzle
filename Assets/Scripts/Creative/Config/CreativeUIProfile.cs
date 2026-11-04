using System;
using UnityEngine;

namespace GravityPuzzle.Creative.Config
{
    /// <summary>
    /// UI groups a creative take can show or hide. Each group is bound to concrete scene
    /// objects by <see cref="GravityPuzzle.Creative.Runtime.CreativeSceneBindings"/>.
    /// </summary>
    public enum CreativeUIGroup
    {
        LevelHeader = 0,
        Timer = 1,
        Coins = 2,
        BoosterBar = 3,
        ProgressBar = 4,
        SettingsButton = 5,
        RetryButton = 6,
        TutorialAndRewardPopups = 7,
        WinPanel = 8,
        FailAndRevivePanels = 9,
    }

    /// <summary>
    /// Which UI groups are visible during a creative take. Groups not listed keep the
    /// game's own behaviour. Nothing is destroyed: hiding is a reversible runtime toggle.
    /// </summary>
    [CreateAssetMenu(menuName = "Gravity Puzzle/Creative/UI Profile", fileName = "CreativeUIProfile")]
    public sealed class CreativeUIProfile : ScriptableObject
    {
        [Serializable]
        public struct GroupVisibility
        {
            public CreativeUIGroup group;
            public bool visible;
        }

        [Tooltip("Visibility per UI group. Unlisted groups are left as the game sets them.")]
        [SerializeField] private GroupVisibility[] groups = Array.Empty<GroupVisibility>();

        public int Count => groups != null ? groups.Length : 0;

        public GroupVisibility Get(int index) => groups[index];
    }
}
