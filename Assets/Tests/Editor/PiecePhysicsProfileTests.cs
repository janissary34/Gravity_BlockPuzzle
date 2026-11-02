using System;
using System.Collections.Generic;
using System.Reflection;
using GravityPuzzle.Config;
using GravityPuzzle.Core.Grid;
using GravityPuzzle.Gameplay.Pieces;
using NUnit.Framework;
using UnityEngine;

namespace GravityPuzzle.Tests.Editor
{
    public sealed class PiecePhysicsProfileTests
    {
        [Test]
        public void PieceLifecycle_RequiresOpenFreefallBeforeOffBoardShredding()
        {
            PieceModel model = new PieceModel(
                1,
                new GridCoordinate(0, 0),
                new GridCoordinate(0, 0),
                new List<GridCoordinate> { new GridCoordinate(0, 0) });

            Assert.That(model.TrySetState(PieceState.HandoffToPhysics), Is.True);
            Assert.That(model.TrySetState(PieceState.OpenFreefall), Is.True);
            Assert.That(model.TrySetState(PieceState.Shredding), Is.True);
        }

        [Test]
        public void PhysicsProfiles_ResetEveryContactRelevantSetting()
        {
            ConfigurePhysicsLayers();
            GameObject root = new GameObject("PiecePhysicsProfileTest");
            GameObject cell = new GameObject("Cell");
            cell.transform.SetParent(root.transform, false);
            BoxCollider2D cellCollider = cell.AddComponent<BoxCollider2D>();
            PuzzlePiece piece = root.AddComponent<PuzzlePiece>();
            ShredderConfig config = ScriptableObject.CreateInstance<ShredderConfig>();

            try
            {
                piece.ApplyGridPhysicsProfile();
                Assert.That(piece.PhysicsProfile, Is.EqualTo(PiecePhysicsProfile.Grid));
                Assert.That(piece.Body.bodyType, Is.EqualTo(RigidbodyType2D.Kinematic));
                Assert.That(piece.Body.gravityScale, Is.Zero);
                Assert.That(cellCollider.enabled, Is.True,
                    "Grid input selects pieces through overlap queries, so its collider must remain queryable.");

                piece.ApplyOpenFreefallPhysicsProfile(config, 3f);
                Assert.That(piece.PhysicsProfile, Is.EqualTo(PiecePhysicsProfile.OpenFreefall));
                Assert.That(piece.Body.bodyType, Is.EqualTo(RigidbodyType2D.Dynamic));
                Assert.That(piece.Body.simulated, Is.True);
                Assert.That(piece.Body.constraints, Is.EqualTo(RigidbodyConstraints2D.None));
                Assert.That(piece.Body.gravityScale, Is.EqualTo(config.PhysicsFreefallGravityScale));
                Assert.That(cellCollider.enabled, Is.True);
                Assert.That(cellCollider.isTrigger, Is.False);
                int freefallLayer = LayerMask.NameToLayer("ShredderPhysicsFreefall");
                Assert.That(root.layer, Is.EqualTo(freefallLayer));
                Assert.That(cell.layer, Is.EqualTo(freefallLayer));
                for (int layer = 0; layer < 32; layer++)
                {
                    bool expectedIgnored = layer != freefallLayer;
                    Assert.That(
                        Physics2D.GetIgnoreLayerCollision(freefallLayer, layer),
                        Is.EqualTo(expectedIgnored),
                        $"Open freefall must only contact its own dedicated layer (layer {layer}).");
                }

                piece.ApplyShreddingPhysicsProfile();
                Assert.That(piece.PhysicsProfile, Is.EqualTo(PiecePhysicsProfile.Shredding));
                Assert.That(piece.Body.simulated, Is.False);
                Assert.That(piece.Body.velocity, Is.EqualTo(Vector2.zero));
                Assert.That(piece.Body.angularVelocity, Is.Zero);
                Assert.That(cellCollider.enabled, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void ConfigurePhysicsLayers()
        {
            Type layersType = typeof(PuzzlePiece).Assembly.GetType("GravityPuzzle.ShredderPhysicsLayers");
            MethodInfo configure = layersType?.GetMethod(
                "Configure",
                BindingFlags.Public | BindingFlags.Static);
            Assert.That(configure, Is.Not.Null, "Shredder physics layer setup must remain available to the profile owner.");
            Assert.That((bool)configure.Invoke(null, null), Is.True,
                "The ShredderPhysicsFreefall project layer must exist for isolated open freefall.");
        }
    }
}
