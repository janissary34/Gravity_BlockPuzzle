using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace GravityPuzzle.Tests.PlayMode
{
    public sealed class BoxRevealPresentationPlayModeTests
    {
        private static readonly Type ViewType = Type.GetType("GravityPuzzle.Presentation.Views.BoxRevealAreaView, Assembly-CSharp");
        private static readonly Type KindType = Type.GetType("GravityPuzzle.Gameplay.Reveal.RevealAreaKind, Assembly-CSharp");
        private static readonly Type ConfigType = Type.GetType("GravityPuzzle.Config.RevealPresentationConfig, Assembly-CSharp");
        private static readonly Type LevelType = Type.GetType("GravityPuzzle.GravityLevelDefinition, Assembly-CSharp");
        private static readonly Type BoxDefinitionType = Type.GetType("GravityPuzzle.BoxRevealDefinition, Assembly-CSharp");
        private static readonly Type BoundsType = Type.GetType("GravityPuzzle.RevealAreaBounds, Assembly-CSharp");
        private static readonly Type PieceDefinitionType = Type.GetType("GravityPuzzle.PieceDefinition, Assembly-CSharp");
        private static readonly Type PieceCellDefinitionType = Type.GetType("GravityPuzzle.PieceCellDefinition, Assembly-CSharp");
        private static readonly Type PieceCellType = Type.GetType("GravityPuzzle.PieceCellType, Assembly-CSharp");

        [UnityTest]
        public IEnumerator ProgressRetargetsWithoutMovingTheFrameAndCompletesBeforeUnlock()
        {
            Assert.That(ViewType, Is.Not.Null);
            Assert.That(KindType, Is.Not.Null);
            Assert.That(ConfigType, Is.Not.Null);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/BoxRevealArea.prefab");
            var config = AssetDatabase.LoadAssetAtPath("Assets/Config/RevealPresentationConfig.asset", ConfigType);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(config, Is.Not.Null);
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            Component view = instance.GetComponent(ViewType);
            var initialize = ViewType.GetMethod("Initialize");
            var setRemaining = ViewType.GetMethod("SetBoxRemaining");
            var playOpening = ViewType.GetMethod("PlayOpening");
            var frame = instance.transform.Find("Shutter/Box Frame").GetComponent<SpriteRenderer>();
            var panel = instance.transform.Find("Shutter/Box Moving Shutter").GetComponent<SpriteRenderer>();
            var backing = instance.transform.Find("Shutter/Box Opaque Backing").GetComponent<SpriteRenderer>();
            var properties = new MaterialPropertyBlock();
            bool completed = false;
            try
            {
                Assert.That(frame.sprite, Is.EqualTo(ConfigType.GetField("boxCoverSprite").GetValue(config)),
                    "The static frame must use the authored open-bottom Tunnel sprite from the reveal config.");
                Vector4 tunnelInsets = (Vector4)ConfigType.GetField("boxOpeningInsets").GetValue(config);
                Assert.That(tunnelInsets.x, Is.Zero);
                Assert.That(tunnelInsets.y, Is.Zero);
                Assert.That(tunnelInsets.z, Is.Zero,
                    "The open-bottom Tunnel shutter must extend under the frame at both sides and the bottom.");
                initialize.Invoke(view, new object[] { "test", Vector2.zero, new Vector2(3f, 2f), config });
                Vector2 frameSize = frame.size;
                Vector3 framePosition = frame.transform.localPosition;
                foreach (int remaining in new[] { 4, 3, 2, 1 })
                {
                    setRemaining.Invoke(view, new object[] { remaining, 5 });
                    yield return new WaitForSeconds(.6f);
                    panel.GetPropertyBlock(properties);
                    Assert.That(properties.GetFloat("_Opening"), Is.EqualTo((5f - remaining) / 5f).Within(.01f));
                    Assert.That(frame.size, Is.EqualTo(frameSize));
                    Assert.That(frame.transform.localPosition, Is.EqualTo(framePosition));
                    Assert.That(backing.gameObject.activeSelf, Is.True);
                }

                // Reinitialization also exercises the pooled visual reset. A
                // second target arrives while the first tween is in flight.
                ViewType.GetMethod("OnDespawn").Invoke(view, null);
                initialize.Invoke(view, new object[] { "test", Vector2.zero, new Vector2(2f, 3f), config });
                setRemaining.Invoke(view, new object[] { 2, 3 });
                yield return new WaitForSeconds(.1f);
                panel.GetPropertyBlock(properties);
                float interrupted = properties.GetFloat("_Opening");
                setRemaining.Invoke(view, new object[] { 1, 3 });
                panel.GetPropertyBlock(properties);
                Assert.That(properties.GetFloat("_Opening"), Is.EqualTo(interrupted).Within(.001f));
                yield return new WaitForSeconds(.6f);
                panel.GetPropertyBlock(properties);
                Assert.That(properties.GetFloat("_Opening"), Is.EqualTo(2f / 3f).Within(.01f));

                setRemaining.Invoke(view, new object[] { 0, 3 });
                playOpening.Invoke(view, new object[] { Enum.ToObject(KindType, 0), (Action)(() => completed = true) });
                Assert.That(completed, Is.False);
                Assert.That(backing.gameObject.activeSelf, Is.True);
                yield return new WaitForSeconds(.6f);
                Assert.That(backing.color.a, Is.EqualTo(0f).Within(.01f));
                yield return new WaitForSeconds(1f);
                Assert.That(completed, Is.True);
            }
            finally
            {
                if (instance != null)
                    UnityEngine.Object.Destroy(instance);
            }
        }
        [UnityTest]
        public IEnumerator SimultaneousBurstsRemainIndependentWhenOneViewReturnsToPool()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/BoxRevealArea.prefab");
            var sourceConfig = AssetDatabase.LoadAssetAtPath("Assets/Config/RevealPresentationConfig.asset", ConfigType);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(sourceConfig, Is.Not.Null);
            var config = UnityEngine.Object.Instantiate(sourceConfig);
            ConfigType.GetField("boxOpenDuration").SetValue(config, .15f);
            ConfigType.GetField("boxBreakAnticipationDuration").SetValue(config, .05f);
            ConfigType.GetField("boxBurstDuration").SetValue(config, .35f);
            var first = UnityEngine.Object.Instantiate(prefab);
            var second = UnityEngine.Object.Instantiate(prefab);
            var firstView = first.GetComponent(ViewType);
            var secondView = second.GetComponent(ViewType);
            var initialize = ViewType.GetMethod("Initialize");
            var play = ViewType.GetMethod("PlayOpening");
            var despawn = ViewType.GetMethod("OnDespawn");
            bool firstCompleted = false;
            bool secondCompleted = false;
            try
            {
                initialize.Invoke(firstView, new object[] { "first", Vector2.zero, new Vector2(3f, 2f), config });
                initialize.Invoke(secondView, new object[] { "second", Vector2.right, new Vector2(2f, 3f), config });
                object kind = Enum.ToObject(KindType, 0);
                play.Invoke(firstView, new object[] { kind, (Action)(() => firstCompleted = true) });
                play.Invoke(secondView, new object[] { kind, (Action)(() => secondCompleted = true) });
                yield return new WaitForSeconds(.27f);

                Transform firstFragment = first.transform.Find("Shutter/Burst Fragment 1");
                Transform secondFragment = second.transform.Find("Shutter/Burst Fragment 1");
                Assert.That(firstFragment.gameObject.activeSelf, Is.True);
                Assert.That(secondFragment.gameObject.activeSelf, Is.True);
                Assert.That(firstCompleted, Is.False);
                despawn.Invoke(firstView, null);
                Assert.That(firstFragment.gameObject.activeSelf, Is.False);
                Assert.That(secondFragment.gameObject.activeSelf, Is.True);
                yield return new WaitForSeconds(.5f);
                Assert.That(firstCompleted, Is.False);
                Assert.That(secondCompleted, Is.True);

                initialize.Invoke(firstView, new object[] { "reused", Vector2.zero, new Vector2(2f, 2f), config });
                Assert.That(firstFragment.gameObject.activeSelf, Is.False);
                Assert.That(first.transform.Find("Shutter/Box Frame").GetComponent<SpriteRenderer>().color.a, Is.EqualTo(1f));
                play.Invoke(firstView, new object[] { kind, (Action)(() => firstCompleted = true) });
                yield return new WaitForSeconds(.8f);
                Assert.That(firstCompleted, Is.True);
            }
            finally
            {
                UnityEngine.Object.Destroy(first);
                UnityEngine.Object.Destroy(second);
                UnityEngine.Object.Destroy(config);
            }
        }

        [UnityTest]
        public IEnumerator HiddenPiecePreviewTracksOpeningAndResetsOnPoolReturn()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/BoxRevealArea.prefab");
            var sourceConfig = AssetDatabase.LoadAssetAtPath("Assets/Config/RevealPresentationConfig.asset", ConfigType);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(sourceConfig, Is.Not.Null);
            var config = UnityEngine.Object.Instantiate(sourceConfig);
            Assert.That(LevelType, Is.Not.Null);
            Assert.That(BoxDefinitionType, Is.Not.Null);
            Assert.That(BoundsType, Is.Not.Null);
            Assert.That(PieceDefinitionType, Is.Not.Null);
            Assert.That(PieceCellDefinitionType, Is.Not.Null);
            Assert.That(PieceCellType, Is.Not.Null);
            ScriptableObject level = ScriptableObject.CreateInstance(LevelType);
            LevelType.GetField("boardColumns").SetValue(level, 6);
            LevelType.GetField("boardRows").SetValue(level, 6);
            LevelType.GetField("subdivisions").SetValue(level, 4);
            object definition = Activator.CreateInstance(BoxDefinitionType);
            object bounds = Activator.CreateInstance(BoundsType);
            BoundsType.GetField("origin").SetValue(bounds, new Vector2Int(4, 4));
            BoundsType.GetField("size").SetValue(bounds, new Vector2Int(8, 8));
            BoxDefinitionType.GetField("bounds").SetValue(definition, bounds);
            IList hiddenPieces = CreateTypedList(PieceDefinitionType);
            BoxDefinitionType.GetField("hiddenPieces").SetValue(definition, hiddenPieces);
            object piece = Activator.CreateInstance(PieceDefinitionType);
            PieceDefinitionType.GetField("origin").SetValue(piece, new Vector2Int(6, 6));
            PieceDefinitionType.GetField("color").SetValue(piece, Color.red);
            IList cells = CreateTypedList(PieceCellDefinitionType);
            PieceDefinitionType.GetField("cells").SetValue(piece, cells);
            object blockType = Enum.ToObject(PieceCellType, 0);
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                    cells.Add(Activator.CreateInstance(PieceCellDefinitionType, new Vector2Int(x, y), blockType));
            }
            hiddenPieces.Add(piece);

            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            Component view = instance.GetComponent(ViewType);
            MethodInfo initialize = ViewType.GetMethod("Initialize");
            MethodInfo configurePreview = ViewType.GetMethod("ConfigureBoxPreview");
            MethodInfo applyShutterHeight = ViewType.GetMethod(
                "ApplyShutterHeight",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo playOpening = ViewType.GetMethod("PlayOpening");
            MethodInfo despawn = ViewType.GetMethod("OnDespawn");
            Transform preview = instance.transform.Find("Shutter/Preview Content/Preview Slot 1");
            Transform shadow = instance.transform.Find("Shutter/Preview Content/Preview Shadow Slot 1");
            Transform mask = instance.transform.Find("Shutter/Preview Content/Aperture Mask");
            try
            {
                Assert.That(configurePreview, Is.Not.Null);
                Assert.That(applyShutterHeight, Is.Not.Null);
                initialize.Invoke(view, new object[] { "preview", Vector2.zero, new Vector2(2f, 2f), config });
                configurePreview.Invoke(view, new object[] { level, definition });

                var previewRenderer = preview.GetComponent<SpriteRenderer>();
                var shadowRenderer = shadow.GetComponent<SpriteRenderer>();
                Assert.That(preview.gameObject.activeSelf, Is.True);
                Assert.That(previewRenderer.sprite, Is.Not.Null);
                Assert.That(previewRenderer.maskInteraction, Is.EqualTo(SpriteMaskInteraction.VisibleInsideMask));
                Assert.That(shadow.gameObject.activeSelf, Is.True);
                Assert.That(shadowRenderer.sprite, Is.EqualTo(previewRenderer.sprite));
                Assert.That(shadowRenderer.sortingOrder, Is.LessThan(previewRenderer.sortingOrder));
                Assert.That(shadowRenderer.color.a, Is.GreaterThan(0f));
                SpriteMask previewMask = mask.GetComponent<SpriteMask>();
                Assert.That(previewMask.sprite, Is.Not.Null);
                Assert.That(previewMask.isCustomRangeActive, Is.True);
                Assert.That(previewMask.backSortingOrder, Is.LessThan(previewRenderer.sortingOrder));
                Assert.That(previewMask.frontSortingOrder, Is.GreaterThan(previewRenderer.sortingOrder));

                Vector3 recessedPosition = preview.localPosition;
                Vector3 recessedScale = preview.localScale;
                applyShutterHeight.Invoke(view, new object[] { 1f });
                Vector3 partialScale = preview.localScale;
                applyShutterHeight.Invoke(view, new object[] { 0f });
                Vector3 openPosition = preview.localPosition;
                Vector3 openScale = preview.localScale;

                Assert.That(partialScale.x, Is.GreaterThan(recessedScale.x));
                Assert.That(partialScale.x, Is.LessThan(openScale.x));
                Assert.That(openPosition.y, Is.GreaterThan(recessedPosition.y));

                playOpening.Invoke(view, new object[] { Enum.ToObject(KindType, 0), (Action)null });
                float burstStart = (float)ConfigType.GetField("boxOpenDuration").GetValue(config) +
                    (float)ConfigType.GetField("boxBreakAnticipationDuration").GetValue(config);
                yield return new WaitForSeconds(burstStart + .1f);
                Assert.That(shadowRenderer.gameObject.activeSelf, Is.False);
                Assert.That(preview.gameObject.activeSelf, Is.True);
                Assert.That(previewRenderer.maskInteraction, Is.EqualTo(SpriteMaskInteraction.None));
                Assert.That(mask.gameObject.activeSelf, Is.False);

                despawn.Invoke(view, null);
                Assert.That(preview.gameObject.activeSelf, Is.False);
                Assert.That(shadow.gameObject.activeSelf, Is.False);
                Assert.That(mask.gameObject.activeSelf, Is.True);
            }
            finally
            {
                UnityEngine.Object.Destroy(instance);
                UnityEngine.Object.Destroy(config);
                UnityEngine.Object.Destroy(level);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator PreviewOwnershipAndExposureCallbackAreScopedPerHiddenDefinition()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/BoxRevealArea.prefab");
            var sourceConfig = AssetDatabase.LoadAssetAtPath("Assets/Config/RevealPresentationConfig.asset", ConfigType);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(sourceConfig, Is.Not.Null);
            var config = UnityEngine.Object.Instantiate(sourceConfig);
            ScriptableObject level = ScriptableObject.CreateInstance(LevelType);
            LevelType.GetField("boardColumns").SetValue(level, 4);
            LevelType.GetField("boardRows").SetValue(level, 4);
            LevelType.GetField("subdivisions").SetValue(level, 1);
            object definition = Activator.CreateInstance(BoxDefinitionType);
            object bounds = Activator.CreateInstance(BoundsType);
            BoundsType.GetField("origin").SetValue(bounds, Vector2Int.zero);
            BoundsType.GetField("size").SetValue(bounds, new Vector2Int(2, 2));
            BoxDefinitionType.GetField("bounds").SetValue(definition, bounds);
            IList hiddenPieces = CreateTypedList(PieceDefinitionType);
            BoxDefinitionType.GetField("hiddenPieces").SetValue(definition, hiddenPieces);
            hiddenPieces.Add(CreateSingleBlockPiece(new Vector2Int(0, 0), Color.red));
            hiddenPieces.Add(CreateSingleBlockPiece(new Vector2Int(0, 1), Color.blue));

            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            Component view = instance.GetComponent(ViewType);
            MethodInfo initialize = ViewType.GetMethod("Initialize");
            MethodInfo configurePreview = ViewType.GetMethod("ConfigureBoxPreview");
            MethodInfo setExposure = ViewType.GetMethod("SetBoxExposureChangedHandler");
            MethodInfo canPresent = ViewType.GetMethod("CanPresentContent");
            MethodInfo setPreviewVisible = ViewType.GetMethod("SetContentPreviewVisible");
            MethodInfo applyShutterHeight = ViewType.GetMethod(
                "ApplyShutterHeight",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Transform preview = instance.transform.Find("Shutter/Preview Content/Preview Slot 1");
            try
            {
                initialize.Invoke(view, new object[] { "ownership", Vector2.zero, new Vector2(2f, 2f), config });
                configurePreview.Invoke(view, new object[] { level, definition });
                Assert.That((bool)canPresent.Invoke(view, new object[] { 0 }), Is.True);
                Assert.That((bool)canPresent.Invoke(view, new object[] { 1 }), Is.True);

                float observedExposure = -1f;
                setExposure.Invoke(view, new object[] { (Action<float>)(value => observedExposure = value) });
                applyShutterHeight.Invoke(view, new object[] { 1f });
                Assert.That(observedExposure, Is.EqualTo(.5f).Within(.001f));

                setPreviewVisible.Invoke(view, new object[] { 0, false });
                Assert.That(preview.gameObject.activeSelf, Is.False);
                setPreviewVisible.Invoke(view, new object[] { 0, true });
                Assert.That(preview.gameObject.activeSelf, Is.True);
            }
            finally
            {
                UnityEngine.Object.Destroy(instance);
                UnityEngine.Object.Destroy(config);
                UnityEngine.Object.Destroy(level);
            }
            yield return null;
        }

        private static IList CreateTypedList(Type itemType)
        {
            return (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType));
        }

        private static object CreateSingleBlockPiece(Vector2Int origin, Color color)
        {
            object piece = Activator.CreateInstance(PieceDefinitionType);
            PieceDefinitionType.GetField("origin").SetValue(piece, origin);
            PieceDefinitionType.GetField("color").SetValue(piece, color);
            IList cells = CreateTypedList(PieceCellDefinitionType);
            PieceDefinitionType.GetField("cells").SetValue(piece, cells);
            cells.Add(Activator.CreateInstance(
                PieceCellDefinitionType,
                Vector2Int.zero,
                Enum.ToObject(PieceCellType, 0)));
            return piece;
        }
    }
}
