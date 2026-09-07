using GravityPuzzle.Config;
using UnityEngine;

namespace GravityPuzzle.Presentation.Views
{
    /// <summary>Links one pre-authored New Booster content object to its configuration.</summary>
    [DisallowMultipleComponent]
    public sealed class BoosterRewardContentView : MonoBehaviour
    {
        [SerializeField] private BoosterRewardConfig rewardConfig;

        public BoosterRewardConfig RewardConfig => rewardConfig;
    }
}
