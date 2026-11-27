using GravityPuzzle.Bootstrap;
using GravityPuzzle.Config;
using GravityPuzzle.Gameplay.Tutorial;
using GravityPuzzle.Infrastructure.Services;
using GravityPuzzle.Presentation.Views;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GravityPuzzle.Tests.Editor
{
    public sealed class FeatureMilestoneTests
    {
        [TestCase(4, "Freeze_Timer", true)]
        [TestCase(5, "CombinedBlock", false)]
        [TestCase(6, "Hammer", true)]
        [TestCase(8, "Ice_block", false)]
        [TestCase(10, "Rocket", true)]
        [TestCase(16, "BombBlock", false)]
        [TestCase(23, "Box", false)]
        [TestCase(32, "Elevator", false)]
        [TestCase(53, "KeyLock", false)]
        public void GetFeatureTargetNameForLevel_ReturnsExpectedSlotAndKind(
            int levelNumber,
            string expectedTargetName,
            bool expectedIsBooster)
        {
            Assert.That(
                NewBoosterPanelView.GetFeatureTargetNameForLevel(levelNumber),
                Is.EqualTo(expectedTargetName));
            Assert.That(
                NewBoosterPanelView.IsBoosterMilestoneLevel(levelNumber),
                Is.EqualTo(expectedIsBooster));
        }

        [TestCase(1)]
        [TestCase(11)]
        [TestCase(15)]
        [TestCase(18)]
        [TestCase(21)]
        [TestCase(24)]
        [TestCase(25)]
        [TestCase(33)]
        [TestCase(54)]
        public void GetFeatureTargetNameForLevel_ReturnsNullForNonMilestoneLevels(int levelNumber)
        {
            Assert.That(NewBoosterPanelView.GetFeatureTargetNameForLevel(levelNumber), Is.Null);
        }

        [Test]
        public void LevelSequence_ContainsExpectedFeatureIntroductionLevels()
        {
            GravityLevelSequence sequence = AssetDatabase.LoadAssetAtPath<GravityLevelSequence>(
                "Assets/Resources/LevelSequence.asset");
            Assert.That(sequence, Is.Not.Null);
            Assert.That(sequence.levels, Is.Not.Null);
            Assert.That(sequence.levels.Count, Is.GreaterThanOrEqualTo(53));

            Assert.That(sequence.levels[4], Is.Not.Null, "Level 5 (CombinedBlock milestone) must be assigned.");
            Assert.That(sequence.levels[9], Is.Not.Null, "Level 10 (Rocket milestone) must be assigned.");

            GravityLevelDefinition bombLevel = sequence.levels[15];
            Assert.That(bombLevel, Is.Not.Null, "Level 16 (BombBlock milestone) must be assigned.");
            Assert.That(HasSpecialBlockType(bombLevel, PieceSpecialBlockType.Bomb), Is.True,
                "Level 16 must contain at least one Bomb block.");

            GravityLevelDefinition boxLevel = sequence.levels[22];
            Assert.That(boxLevel, Is.Not.Null, "Level 23 (Box milestone) must be assigned.");
            Assert.That(boxLevel.boxes != null && boxLevel.boxes.Count > 0, Is.True,
                "Level 23 must contain at least one Box reveal area.");

            GravityLevelDefinition elevatorLevel = sequence.levels[31];
            Assert.That(elevatorLevel, Is.Not.Null, "Level 32 (Elevator milestone) must be assigned.");
            Assert.That(elevatorLevel.elevators != null && elevatorLevel.elevators.Count > 0, Is.True,
                "Level 32 must contain at least one Elevator reveal area.");

            GravityLevelDefinition keyLockLevel = sequence.levels[52];
            Assert.That(keyLockLevel, Is.Not.Null, "Level 53 (KeyLock milestone) must be assigned.");
            Assert.That(
                HasSpecialBlockType(keyLockLevel, PieceSpecialBlockType.Key) &&
                HasSpecialBlockType(keyLockLevel, PieceSpecialBlockType.Lock),
                Is.True,
                "Level 53 must contain both Key and Lock blocks.");
        }

        [Test]
        public void RocketIntroLevel_HasAuthoredRocketTutorialTargetPiece()
        {
            GravityLevelSequence sequence = AssetDatabase.LoadAssetAtPath<GravityLevelSequence>(
                "Assets/Resources/LevelSequence.asset");
            BoosterRewardConfig rocketConfig = AssetDatabase.LoadAssetAtPath<BoosterRewardConfig>(
                "Assets/Config/Boosters/RocketBoosterRewardConfig.asset");

            Assert.That(sequence, Is.Not.Null);
            Assert.That(rocketConfig, Is.Not.Null);
            Assert.That(rocketConfig.PresentationLevel, Is.EqualTo(10));
            Assert.That(sequence.levels.Count, Is.GreaterThanOrEqualTo(rocketConfig.PresentationLevel));

            GravityLevelDefinition level = sequence.levels[rocketConfig.PresentationLevel - 1];
            Assert.That(level, Is.Not.Null);
            Assert.That(string.IsNullOrWhiteSpace(rocketConfig.TutorialTargetPieceId), Is.False);

            bool foundTarget = false;
            for (int i = 0; i < level.pieces.Count; i++)
            {
                if (level.pieces[i] != null &&
                    level.pieces[i].tutorialTargetId == rocketConfig.TutorialTargetPieceId)
                {
                    foundTarget = true;
                    break;
                }
            }

            Assert.That(foundTarget, Is.True,
                $"Level {rocketConfig.PresentationLevel} ({level.name}) must tag a piece with {rocketConfig.TutorialTargetPieceId}.");
        }

        private static bool HasSpecialBlockType(GravityLevelDefinition level, PieceSpecialBlockType targetType)
        {
            if (level == null || level.pieces == null)
                return false;

            for (int i = 0; i < level.pieces.Count; i++)
            {
                if (level.pieces[i] != null && level.pieces[i].specialBlockType == targetType)
                    return true;
            }

            return false;
        }

        [Test]
        public void BoosterInventoryRuntime_PreservesCountChangedListenersAcrossConfigure()
        {
            int observedCount = -1;
            BoosterRewardType observedType = default;
            void OnCountChanged(BoosterRewardType type, int count)
            {
                observedType = type;
                observedCount = count;
            }

            BoosterInventoryRuntime.Current.CountChanged += OnCountChanged;
            try
            {
                var replacement = new StubBoosterInventory();
                BoosterInventoryRuntime.Configure(replacement);
                BoosterInventoryRuntime.Current.Grant(BoosterRewardType.FreezeTimer, 1);

                Assert.That(observedType, Is.EqualTo(BoosterRewardType.FreezeTimer));
                Assert.That(observedCount, Is.EqualTo(1));
            }
            finally
            {
                BoosterInventoryRuntime.Current.CountChanged -= OnCountChanged;
                BoosterInventoryRuntime.Configure(new PlayerPrefsBoosterInventory());
            }
        }

        [TestCase(LevelDifficulty.Normal, "Normal")]
        [TestCase(LevelDifficulty.Hard, "Hard")]
        [TestCase(LevelDifficulty.VeryHard, "Very Hard")]
        public void FormatDifficultyLabel_MapsAllEnumValues(LevelDifficulty difficulty, string expected)
        {
            Assert.That(
                RuntimePieceFactoryBootstrap.FormatDifficultyLabel(difficulty),
                Is.EqualTo(expected));
        }

        [TestCase(LevelDifficulty.Normal, false)]
        [TestCase(LevelDifficulty.Hard, true)]
        [TestCase(LevelDifficulty.VeryHard, true)]
        public void ShouldShowDifficultyTag_IsTrueOnlyForHardAndVeryHard(
            LevelDifficulty difficulty,
            bool expectedVisible)
        {
            Assert.That(
                RuntimePieceFactoryBootstrap.ShouldShowDifficultyTag(difficulty),
                Is.EqualTo(expectedVisible));
        }

        [Test]
        public void TryResolveDifficultyTagPresentation_UsesAuthoredSpritesAndHidesFallbackTextForHardAndVeryHard()
        {
            string sheetPath = AssetDatabase.GUIDToAssetPath("b4f282748a47c44718bbf15d213dcb1c");
            Assert.That(string.IsNullOrEmpty(sheetPath), Is.False, "Difficulty sprite sheet must exist by GUID.");

            Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(sheetPath);
            Sprite hardSprite = null;
            Sprite veryHardSprite = null;
            for (int i = 0; i < subAssets.Length; i++)
            {
                if (subAssets[i] is Sprite sprite)
                {
                    if (sprite.name == "Hard")
                        hardSprite = sprite;
                    else if (sprite.name == "VeryHard")
                        veryHardSprite = sprite;
                }
            }

            Assert.That(hardSprite, Is.Not.Null, "Sliced Hard sprite must exist.");
            Assert.That(veryHardSprite, Is.Not.Null, "Sliced VeryHard sprite must exist.");

            Assert.That(
                RuntimePieceFactoryBootstrap.TryResolveDifficultyTagPresentation(
                    LevelDifficulty.Normal, hardSprite, veryHardSprite, out Sprite normalResolved, out bool normalBaked),
                Is.False);
            Assert.That(normalResolved, Is.Null);
            Assert.That(normalBaked, Is.False);

            Assert.That(
                RuntimePieceFactoryBootstrap.TryResolveDifficultyTagPresentation(
                    LevelDifficulty.Hard, hardSprite, veryHardSprite, out Sprite hardResolved, out bool hardBaked),
                Is.True);
            Assert.That(hardResolved, Is.SameAs(hardSprite));
            Assert.That(hardBaked, Is.True);

            Assert.That(
                RuntimePieceFactoryBootstrap.TryResolveDifficultyTagPresentation(
                    LevelDifficulty.VeryHard, hardSprite, veryHardSprite, out Sprite veryHardResolved, out bool veryHardBaked),
                Is.True);
            Assert.That(veryHardResolved, Is.SameAs(veryHardSprite));
            Assert.That(veryHardBaked, Is.True);
        }

        [Test]
        public void ResolveFirstHardLevelNumber_FindsFirstHardOrVeryHardLevelInSequence()
        {
            GravityLevelDefinition l1 = ScriptableObject.CreateInstance<GravityLevelDefinition>();
            GravityLevelDefinition l2 = ScriptableObject.CreateInstance<GravityLevelDefinition>();
            GravityLevelDefinition l3 = ScriptableObject.CreateInstance<GravityLevelDefinition>();
            GravityLevelDefinition l4 = ScriptableObject.CreateInstance<GravityLevelDefinition>();
            try
            {
                l1.difficulty = LevelDifficulty.Normal;
                l2.difficulty = LevelDifficulty.Normal;
                l3.difficulty = LevelDifficulty.Hard;
                l4.difficulty = LevelDifficulty.Normal;

                int firstHard = GravityLevelRuntime.ResolveFirstHardLevelNumber(
                    new[] { l1, l2, l3, l4 });
                Assert.That(firstHard, Is.EqualTo(3));

                int noneHard = GravityLevelRuntime.ResolveFirstHardLevelNumber(
                    new[] { l1, l2 });
                Assert.That(noneHard, Is.EqualTo(int.MaxValue));
            }
            finally
            {
                Object.DestroyImmediate(l1);
                Object.DestroyImmediate(l2);
                Object.DestroyImmediate(l3);
                Object.DestroyImmediate(l4);
            }
        }

        [Test]
        public void TimeBoosterButtonPrefab_UsesTenSecondFreezeDuration()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/MainPrefabs/UI_Booster/Time_Booster_Button.prefab");
            Assert.That(prefab, Is.Not.Null);

            FreezeTimerBooster[] freezeBoosters = prefab.GetComponentsInChildren<FreezeTimerBooster>(true);
            Assert.That(freezeBoosters.Length, Is.GreaterThan(0));
            for (int i = 0; i < freezeBoosters.Length; i++)
                Assert.That(freezeBoosters[i].freezeDuration, Is.EqualTo(10f));
        }

        [Test]
        public void SoundFeedbackConfig_HasAllGameplaySoundCuesAssigned()
        {
            SoundFeedbackConfig config = AssetDatabase.LoadAssetAtPath<SoundFeedbackConfig>(
                "Assets/Config/Presentation/SoundFeedbackConfig.asset");
            Assert.That(config, Is.Not.Null);
            Assert.That(config.BarFillClips, Is.Not.Null.And.Not.Empty);
            Assert.That(config.BarFillClips[0], Is.Not.Null);
            Assert.That(config.ClockTickingClip, Is.Not.Null);
            Assert.That(config.FreezeTimerImpactClips, Is.Not.Null.And.Not.Empty);
            Assert.That(config.FreezeTimerImpactClips[0], Is.Not.Null);
            Assert.That(config.RocketLaunchClips, Is.Not.Null.And.Not.Empty);
            Assert.That(config.RocketLaunchClips[0], Is.Not.Null);
            Assert.That(config.ElevatorOpenClips, Is.Not.Null.And.Not.Empty);
            Assert.That(config.ElevatorOpenClips[0], Is.Not.Null);
            Assert.That(config.BoxCounterStepClips, Is.Not.Null.And.Not.Empty);
            Assert.That(config.BoxCounterStepClips[0], Is.Not.Null);
            Assert.That(config.BoxBreakClips, Is.Not.Null.And.Not.Empty);
            Assert.That(config.BoxBreakClips[0], Is.Not.Null);
        }

        [TestCase("Assets/MainPrefabs/UI_Booster/Time_Booster_Button.prefab", "Level 4")]
        [TestCase("Assets/MainPrefabs/UI_Booster/Hammer_Booster_Button.prefab", "Level 6")]
        [TestCase("Assets/MainPrefabs/UI_Booster/Rocket_Booster_Button.prefab", "Level 10")]
        public void BoosterButtonPrefabs_HaveBottomRightLockAndBottomCenterUnlockLevelText(
            string prefabPath,
            string expectedUnlockText)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null);

            TMPro.TextMeshProUGUI unlockText = null;
            RectTransform lockRect = null;
            RectTransform[] rects = prefab.GetComponentsInChildren<RectTransform>(true);
            for (int i = 0; i < rects.Length; i++)
            {
                if (rects[i].name == "Lock_img")
                    lockRect = rects[i];
                else if (rects[i].name == "UnlockLevel_txt")
                    unlockText = rects[i].GetComponent<TMPro.TextMeshProUGUI>();
            }

            Assert.That(lockRect, Is.Not.Null);
            Assert.That(lockRect.anchorMin, Is.EqualTo(new Vector2(1f, 0f)));
            Assert.That(lockRect.anchorMax, Is.EqualTo(new Vector2(1f, 0f)));

            Assert.That(unlockText, Is.Not.Null);
            Assert.That(unlockText.text, Is.EqualTo(expectedUnlockText));
            RectTransform unlockRect = unlockText.rectTransform;
            Assert.That(unlockRect.anchorMin, Is.EqualTo(new Vector2(0.5f, 0f)));
            Assert.That(unlockRect.anchorMax, Is.EqualTo(new Vector2(0.5f, 0f)));
        }

        [Test]
        public void LockUnlockedBoxes_CapturesInteriorPiecesAndMarksBoxLocked()
        {
            GravityLevelDefinition level = ScriptableObject.CreateInstance<GravityLevelDefinition>();
            try
            {
                PieceDefinition insidePiece = new PieceDefinition
                {
                    pieceId = "inside",
                    origin = new Vector2Int(4, 4),
                    cells = new System.Collections.Generic.List<PieceCellDefinition>
                    {
                        new PieceCellDefinition(Vector2Int.zero, PieceCellType.Block),
                        new PieceCellDefinition(Vector2Int.right, PieceCellType.Block)
                    }
                };
                PieceDefinition outsidePiece = new PieceDefinition
                {
                    pieceId = "outside",
                    origin = new Vector2Int(0, 0),
                    cells = new System.Collections.Generic.List<PieceCellDefinition>
                    {
                        new PieceCellDefinition(Vector2Int.zero, PieceCellType.Block)
                    }
                };

                level.pieces.Add(insidePiece);
                level.pieces.Add(outsidePiece);

                BoxRevealDefinition unlockedBox = new BoxRevealDefinition
                {
                    areaId = "box-1",
                    editorLocked = false,
                    bounds = new RevealAreaBounds
                    {
                        origin = new Vector2Int(4, 4),
                        size = new Vector2Int(4, 4)
                    }
                };
                level.boxes.Add(unlockedBox);

                int captured = level.LockUnlockedBoxes();
                Assert.That(captured, Is.EqualTo(1));
                Assert.That(unlockedBox.editorLocked, Is.True);
                Assert.That(unlockedBox.hiddenPieces.Count, Is.EqualTo(1));
                Assert.That(unlockedBox.hiddenPieces[0], Is.SameAs(insidePiece));
                Assert.That(level.pieces.Count, Is.EqualTo(1));
                Assert.That(level.pieces[0], Is.SameAs(outsidePiece));
            }
            finally
            {
                Object.DestroyImmediate(level);
            }
        }

        [Test]
        public void BoosterFirstUseTutorial_ActivatesOnUnlockLevelEvenAfterPriorCompletion()
        {
            GravityLevelSequence sequence = AssetDatabase.LoadAssetAtPath<GravityLevelSequence>(
                "Assets/Resources/LevelSequence.asset");
            BoosterRewardConfig freezeConfig = AssetDatabase.LoadAssetAtPath<BoosterRewardConfig>(
                "Assets/Config/Boosters/FreezeTimerBoosterRewardConfig.asset");
            BoosterEconomyConfig economyConfig = AssetDatabase.LoadAssetAtPath<BoosterEconomyConfig>(
                "Assets/Config/Boosters/BoosterEconomyConfig.asset");

            Assert.That(sequence, Is.Not.Null);
            Assert.That(freezeConfig, Is.Not.Null);
            Assert.That(economyConfig, Is.Not.Null);

            var stubInventory = new StubBoosterInventory();
            var stubPersistence = new StubEconomyPersistence();
            var stubCampaign = new StubCampaignPersistence();
            BoosterFirstUseTutorialState originalTutorialState = BoosterFirstUseTutorialProgress.GetState(freezeConfig);

            try
            {
                CampaignProgressRuntime.Configure(stubCampaign);
                BoosterInventoryRuntime.Configure(stubInventory);
                BoosterEconomyRuntime.Configure(new BoosterEconomyService(economyConfig, stubInventory, stubPersistence));
                GravityLevelRuntime.ResetCampaignState();
                GravityLevelRuntime.ConfigureLevelSequence(sequence);
                GravityLevelRuntime.FindLevelToPlay();

                // Advance from Level 1 to Level 4 (FreezeTimer unlock level).
                Assert.That(GravityLevelRuntime.TryStepLevel(GravityLevelRuntime.LevelStep.Next), Is.True);
                Assert.That(GravityLevelRuntime.TryStepLevel(GravityLevelRuntime.LevelStep.Next), Is.True);
                Assert.That(GravityLevelRuntime.TryStepLevel(GravityLevelRuntime.LevelStep.Next), Is.True);
                Assert.That(GravityLevelRuntime.CurrentLevelNumber, Is.EqualTo(4));

                // Simulate a prior completion saved in PlayerPrefs (e.g., from Editor or previous install).
                BoosterFirstUseTutorialProgress.SetState(freezeConfig, BoosterFirstUseTutorialState.Completed);

                var claimCoordinator = new BoosterFirstUseTutorialCoordinator(null);
                claimCoordinator.BeginClaim(freezeConfig);
                Assert.That(claimCoordinator.IsFirstUseActive(BoosterRewardType.FreezeTimer), Is.True);

                BoosterFirstUseTutorialProgress.SetState(freezeConfig, BoosterFirstUseTutorialState.Completed);
                var resumeCoordinator = new BoosterFirstUseTutorialCoordinator(null);
                resumeCoordinator.ResumePending(new[] { freezeConfig });

                Assert.That(resumeCoordinator.IsFirstUseActive(BoosterRewardType.FreezeTimer), Is.True);
                Assert.That(resumeCoordinator.IsBoosterHighlighted(BoosterRewardType.FreezeTimer), Is.True);
                Assert.That(stubInventory.GetCount(BoosterRewardType.FreezeTimer), Is.GreaterThanOrEqualTo(2));

                // Stepping to an unrelated level must not activate FreezeTimer's first-use tutorial.
                Assert.That(GravityLevelRuntime.TryStepLevel(GravityLevelRuntime.LevelStep.Next), Is.True);
                Assert.That(GravityLevelRuntime.CurrentLevelNumber, Is.EqualTo(5));

                var nextLevelCoordinator = new BoosterFirstUseTutorialCoordinator(null);
                nextLevelCoordinator.ResumePending(new[] { freezeConfig });
                Assert.That(nextLevelCoordinator.IsFirstUseActive(BoosterRewardType.FreezeTimer), Is.False);
            }
            finally
            {
                if (originalTutorialState == BoosterFirstUseTutorialState.NotStarted)
                    BoosterFirstUseTutorialProgress.ClearState(freezeConfig);
                else
                    BoosterFirstUseTutorialProgress.SetState(freezeConfig, originalTutorialState);

                BoosterInventoryRuntime.Configure(new PlayerPrefsBoosterInventory());
                BoosterEconomyRuntime.Configure(new BoosterEconomyService(
                    economyConfig,
                    BoosterInventoryRuntime.Current,
                    new PlayerPrefsBoosterEconomyPersistence()));
                CampaignProgressRuntime.ResetToDefault();
                GravityLevelRuntime.ResetCampaignState();
            }
        }

        [TestCase(PieceSpecialBlockType.Normal)]
        [TestCase(PieceSpecialBlockType.Ice)]
        [TestCase(PieceSpecialBlockType.Bomb)]
        [TestCase(PieceSpecialBlockType.Key)]
        [TestCase(PieceSpecialBlockType.Lock)]
        public void UsesIceCountdown_AllowsIceLayerOnAllBlockTypes(PieceSpecialBlockType blockType)
        {
            Assert.That(PuzzlePiece.UsesIceCountdown(blockType), Is.True);
        }

        private sealed class StubBoosterInventory : IPlayerBoosterInventory
        {
            private int freezeCount;
            public event System.Action<BoosterRewardType, int> CountChanged;

            public int GetCount(BoosterRewardType boosterType) =>
                boosterType == BoosterRewardType.FreezeTimer ? freezeCount : 0;

            public bool IsUnlocked(BoosterRewardType boosterType) => freezeCount > 0;

            public void Grant(BoosterRewardType boosterType, int amount)
            {
                if (boosterType == BoosterRewardType.FreezeTimer)
                    freezeCount += amount;
                CountChanged?.Invoke(boosterType, GetCount(boosterType));
            }

            public bool TryConsume(BoosterRewardType boosterType) => false;
        }

        private sealed class StubEconomyPersistence : IBoosterEconomyPersistence
        {
            private readonly System.Collections.Generic.HashSet<BoosterRewardType> initialClaims =
                new System.Collections.Generic.HashSet<BoosterRewardType>();
            private readonly System.Collections.Generic.HashSet<int> levelClaims =
                new System.Collections.Generic.HashSet<int>();

            public bool HasClaimedInitialUnlock(BoosterRewardType boosterType) => initialClaims.Contains(boosterType);
            public void MarkInitialUnlockClaimed(BoosterRewardType boosterType) => initialClaims.Add(boosterType);
            public bool HasClaimedLevelCompletion(int levelNumber) => levelClaims.Contains(levelNumber);
            public void MarkLevelCompletionClaimed(int levelNumber) => levelClaims.Add(levelNumber);
        }

        private sealed class StubCampaignPersistence : ICampaignProgressPersistence
        {
            private bool hasSaved;
            private int savedIndex;

            public bool TryLoadLevelIndex(out int levelIndex)
            {
                levelIndex = savedIndex;
                return hasSaved;
            }

            public void SaveLevelIndex(int levelIndex)
            {
                savedIndex = levelIndex;
                hasSaved = true;
            }

            public void Clear()
            {
                hasSaved = false;
                savedIndex = 0;
            }
        }
    }
}
