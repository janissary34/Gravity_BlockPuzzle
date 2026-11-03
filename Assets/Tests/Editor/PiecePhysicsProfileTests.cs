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
            // PuzzlePiece caches its required Rigidbody2D in Awake. A runtime
            // piece prefab always owns one before PuzzlePiece is enabled, so
            // mirror that construction order in this isolated profile test.
            root.AddComponent<Rigidbody2D>();
            PuzzlePiece piece = root.AddComponent<PuzzlePiece>();
            InvokeAwake(piece);
            ShredderConfig config = ScriptableObject.CreateInstance<ShredderConfig>();

            try
            {
                piece.ApplyLifecyclePhysicsProfile(PieceState.Placed);
                Assert.That(piece.PhysicsProfile, Is.EqualTo(PiecePhysicsProfile.Grid));
                Assert.That(piece.Body.bodyType, Is.EqualTo(RigidbodyType2D.Kinematic));
                Assert.That(piece.Body.gravityScale, Is.Zero);
                Assert.That(piece.Body.simulated, Is.False);
                Assert.That(cellCollider.enabled, Is.False,
                    "Grid input is resolved through the authoritative board, not a physics collider.");

                float handoffSpeed = config.PhysicsFreefallEntrySpeed + 1f;
                piece.ApplyLifecyclePhysicsProfile(PieceState.OpenFreefall, config, handoffSpeed);
                Assert.That(piece.PhysicsProfile, Is.EqualTo(PiecePhysicsProfile.OpenFreefall));
                Assert.That(piece.Body.bodyType, Is.EqualTo(RigidbodyType2D.Dynamic));
                Assert.That(piece.Body.simulated, Is.True);
                Assert.That(piece.Body.constraints, Is.EqualTo(RigidbodyConstraints2D.None));
                Assert.That(piece.Body.gravityScale, Is.EqualTo(config.PhysicsFreefallGravityScale));
                Assert.That(piece.Body.velocity, Is.EqualTo(Vector2.down * handoffSpeed),
                    "Open freefall must retain a faster authored handoff speed instead of resetting it.");
                Assert.That(cellCollider.enabled, Is.True);
                Assert.That(cellCollider.isTrigger, Is.False);
                int freefallLayer = LayerMask.NameToLayer("ShredderPhysicsFreefall");
                Assert.That(root.layer, Is.EqualTo(freefallLayer));
                Assert.That(cell.layer, Is.EqualTo(freefallLayer));
                int feedLayer = LayerMask.NameToLayer("ShredderPhysicsFeed");
                for (int layer = 0; layer < 32; layer++)
                {
                    bool expectedIgnored = layer != freefallLayer && layer != feedLayer;
                    Assert.That(
                        Physics2D.GetIgnoreLayerCollision(freefallLayer, layer),
                        Is.EqualTo(expectedIgnored),
                        $"Open freefall must only contact freefall and cutting pieces (layer {layer}).");
                }

                piece.ApplyLifecyclePhysicsProfile(PieceState.Shredding, config);
                Assert.That(piece.PhysicsProfile, Is.EqualTo(PiecePhysicsProfile.Shredding));
                Assert.That(piece.Body.bodyType, Is.EqualTo(RigidbodyType2D.Dynamic));
                Assert.That(piece.Body.simulated, Is.True);
                Assert.That(piece.Body.velocity, Is.EqualTo(Vector2.down * handoffSpeed),
                    "Cutting must preserve the solved freefall velocity.");
                Assert.That(cellCollider.enabled, Is.True);
                Assert.That(cellCollider.isTrigger, Is.False);
                Assert.That(root.layer, Is.EqualTo(feedLayer));
                Assert.That(cell.layer, Is.EqualTo(feedLayer));
                for (int layer = 0; layer < 32; layer++)
                {
                    bool expectedIgnored = layer != freefallLayer && layer != feedLayer;
                    Assert.That(
                        Physics2D.GetIgnoreLayerCollision(feedLayer, layer),
                        Is.EqualTo(expectedIgnored),
                        $"Cutting pieces must only contact freefall and cutting pieces (layer {layer}).");
                }
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

        private static void InvokeAwake(PuzzlePiece piece)
        {
            MethodInfo awake = typeof(PuzzlePiece).GetMethod(
                "Awake",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(awake, Is.Not.Null);
            awake.Invoke(piece, null);
        }
    }
}
