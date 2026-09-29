using System;
using DG.Tweening;
using GravityPuzzle.Config;
using GravityPuzzle.Gameplay.Reveal;
using UnityEngine;

namespace GravityPuzzle.Presentation.Views
{
    /// <summary>
    /// Fallback presentation for level-authored reveal areas that do not have
    /// a scene-specific view. It preserves the reveal coordinator's explicit
    /// completion contract while making every authored Box/Elevator visible.
    /// </summary>
    public sealed class RuntimeRevealAreaPresentation : IRevealAreaPresentation
    {
        private const int BoxSlatCount = 4;
        private readonly GameObject root;
        private readonly RevealAreaKind kind;
        private readonly RevealPresentationConfig config;
        private readonly Transform boxShutter;
        private readonly Transform elevatorLeftDoor;
        private readonly Transform elevatorRightDoor;
        private readonly SpriteRenderer[] doorRenderers;
        private readonly TextMesh boxCounter;
        private readonly Vector3 shutterClosedPosition;
        private readonly Vector3 leftDoorClosedPosition;
        private readonly Vector3 rightDoorClosedPosition;
        private Sequence openingSequence;
        private Action completion;
        private bool isComplete;

        private RuntimeRevealAreaPresentation(
            GameObject root,
            RevealAreaKind kind,
            RevealPresentationConfig config,
            Transform boxShutter,
            Transform elevatorLeftDoor,
            Transform elevatorRightDoor,
            SpriteRenderer[] doorRenderers,
            TextMesh boxCounter)
        {
            this.root = root;
            this.kind = kind;
            this.config = config;
            this.boxShutter = boxShutter;
            this.elevatorLeftDoor = elevatorLeftDoor;
            this.elevatorRightDoor = elevatorRightDoor;
            this.doorRenderers = doorRenderers;
            this.boxCounter = boxCounter;
            shutterClosedPosition = boxShutter != null ? boxShutter.localPosition : Vector3.zero;
            leftDoorClosedPosition = elevatorLeftDoor != null ? elevatorLeftDoor.localPosition : Vector3.zero;
            rightDoorClosedPosition = elevatorRightDoor != null ? elevatorRightDoor.localPosition : Vector3.zero;
        }

        public static RuntimeRevealAreaPresentation Create(
            GravityLevelDefinition level,
            RevealAreaDefinition definition,
            RevealAreaKind kind,
            RevealPresentationConfig config)
        {
            Vector2 size = (Vector2)definition.bounds.size / Mathf.Max(1, level.subdivisions);
            Vector2 centre = new Vector2(
                -level.boardColumns * .5f +
                (definition.bounds.origin.x + definition.bounds.size.x * .5f) / level.subdivisions,
                -level.boardRows * .5f +
                (definition.bounds.origin.y + definition.bounds.size.y * .5f) / level.subdivisions);
            GameObject root = new GameObject($"Reveal {kind} - {definition.name}");
            root.transform.position = centre;

            return kind == RevealAreaKind.Box
                ? CreateBox(root, size, config)
                : CreateElevator(root, size, config);
        }

        public void SetBoxRemaining(int remaining)
        {
            if (boxCounter != null)
                boxCounter.text = Mathf.Max(0, remaining).ToString();
        }

        public void PlayOpening(RevealAreaKind requestedKind, Action onCompleted)
        {
            completion = onCompleted;
            isComplete = false;
            openingSequence?.Kill();
            openingSequence = DOTween.Sequence()
                .SetLink(root, LinkBehaviour.KillOnDisable)
                .SetAutoKill(true);

            if (kind == RevealAreaKind.Box && boxShutter != null)
            {
                openingSequence.Append(boxShutter.DOLocalMoveY(
                    shutterClosedPosition.y + config.boxOpenDistance,
                    config.boxOpenDuration).SetEase(config.boxOpenEase));
            }
            else if (kind == RevealAreaKind.Elevator)
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

                for (int index = 0; index < doorRenderers.Length; index++)
                {
                    SpriteRenderer renderer = doorRenderers[index];
                    if (renderer != null)
                        openingSequence.Join(renderer.DOFade(0f, config.elevatorFadeDuration));
                }
            }

            openingSequence.OnComplete(CompleteOpening);
        }

        private static RuntimeRevealAreaPresentation CreateBox(
            GameObject root,
            Vector2 size,
            RevealPresentationConfig config)
        {
            Transform shutter = new GameObject("Shutter").transform;
            shutter.SetParent(root.transform, false);
            float slatHeight = size.y / BoxSlatCount;
            for (int index = 0; index < BoxSlatCount; index++)
            {
                GameObject slat = PrototypeBootstrap.CreateVisualBlock(
                    $"Shutter Slat {index + 1}",
                    Vector2.zero,
                    new Vector2(size.x, Mathf.Max(.01f, slatHeight - .015f)),
                    new Color(.22f, .26f, .34f, 1f));
                slat.transform.SetParent(shutter, false);
                slat.transform.localPosition = new Vector3(
                    0f,
                    -size.y * .5f + slatHeight * (index + .5f),
                    0f);
                slat.GetComponent<SpriteRenderer>().sortingOrder = config.boxSortingOrder + index;
            }

            GameObject counterObject = new GameObject("Shred Counter");
            counterObject.transform.SetParent(shutter, false);
            counterObject.transform.localPosition = new Vector3(0f, 0f, -.1f);
            TextMesh counter = counterObject.AddComponent<TextMesh>();
            counter.anchor = TextAnchor.MiddleCenter;
            counter.alignment = TextAlignment.Center;
            counter.characterSize = .18f;
            counter.fontSize = 48;
            counter.color = Color.white;
            counter.GetComponent<MeshRenderer>().sortingOrder = config.boxSortingOrder + BoxSlatCount + 1;

            return new RuntimeRevealAreaPresentation(
                root,
                RevealAreaKind.Box,
                config,
                shutter,
                null,
                null,
                Array.Empty<SpriteRenderer>(),
                counter);
        }

        private static RuntimeRevealAreaPresentation CreateElevator(
            GameObject root,
            Vector2 size,
            RevealPresentationConfig config)
        {
            Vector2 doorSize = new Vector2(size.x * .5f, size.y);
            GameObject leftDoor = CreateDoor("Left Door", -size.x * .25f, doorSize, config.elevatorSortingOrder);
            leftDoor.transform.SetParent(root.transform, false);
            GameObject rightDoor = CreateDoor("Right Door", size.x * .25f, doorSize, config.elevatorSortingOrder + 1);
            rightDoor.transform.SetParent(root.transform, false);
            SpriteRenderer[] renderers =
            {
                leftDoor.GetComponent<SpriteRenderer>(),
                rightDoor.GetComponent<SpriteRenderer>()
            };

            return new RuntimeRevealAreaPresentation(
                root,
                RevealAreaKind.Elevator,
                config,
                null,
                leftDoor.transform,
                rightDoor.transform,
                renderers,
                null);
        }

        private static GameObject CreateDoor(string name, float localX, Vector2 size, int sortingOrder)
        {
            GameObject door = PrototypeBootstrap.CreateVisualBlock(
                name,
                Vector2.zero,
                size,
                new Color(.18f, .34f, .46f, 1f));
            door.transform.localPosition = new Vector3(localX, 0f, 0f);
            door.GetComponent<SpriteRenderer>().sortingOrder = sortingOrder;
            return door;
        }

        private void CompleteOpening()
        {
            if (isComplete)
                return;

            isComplete = true;
            root.SetActive(false);
            Action callback = completion;
            completion = null;
            callback?.Invoke();
        }
    }
}
