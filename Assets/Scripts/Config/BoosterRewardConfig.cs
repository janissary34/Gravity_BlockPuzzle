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
        [FormerlySerializedAs("usesPerLevel")]
        [SerializeField, Min(1)] private int grantAmount = 3;
        [Header("First-use Tutorial")]
        [Tooltip("Immutable identifier for the tutorial-progress save. Change only when intentionally re-running this tutorial for every player.")]
        [SerializeField] private string tutorialId;
        [Tooltip("Stable Piece Definition Tutorial Target Id on the destination level. Required for Rocket and Hammer tutorials.")]
        [SerializeField] private string tutorialTargetPieceId;
        [Tooltip("Exact fine-grid cell to tap for a Hammer tutorial. Rocket ignores this value.")]
        [SerializeField] private Vector2Int tutorialTargetFineCell;

        public BoosterRewardType BoosterType => boosterType;
        public GameObject VisualPrefab => visualPrefab;
        public int PresentationLevel => presentationLevel;
        public int GrantAmount => grantAmount;
        /// <summary>
        /// Backwards-compatible editor-authoring alias. Runtime inventory uses
        /// <see cref="GrantAmount"/> as the persistent claim quantity.
        /// </summary>
        public int UsesPerLevel => grantAmount;
        public string TutorialId => tutorialId;
        public string TutorialTargetPieceId => tutorialTargetPieceId;
        public Vector2Int TutorialTargetFineCell => tutorialTargetFineCell;
    }

    public enum BoosterRewardType
    {
        FreezeTimer,
        Rocket,
        Hammer,
        IceBlock,
        BombBlock,
        Elevator,
        Box
    }
}
