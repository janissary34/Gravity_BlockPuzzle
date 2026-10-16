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
        private Vector3 leftDoorClosedPosition;
        private Vector3 rightDoorClosedPosition;
        private float doorOpenDistance;

        public string AreaId { get; private set; }

        private void Awake()
        {
            if (elevatorLeftDoor != null)
                leftDoorClosedPosition = elevatorLeftDoor.localPosition;
            if (elevatorRightDoor != null)
                rightDoorClosedPosition = elevatorRightDoor.localPosition;
        }

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

            if (elevatorLeftDoor != null)
            {
                elevatorLeftDoor.localPosition = leftDoorClosedPosition;
                elevatorLeftDoor.gameObject.SetActive(true);
            }

            if (elevatorRightDoor != null)
            {
                elevatorRightDoor.localPosition = rightDoorClosedPosition;
                elevatorRightDoor.gameObject.SetActive(true);
            }

            if (doorRenderers != null)
            {
                for (int i = 0; i < doorRenderers.Length; i++)
                {
                    if (doorRenderers[i] != null)
                    {
                        Color c = doorRenderers[i].color;
                        c.a = 1f;
                        doorRenderers[i].color = c;
                        if (config != null)
                            doorRenderers[i].sortingOrder = config.elevatorSortingOrder + 2 + i;
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
            }

            doorOpenDistance = config != null ? config.elevatorDoorDistance : 1.2f;
            isComplete = false;
        }

        public void SetBoxRemaining(int remaining)
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

            bool hasDoorMotion = false;
            if (elevatorLeftDoor != null)
            {
                openingSequence.Append(elevatorLeftDoor.DOLocalMoveX(
                    leftDoorClosedPosition.x - doorOpenDistance,
                    config.elevatorOpenDuration).SetEase(config.elevatorOpenEase));
                hasDoorMotion = true;
            }

            if (elevatorRightDoor != null)
            {
                Tween rightDoorTween = elevatorRightDoor.DOLocalMoveX(
                    rightDoorClosedPosition.x + doorOpenDistance,
                    config.elevatorOpenDuration).SetEase(config.elevatorOpenEase);
                if (hasDoorMotion)
                    openingSequence.Join(rightDoorTween);
                else
                    openingSequence.Append(rightDoorTween);
                hasDoorMotion = true;
            }

            bool hasFade = false;
            if (doorRenderers != null)
            {
                for (int index = 0; index < doorRenderers.Length; index++)
                {
                    SpriteRenderer renderer = doorRenderers[index];
                    if (renderer == null)
                        continue;

                    Tween fadeTween = renderer.DOFade(0f, config.elevatorFadeDuration);
                    if (hasFade)
                        openingSequence.Join(fadeTween);
                    else
                        openingSequence.Append(fadeTween);
                    hasFade = true;
                }
            }

            openingSequence.OnComplete(CompleteOpening);
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
