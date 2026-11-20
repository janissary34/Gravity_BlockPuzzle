using System;
using DG.Tweening;
using GravityPuzzle.Config;
using GravityPuzzle.Gameplay.Reveal;
using GravityPuzzle.Infrastructure.Pooling;
using GravityPuzzle.Infrastructure.Services;
using UnityEngine;

namespace GravityPuzzle.Presentation.Views
{
    public sealed class ElevatorRevealAreaView : MonoBehaviour, IRevealAreaPresentation, IPoolable, IPoolReturnReceiver<ElevatorRevealAreaView>
    {
        [SerializeField] private SpriteRenderer frameRenderer;
        [SerializeField] private SpriteRenderer openingRenderer;
        [SerializeField] private Transform elevatorLeftDoor;
        [SerializeField] private Transform elevatorRightDoor;
        [Tooltip("Flat door panels inside the rims; they collapse toward the outer edges while opening.")]
        [SerializeField] private SpriteRenderer[] doorRenderers;
        [Tooltip("Yellow door rims drawn above the panels; they collapse together with their panels.")]
        [SerializeField] private SpriteRenderer[] doorRimRenderers;

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
            float widthRatio = config != null ? config.elevatorDoorWidthRatio : 1f;
            float heightRatio = config != null ? config.elevatorDoorHeightRatio : 1f;
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

            bool usesSharedDoorSprite = config == null ||
                                        config.elevatorLeftDoorSprite == null ||
                                        config.elevatorRightDoorSprite == null ||
                                        config.elevatorLeftDoorSprite == config.elevatorRightDoorSprite;
            Sprite rimSprite = config != null ? config.elevatorDoorRimSprite : null;
            Sprite panelSprite = config != null ? config.elevatorDoorPanelSprite : null;
            bool usesRimPanels = rimSprite != null && panelSprite != null &&
                            GetDoorRimRenderer(0) != null && GetDoorRimRenderer(1) != null;
            if (!usesRimPanels)
            {
                Debug.LogWarning(
                    $"[ElevatorRevealAreaView] '{name}' has no door rim setup; the full door art will collapse instead of only its panel. Run Gravity Puzzle/Art/Author Elevator Doorway.",
                    this);
            }

            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                Vector3 doorPosition = new Vector3(side * doorSize.x * .5f, doorCenterY, 0f);
                Transform door = i == 0 ? elevatorLeftDoor : elevatorRightDoor;
                if (door != null)
                {
                    door.localPosition = doorPosition;
                    door.gameObject.SetActive(true);
                }

                SpriteRenderer doorRenderer = GetDoorRenderer(i);
                SpriteRenderer rimRenderer = GetDoorRimRenderer(i);
                if (usesRimPanels)
                {
                    // The yellow rim is a separate layer above the flat panel;
                    // both collapse together toward the door's outer edge.
                    rimRenderer.gameObject.SetActive(true);
                    rimRenderer.transform.localPosition = doorPosition;
                    rimRenderer.sprite = rimSprite;
                    rimRenderer.flipX = i == 0;
                    float rimArtScale = ResolveUniformFitScale(rimRenderer, doorSize);
                    ApplySlicedSize(rimRenderer, doorSize, rimArtScale);

                    if (doorRenderer != null)
                    {
                        float inset = config.elevatorDoorPanelInsetPixels / rimSprite.pixelsPerUnit * rimArtScale;
                        Vector2 panelSize = new Vector2(
                            Mathf.Max(.01f, doorSize.x - inset * 2f),
                            Mathf.Max(.01f, doorSize.y - inset * 2f));
                        doorRenderer.sprite = panelSprite;
                        doorRenderer.color = config.elevatorDoorPanelColor;
                        doorRenderer.flipX = false;
                        ApplySlicedSize(doorRenderer, panelSize, 1f);
                    }
                }
                else
                {
                    if (rimRenderer != null)
                        rimRenderer.gameObject.SetActive(false);

                    if (doorRenderer != null)
                    {
                        Sprite doorSprite = config != null
                            ? (i == 0 ? config.elevatorLeftDoorSprite : config.elevatorRightDoorSprite)
                            : null;
                        if (doorSprite != null)
                            doorRenderer.sprite = doorSprite;
                        doorRenderer.color = Color.white;
                        doorRenderer.flipX = usesSharedDoorSprite && i == 0;
                        ApplySlicedSize(doorRenderer, doorSize, ResolveUniformFitScale(doorRenderer, doorSize));
                    }
                }
            }

            SetDoorLayers(opening: false);
            isComplete = false;
        }

        /// <summary>
        /// Closed doors are board decoration behind the pieces standing on the
        /// Elevator; opening doors sit above the interior pieces. Rims always
        /// render directly above their panels.
        /// </summary>
        private void SetDoorLayers(bool opening)
        {
            int panelOrder = opening
                ? (config != null ? config.elevatorDoorSortingOrder : 60)
                : (config != null ? config.elevatorSortingOrder : 1) + 2;
            for (int i = 0; i < 2; i++)
            {
                SpriteRenderer doorRenderer = GetDoorRenderer(i);
                if (doorRenderer != null)
                {
                    doorRenderer.sortingOrder = panelOrder;
                    doorRenderer.maskInteraction = SpriteMaskInteraction.None;
                }

                SpriteRenderer rimRenderer = GetDoorRimRenderer(i);
                if (rimRenderer != null)
                    rimRenderer.sortingOrder = panelOrder + 1;
            }
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
            SoundFeedbackRuntime.ElevatorOpened();

            if (config == null)
            {
                CompleteOpening();
                return;
            }

            // Top-layer pieces have all left once opening starts, so only now
            // the doors lift above the freshly activated interior pieces.
            SetDoorLayers(opening: true);

            openingSequence = DOTween.Sequence()
                .SetLink(gameObject, LinkBehaviour.KillOnDisable)
                .SetAutoKill(true);

            // Each door (panel and its yellow rim together) keeps its height and
            // its outer edge, while its X scale shrinks to zero toward that edge,
            // revealing the interior pieces from the middle outward.
            if (elevatorLeftDoor != null)
                openingSequence.Join(CreateDoorCollapse(elevatorLeftDoor, GetDoorRenderer(0), GetDoorRimRenderer(0), -1f));
            if (elevatorRightDoor != null)
                openingSequence.Join(CreateDoorCollapse(elevatorRightDoor, GetDoorRenderer(1), GetDoorRimRenderer(1), 1f));

            if (elevatorLeftDoor == null && elevatorRightDoor == null)
                openingSequence.AppendInterval(config.elevatorOpenDuration);

            openingSequence.OnComplete(CompleteOpening);
        }

        /// <summary>
        /// Shrinks one door's X scale from 1 to 0 while its outer vertical edge
        /// stays fixed and its height is untouched. The rim (when present) and
        /// the panel inside it share one centre, so both collapse together.
        /// Position is compensated from the door's measured closed bounds
        /// instead of creating runtime pivot parents.
        /// </summary>
        private Tween CreateDoorCollapse(
            Transform door,
            SpriteRenderer panelRenderer,
            SpriteRenderer rimRenderer,
            float outwardSign)
        {
            Transform parent = door.parent;
            Transform rim = rimRenderer != null && rimRenderer.gameObject.activeSelf ? rimRenderer.transform : null;
            SpriteRenderer outlineRenderer = rim != null ? rimRenderer : panelRenderer;
            Vector3 closedDoorPosition = door.localPosition;
            Vector3 closedDoorScale = door.localScale;
            Vector3 closedRimPosition = rim != null ? rim.localPosition : Vector3.zero;
            Vector3 closedRimScale = rim != null ? rim.localScale : Vector3.one;

            float closedCenterX = closedDoorPosition.x;
            float closedWidth = 0f;
            if (outlineRenderer != null && parent != null)
            {
                Bounds closedBounds = outlineRenderer.bounds;
                closedCenterX = parent.InverseTransformPoint(closedBounds.center).x;
                closedWidth = closedBounds.size.x / Mathf.Max(.0001f, Mathf.Abs(parent.lossyScale.x));
            }

            float doorOffsetX = closedDoorPosition.x - closedCenterX;
            float rimOffsetX = closedRimPosition.x - closedCenterX;
            float outerEdgeX = closedCenterX + outwardSign * closedWidth * .5f;
            return DOTween.To(
                    () => 1f,
                    widthFraction =>
                    {
                        float centerX = outerEdgeX - outwardSign * closedWidth * widthFraction * .5f;
                        door.localScale = new Vector3(closedDoorScale.x * widthFraction, closedDoorScale.y, closedDoorScale.z);
                        door.localPosition = new Vector3(
                            centerX + doorOffsetX * widthFraction,
                            closedDoorPosition.y,
                            closedDoorPosition.z);
                        if (rim == null)
                            return;

                        rim.localScale = new Vector3(closedRimScale.x * widthFraction, closedRimScale.y, closedRimScale.z);
                        rim.localPosition = new Vector3(
                            centerX + rimOffsetX * widthFraction,
                            closedRimPosition.y,
                            closedRimPosition.z);
                    },
                    0f,
                    config.elevatorOpenDuration)
                .SetEase(config.elevatorOpenEase);
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

            for (int index = 0; index < 2; index++)
            {
                SpriteRenderer doorRenderer = GetDoorRenderer(index);
                if (doorRenderer != null)
                    doorRenderer.gameObject.SetActive(visible);
                SpriteRenderer rimRenderer = GetDoorRimRenderer(index);
                if (rimRenderer != null)
                    rimRenderer.gameObject.SetActive(visible);
            }
        }

        private SpriteRenderer GetDoorRenderer(int index)
        {
            return doorRenderers != null && index >= 0 && index < doorRenderers.Length
                ? doorRenderers[index]
                : null;
        }

        private SpriteRenderer GetDoorRimRenderer(int index)
        {
            return doorRimRenderers != null && index >= 0 && index < doorRimRenderers.Length
                ? doorRimRenderers[index]
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
