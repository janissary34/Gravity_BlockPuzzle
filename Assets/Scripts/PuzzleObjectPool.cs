using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GravityPuzzle
{
    public static class PuzzleObjectPool
    {
        private static Transform poolRoot;
        private static readonly Queue<GameObject> flyingVoxelUiPool = new Queue<GameObject>();

        private static void EnsurePoolRoot()
        {
            if (poolRoot == null)
            {
                GameObject root = new GameObject("PuzzleObjectPool");
                Object.DontDestroyOnLoad(root);
                poolRoot = root.transform;
            }
        }

        // ──────────────────────────────────────────────────────────
        //  UI Flying Voxel Pool
        // ──────────────────────────────────────────────────────────

        public static GameObject GetFlyingVoxelUI(Transform parentCanvas)
        {
            EnsurePoolRoot();
            GameObject obj;
            while (flyingVoxelUiPool.Count > 0)
            {
                obj = flyingVoxelUiPool.Dequeue();
                if (obj != null)
                {
                    obj.transform.SetParent(parentCanvas, false);
                    obj.transform.localScale = Vector3.one;
                    obj.transform.localRotation = Quaternion.identity;
                    obj.SetActive(true);
                    return obj;
                }
            }

            obj = new GameObject("Flying Voxel UI", typeof(RectTransform), typeof(Image));
            obj.transform.SetParent(parentCanvas, false);
            return obj;
        }

        public static void ReturnFlyingVoxelUI(GameObject obj)
        {
            if (obj == null) return;
            EnsurePoolRoot();
            obj.SetActive(false);
            if (poolRoot != null)
                obj.transform.SetParent(poolRoot, false);
            flyingVoxelUiPool.Enqueue(obj);
        }
    }
}
