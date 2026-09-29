using System;
using DG.Tweening;
using GravityPuzzle.Config;
using GravityPuzzle.Gameplay.Reveal;
using TMPro;
using UnityEngine;

namespace GravityPuzzle.Presentation.Views
{
    /// <summary>
    /// Scene/prefab adapter for a reveal area. It only animates authored
    /// transforms and renderers; the coordinator owns all unlock decisions.
    /// </summary>
    public sealed class RevealAreaPresentation : MonoBehaviour, IRevealAreaPresentation
    {
        [SerializeField] private string areaId;
        [SerializeField] private RevealAreaKind visualKind;
        [SerializeField] private RevealPresentationConfig config;
        [SerializeField] private SpriteRenderer[] sortingRenderers;
        [Header("Box")]
        [SerializeField] private Transform boxShutter;
        [SerializeField] private ParticleSystem boxBurst;
        [SerializeField] private TMP_Text boxCounter;
        [Header("Elevator")]
        [SerializeField] private Transform elevatorLeftDoor;
        [SerializeField] private Transform elevatorRightDoor;
        [SerializeField] private SpriteRenderer[] elevatorDoorRenderers;

        private Vector3 shutterClosedPosition;
        private Vector3 leftDoorClosedPosition;
        private Vector3 rightDoorClosedPosition;
        private Sequence openingSequence;
        private Action pendingCompletion;
        private bool completed;

        public string AreaId => areaId;

        private void Awake()
        {
            if (boxShutter != null)
                shutterClosedPosition = boxShutter.localPosition;
            if (elevatorLeftDoor != null)
                leftDoorClosedPosition = elevatorLeftDoor.localPosition;
            if (elevatorRightDoor != null)
                rightDoorClosedPosition = elevatorRightDoor.localPosition;

            ApplySorting();
        }

        public void SetBoxRemaining(int remaining)
        {
            if (boxCounter != null)
                boxCounter.SetText("{0}", Mathf.Max(0, remaining));
        }

        public void PlayOpening(RevealAreaKind kind, Action onCompleted)
        {
            pendingCompletion = onCompleted;
            completed = false;
            openingSequence?.Kill();

            if (config == null)
            {
                Debug.LogWarning("[Reveal] Presentation config is missing; opening immediately.", this);
                CompleteOpening();
                return;
            }

            openingSequence = DOTween.Sequence()
                .SetLink(gameObject, LinkBehaviour.KillOnDisable)
                .SetAutoKill(true);

            if (kind == RevealAreaKind.Box)
            {
                if (boxShutter != null)
                {
                    openingSequence.Append(boxShutter.DOLocalMoveY(
                        shutterClosedPosition.y + config.boxOpenDistance,
                        config.boxOpenDuration).SetEase(config.boxOpenEase));
                }

                openingSequence.AppendCallback(() =>
                {
                    if (boxBurst != null)
                        boxBurst.Play(true);
                    if (boxShutter != null)
                        boxShutter.gameObject.SetActive(false);
                });
            }
            else
            {
                if (elevatorLeftDoor != null)
                {
                    openingSequence.Join(elevatorLeftDoor.DOLocalMoveX(
                        leftDoorClosedPosition.x - config.elevatorDoorDistance,
                        config.elevatorOpenDuration).SetEase(config.elevatorOpenEase));
                }
                if (elevatorRightDoor != null)
                {
                    openingSequence.Join(elevatorRightDoor.DOLocalMoveX(
                        rightDoorClosedPosition.x + config.elevatorDoorDistance,
                        config.elevatorOpenDuration).SetEase(config.elevatorOpenEase));
                }

                int rendererCount = elevatorDoorRenderers != null ? elevatorDoorRenderers.Length : 0;
                for (int index = 0; index < rendererCount; index++)
                {
                    SpriteRenderer renderer = elevatorDoorRenderers[index];
                    if (renderer == null)
                        continue;
                    Color color = renderer.color;
                    openingSequence.Join(renderer.DOFade(0f, config.elevatorFadeDuration));
                }
            }

            openingSequence.OnComplete(CompleteOpening);
        }

        private void CompleteOpening()
        {
            if (completed)
                return;

            completed = true;
            Action completion = pendingCompletion;
            pendingCompletion = null;
            completion?.Invoke();
        }

        private void ApplySorting()
        {
            if (config == null || sortingRenderers == null)
                return;

            int sortingOrder = visualKind == RevealAreaKind.Box
                ? config.boxSortingOrder
                : config.elevatorSortingOrder;
            for (int index = 0; index < sortingRenderers.Length; index++)
            {
                SpriteRenderer renderer = sortingRenderers[index];
                if (renderer != null)
                    renderer.sortingOrder = sortingOrder + index;
            }
        }
    }
}
