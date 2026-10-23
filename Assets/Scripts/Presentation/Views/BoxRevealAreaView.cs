using System;
using System.Collections.Generic;
using DG.Tweening;
using GravityPuzzle.Config;
using GravityPuzzle.Gameplay.Pieces;
using GravityPuzzle.Gameplay.Reveal;
using GravityPuzzle.Infrastructure.Pooling;
using TMPro;
using UnityEngine;

namespace GravityPuzzle.Presentation.Views
{
    public sealed class BoxRevealAreaView : MonoBehaviour, IRevealAreaPresentation, IBoxRevealExposurePresentation, IPoolable, IPoolReturnReceiver<BoxRevealAreaView>
    {
        private const int BoxBurstTopCount = 6;
        private const int BoxBurstFragmentCount = 12;
        private const float BurstRiseFraction = .35f;
        private const float BurstFadeStartFraction = .45f;
        private static readonly int OpeningId = Shader.PropertyToID("_Opening");
        private static readonly int ContentUvRectId = Shader.PropertyToID("_ContentUvRect");
        private static readonly int SpriteUvId = Shader.PropertyToID("_SpriteUV");
        private static readonly int FragmentUvId = Shader.PropertyToID("_FragmentUV");

        [SerializeField] private Transform boxShutter;
        [SerializeField] private SpriteRenderer boxCoverRenderer;
        [SerializeField] private SpriteRenderer boxBackingRenderer;
        [SerializeField] private SpriteRenderer boxPanelRenderer;
        [SerializeField] private TMP_Text boxCounter;
        [SerializeField] private SpriteMask previewMask;
        [SerializeField] private SpriteRenderer[] boxPreviewShadowRenderers;
        [SerializeField] private SpriteRenderer[] boxPreviewRenderers;
        [SerializeField] private Transform[] boxBurstFragments;
        [SerializeField] private SpriteRenderer[] boxBurstRenderers;

        private Action<BoxRevealAreaView> returnHandler;
        private RevealPresentationConfig config;
        private Sequence openingSequence;
        private Tween progressTween;
        private float currentHeight;
        private float apertureHeight;
        private Vector4 panelSourceUvRect;
        private MaterialPropertyBlock panelProperties;
        private MaterialPropertyBlock fragmentProperties;
        private Action completion;
        private bool isComplete;
        private Vector3 shutterClosedPosition;
        private Vector3 coverClosedPosition;
        private Vector3 counterClosedPosition;
        private Vector2 authoredSize;
        private MeshRenderer counterRenderer;
        private Vector3[] fragmentClosedPositions;
        private Quaternion[] fragmentClosedRotations;
        private Vector3[] fragmentClosedScales;
        private readonly List<RuntimePiecePreviewVisual> previewVisuals = new List<RuntimePiecePreviewVisual>();
        private readonly List<RuntimePiecePreviewVisual> previewPieceVisuals = new List<RuntimePiecePreviewVisual>();
        private readonly List<PreviewContentRange> previewContentRanges = new List<PreviewContentRange>();
        private Vector3[] previewBasePositions;
        private Vector3[] previewBaseScales;
        private Quaternion[] previewBaseRotations;
        private Color[] previewBaseColors;
        private int activePreviewCount;
        private float previewShadowBurstAlpha = 1f;
        private Action<float> exposureChanged;

        public string AreaId { get; private set; }
        private readonly struct PreviewContentRange
        {
            public PreviewContentRange(int firstSlot, int count)
            {
                FirstSlot = firstSlot;
                Count = count;
            }

            public int FirstSlot { get; }
            public int Count { get; }
        }

        private void Awake()
        {
            panelProperties = new MaterialPropertyBlock();
            fragmentProperties = new MaterialPropertyBlock();
            if (boxShutter != null)
                shutterClosedPosition = boxShutter.localPosition;
            if (boxCoverRenderer != null)
                coverClosedPosition = boxCoverRenderer.transform.localPosition;
            if (boxCounter != null)
            {
                counterClosedPosition = boxCounter.transform.localPosition;
                counterRenderer = boxCounter.GetComponent<MeshRenderer>();
            }

            if (boxBurstFragments == null)
                CachePreviewState();
            else
            {
                fragmentClosedPositions = new Vector3[boxBurstFragments.Length];
                fragmentClosedRotations = new Quaternion[boxBurstFragments.Length];
                fragmentClosedScales = new Vector3[boxBurstFragments.Length];
                for (int index = 0; index < boxBurstFragments.Length; index++)
                {
                    Transform fragment = boxBurstFragments[index];
                    if (fragment == null)
                        continue;

                    fragmentClosedPositions[index] = fragment.localPosition;
                    fragmentClosedRotations[index] = fragment.localRotation;
                    fragmentClosedScales[index] = fragment.localScale;
                }

                CachePreviewState();
            }
        }

        public void SetPoolReturnHandler(Action<BoxRevealAreaView> handler)
        {
            returnHandler = handler;
        }

        public void Initialize(
            string areaId,
            Vector2 worldPosition,
            Vector2 size,
            RevealPresentationConfig presentationConfig)
        {
            AreaId = areaId;
            config = presentationConfig;
            authoredSize = size;
            currentHeight = Mathf.Max(0f, size.y);
            previewShadowBurstAlpha = 1f;
            transform.position = new Vector3(worldPosition.x, worldPosition.y, 0f);

            if (boxShutter != null)
            {
                boxShutter.localPosition = shutterClosedPosition;
                boxShutter.gameObject.SetActive(true);
            }

            if (boxCoverRenderer != null)
            {
                boxCoverRenderer.transform.localPosition = coverClosedPosition;
                boxCoverRenderer.transform.localScale = Vector3.one;
                boxCoverRenderer.gameObject.SetActive(true);
                Color c = boxCoverRenderer.color;
                c.a = 1f;
                boxCoverRenderer.color = c;
                if (boxCoverRenderer.sprite != null && boxCoverRenderer.sprite.border != Vector4.zero)
                {
                    float thickness = config != null ? Mathf.Max(.01f, config.boxFrameThickness) : 1f;
                    boxCoverRenderer.drawMode = SpriteDrawMode.Sliced;
                    boxCoverRenderer.size = size / thickness;
                    boxCoverRenderer.transform.localScale = new Vector3(thickness, thickness, 1f);
                }
                else
                {
                    boxCoverRenderer.drawMode = SpriteDrawMode.Simple;
                    Vector2 spriteSize = boxCoverRenderer.sprite != null
                        ? boxCoverRenderer.sprite.rect.size / boxCoverRenderer.sprite.pixelsPerUnit
                        : size;
                    boxCoverRenderer.transform.localScale = new Vector3(
                        spriteSize.x > 0f ? size.x / spriteSize.x : 1f,
                        spriteSize.y > 0f ? size.y / spriteSize.y : 1f,
                        1f);
                }

                if (config != null)
                    boxCoverRenderer.sortingOrder = config.boxSortingOrder;
            }

            ConfigureLayers(size);
            ApplyShutterHeight(currentHeight);

            if (boxCounter != null)
            {
                boxCounter.transform.localPosition = counterClosedPosition +
                    (config != null ? (Vector3)config.boxCounterOffset : Vector3.zero);
                boxCounter.gameObject.SetActive(true);
                if (config != null)
                {
                    boxCounter.fontSize = config.boxCounterFontSize > 0f ? config.boxCounterFontSize : 0.55f;
                    if (config.boxCounterFont != null)
                        boxCounter.font = config.boxCounterFont;
                    boxCounter.color = config.boxCounterTextColor;
                    boxCounter.outlineColor = config.boxCounterOutlineColor;
                    boxCounter.outlineWidth = config.boxCounterOutlineWidth;
                    if (counterRenderer != null)
                        counterRenderer.sortingOrder = config.boxCounterSortingOrder;
                }
            }

            if (boxBurstFragments != null)
            {
                for (int i = 0; i < boxBurstFragments.Length; i++)
                {
                    if (boxBurstFragments[i] != null)
                    {
                        boxBurstFragments[i].localPosition = fragmentClosedPositions != null &&
                            i < fragmentClosedPositions.Length ? fragmentClosedPositions[i] : Vector3.zero;
                        boxBurstFragments[i].localRotation = fragmentClosedRotations != null &&
                            i < fragmentClosedRotations.Length ? fragmentClosedRotations[i] : Quaternion.identity;
                        boxBurstFragments[i].localScale = fragmentClosedScales != null &&
                            i < fragmentClosedScales.Length ? fragmentClosedScales[i] : Vector3.one;
                        boxBurstFragments[i].gameObject.SetActive(false);
                    }
                }
            }

            isComplete = false;
        }

        public void ConfigureBoxPreview(GravityLevelDefinition level, BoxRevealDefinition definition)
        {
            ClearPreviewVisuals();
            if (level == null || definition == null || definition.hiddenPieces == null ||
                boxPreviewRenderers == null || boxPreviewRenderers.Length == 0)
            {
                return;
            }

            previewVisuals.Clear();
            for (int pieceIndex = 0; pieceIndex < definition.hiddenPieces.Count; pieceIndex++)
            {
                PieceDefinition piece = definition.hiddenPieces[pieceIndex];
                int firstSlot = previewVisuals.Count;
                previewPieceVisuals.Clear();
                if (piece != null)
                {
                    RuntimePieceFactory.AppendPreviewVisuals(level, piece, previewPieceVisuals);
                    Vector2 pieceOrigin = new Vector2(
                        -level.boardColumns * .5f +
                        (piece.origin.x + .5f) / Mathf.Max(1, level.subdivisions),
                        -level.boardRows * .5f +
                        (piece.origin.y + .5f) / Mathf.Max(1, level.subdivisions));
                    Vector2 localOrigin = pieceOrigin - (Vector2)transform.position;
                    for (int visualIndex = 0; visualIndex < previewPieceVisuals.Count; visualIndex++)
                    {
                        RuntimePiecePreviewVisual visual = previewPieceVisuals[visualIndex];
                        previewVisuals.Add(new RuntimePiecePreviewVisual(
                            visual.Sprite,
                            visual.Material,
                            visual.Color,
                            localOrigin + visual.LocalPosition,
                            visual.LocalScale,
                            visual.LocalRotation,
                            visual.DrawMode,
                            visual.Size,
                            visual.FlipX));
                    }
                }

                previewContentRanges.Add(new PreviewContentRange(
                    firstSlot,
                    previewVisuals.Count - firstSlot));
            }

            if (previewVisuals.Count > boxPreviewRenderers.Length)
            {
                Debug.LogWarning(
                    $"[BoxRevealAreaView] Box '{AreaId}' needs {previewVisuals.Count} preview slots but its pooled view has {boxPreviewRenderers.Length}. " +
                    "Increase Box Preview Slot Capacity and refresh the Box reveal prefab.",
                    this);
            }

            int count = Mathf.Min(previewVisuals.Count, boxPreviewRenderers.Length);
            for (int index = 0; index < count; index++)
            {
                SpriteRenderer renderer = boxPreviewRenderers[index];
                if (renderer == null)
                    continue;

                RuntimePiecePreviewVisual visual = previewVisuals[index];
                renderer.sprite = visual.Sprite;
                renderer.sharedMaterial = visual.Material;
                renderer.color = visual.Color;
                renderer.drawMode = visual.DrawMode;
                renderer.size = visual.Size;
                renderer.flipX = visual.FlipX;
                renderer.flipY = false;
                renderer.sortingOrder = config != null ? config.boxSortingOrder - 2 : 198;
                renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
                renderer.transform.localPosition = visual.LocalPosition;
                renderer.transform.localScale = visual.LocalScale;
                renderer.transform.localRotation = visual.LocalRotation;
                renderer.gameObject.SetActive(true);

                if (boxPreviewShadowRenderers != null && index < boxPreviewShadowRenderers.Length)
                {
                    SpriteRenderer shadowRenderer = boxPreviewShadowRenderers[index];
                    if (shadowRenderer != null)
                    {
                        shadowRenderer.sprite = visual.Sprite;
                        shadowRenderer.sharedMaterial = visual.Material;
                        shadowRenderer.drawMode = visual.DrawMode;
                        shadowRenderer.size = visual.Size;
                        shadowRenderer.flipX = visual.FlipX;
                        shadowRenderer.flipY = false;
                        shadowRenderer.sortingOrder = config != null ? config.boxSortingOrder - 3 : 197;
                        shadowRenderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
                        shadowRenderer.transform.localPosition = visual.LocalPosition;
                        shadowRenderer.transform.localScale = visual.LocalScale;
                        shadowRenderer.transform.localRotation = visual.LocalRotation;
                        shadowRenderer.gameObject.SetActive(true);
                    }
                }

                previewBasePositions[index] = renderer.transform.localPosition;
                previewBaseScales[index] = renderer.transform.localScale;
                previewBaseRotations[index] = renderer.transform.localRotation;
                previewBaseColors[index] = visual.Color;
                activePreviewCount = Mathf.Max(activePreviewCount, index + 1);
            }

            ApplyPreviewDepth(authoredSize.y > 0f ? 1f - currentHeight / authoredSize.y : 0f);
        }

        public void SetBoxExposureChangedHandler(Action<float> handler)
        {
            exposureChanged = handler;
        }

        public bool CanPresentContent(int contentIndex)
        {
            if (contentIndex < 0 || contentIndex >= previewContentRanges.Count ||
                boxPreviewRenderers == null)
            {
                return false;
            }

            PreviewContentRange range = previewContentRanges[contentIndex];
            return range.Count > 0 && range.FirstSlot + range.Count <= boxPreviewRenderers.Length;
        }

        public void SetContentPreviewVisible(int contentIndex, bool visible)
        {
            if (!CanPresentContent(contentIndex))
                return;

            PreviewContentRange range = previewContentRanges[contentIndex];
            for (int index = range.FirstSlot; index < range.FirstSlot + range.Count; index++)
            {
                SpriteRenderer renderer = boxPreviewRenderers[index];
                if (renderer != null)
                    renderer.gameObject.SetActive(visible);

                if (boxPreviewShadowRenderers != null && index < boxPreviewShadowRenderers.Length &&
                    boxPreviewShadowRenderers[index] != null)
                {
                    boxPreviewShadowRenderers[index].gameObject.SetActive(visible);
                }
            }

            if (visible)
                ApplyPreviewDepth(authoredSize.y > 0f ? 1f - currentHeight / authoredSize.y : 0f);
        }

        public void SetBoxRemaining(int remaining, int target)
        {
            if (boxCounter != null)
                boxCounter.SetText("{0}", Mathf.Max(0, remaining));

            // The final step is owned by PlayOpening so the burst and unlock
            // completion follow the same uninterrupted animation.
            if (remaining <= 0 || target <= 0 || config == null)
                return;

            float height = authoredSize.y * Mathf.Clamp01((float)remaining / target);
            progressTween?.Kill();
            if (Mathf.Approximately(currentHeight, height))
                return;

            progressTween = DOVirtual.Float(currentHeight, height, config.boxOpenDuration, ApplyShutterHeight)
                .SetEase(config.boxOpenEase)
                .SetLink(gameObject, LinkBehaviour.KillOnDisable)
                .SetAutoKill(true);
        }

        public void PlayOpening(RevealAreaKind requestedKind, Action onCompleted)
        {
            completion = onCompleted;
            isComplete = false;
            progressTween?.Kill();
            progressTween = null;
            openingSequence?.Kill();

            if (config == null || boxShutter == null)
            {
                CompleteOpening();
                return;
            }

            PrepareBoxBurstFragments();

            openingSequence = DOTween.Sequence()
                .SetLink(gameObject, LinkBehaviour.KillOnDisable)
                .SetAutoKill(true);
            openingSequence.Append(DOVirtual.Float(currentHeight, 0f, config.boxOpenDuration, ApplyShutterHeight)
                .SetEase(config.boxOpenEase).SetLink(gameObject, LinkBehaviour.KillOnDisable).SetAutoKill(true));
            if (boxCoverRenderer != null && config.boxBreakAnticipationDuration > 0f)
            {
                Vector3 scale = boxCoverRenderer.transform.localScale;
                openingSequence.Append(boxCoverRenderer.transform.DOPunchScale(
                    scale * config.boxBreakAnticipationScale,
                    config.boxBreakAnticipationDuration, 2, 0f).SetLink(gameObject, LinkBehaviour.KillOnDisable).SetAutoKill(true));
            }

            openingSequence.AppendCallback(BeginBoxBurst);
            float burstStart = openingSequence.Duration();
            openingSequence.AppendInterval(config.boxBurstDuration);
            if (boxCoverRenderer != null)
                openingSequence.Insert(burstStart, boxCoverRenderer.DOFade(0f, config.boxBurstDuration * BurstRiseFraction)
                    .SetLink(gameObject, LinkBehaviour.KillOnDisable).SetAutoKill(true));
            if (boxPreviewShadowRenderers != null && activePreviewCount > 0)
            {
                openingSequence.Insert(burstStart, DOVirtual.Float(
                        previewShadowBurstAlpha,
                        0f,
                        config.boxBurstDuration * config.boxPreviewShadowBurstFadeFraction,
                        ApplyPreviewShadowBurstAlpha)
                    .SetEase(Ease.OutQuad)
                    .SetLink(gameObject, LinkBehaviour.KillOnDisable)
                    .SetAutoKill(true));
            }
            AddBoxFragmentTweens(openingSequence, burstStart);
            openingSequence.OnComplete(CompleteOpening);
        }

        private void ConfigureLayers(Vector2 size)
        {
            if (boxCoverRenderer == null || boxCoverRenderer.sprite == null ||
                boxBackingRenderer == null || boxBackingRenderer.sprite == null || boxPanelRenderer == null ||
                boxPanelRenderer.sprite == null || boxPanelRenderer.sharedMaterial == null || config == null)
            {
                Debug.LogError($"[BoxRevealAreaView] Missing box presentation layers or config on '{name}'.", this);
                return;
            }

            Vector2 panelSize = Vector2.Scale(size, config.boxPanelSizeMultiplier);
            if (panelSize.x <= 0f || panelSize.y <= 0f)
            {
                Debug.LogError($"[BoxRevealAreaView] Box '{AreaId}' has a non-positive panel size.", this);
                return;
            }

            panelSourceUvRect = GetValidatedPanelSourceUvRect(config.boxPanelSourceUvRect);
            Vector3 panelPosition = coverClosedPosition + (Vector3)config.boxPanelOffset;
            apertureHeight = panelSize.y;

            boxBackingRenderer.transform.localPosition = panelPosition;
            boxBackingRenderer.drawMode = SpriteDrawMode.Simple;
            Vector2 backingSize = boxBackingRenderer.sprite.bounds.size;
            boxBackingRenderer.transform.localScale = new Vector3(panelSize.x / backingSize.x, panelSize.y / backingSize.y, 1f);
            boxBackingRenderer.color = config.boxBackingColor;
            boxBackingRenderer.sortingOrder = config.boxSortingOrder - 4;
            boxBackingRenderer.gameObject.SetActive(true);

            boxPanelRenderer.transform.localPosition = panelPosition;
            boxPanelRenderer.drawMode = SpriteDrawMode.Simple;
            Vector2 sourcePanelSize = boxPanelRenderer.sprite.bounds.size;
            boxPanelRenderer.transform.localScale = new Vector3(
                panelSize.x / sourcePanelSize.x,
                panelSize.y / sourcePanelSize.y,
                1f);
            boxPanelRenderer.sortingOrder = config.boxSortingOrder - 1;
            boxPanelRenderer.gameObject.SetActive(true);

            if (previewMask != null)
            {
                previewMask.transform.localPosition = panelPosition;
                Vector2 maskSize = previewMask.sprite != null ? previewMask.sprite.bounds.size : Vector2.one;
                previewMask.transform.localScale = new Vector3(panelSize.x / maskSize.x, panelSize.y / maskSize.y, 1f);
                previewMask.transform.localRotation = boxPanelRenderer.transform.localRotation;
                previewMask.isCustomRangeActive = true;
                // SpriteMask's custom range has to straddle the preview
                // sorting order. A collapsed range at the preview's order
                // rejects renderers using the real piece Shader Graph
                // material on some URP versions, leaving only the backing
                // visible through an opened shutter.
                previewMask.backSortingOrder = config.boxSortingOrder - 4;
                previewMask.frontSortingOrder = config.boxSortingOrder - 1;
                previewMask.gameObject.SetActive(true);
            }
        }

        private void ApplyShutterHeight(float height)
        {
            currentHeight = height;
            float opening = authoredSize.y > 0f ? 1f - Mathf.Clamp01(height / authoredSize.y) : 0f;
            if (boxPanelRenderer != null)
            {
                boxPanelRenderer.GetPropertyBlock(panelProperties);
                panelProperties.SetFloat(OpeningId, opening);
                panelProperties.SetVector(ContentUvRectId, panelSourceUvRect);
                boxPanelRenderer.SetPropertyBlock(panelProperties);
            }

            if (boxBackingRenderer != null && config != null)
            {
                float backingFade = Mathf.InverseLerp(config.boxBackingFadeStart, 1f, opening);
                Color backingColor = config.boxBackingColor;
                backingColor.a *= 1f - backingFade;
                boxBackingRenderer.color = backingColor;
            }

            if (boxCounter != null)
                boxCounter.transform.localPosition = counterClosedPosition +
                    (config != null ? (Vector3)config.boxCounterOffset : Vector3.zero) +
                    Vector3.up * (opening * apertureHeight * .5f);

            ApplyPreviewDepth(opening);
            exposureChanged?.Invoke(opening);
        }

        private void ApplyPreviewDepth(float opening)
        {
            if (activePreviewCount == 0 || boxPreviewRenderers == null)
                return;

            float progress = Mathf.Clamp01(opening);
            float scale = config != null
                ? Mathf.Lerp(config.boxPreviewRecessedScale, 1f, progress)
                : 1f;
            float recess = config != null ? config.boxPreviewRecessDistance * (1f - progress) : 0f;
            float brightness = config != null
                ? Mathf.Lerp(config.boxPreviewRecessedBrightness, 1f, progress)
                : 1f;
            for (int index = 0; index < activePreviewCount; index++)
            {
                SpriteRenderer renderer = boxPreviewRenderers[index];
                if (renderer == null || !renderer.gameObject.activeSelf)
                    continue;

                renderer.transform.localPosition = previewBasePositions[index] + Vector3.down * recess;
                renderer.transform.localScale = previewBaseScales[index] * scale;
                renderer.transform.localRotation = previewBaseRotations[index];
                Color color = previewBaseColors[index];
                renderer.color = new Color(
                    color.r * brightness,
                    color.g * brightness,
                    color.b * brightness,
                    color.a);

                if (boxPreviewShadowRenderers == null || index >= boxPreviewShadowRenderers.Length)
                    continue;

                SpriteRenderer shadowRenderer = boxPreviewShadowRenderers[index];
                if (shadowRenderer == null || !shadowRenderer.gameObject.activeSelf)
                    continue;

                Vector2 shadowOffset = config != null ? config.boxPreviewShadowOffset : Vector2.zero;
                shadowRenderer.transform.localPosition = previewBasePositions[index] +
                    Vector3.down * recess + (Vector3)shadowOffset;
                shadowRenderer.transform.localScale = previewBaseScales[index] * scale *
                    (config != null ? config.boxPreviewShadowScale : 1f);
                shadowRenderer.transform.localRotation = previewBaseRotations[index];
                float shadowFade = config != null
                    ? 1f - Mathf.InverseLerp(config.boxBackingFadeStart, 1f, progress)
                    : 1f;
                Color shadowColor = config != null ? config.boxPreviewShadowColor : Color.clear;
                shadowColor.a *= shadowFade * previewShadowBurstAlpha;
                shadowRenderer.color = shadowColor;
            }
        }

        private void ApplyPreviewShadowBurstAlpha(float alpha)
        {
            previewShadowBurstAlpha = Mathf.Clamp01(alpha);
            ApplyPreviewDepth(authoredSize.y > 0f ? 1f - currentHeight / authoredSize.y : 0f);
        }

        private void PrepareBoxBurstFragments()
        {
            if (boxBurstFragments == null || boxBurstRenderers == null || config == null)
                return;

            float width = Mathf.Max(0f, authoredSize.x);
            float height = Mathf.Max(0f, authoredSize.y);
            float thickness = Mathf.Min(Mathf.Min(width, height), Mathf.Max(0f, config.boxBurstResidualHeight));
            int count = Mathf.Min(BoxBurstFragmentCount, Mathf.Min(boxBurstFragments.Length, boxBurstRenderers.Length));
            for (int index = 0; index < count; index++)
            {
                Transform fragment = boxBurstFragments[index];
                SpriteRenderer renderer = boxBurstRenderers[index];
                if (fragment == null || renderer == null)
                    continue;

                Vector2 position;
                Vector2 fragmentSize;
                Vector4 sourceRegion;
                if (index < BoxBurstTopCount)
                {
                    position = new Vector2(-width * .5f + width * (index + .5f) / BoxBurstTopCount, (height - thickness) * .5f);
                    fragmentSize = new Vector2(width / BoxBurstTopCount, thickness);
                    sourceRegion = config.boxFragmentTopRegion;
                }
                else if (index < 10)
                {
                    int sideIndex = index - BoxBurstTopCount;
                    float side = sideIndex < 2 ? -1f : 1f;
                    float row = sideIndex % 2 == 0 ? -.25f : .25f;
                    position = new Vector2(side * (width - thickness) * .5f, row * height);
                    fragmentSize = new Vector2(thickness, height * .35f);
                    sourceRegion = side < 0f ? config.boxFragmentLeftRegion : config.boxFragmentRightRegion;
                }
                else
                {
                    position = new Vector2((index == 10 ? -.25f : .25f) * width, -(height - thickness) * .5f);
                    fragmentSize = new Vector2(width * .35f, thickness);
                    sourceRegion = config.boxFragmentBottomRegion;
                }

                float sizeVariation = Mathf.Lerp(.65f, 1f, GetFragmentUnitValue(index, 5));
                fragment.localPosition = coverClosedPosition + (Vector3)position;
                float edgeScale = IsEdgeFragment(index) ? config.boxBurstEdgeFragmentScale : 1f;
                renderer.size = fragmentSize * sizeVariation * edgeScale;
                renderer.sortingOrder = config.boxSortingOrder + 1;
                ConfigureFragmentArtwork(renderer, sourceRegion);
            }
        }

        private void ConfigureFragmentArtwork(SpriteRenderer renderer, Vector4 region)
        {
            Sprite sprite = renderer.sprite;
            if (sprite == null || sprite.texture == null)
            {
                Debug.LogWarning($"[BoxRevealAreaView] Missing burst fragment sprite on '{name}'.", this);
                return;
            }

            Rect rect = sprite.textureRect;
            Vector4 spriteUv = new Vector4(
                rect.x / sprite.texture.width, rect.y / sprite.texture.height,
                rect.width / sprite.texture.width, rect.height / sprite.texture.height);
            Vector4 fragmentUv = new Vector4(
                spriteUv.x + region.x * spriteUv.z, spriteUv.y + region.y * spriteUv.w,
                region.z * spriteUv.z, region.w * spriteUv.w);
            renderer.GetPropertyBlock(fragmentProperties);
            fragmentProperties.SetVector(SpriteUvId, spriteUv);
            fragmentProperties.SetVector(FragmentUvId, fragmentUv);
            renderer.SetPropertyBlock(fragmentProperties);
        }

        private void BeginBoxBurst()
        {
            if (boxCounter != null)
                boxCounter.gameObject.SetActive(false);
            if (boxPanelRenderer != null)
                boxPanelRenderer.gameObject.SetActive(false);
            if (boxBackingRenderer != null)
                boxBackingRenderer.gameObject.SetActive(false);
            ReleasePreviewMasking();
            if (previewMask != null)
                previewMask.gameObject.SetActive(false);
            DisablePreviewShadows();
            if (boxBurstFragments == null || boxBurstRenderers == null)
                return;

            int count = Mathf.Min(BoxBurstFragmentCount, Mathf.Min(boxBurstFragments.Length, boxBurstRenderers.Length));
            for (int index = 0; index < count; index++)
            {
                Transform fragment = boxBurstFragments[index];
                SpriteRenderer renderer = boxBurstRenderers[index];
                if (fragment == null || renderer == null)
                    continue;

                Color color = renderer.color;
                color.a = 1f;
                renderer.color = color;
                fragment.gameObject.SetActive(true);
            }
        }

        private void AddBoxFragmentTweens(Sequence sequence, float burstStart)
        {
            if (boxBurstFragments == null || boxBurstRenderers == null || config == null)
                return;

            int count = Mathf.Min(BoxBurstFragmentCount, Mathf.Min(boxBurstFragments.Length, boxBurstRenderers.Length));
            for (int index = 0; index < count; index++)
            {
                Transform fragment = boxBurstFragments[index];
                SpriteRenderer renderer = boxBurstRenderers[index];
                if (fragment == null || renderer == null)
                    continue;

                Vector3 start = fragment.localPosition;
                float horizontal = Mathf.Sign(start.x - coverClosedPosition.x);
                if (Mathf.Approximately(start.x, coverClosedPosition.x))
                    horizontal = GetFragmentUnitValue(index, 1) < .5f ? -1f : 1f;
                float spread = Mathf.Lerp(.45f, 1f, GetFragmentUnitValue(index, 2)) * config.boxBreakScatterDistance;
                float lift = Mathf.Lerp(.35f, 1f, GetFragmentUnitValue(index, 3)) * config.boxBreakLiftDistance;
                float drop = Mathf.Lerp(.5f, 1f, GetFragmentUnitValue(index, 4)) * config.boxBreakDropDistance;
                float lifetime = config.boxBurstDuration * Mathf.Lerp(.7f, 1f, GetFragmentUnitValue(index, 6));
                float holdDuration = IsEdgeFragment(index) ? lifetime * config.boxBurstEdgeHoldFraction : 0f;
                float movementDuration = Mathf.Max(.01f, lifetime - holdDuration);
                float rotation = horizontal * Mathf.Lerp(.45f, 1f, GetFragmentUnitValue(index, 7)) * config.boxBreakRotation;
                Vector3 peak = start + new Vector3(horizontal * (spread + config.boxBreakSeparation) * .65f, lift, 0f);
                Vector3 end = start + new Vector3(horizontal * (spread + config.boxBreakSeparation), -drop, 0f);

                Sequence movement = DOTween.Sequence()
                    .SetLink(gameObject, LinkBehaviour.KillOnDisable).SetAutoKill(true);
                if (holdDuration > 0f)
                    movement.AppendInterval(holdDuration);
                movement.Append(fragment.DOLocalMove(peak, movementDuration * BurstRiseFraction).SetEase(Ease.OutQuad)
                    .SetLink(gameObject, LinkBehaviour.KillOnDisable).SetAutoKill(true));
                movement.Append(fragment.DOLocalMove(end, movementDuration * (1f - BurstRiseFraction)).SetEase(Ease.InQuad)
                    .SetLink(gameObject, LinkBehaviour.KillOnDisable).SetAutoKill(true));
                sequence.Insert(burstStart, movement);
                sequence.Insert(burstStart, fragment.DOLocalRotate(new Vector3(0f, 0f, rotation), lifetime, RotateMode.LocalAxisAdd).SetEase(Ease.OutQuad)
                    .SetLink(gameObject, LinkBehaviour.KillOnDisable).SetAutoKill(true));
                float fadeStart = holdDuration + movementDuration * BurstFadeStartFraction;
                sequence.Insert(burstStart + fadeStart,
                    renderer.DOFade(0f, lifetime - fadeStart).SetEase(Ease.InQuad)
                        .SetLink(gameObject, LinkBehaviour.KillOnDisable).SetAutoKill(true));
            }
        }

        private static bool IsEdgeFragment(int index)
        {
            return index < 10;
        }

        private static float GetFragmentUnitValue(int index, int salt)
        {
            unchecked
            {
                uint value = (uint)(index + 1) * 747796405u + (uint)salt * 2891336453u;
                value = (value >> ((int)(value >> 28) + 4)) ^ value;
                value *= 277803737u;
                value = (value >> 22) ^ value;
                return (value & 0x00FFFFFFu) / 16777215f;
            }
        }

        private void CompleteOpening()
        {
            if (isComplete)
                return;

            isComplete = true;
            Action callback = completion;
            completion = null;
            Despawn();
            callback?.Invoke();
        }

        public void OnSpawn()
        {
            isComplete = false;
            previewShadowBurstAlpha = 1f;
            progressTween?.Kill();
            openingSequence?.Kill();
        }

        public void OnDespawn()
        {
            progressTween?.Kill();
            progressTween = null;
            openingSequence?.Kill();
            openingSequence = null;
            completion = null;
            ClearPreviewVisuals();
            exposureChanged = null;
            ResetVisualState();
            AreaId = null;
            config = null;
        }

        public void Despawn()
        {
            returnHandler?.Invoke(this);
        }

        private void ResetVisualState()
        {
            previewShadowBurstAlpha = 1f;
            if (boxShutter != null)
                boxShutter.localPosition = shutterClosedPosition;

            if (boxCoverRenderer != null)
            {
                boxCoverRenderer.transform.localPosition = coverClosedPosition;
                boxCoverRenderer.transform.localScale = Vector3.one;
                Color coverColor = boxCoverRenderer.color;
                coverColor.a = 1f;
                boxCoverRenderer.color = coverColor;
                boxCoverRenderer.gameObject.SetActive(true);
            }
            if (boxPanelRenderer != null)
            {
                boxPanelRenderer.SetPropertyBlock(null);
                boxPanelRenderer.gameObject.SetActive(true);
            }
            if (boxBackingRenderer != null)
                boxBackingRenderer.gameObject.SetActive(true);
            if (previewMask != null)
                previewMask.gameObject.SetActive(true);

            if (boxCounter != null)
            {
                boxCounter.transform.localPosition = counterClosedPosition;
                boxCounter.gameObject.SetActive(true);
            }

            if (boxBurstFragments == null)
                return;

            for (int index = 0; index < boxBurstFragments.Length; index++)
            {
                Transform fragment = boxBurstFragments[index];
                if (fragment == null)
                    continue;

                fragment.localPosition = fragmentClosedPositions != null && index < fragmentClosedPositions.Length
                    ? fragmentClosedPositions[index]
                    : Vector3.zero;
                fragment.localRotation = fragmentClosedRotations != null && index < fragmentClosedRotations.Length
                    ? fragmentClosedRotations[index]
                    : Quaternion.identity;
                fragment.localScale = fragmentClosedScales != null && index < fragmentClosedScales.Length
                    ? fragmentClosedScales[index]
                    : Vector3.one;
                fragment.gameObject.SetActive(false);

                if (boxBurstRenderers != null && index < boxBurstRenderers.Length &&
                    boxBurstRenderers[index] != null)
                {
                    boxBurstRenderers[index].SetPropertyBlock(null);
                    Color fragmentColor = boxBurstRenderers[index].color;
                    fragmentColor.a = 1f;
                    boxBurstRenderers[index].color = fragmentColor;
                }
            }
        }

        private Vector4 GetValidatedPanelSourceUvRect(Vector4 sourceUvRect)
        {
            if (sourceUvRect.x >= 0f && sourceUvRect.y >= 0f &&
                sourceUvRect.z > 0f && sourceUvRect.w > 0f &&
                sourceUvRect.x + sourceUvRect.z <= 1f &&
                sourceUvRect.y + sourceUvRect.w <= 1f)
            {
                return sourceUvRect;
            }

            Debug.LogError(
                $"[BoxRevealAreaView] Box '{AreaId}' has an invalid TunnelShutter source UV rectangle {sourceUvRect}.",
                this);
            return new Vector4(.02f, .02f, .96f, .96f);
        }

        private void CachePreviewState()
        {
            int count = boxPreviewRenderers != null ? boxPreviewRenderers.Length : 0;
            previewBasePositions = new Vector3[count];
            previewBaseScales = new Vector3[count];
            previewBaseRotations = new Quaternion[count];
            previewBaseColors = new Color[count];
        }

        private void ClearPreviewVisuals()
        {
            activePreviewCount = 0;
            previewVisuals.Clear();
            previewPieceVisuals.Clear();
            previewContentRanges.Clear();
            if (boxPreviewRenderers != null)
            {
                for (int index = 0; index < boxPreviewRenderers.Length; index++)
                {
                    SpriteRenderer renderer = boxPreviewRenderers[index];
                    if (renderer == null)
                        continue;

                    renderer.gameObject.SetActive(false);
                    renderer.sprite = null;
                    renderer.sharedMaterial = null;
                    renderer.color = Color.white;
                    renderer.transform.localPosition = Vector3.zero;
                    renderer.transform.localRotation = Quaternion.identity;
                    renderer.transform.localScale = Vector3.one;
                }
            }

            if (boxPreviewShadowRenderers == null)
                return;

            for (int index = 0; index < boxPreviewShadowRenderers.Length; index++)
            {
                SpriteRenderer shadowRenderer = boxPreviewShadowRenderers[index];
                if (shadowRenderer == null)
                    continue;

                shadowRenderer.gameObject.SetActive(false);
                shadowRenderer.sprite = null;
                shadowRenderer.sharedMaterial = null;
                shadowRenderer.color = Color.white;
                shadowRenderer.transform.localPosition = Vector3.zero;
                shadowRenderer.transform.localRotation = Quaternion.identity;
                shadowRenderer.transform.localScale = Vector3.one;
            }
        }

        private void DisablePreviewShadows()
        {
            if (boxPreviewShadowRenderers == null)
                return;

            for (int index = 0; index < boxPreviewShadowRenderers.Length; index++)
            {
                SpriteRenderer shadowRenderer = boxPreviewShadowRenderers[index];
                if (shadowRenderer != null)
                    shadowRenderer.gameObject.SetActive(false);
            }
        }

        private void ReleasePreviewMasking()
        {
            if (boxPreviewRenderers == null)
                return;

            for (int index = 0; index < activePreviewCount; index++)
            {
                SpriteRenderer previewRenderer = boxPreviewRenderers[index];
                if (previewRenderer != null)
                    previewRenderer.maskInteraction = SpriteMaskInteraction.None;
            }
        }
    }
}
