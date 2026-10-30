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

        public void SetBoxRemaining(int remaining, int target)
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
                bool hasDoorCollapse = false;
                SpriteRenderer leftDoorRenderer = GetDoorRenderer(0);
                if (elevatorLeftDoor != null && leftDoorRenderer != null)
                {
                    float closedWidth = leftDoorRenderer.size.x;
                    float closedHeight = leftDoorRenderer.size.y;
                    float fixedLeftEdge = leftDoorClosedPosition.x - closedWidth * .5f;
                    openingSequence.Append(DOTween.To(
                            () => leftDoorRenderer.size.x,
                            width =>
                            {
                                leftDoorRenderer.size = new Vector2(width, closedHeight);
                                elevatorLeftDoor.localPosition = new Vector3(
                                    fixedLeftEdge + width * .5f,
                                    leftDoorClosedPosition.y,
                                    leftDoorClosedPosition.z);
                            },
                            .001f,
                            config.elevatorOpenDuration)
                        .SetEase(config.elevatorOpenEase));
                    hasDoorCollapse = true;
                }

                SpriteRenderer rightDoorRenderer = GetDoorRenderer(1);
                if (elevatorRightDoor != null && rightDoorRenderer != null)
                {
                    float closedWidth = rightDoorRenderer.size.x;
                    float closedHeight = rightDoorRenderer.size.y;
                    float fixedRightEdge = rightDoorClosedPosition.x + closedWidth * .5f;
                    Tween rightDoorTween = DOTween.To(
                            () => rightDoorRenderer.size.x,
                            width =>
                            {
                                rightDoorRenderer.size = new Vector2(width, closedHeight);
                                elevatorRightDoor.localPosition = new Vector3(
                                    fixedRightEdge - width * .5f,
                                    rightDoorClosedPosition.y,
                                    rightDoorClosedPosition.z);
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
            }

            openingSequence.OnComplete(CompleteOpening);
        }

        private void CompleteOpening()
        {
            if (completed)
                return;

            completed = true;
            if (visualKind == RevealAreaKind.Elevator)
                SetElevatorVisualsVisible(false);
            Action completion = pendingCompletion;
            pendingCompletion = null;
            completion?.Invoke();
        }

        private void SetElevatorVisualsVisible(bool visible)
        {
            if (sortingRenderers != null)
            {
                for (int index = 0; index < sortingRenderers.Length; index++)
                {
                    SpriteRenderer renderer = sortingRenderers[index];
                    if (renderer != null)
                        renderer.gameObject.SetActive(visible);
                }
            }

            if (elevatorDoorRenderers == null)
                return;

            for (int index = 0; index < elevatorDoorRenderers.Length; index++)
            {
                SpriteRenderer renderer = elevatorDoorRenderers[index];
                if (renderer != null)
                    renderer.gameObject.SetActive(visible);
            }
        }

        private SpriteRenderer GetDoorRenderer(int index)
        {
            if (elevatorDoorRenderers == null || index < 0 || index >= elevatorDoorRenderers.Length ||
                elevatorDoorRenderers[index] == null)
            {
                return null;
            }

            return elevatorDoorRenderers[index];
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
