using System;
using DG.Tweening;
using GravityPuzzle.Config;
using GravityPuzzle.Gameplay.Reveal;
using GravityPuzzle.Infrastructure.Pooling;
using TMPro;
using UnityEngine;

namespace GravityPuzzle.Presentation.Views
{
    public sealed class BoxRevealAreaView : MonoBehaviour, IRevealAreaPresentation, IPoolable, IPoolReturnReceiver<BoxRevealAreaView>
    {
        private const int BoxBurstColumns = 6;
        private const int BoxBurstRows = 2;
        private static readonly int OpeningId = Shader.PropertyToID("_Opening");

        [SerializeField] private Transform boxShutter;
        [SerializeField] private SpriteRenderer boxCoverRenderer;
        [SerializeField] private SpriteRenderer boxBackingRenderer;
        [SerializeField] private SpriteRenderer boxPanelRenderer;
        [SerializeField] private TMP_Text boxCounter;
        [SerializeField] private Transform[] boxBurstFragments;
        [SerializeField] private SpriteRenderer[] boxBurstRenderers;

        private Action<BoxRevealAreaView> returnHandler;
        private RevealPresentationConfig config;
        private Sequence openingSequence;
        private Tween progressTween;
        private float currentHeight;
        private float apertureHeight;
        private MaterialPropertyBlock panelProperties;
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

        public string AreaId { get; private set; }

        private void Awake()
        {
            panelProperties = new MaterialPropertyBlock();
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
                return;

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
                    boxCoverRenderer.drawMode = SpriteDrawMode.Sliced;
                    boxCoverRenderer.size = size;
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

            float residualHeight = Mathf.Min(authoredSize.y, Mathf.Max(0f, config.boxBurstResidualHeight));
            float fixedTopY = coverClosedPosition.y + authoredSize.y * .5f;
            float residualCentreY = fixedTopY - residualHeight * .5f;
            PrepareBoxBurstFragments(new Vector3(0f, residualCentreY, 0f), residualHeight);

            openingSequence = DOTween.Sequence()
                .SetLink(gameObject, LinkBehaviour.KillOnDisable)
                .SetAutoKill(true);
            openingSequence.Append(DOVirtual.Float(currentHeight, 0f, config.boxOpenDuration, ApplyShutterHeight)
                .SetEase(config.boxOpenEase));
            openingSequence.AppendCallback(PlayBoxBurst);
            openingSequence.AppendInterval(config.boxBurstDuration);
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

            Sprite frameSprite = boxCoverRenderer.sprite;
            float ppu = frameSprite.pixelsPerUnit;
            Vector4 border = frameSprite.border / ppu;
            Vector4 insets = config.boxOpeningInsets / ppu;
            float horizontalScale = Mathf.Min(1f, size.x / Mathf.Max(.001f, border.x + border.z));
            float verticalScale = Mathf.Min(1f, size.y / Mathf.Max(.001f, border.y + border.w));
            float left = insets.x * horizontalScale;
            float right = insets.z * horizontalScale;
            float bottom = insets.y * verticalScale;
            float top = insets.w * verticalScale;
            float width = Mathf.Max(0f, size.x - left - right);
            apertureHeight = Mathf.Max(0f, size.y - bottom - top);

            boxBackingRenderer.transform.localPosition = coverClosedPosition;
            boxBackingRenderer.drawMode = SpriteDrawMode.Simple;
            Vector2 backingSize = boxBackingRenderer.sprite.bounds.size;
            boxBackingRenderer.transform.localScale = new Vector3(size.x / backingSize.x, size.y / backingSize.y, 1f);
            Color backingColor = config.boxBackingColor;
            backingColor.a = 1f;
            boxBackingRenderer.color = backingColor;
            boxBackingRenderer.sortingOrder = config.boxSortingOrder - 2;
            boxBackingRenderer.gameObject.SetActive(true);

            boxPanelRenderer.transform.localPosition = coverClosedPosition + new Vector3((left - right) * .5f, (bottom - top) * .5f, 0f);
            boxPanelRenderer.drawMode = SpriteDrawMode.Simple;
            Vector2 panelSize = boxPanelRenderer.sprite.bounds.size;
            boxPanelRenderer.transform.localScale = new Vector3(width / panelSize.x, apertureHeight / panelSize.y, 1f);
            boxPanelRenderer.sortingOrder = config.boxSortingOrder - 1;
            boxPanelRenderer.gameObject.SetActive(true);
        }

        private void ApplyShutterHeight(float height)
        {
            currentHeight = height;
            float opening = authoredSize.y > 0f ? 1f - Mathf.Clamp01(height / authoredSize.y) : 0f;
            if (boxPanelRenderer != null)
            {
                boxPanelRenderer.GetPropertyBlock(panelProperties);
                panelProperties.SetFloat(OpeningId, opening);
                boxPanelRenderer.SetPropertyBlock(panelProperties);
            }

            if (boxCounter != null)
                boxCounter.transform.localPosition = counterClosedPosition +
                    (config != null ? (Vector3)config.boxCounterOffset : Vector3.zero) +
                    Vector3.up * (opening * apertureHeight * .5f);
        }

        private void PrepareBoxBurstFragments(Vector3 burstOrigin, float residualHeight)
        {
            if (boxBurstFragments == null || boxBurstRenderers == null)
                return;

            float coverWidth = authoredSize.x > 0 ? authoredSize.x : 1f;
            int count = Mathf.Min(boxBurstFragments.Length, BoxBurstColumns * BoxBurstRows);
            for (int index = 0; index < count; index++)
            {
                Transform fragment = boxBurstFragments[index];
                if (fragment == null)
                    continue;

                int column = index % BoxBurstColumns;
                int row = index / BoxBurstColumns;
                float fragmentHeight = residualHeight / BoxBurstRows;
                fragment.localPosition = burstOrigin + new Vector3(
                    -coverWidth * .5f + coverWidth / BoxBurstColumns * (column + .5f),
                    residualHeight * .5f - fragmentHeight * (row + .5f),
                    0f);

                if (index < boxBurstRenderers.Length && boxBurstRenderers[index] != null)
                {
                    boxBurstRenderers[index].size = new Vector2(coverWidth / BoxBurstColumns, fragmentHeight);
                    if (config != null)
                        boxBurstRenderers[index].sortingOrder = config.boxSortingOrder + 1;
                }
            }
        }

        private void PlayBoxBurst()
        {
            if (boxCoverRenderer != null)
                boxCoverRenderer.gameObject.SetActive(false);
            if (boxCounter != null)
                boxCounter.gameObject.SetActive(false);
            if (boxPanelRenderer != null)
                boxPanelRenderer.gameObject.SetActive(false);

            if (boxBurstFragments == null || boxBurstRenderers == null || config == null)
                return;

            int count = Mathf.Min(boxBurstFragments.Length, BoxBurstColumns * BoxBurstRows);
            for (int index = 0; index < count; index++)
            {
                Transform fragment = boxBurstFragments[index];
                SpriteRenderer renderer = index < boxBurstRenderers.Length ? boxBurstRenderers[index] : null;
                if (fragment == null || renderer == null)
                    continue;

                Color color = renderer.color;
                color.a = 1f;
                renderer.color = color;
                fragment.gameObject.SetActive(true);

                Vector3 startPosition = fragment.localPosition;
                int row = index / BoxBurstColumns;
                float horizontalDirection = GetFragmentUnitValue(index, 1) * 2f - 1f;
                float dropFactor = Mathf.Lerp(.4f, 1f, GetFragmentUnitValue(index, 2));
                float rotationDirection = GetFragmentUnitValue(index, 3) < .5f ? -1f : 1f;
                float rotationAmount = Mathf.Lerp(.35f, 1f, GetFragmentUnitValue(index, 4)) * config.boxBreakRotation;
                float horizontalOffset = horizontalDirection * config.boxBreakScatterDistance;
                float dropDistance = config.boxBreakDropDistance * dropFactor;

                if (row > 0)
                    horizontalOffset += horizontalDirection * config.boxBreakSeparation;

                fragment.DOLocalMove(
                    startPosition + new Vector3(horizontalOffset, -dropDistance, 0f),
                    config.boxBurstDuration)
                    .SetEase(Ease.OutQuad)
                    .SetLink(gameObject, LinkBehaviour.KillOnDisable);

                fragment.DORotate(
                    new Vector3(0f, 0f, rotationDirection * rotationAmount),
                    config.boxBurstDuration,
                    RotateMode.LocalAxisAdd)
                    .SetEase(Ease.OutQuad)
                    .SetLink(gameObject, LinkBehaviour.KillOnDisable);

                renderer.DOFade(0f, config.boxBurstDuration)
                    .SetEase(Ease.InQuad)
                    .SetLink(gameObject, LinkBehaviour.KillOnDisable);
            }
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
                    Color fragmentColor = boxBurstRenderers[index].color;
                    fragmentColor.a = 1f;
                    boxBurstRenderers[index].color = fragmentColor;
                }
            }
        }
    }
}
