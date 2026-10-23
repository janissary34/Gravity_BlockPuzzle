using GravityPuzzle.Config;
using UnityEngine;

namespace GravityPuzzle.Presentation.Views
{
    /// <summary>Links one pre-authored New Booster content object to its configuration.</summary>
    [DisallowMultipleComponent]
    public sealed class BoosterRewardContentView : MonoBehaviour
    {
        [SerializeField] private BoosterRewardConfig rewardConfig;

        private BoosterVisualView[] visualRoots;

        public BoosterRewardConfig RewardConfig => rewardConfig;

        private void Awake()
        {
            // A reward slot owns its authored visual children. Cache these once
            // so reopening a reward cannot inherit a disabled icon from an
            // earlier presentation.
            visualRoots = GetComponentsInChildren<BoosterVisualView>(true);
        }

        /// <summary>
        /// Shows or hides this authored reward slot and its visual roots as one
        /// presentation state. No gameplay state is changed here.
        /// </summary>
        public void SetVisible(bool visible)
        {
            gameObject.SetActive(visible);

            if (visualRoots == null)
                return;

            for (int index = 0; index < visualRoots.Length; index++)
            {
                BoosterVisualView visualRoot = visualRoots[index];
                if (visualRoot != null)
                    visualRoot.gameObject.SetActive(visible);
            }
        }
    }
}
