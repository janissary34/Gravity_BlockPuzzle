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
        // The final shutter strip breaks into a compact grid rather than a few large chunks.
        private const int BoxBurstColumns = 6;
        private const int BoxBurstRows = 2;
        private readonly GameObject root;
        private readonly RevealAreaKind kind;
        private readonly RevealPresentationConfig config;
        private readonly Transform boxShutter;
        private readonly Transform elevatorLeftDoor;
        private readonly Transform elevatorRightDoor;
        private readonly SpriteRenderer[] doorRenderers;
        private readonly GameObject boxCover;
        private readonly SpriteRenderer boxCoverRenderer;
        private readonly Transform[] boxBurstFragments;
        private readonly SpriteRenderer[] boxBurstRenderers;
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
            GameObject boxCover,
            SpriteRenderer boxCoverRenderer,
            Transform[] boxBurstFragments,
            SpriteRenderer[] boxBurstRenderers,
            TextMesh boxCounter)
        {
            this.root = root;
            this.kind = kind;
            this.config = config;
            this.boxShutter = boxShutter;
            this.elevatorLeftDoor = elevatorLeftDoor;
            this.elevatorRightDoor = elevatorRightDoor;
            this.doorRenderers = doorRenderers;
            this.boxCover = boxCover;
            this.boxCoverRenderer = boxCoverRenderer;
            this.boxBurstFragments = boxBurstFragments;
            this.boxBurstRenderers = boxBurstRenderers;
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
                : CreateElevator(root, size, level.backgroundColor, config);
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
                Transform coverTransform = boxCover != null ? boxCover.transform : null;
                float fullHeight = GetVisualBlockSize(boxCoverRenderer).y;
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
                        height => SetVisualBlockSize(
                            boxCoverRenderer,
                            new Vector2(GetVisualBlockSize(boxCoverRenderer).x, height)))
                        .SetEase(config.boxOpenEase));
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
            }
            else if (kind == RevealAreaKind.Elevator)
            {
                bool hasDoorMotion = false;
                if (elevatorLeftDoor != null)
                {
                    openingSequence.Append(elevatorLeftDoor.DOLocalMoveX(
                        leftDoorClosedPosition.x - config.elevatorDoorDistance,
                        config.elevatorOpenDuration).SetEase(config.elevatorOpenEase));
                    hasDoorMotion = true;
                }

                if (elevatorRightDoor != null)
                {
                    Tween rightDoorTween = elevatorRightDoor.DOLocalMoveX(
                        rightDoorClosedPosition.x + config.elevatorDoorDistance,
                        config.elevatorOpenDuration).SetEase(config.elevatorOpenEase);
                    if (hasDoorMotion)
                        openingSequence.Join(rightDoorTween);
                    else
                        openingSequence.Append(rightDoorTween);

                    hasDoorMotion = true;
                }

                bool hasFade = false;
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

        private static RuntimeRevealAreaPresentation CreateBox(
            GameObject root,
            Vector2 size,
            RevealPresentationConfig config)
        {
            Transform shutter = new GameObject("Shutter").transform;
            shutter.SetParent(root.transform, false);
            GameObject cover = PrototypeBootstrap.CreateVisualBlock(
                "Garage Shutter Cover",
                Vector2.zero,
                size,
                new Color(.22f, .26f, .34f, 1f));
            cover.transform.SetParent(shutter, false);
            SpriteRenderer coverRenderer = cover.GetComponent<SpriteRenderer>();
            SetVisualBlockSpriteToFit(coverRenderer, config.boxCoverSprite, size);
            coverRenderer.sortingOrder = config.boxSortingOrder;

            int fragmentCount = BoxBurstColumns * BoxBurstRows;
            Transform[] fragments = new Transform[fragmentCount];
            SpriteRenderer[] fragmentRenderers = new SpriteRenderer[fragmentCount];
            Vector2 fragmentSize = new Vector2(
                size.x / BoxBurstColumns,
                size.y / BoxBurstRows);
            int fragmentIndex = 0;
            for (int y = 0; y < BoxBurstRows; y++)
            for (int x = 0; x < BoxBurstColumns; x++)
            {
                GameObject fragment = PrototypeBootstrap.CreateVisualBlock(
                    $"Shutter Burst Fragment {fragmentIndex + 1}",
                    Vector2.zero,
                    fragmentSize,
                    new Color(.22f, .26f, .34f, 1f));
                fragment.transform.SetParent(shutter, false);
                fragment.transform.localPosition = new Vector3(
                    -size.x * .5f + fragmentSize.x * (x + .5f),
                    -size.y * .5f + fragmentSize.y * (y + .5f),
                    0f);
                SpriteRenderer renderer = fragment.GetComponent<SpriteRenderer>();
                SetVisualBlockSpriteToFit(renderer, config.boxCoverSprite, fragmentSize);
                renderer.sortingOrder = config.boxSortingOrder + 1;
                fragment.SetActive(false);
                fragments[fragmentIndex] = fragment.transform;
                fragmentRenderers[fragmentIndex] = renderer;
                fragmentIndex++;
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
            counter.GetComponent<MeshRenderer>().sortingOrder = config.boxSortingOrder + 1;

            return new RuntimeRevealAreaPresentation(
                root,
                RevealAreaKind.Box,
                config,
                shutter,
                null,
                null,
                Array.Empty<SpriteRenderer>(),
                cover,
                coverRenderer,
                fragments,
                fragmentRenderers,
                counter);
        }

        private static RuntimeRevealAreaPresentation CreateElevator(
            GameObject root,
            Vector2 size,
            Color boardColor,
            RevealPresentationConfig config)
        {
            GameObject frame = PrototypeBootstrap.CreateVisualBlock(
                "Elevator Frame",
                Vector2.zero,
                size,
                Color.white);
            frame.transform.SetParent(root.transform, false);
            SpriteRenderer frameRenderer = frame.GetComponent<SpriteRenderer>();
            SetVisualBlockSpriteToFit(frameRenderer,
                config.elevatorFrameSprite != null ? config.elevatorFrameSprite : config.elevatorDoorSprite,
                size);
            frameRenderer.sortingOrder = config.elevatorSortingOrder;

            Vector2 doorSize = new Vector2(
                size.x * config.elevatorDoorWidthRatio * .5f,
                size.y * config.elevatorDoorHeightRatio);
            float doorCentreY = size.y * config.elevatorDoorCenterYOffset;
            GameObject opening = PrototypeBootstrap.CreateVisualBlock(
                "Elevator Opening",
                Vector2.zero,
                new Vector2(doorSize.x * 2f, doorSize.y),
                config.elevatorOpeningColor.a > 0f ? config.elevatorOpeningColor : boardColor);
            opening.transform.SetParent(root.transform, false);
            opening.transform.localPosition = new Vector3(0f, doorCentreY, 0f);
            SpriteRenderer openingRenderer = opening.GetComponent<SpriteRenderer>();
            SetVisualBlockSize(openingRenderer, new Vector2(doorSize.x * 2f, doorSize.y));
            openingRenderer.sortingOrder = config.elevatorSortingOrder + 1;

            Sprite leftDoorSprite = config.elevatorLeftDoorSprite != null
                ? config.elevatorLeftDoorSprite
                : config.elevatorFrameSprite != null ? config.elevatorFrameSprite : config.elevatorDoorSprite;
            Sprite rightDoorSprite = config.elevatorRightDoorSprite != null
                ? config.elevatorRightDoorSprite
                : config.elevatorFrameSprite != null ? config.elevatorFrameSprite : config.elevatorDoorSprite;
            GameObject leftDoor = CreateDoor(
                "Left Door",
                -doorSize.x * .5f,
                doorSize,
                config.elevatorSortingOrder + 2,
                leftDoorSprite);
            leftDoor.transform.SetParent(root.transform, false);
            leftDoor.transform.localPosition += Vector3.up * doorCentreY;
            GameObject rightDoor = CreateDoor(
                "Right Door",
                doorSize.x * .5f,
                doorSize,
                config.elevatorSortingOrder + 3,
                rightDoorSprite);
            rightDoor.transform.SetParent(root.transform, false);
            rightDoor.transform.localPosition += Vector3.up * doorCentreY;
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
                null,
                null,
                Array.Empty<Transform>(),
                Array.Empty<SpriteRenderer>(),
                null);
        }

        private static GameObject CreateDoor(
            string name,
            float localX,
            Vector2 size,
            int sortingOrder,
            Sprite doorSprite)
        {
            GameObject door = PrototypeBootstrap.CreateVisualBlock(
                name,
                Vector2.zero,
                size,
                new Color(.18f, .34f, .46f, 1f));
            door.transform.localPosition = new Vector3(localX, 0f, 0f);
            SpriteRenderer renderer = door.GetComponent<SpriteRenderer>();
            SetVisualBlockSpriteToFit(renderer, doorSprite, size);
            renderer.sortingOrder = sortingOrder;
            return door;
        }

        private void PlayBoxBurst()
        {
            if (boxCover != null)
                boxCover.SetActive(false);
            if (boxCounter != null)
                boxCounter.gameObject.SetActive(false);

            for (int index = 0; index < boxBurstFragments.Length; index++)
            {
                Transform fragment = boxBurstFragments[index];
                SpriteRenderer renderer = boxBurstRenderers[index];
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
                float rotationAmount = Mathf.Lerp(.35f, 1f, GetFragmentUnitValue(index, 4))
                    * config.boxBreakRotation;
                float horizontalOffset = horizontalDirection * config.boxBreakScatterDistance;
                float dropDistance = config.boxBreakDropDistance * dropFactor;

                // The lower row keeps a small outward bias so the original strip visibly breaks apart.
                if (row > 0)
                    horizontalOffset += horizontalDirection * config.boxBreakSeparation;
                fragment.DOLocalMove(
                        startPosition + new Vector3(horizontalOffset, -dropDistance, 0f),
                        config.boxBurstDuration)
                    .SetEase(Ease.OutQuad)
                    .SetLink(root, LinkBehaviour.KillOnDisable);
                fragment.DORotate(
                        new Vector3(0f, 0f, rotationDirection * rotationAmount),
                        config.boxBurstDuration,
                        RotateMode.LocalAxisAdd)
                    .SetEase(Ease.OutQuad)
                    .SetLink(root, LinkBehaviour.KillOnDisable);
                renderer.DOFade(0f, config.boxBurstDuration)
                    .SetEase(Ease.InQuad)
                    .SetLink(root, LinkBehaviour.KillOnDisable);
            }
        }

        private void PrepareBoxBurstFragments(Vector3 burstOrigin, float residualHeight)
        {
            float coverWidth = GetVisualBlockSize(boxCoverRenderer).x;
            for (int index = 0; index < boxBurstFragments.Length; index++)
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
                SetVisualBlockSize(
                    boxBurstRenderers[index],
                    new Vector2(coverWidth / BoxBurstColumns, fragmentHeight));
            }
        }

        /// <summary>
        /// Uses a SpriteRenderer's nine-slice size instead of transform scale.
        /// The area therefore remains inside its authored grid bounds while the
        /// imported frame corners retain their intended proportions.
        /// </summary>
        private static void SetVisualBlockSpriteToFit(
            SpriteRenderer renderer,
            Sprite sprite,
            Vector2 targetSize)
        {
            if (renderer == null)
                return;

            if (sprite != null)
                renderer.sprite = sprite;

            renderer.color = Color.white;
            renderer.drawMode = SpriteDrawMode.Sliced;
            SetVisualBlockSize(renderer, targetSize);
        }

        private static void SetVisualBlockSize(SpriteRenderer renderer, Vector2 targetSize)
        {
            if (renderer == null || renderer.sprite == null)
                return;

            renderer.transform.localScale = Vector3.one;
            renderer.size = targetSize;
        }

        private static Vector2 GetVisualBlockSize(SpriteRenderer renderer)
        {
            if (renderer == null || renderer.sprite == null)
                return Vector2.zero;

            return renderer.drawMode == SpriteDrawMode.Simple
                ? renderer.sprite.bounds.size
                : renderer.size;
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
            root.SetActive(false);
            Action callback = completion;
            completion = null;
            callback?.Invoke();
        }
    }
}
