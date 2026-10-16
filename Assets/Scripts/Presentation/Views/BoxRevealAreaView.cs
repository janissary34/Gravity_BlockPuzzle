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

        [SerializeField] private Transform boxShutter;
        [SerializeField] private SpriteRenderer boxCoverRenderer;
        [SerializeField] private TMP_Text boxCounter;
        [SerializeField] private Transform[] boxBurstFragments;
        [SerializeField] private SpriteRenderer[] boxBurstRenderers;

        private Action<BoxRevealAreaView> returnHandler;
        private RevealPresentationConfig config;
        private Sequence openingSequence;
        private Action completion;
        private bool isComplete;
        private Vector3 shutterClosedPosition;
        private Vector2 authoredSize;

        public string AreaId { get; private set; }

        private void Awake()
        {
            if (boxShutter != null)
                shutterClosedPosition = boxShutter.localPosition;
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
            transform.position = new Vector3(worldPosition.x, worldPosition.y, 0f);

            if (boxShutter != null)
            {
                boxShutter.localPosition = shutterClosedPosition;
                boxShutter.gameObject.SetActive(true);
            }

            if (boxCoverRenderer != null)
            {
                boxCoverRenderer.gameObject.SetActive(true);
                Color c = boxCoverRenderer.color;
                c.a = 1f;
                boxCoverRenderer.color = c;
                if (config != null && config.boxCoverSprite != null)
                    boxCoverRenderer.sprite = config.boxCoverSprite;
                boxCoverRenderer.drawMode = SpriteDrawMode.Sliced;
                boxCoverRenderer.size = size;
                if (config != null)
                    boxCoverRenderer.sortingOrder = config.boxSortingOrder;
            }

            if (boxCounter != null)
            {
                boxCounter.gameObject.SetActive(true);
                if (config != null)
                    boxCounter.GetComponent<MeshRenderer>().sortingOrder = config.boxSortingOrder + 1;
            }

            if (boxBurstFragments != null)
            {
                for (int i = 0; i < boxBurstFragments.Length; i++)
                {
                    if (boxBurstFragments[i] != null)
                        boxBurstFragments[i].gameObject.SetActive(false);
                }
            }

            isComplete = false;
        }

        public void SetBoxRemaining(int remaining)
        {
            if (boxCounter != null)
                boxCounter.SetText("{0}", Mathf.Max(0, remaining));
        }

        public void PlayOpening(RevealAreaKind requestedKind, Action onCompleted)
        {
            completion = onCompleted;
            isComplete = false;
            openingSequence?.Kill();

            if (config == null || boxShutter == null)
            {
                CompleteOpening();
                return;
            }

            openingSequence = DOTween.Sequence()
                .SetLink(gameObject, LinkBehaviour.KillOnDisable)
                .SetAutoKill(true);

            Transform coverTransform = boxCoverRenderer != null ? boxCoverRenderer.transform : null;
            float fullHeight = authoredSize.y > 0 ? authoredSize.y : 1f;
            float residualHeight = Mathf.Min(fullHeight, config.boxBurstResidualHeight);
            float fixedTopY = coverTransform != null
                ? coverTransform.localPosition.y + fullHeight * .5f
                : shutterClosedPosition.y;
            float residualCentreY = fixedTopY - residualHeight * .5f;

            PrepareBoxBurstFragments(
                new Vector3(0f, residualCentreY, 0f),
                residualHeight);

            if (coverTransform != null)
            {
                openingSequence.Append(DOVirtual.Float(
                    fullHeight,
                    residualHeight,
                    config.boxOpenDuration,
                    height =>
                    {
                        if (boxCoverRenderer != null)
                            boxCoverRenderer.size = new Vector2(boxCoverRenderer.size.x, height);
                    }).SetEase(config.boxOpenEase));

                openingSequence.Join(coverTransform.DOLocalMoveY(
                    residualCentreY,
                    config.boxOpenDuration).SetEase(config.boxOpenEase));
            }

            if (boxCounter != null)
            {
                openingSequence.Join(boxCounter.transform.DOLocalMoveY(
                    residualCentreY,
                    config.boxOpenDuration).SetEase(config.boxOpenEase));
            }

            openingSequence.AppendCallback(PlayBoxBurst);
            openingSequence.AppendInterval(config.boxBurstDuration);
            openingSequence.OnComplete(CompleteOpening);
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
            gameObject.SetActive(false);
            Action callback = completion;
            completion = null;
            callback?.Invoke();
        }

        public void OnSpawn()
        {
            isComplete = false;
            openingSequence?.Kill();
        }

        public void OnDespawn()
        {
            openingSequence?.Kill();
            openingSequence = null;
            completion = null;
            AreaId = null;
            config = null;
        }

        public void Despawn()
        {
            returnHandler?.Invoke(this);
        }
    }
}
