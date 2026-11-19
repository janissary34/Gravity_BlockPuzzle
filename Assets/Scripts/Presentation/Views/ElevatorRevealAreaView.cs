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
        [Tooltip("Clips the sliding doors to the doorway so they disappear under the cabinet rim.")]
        [SerializeField] private SpriteMask doorMask;

        private Action<ElevatorRevealAreaView> returnHandler;
        private RevealPresentationConfig config;
        private Sequence openingSequence;
        private Action completion;
        private bool isComplete;
        private float doorTravel;

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
            float widthRatio = config != null ? config.elevatorDoorWidthRatio : .86f;
            float heightRatio = config != null ? config.elevatorDoorHeightRatio : .83f;
            float doorCenterY = areaSize.y * (config != null ? config.elevatorDoorCenterYOffset : 0f);
            Vector2 frameSize = new Vector2(areaSize.x * widthScale, areaSize.y);

            if (frameRenderer != null && config != null)
            {
                if (config.elevatorFrameSprite != null)
                    frameRenderer.sprite = config.elevatorFrameSprite;
                else if (config.elevatorDoorSprite != null)
                    frameRenderer.sprite = config.elevatorDoorSprite;
            }

            // The cabinet is drawn at a uniform art scale (fitted to the shorter
            // side) and 9-sliced to the area, so a wide area stretches only the
            // cabinet middle instead of squashing its rim and corners.
            float frameArtScale = ResolveUniformFitScale(frameRenderer, frameSize);
            Vector2 frameArtSize = frameRenderer != null && frameRenderer.sprite != null
                ? (Vector2)frameRenderer.sprite.bounds.size * frameArtScale
                : frameSize;
            Vector2 aperture = new Vector2(
                Mathf.Max(.01f, frameSize.x - frameArtSize.x * (1f - widthRatio)),
                Mathf.Max(.01f, frameSize.y - frameArtSize.y * (1f - heightRatio)));
            Vector2 doorSize = new Vector2(aperture.x * .5f, aperture.y);
            doorTravel = doorSize.x;

            if (frameRenderer != null)
            {
                frameRenderer.gameObject.SetActive(true);
                if (config != null)
                    frameRenderer.sortingOrder = config.elevatorSortingOrder;
                ApplySlicedSize(frameRenderer, frameSize, frameArtScale);
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
                openingRenderer.size = aperture;
                openingRenderer.transform.localPosition = new Vector3(0f, doorCenterY, 0f);
            }

            int doorSortingOrder = config != null ? config.elevatorDoorSortingOrder : 60;
            if (doorMask != null)
            {
                doorMask.gameObject.SetActive(true);
                doorMask.transform.localPosition = new Vector3(0f, doorCenterY, 0f);
                Vector2 maskSpriteSize = doorMask.sprite != null ? (Vector2)doorMask.sprite.bounds.size : Vector2.one;
                doorMask.transform.localScale = new Vector3(
                    aperture.x / Mathf.Max(.0001f, maskSpriteSize.x),
                    aperture.y / Mathf.Max(.0001f, maskSpriteSize.y),
                    1f);
                // The custom range straddles only the two door orders so the
                // aperture clip never affects pieces or other masked layers.
                doorMask.isCustomRangeActive = true;
                doorMask.backSortingOrder = doorSortingOrder - 1;
                doorMask.frontSortingOrder = doorSortingOrder + 2;
            }
            else
            {
                Debug.LogWarning(
                    $"[ElevatorRevealAreaView] '{name}' has no door mask; sliding doors will not be clipped to the doorway. Run Gravity Puzzle/Art/Add Elevator Door Mask.",
                    this);
            }

            bool usesSharedDoorSprite = config == null ||
                                        config.elevatorLeftDoorSprite == null ||
                                        config.elevatorRightDoorSprite == null ||
                                        config.elevatorLeftDoorSprite == config.elevatorRightDoorSprite;
            for (int i = 0; i < 2; i++)
            {
                Transform door = i == 0 ? elevatorLeftDoor : elevatorRightDoor;
                SpriteRenderer doorRenderer = GetDoorRenderer(i);
                if (door != null)
                {
                    float side = i == 0 ? -1f : 1f;
                    door.localPosition = new Vector3(side * doorSize.x * .5f, doorCenterY, 0f);
                    door.gameObject.SetActive(true);
                }

                if (doorRenderer == null)
                    continue;

                Color c = doorRenderer.color;
                c.a = 1f;
                doorRenderer.color = c;
                if (config != null)
                {
                    Sprite doorSprite = i == 0 ? config.elevatorLeftDoorSprite : config.elevatorRightDoorSprite;
                    if (doorSprite != null)
                        doorRenderer.sprite = doorSprite;
                }
                doorRenderer.sortingOrder = doorSortingOrder + i;
                doorRenderer.maskInteraction = doorMask != null
                    ? SpriteMaskInteraction.VisibleInsideMask
                    : SpriteMaskInteraction.None;
                doorRenderer.flipX = usesSharedDoorSprite && i == 0;
                ApplySlicedSize(doorRenderer, doorSize, ResolveUniformFitScale(doorRenderer, doorSize));
            }

            isComplete = false;
        }

        /// <summary>
        /// Largest uniform scale at which the renderer's sprite still fits inside
        /// <paramref name="targetSize"/>; keeps 9-slice borders at art proportions.
        /// </summary>
        private static float ResolveUniformFitScale(SpriteRenderer renderer, Vector2 targetSize)
        {
            if (renderer == null || renderer.sprite == null)
                return 1f;

            Vector2 spriteSize = renderer.sprite.bounds.size;
            if (spriteSize.x <= 0f || spriteSize.y <= 0f)
                return 1f;

            return Mathf.Max(.0001f, Mathf.Min(targetSize.x / spriteSize.x, targetSize.y / spriteSize.y));
        }

        private static void ApplySlicedSize(SpriteRenderer renderer, Vector2 worldSize, float artScale)
        {
            renderer.drawMode = SpriteDrawMode.Sliced;
            renderer.transform.localScale = new Vector3(artScale, artScale, 1f);
            renderer.size = worldSize / artScale;
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

            // Each panel slides straight outward by its own width behind the
            // doorway mask, revealing the interior pieces between them.
            if (elevatorLeftDoor != null)
            {
                openingSequence.Join(elevatorLeftDoor
                    .DOLocalMoveX(elevatorLeftDoor.localPosition.x - doorTravel, config.elevatorOpenDuration)
                    .SetEase(config.elevatorOpenEase));
            }

            if (elevatorRightDoor != null)
            {
                openingSequence.Join(elevatorRightDoor
                    .DOLocalMoveX(elevatorRightDoor.localPosition.x + doorTravel, config.elevatorOpenDuration)
                    .SetEase(config.elevatorOpenEase));
            }

            if (elevatorLeftDoor == null && elevatorRightDoor == null)
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
            if (doorMask != null)
                doorMask.gameObject.SetActive(visible);

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
