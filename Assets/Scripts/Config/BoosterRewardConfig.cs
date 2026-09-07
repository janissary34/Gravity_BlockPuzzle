using UnityEngine;
using UnityEngine.Serialization;

namespace GravityPuzzle.Config
{
    /// <summary>
    /// Designer-owned definition of the booster reward shown by NewBoosterPanel.
    /// The visual prefab is an authored identity reference; the panel activates
    /// its matching scene slot and never instantiates this prefab at runtime.
    /// </summary>
    [CreateAssetMenu(fileName = "BoosterRewardConfig", menuName = "Gravity Puzzle/Boosters/Booster Reward Config")]
    public sealed class BoosterRewardConfig : ScriptableObject
    {
        [SerializeField] private BoosterRewardType boosterType;
        [SerializeField] private GameObject visualPrefab;
        [FormerlySerializedAs("unlockAfterLevel")]
        [SerializeField, Min(1)] private int presentationLevel = 1;
        [SerializeField, Min(1)] private int usesPerLevel = 3;

        public BoosterRewardType BoosterType => boosterType;
        public GameObject VisualPrefab => visualPrefab;
        public int PresentationLevel => presentationLevel;
        public int UsesPerLevel => usesPerLevel;
    }

    public enum BoosterRewardType
    {
        FreezeTimer,
        Rocket,
        Hammer
    }
}
