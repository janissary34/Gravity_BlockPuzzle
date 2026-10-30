using System;
using DG.Tweening;
using GravityPuzzle.Config;
using GravityPuzzle.Gameplay.Reveal;
using GravityPuzzle.Infrastructure.Pooling;
using UnityEngine;

namespace GravityPuzzle.Presentation.Views
{
    public sealed class ElevatorRevealAreaView : MonoBehaviour, IRevealAreaPresentation, IPoolable, IPoolReturnReceiver<ElevatorRevealAreaView>
    {
        [SerializeField] private SpriteRenderer frameRenderer;
        [SerializeField] private SpriteRenderer openingRenderer;
        [SerializeField] private Transform elevatorLeftDoor;
        [SerializeField] private Transform elevatorRightDoor;
        [SerializeField] private SpriteRenderer[] doorRenderers;

        private Action<ElevatorRevealAreaView> returnHandler;
        private RevealPresentationConfig config;
        private Sequence openingSequence;
        private Action completion;
        private bool isComplete;

        public string AreaId { get; private set; }

        public void SetPoolReturnHandler(Action<ElevatorRevealAreaView> handler)
        {
            returnHandler = handler;
        }

        public void Initialize(
            string areaId,
            Vector2 worldPosition,
            Vector2 size,
            Color boardColor,
            RevealPresentationConfig presentationConfig)
        {
            AreaId = areaId;
            config = presentationConfig;
            transform.position = new Vector3(worldPosition.x, worldPosition.y, 0f);
            Vector2 areaSize = new Vector2(Mathf.Max(.01f, size.x), Mathf.Max(.01f, size.y));

            float widthScale = config != null ? config.elevatorVisualWidthScale : 1f;
            float frameWidth = areaSize.x * widthScale;
            float doorApertureWidth = areaSize.x *
                (config != null ? config.elevatorDoorWidthRatio : .86f);
            float doorApertureHeight = areaSize.y *
                (config != null ? config.elevatorDoorHeightRatio : .83f);
            float doorCenterY = areaSize.y *
                (config != null ? config.elevatorDoorCenterYOffset : 0f);
            float doorWidth = doorApertureWidth * .5f;

            if (elevatorLeftDoor != null)
            {
                elevatorLeftDoor.localPosition = new Vector3(-doorWidth * .5f, doorCenterY, 0f);
                elevatorLeftDoor.gameObject.SetActive(true);
            }

            if (elevatorRightDoor != null)
            {
                elevatorRightDoor.localPosition = new Vector3(doorWidth * .5f, doorCenterY, 0f);
                elevatorRightDoor.gameObject.SetActive(true);
            }

            if (doorRenderers != null)
            {
                bool usesSharedDoorSprite = config == null ||
                                            config.elevatorLeftDoorSprite == null ||
                                            config.elevatorRightDoorSprite == null ||
                                            config.elevatorLeftDoorSprite == config.elevatorRightDoorSprite;
                for (int i = 0; i < doorRenderers.Length; i++)
                {
                    if (doorRenderers[i] != null)
                    {
                        Color c = doorRenderers[i].color;
                        c.a = 1f;
                        doorRenderers[i].color = c;
                        if (config != null)
                        {
                            doorRenderers[i].sortingOrder = config.elevatorSortingOrder + 2 + i;
                            Sprite doorSprite = i == 0
                                ? config.elevatorLeftDoorSprite
                                : config.elevatorRightDoorSprite;
                            if (doorSprite != null)
                                doorRenderers[i].sprite = doorSprite;
                        }
                        doorRenderers[i].drawMode = SpriteDrawMode.Sliced;
                        doorRenderers[i].size = new Vector2(doorWidth, doorApertureHeight);
                        doorRenderers[i].flipX = usesSharedDoorSprite && i == 0;
                    }
                }
            }

            if (frameRenderer != null)
            {
                frameRenderer.gameObject.SetActive(true);
                if (config != null)
                {
                    if (config.elevatorFrameSprite != null)
                        frameRenderer.sprite = config.elevatorFrameSprite;
                    else if (config.elevatorDoorSprite != null)
                        frameRenderer.sprite = config.elevatorDoorSprite;
                    frameRenderer.sortingOrder = config.elevatorSortingOrder;
                }
                frameRenderer.drawMode = SpriteDrawMode.Sliced;
                frameRenderer.size = new Vector2(frameWidth, areaSize.y);
            }

            if (openingRenderer != null)
            {
                openingRenderer.gameObject.SetActive(true);
                if (config != null)
                {
                    openingRenderer.color = config.elevatorOpeningColor.a > 0f
                        ? config.elevatorOpeningColor
                        : boardColor;
                    openingRenderer.sortingOrder = config.elevatorSortingOrder + 1;
                }
                openingRenderer.drawMode = SpriteDrawMode.Sliced;
                openingRenderer.size = new Vector2(doorApertureWidth, doorApertureHeight);
                openingRenderer.transform.localPosition = new Vector3(0f, doorCenterY, 0f);
            }

            isComplete = false;
        }

        public void SetBoxRemaining(int remaining, int target)
        {
            // Gameplay contract note: Elevator has no remaining count counter.
        }

        public void PlayOpening(RevealAreaKind requestedKind, Action onCompleted)
        {
            completion = onCompleted;
            isComplete = false;
            openingSequence?.Kill();

            if (config == null)
            {
                CompleteOpening();
                return;
            }

            openingSequence = DOTween.Sequence()
                .SetLink(gameObject, LinkBehaviour.KillOnDisable)
                .SetAutoKill(true);

            bool hasDoorCollapse = false;
            SpriteRenderer leftDoorRenderer = GetDoorRenderer(0);
            if (elevatorLeftDoor != null && leftDoorRenderer != null)
            {
                Vector3 closedPosition = elevatorLeftDoor.localPosition;
                float closedWidth = leftDoorRenderer.size.x;
                float closedHeight = leftDoorRenderer.size.y;
                float fixedLeftEdge = closedPosition.x - closedWidth * .5f;
                openingSequence.Append(DOTween.To(
                        () => leftDoorRenderer.size.x,
                        width =>
                        {
                            leftDoorRenderer.size = new Vector2(width, closedHeight);
                            elevatorLeftDoor.localPosition = new Vector3(
                                fixedLeftEdge + width * .5f,
                                closedPosition.y,
                                closedPosition.z);
                        },
                        .001f,
                        config.elevatorOpenDuration)
                    .SetEase(config.elevatorOpenEase));
                hasDoorCollapse = true;
            }

            SpriteRenderer rightDoorRenderer = GetDoorRenderer(1);
            if (elevatorRightDoor != null && rightDoorRenderer != null)
            {
                Vector3 closedPosition = elevatorRightDoor.localPosition;
                float closedWidth = rightDoorRenderer.size.x;
                float closedHeight = rightDoorRenderer.size.y;
                float fixedRightEdge = closedPosition.x + closedWidth * .5f;
                Tween rightDoorTween = DOTween.To(
                        () => rightDoorRenderer.size.x,
                        width =>
                        {
                            rightDoorRenderer.size = new Vector2(width, closedHeight);
                            elevatorRightDoor.localPosition = new Vector3(
                                fixedRightEdge - width * .5f,
                                closedPosition.y,
                                closedPosition.z);
                        },
                        .001f,
                        config.elevatorOpenDuration)
                    .SetEase(config.elevatorOpenEase);
                if (hasDoorCollapse)
                    openingSequence.Join(rightDoorTween);
                else
                    openingSequence.Append(rightDoorTween);
                hasDoorCollapse = true;
            }

            if (!hasDoorCollapse)
                openingSequence.AppendInterval(config.elevatorOpenDuration);

            openingSequence.OnComplete(CompleteOpening);
        }

        private void CompleteOpening()
        {
            if (isComplete)
                return;

            isComplete = true;
            Action callback = completion;
            completion = null;
            SetElevatorVisualsVisible(false);
            // The elevator is a pooled transient presentation. Returning its root
            // guarantees that no part of the source frame sprite can remain over
            // the revealed board after the door-collapse animation finishes.
            Despawn();
            callback?.Invoke();
        }

        private void SetElevatorVisualsVisible(bool visible)
        {
            if (frameRenderer != null)
                frameRenderer.gameObject.SetActive(visible);
            if (openingRenderer != null)
                openingRenderer.gameObject.SetActive(visible);

            if (doorRenderers == null)
                return;

            for (int index = 0; index < doorRenderers.Length; index++)
            {
                SpriteRenderer renderer = doorRenderers[index];
                if (renderer != null)
                    renderer.gameObject.SetActive(visible);
            }
        }

        private SpriteRenderer GetDoorRenderer(int index)
        {
            return doorRenderers != null && index >= 0 && index < doorRenderers.Length
                ? doorRenderers[index]
                : null;
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
