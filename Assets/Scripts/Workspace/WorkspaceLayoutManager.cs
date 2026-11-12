using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.AI.Navigation;

namespace AIManagerWorkspace
{
    public enum LayoutType
    {
        Grid = 0,
        Circle = 1,
        UShape = 2,
        Row = 3,
        Cluster = 4,
        Open = 5
    }

    public struct DeskTransformData
    {
        public Vector3 position;
        public Quaternion rotation;
    }

    public static class WorkspaceLayout
    {
        public static List<DeskTransformData> GenerateLayout(LayoutType type, int count, float spacing, float radius)
        {
            var results = new List<DeskTransformData>();

            switch (type)
            {
                case LayoutType.Grid:
                    results = GenerateGrid(count, spacing);
                    break;
                case LayoutType.Circle:
                    results = GenerateCircle(count, radius);
                    break;
                case LayoutType.UShape:
                    results = GenerateUShape(count, spacing);
                    break;
                case LayoutType.Row:
                    results = GenerateRow(count, spacing);
                    break;
                case LayoutType.Cluster:
                    results = GenerateCluster(count, spacing);
                    break;
                case LayoutType.Open:
                    results = GenerateOpen(count, spacing);
                    break;
            }

            return results;
        }

        private static List<DeskTransformData> GenerateGrid(int count, float spacing)
        {
            var list = new List<DeskTransformData>();
            int cols = Mathf.CeilToInt(Mathf.Sqrt(count));
            for (int i = 0; i < count; i++)
            {
                int row = i / cols;
                int col = i % cols;
                list.Add(new DeskTransformData
                {
                    position = new Vector3(col * spacing, 0f, row * spacing),
                    rotation = Quaternion.identity
                });
            }
            return list;
        }

        private static List<DeskTransformData> GenerateCircle(int count, float radius)
        {
            var list = new List<DeskTransformData>();
            float angleStep = 360f / count;
            for (int i = 0; i < count; i++)
            {
                float angle = i * angleStep * Mathf.Deg2Rad;
                Vector3 pos = new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius);
                Quaternion rot = Quaternion.LookRotation(-pos.normalized, Vector3.up);
                list.Add(new DeskTransformData { position = pos, rotation = rot });
            }
            return list;
        }

        private static List<DeskTransformData> GenerateUShape(int count, float spacing)
        {
            var list = new List<DeskTransformData>();
            int side = Mathf.CeilToInt(count / 3f);
            int placed = 0;
            // Left arm
            for (int i = 0; i < side && placed < count; i++, placed++)
                list.Add(new DeskTransformData { position = new Vector3(0f, 0f, i * spacing), rotation = Quaternion.Euler(0f, 90f, 0f) });
            // Bottom
            for (int i = 0; i < side && placed < count; i++, placed++)
                list.Add(new DeskTransformData { position = new Vector3(i * spacing, 0f, 0f), rotation = Quaternion.identity });
            // Right arm
            for (int i = 0; i < side && placed < count; i++, placed++)
                list.Add(new DeskTransformData { position = new Vector3((side - 1) * spacing, 0f, i * spacing), rotation = Quaternion.Euler(0f, -90f, 0f) });
            return list;
        }

        private static List<DeskTransformData> GenerateRow(int count, float spacing)
        {
            var list = new List<DeskTransformData>();
            for (int i = 0; i < count; i++)
                list.Add(new DeskTransformData { position = new Vector3(i * spacing, 0f, 0f), rotation = Quaternion.identity });
            return list;
        }

        private static List<DeskTransformData> GenerateCluster(int count, float spacing)
        {
            var list = new List<DeskTransformData>();
            int clusterSize = 3;
            for (int i = 0; i < count; i++)
            {
                int cluster = i / clusterSize;
                int local = i % clusterSize;
                Vector3 clusterOrigin = new Vector3(cluster * spacing * 2.5f, 0f, 0f);
                float angle = local * 120f * Mathf.Deg2Rad;
                Vector3 offset = new Vector3(Mathf.Sin(angle) * spacing * 0.6f, 0f, Mathf.Cos(angle) * spacing * 0.6f);
                list.Add(new DeskTransformData { position = clusterOrigin + offset, rotation = Quaternion.LookRotation(-offset.normalized + Vector3.forward * 0.001f, Vector3.up) });
            }
            return list;
        }

        private static List<DeskTransformData> GenerateOpen(int count, float spacing)
        {
            // Scatter with slight randomness seeded for consistency
            var list = new List<DeskTransformData>();
            System.Random rng = new System.Random(42);
            for (int i = 0; i < count; i++)
            {
                float x = (float)(rng.NextDouble() * count * spacing * 0.5f);
                float z = (float)(rng.NextDouble() * count * spacing * 0.5f);
                list.Add(new DeskTransformData { position = new Vector3(x, 0f, z), rotation = Quaternion.Euler(0f, (float)(rng.NextDouble() * 360f), 0f) });
            }
            return list;
        }
    }

    public class WorkspaceLayoutManager : MonoBehaviour
    {
        [SerializeField] private Transform[] deskTransforms;
        [SerializeField] private Transform managerPlatformTransform;
        [SerializeField] private AgentManager agentManager;
        [SerializeField] private float transitionDuration = 1.2f;
        [SerializeField] private float deskSpacing = 2.4f;
        [SerializeField] private float circleRadius = 5.0f;

        private LayoutType currentLayout = LayoutType.Grid;
        private Coroutine activeTransition;

        public void ApplyLayout(LayoutType type)
        {
            currentLayout = type;
            int activeCount = 0;
            foreach (var t in deskTransforms)
                if (t.gameObject.activeSelf) activeCount++;

            List<DeskTransformData> targets = WorkspaceLayout.GenerateLayout(type, activeCount, deskSpacing, circleRadius);

            if (activeTransition != null)
                StopCoroutine(activeTransition);

            activeTransition = StartCoroutine(AnimateDesksToLayout(targets));
        }

        private IEnumerator AnimateDesksToLayout(List<DeskTransformData> targets)
        {
            List<Transform> activeDesks = new List<Transform>();
            foreach (var t in deskTransforms)
                if (t.gameObject.activeSelf) activeDesks.Add(t);

            int count = Mathf.Min(activeDesks.Count, targets.Count);
            Vector3[] startPositions = new Vector3[count];
            Quaternion[] startRotations = new Quaternion[count];

            for (int i = 0; i < count; i++)
            {
                startPositions[i] = activeDesks[i].position;
                startRotations[i] = activeDesks[i].rotation;
            }

            float elapsed = 0f;
            while (elapsed < transitionDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / transitionDuration);
                float smoothT = Mathf.SmoothStep(0f, 1f, t);

                for (int i = 0; i < count; i++)
                {
                    activeDesks[i].position = Vector3.Lerp(startPositions[i], targets[i].position, smoothT);
                    activeDesks[i].rotation = Quaternion.Lerp(startRotations[i], targets[i].rotation, smoothT);
                }
                yield return null;
            }

            // Snap to final positions
            for (int i = 0; i < count; i++)
            {
                activeDesks[i].position = targets[i].position;
                activeDesks[i].rotation = targets[i].rotation;
            }

            // Rebuild NavMesh
            NavMeshSurface surface = GetComponent<NavMeshSurface>();
            if (surface != null)
            {
                UnityEngine.AI.NavMesh.RemoveAllNavMeshData();
                surface.BuildNavMesh();
            }

            activeTransition = null;
        }

        public void SetDeskCount(int count)
        {
            count = Mathf.Clamp(count, 0, deskTransforms.Length);
            for (int i = 0; i < deskTransforms.Length; i++)
            {
                deskTransforms[i].gameObject.SetActive(i < count);
            }
        }

        public LayoutType GetCurrentLayout() => currentLayout;
    }
}
