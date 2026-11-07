using UnityEngine;

namespace GravityPuzzle
{
    /// <summary>
    /// Owns the collision isolation used only by the temporary shredder
    /// physics island. This keeps dynamic handoff bodies away from normal
    /// grid-owned pieces and non-grid scene colliders.
    /// </summary>
    internal static class ShredderPhysicsLayers
    {
        private const string GuideLayerName = "ShredderPhysicsGuide";
        private const string PieceLayerName = "ShredderPhysicsPiece";
        internal const string BlockerLayerName = "ShredderPhysicsBlocker";
        internal const string FreefallLayerName = "ShredderPhysicsFreefall";

        private static int guideLayer = -1;
        private static int pieceLayer = -1;
        private static int blockerLayer = -1;
        private static int freefallLayer = -1;
        private static bool configured;

        public static int GuideLayer => guideLayer;
        public static int PieceLayer => pieceLayer;
        public static int BlockerLayer => blockerLayer;
        public static int FreefallLayer => freefallLayer;
        public static bool IsAvailable => configured &&
                                          guideLayer >= 0 &&
                                          pieceLayer >= 0 &&
                                          blockerLayer >= 0 &&
                                          freefallLayer >= 0;

        public static bool Configure()
        {
            guideLayer = LayerMask.NameToLayer(GuideLayerName);
            pieceLayer = LayerMask.NameToLayer(PieceLayerName);
            blockerLayer = LayerMask.NameToLayer(BlockerLayerName);
            freefallLayer = LayerMask.NameToLayer(FreefallLayerName);
            configured = guideLayer >= 0 && pieceLayer >= 0 && blockerLayer >= 0 &&
                         freefallLayer >= 0;

            if (!configured)
            {
                Debug.LogError(
                    $"[ShredderPhysicsIsland] Missing required layers '{GuideLayerName}', " +
                    $"'{PieceLayerName}', '{BlockerLayerName}', and/or '{FreefallLayerName}'. " +
                    "The temporary physics island is disabled until all required layers are added in Project Settings.");
                return false;
            }

            // Explicitly isolate this small island. It is intentional that
            // normal Default-layer colliders cannot become accidental supports
            // for a temporary Dynamic piece.
            for (int layer = 0; layer < 32; layer++)
            {
                Physics2D.IgnoreLayerCollision(guideLayer, layer, true);
                Physics2D.IgnoreLayerCollision(pieceLayer, layer, true);
                Physics2D.IgnoreLayerCollision(freefallLayer, layer, true);
            }

            Physics2D.IgnoreLayerCollision(guideLayer, pieceLayer, false);
            Physics2D.IgnoreLayerCollision(pieceLayer, pieceLayer, false);
            // Grid-authored static obstacles and map boundaries are the only
            // normal-board collision surfaces allowed inside the island. They
            // share the same authoritative blocked cells as the grid; all
            // other Default-layer scene objects remain isolated.
            Physics2D.IgnoreLayerCollision(pieceLayer, blockerLayer, false);
            // Off-board freefall bodies are deterministically stacked and controlled
            // by BlockShredder without engine collision solver artifacts (such as
            // SAT contact normal flips or penetration catapulting).
            Physics2D.IgnoreLayerCollision(freefallLayer, freefallLayer, true);
            // This project keeps normally managed pieces on Default. Make the
            // normal board's relationship with the migrated blocker geometry
            // explicit too, rather than relying on the project matrix's
            // current all-enabled default.
            Physics2D.IgnoreLayerCollision(0, blockerLayer, false);
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            guideLayer = -1;
            pieceLayer = -1;
            blockerLayer = -1;
            freefallLayer = -1;
            configured = false;
        }
    }
}
