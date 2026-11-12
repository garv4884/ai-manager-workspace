using System;
using System.Collections.Generic;
using UnityEngine;

namespace AIManagerWorkspace
{
    public enum LayoutType
    {
        Grid,
        Circle,
        UShape,
        HubSpoke,
        Coworking,
        WarRoom
    }

    [System.Serializable]
    public struct DeskTransformData
    {
        public Vector3 position;
        public Quaternion rotation;

        public DeskTransformData(Vector3 pos, Quaternion rot)
        {
            position = pos;
            rotation = rot;
        }
    }

    [CreateAssetMenu(fileName = "NewWorkspaceLayout", menuName = "AI Manager/Workspace Layout", order = 1)]
    public class WorkspaceLayout : ScriptableObject
    {
        public string layoutName;
        public LayoutType layoutType;
        public Vector3 managerPlatformPosition = new Vector3(0, 0.8f, 0);
        public List<DeskTransformData> deskTransforms = new List<DeskTransformData>();

        /// <summary>
        /// Generates desk positions and rotations for any layout type given agent count and parameters.
        /// </summary>
        public static List<DeskTransformData> GenerateLayout(
            LayoutType type,
            int count,
            Vector3 managerPos,
            float spacing = 2.4f,
            float radius = 5.0f)
        {
            return type switch
            {
                LayoutType.Circle => GenerateCircle(count, managerPos, radius),
                LayoutType.Grid => GenerateGrid(count, managerPos, spacing),
                LayoutType.UShape => GenerateUShape(count, managerPos, spacing),
                LayoutType.HubSpoke => GenerateHubSpoke(count, managerPos, spacing),
                LayoutType.Coworking => GenerateCoworking(count, managerPos, spacing),
                LayoutType.WarRoom => GenerateWarRoom(count, managerPos, 3.2f),
                _ => GenerateGrid(count, managerPos, spacing)
            };
        }

        private static List<DeskTransformData> GenerateCircle(int count, Vector3 managerPos, float radius)
        {
            var list = new List<DeskTransformData>();
            if (count <= 0) return list;

            // Semi-circle / fan facing manager desk or full circle
            // To feel like an elevated manager overlooking, fan out 180-240 degrees in front of manager
            float startAngle = -110f * Mathf.Deg2Rad;
            float endAngle = 110f * Mathf.Deg2Rad;
            float step = count > 1 ? (endAngle - startAngle) / (count - 1) : 0f;

            for (int i = 0; i < count; i++)
            {
                float angle = count > 1 ? startAngle + i * step : 0f;
                Vector3 pos = managerPos + new Vector3(Mathf.Sin(angle) * radius, -managerPos.y, Mathf.Cos(angle) * radius);
                // Face toward manager
                Vector3 lookDir = (managerPos - pos);
                lookDir.y = 0;
                Quaternion rot = lookDir != Vector3.zero ? Quaternion.LookRotation(lookDir) : Quaternion.identity;
                list.Add(new DeskTransformData(pos, rot));
            }

            return list;
        }

        private static List<DeskTransformData> GenerateGrid(int count, Vector3 managerPos, float spacing)
        {
            var list = new List<DeskTransformData>();
            if (count <= 0) return list;

            int cols = Mathf.CeilToInt(Mathf.Sqrt(count * 1.5f));
            if (cols < 2) cols = 2;
            int rows = Mathf.CeilToInt((float)count / cols);

            float forwardOffset = 3.5f;

            for (int i = 0; i < count; i++)
            {
                int r = i / cols;
                int c = i % cols;

                float x = (c - (cols - 1) * 0.5f) * spacing;
                float z = managerPos.z + forwardOffset + (r * spacing * 1.2f);
                Vector3 pos = new Vector3(x, 0, z);

                // Face back towards manager
                Vector3 lookDir = (new Vector3(0, 0, managerPos.z) - pos);
                lookDir.y = 0;
                Quaternion rot = lookDir != Vector3.zero ? Quaternion.LookRotation(lookDir) : Quaternion.identity;

                list.Add(new DeskTransformData(pos, rot));
            }

            return list;
        }

        private static List<DeskTransformData> GenerateUShape(int count, Vector3 managerPos, float spacing)
        {
            var list = new List<DeskTransformData>();
            if (count <= 0) return list;

            // Distribute desks: left arm, bottom/facing row, right arm
            int sideCount = Mathf.Max(1, count / 3);
            int bottomCount = count - (sideCount * 2);

            float width = 5.0f;
            float startZ = managerPos.z + 3.0f;

            int index = 0;

            // Left wing
            for (int i = 0; i < sideCount && index < count; i++)
            {
                Vector3 pos = new Vector3(-width * 0.5f, 0, startZ + i * spacing);
                Quaternion rot = Quaternion.Euler(0, 90f, 0); // Face inward to center
                list.Add(new DeskTransformData(pos, rot));
                index++;
            }

            // Bottom wing
            float bottomZ = startZ + sideCount * spacing;
            float bottomSpacing = bottomCount > 1 ? width / (bottomCount + 1) : 0f;
            for (int i = 0; i < bottomCount && index < count; i++)
            {
                float x = -width * 0.5f + (i + 1) * bottomSpacing;
                Vector3 pos = new Vector3(x, 0, bottomZ);
                Quaternion rot = Quaternion.Euler(0, 180f, 0); // Face manager
                list.Add(new DeskTransformData(pos, rot));
                index++;
            }

            // Right wing
            for (int i = 0; i < sideCount && index < count; i++)
            {
                Vector3 pos = new Vector3(width * 0.5f, 0, startZ + (sideCount - 1 - i) * spacing);
                Quaternion rot = Quaternion.Euler(0, -90f, 0); // Face inward to center
                list.Add(new DeskTransformData(pos, rot));
                index++;
            }

            return list;
        }

        private static List<DeskTransformData> GenerateHubSpoke(int count, Vector3 managerPos, float spacing)
        {
            // 2 or 3 mini clusters (e.g., Pod A, Pod B)
            var list = new List<DeskTransformData>();
            int pods = count >= 6 ? 2 : 1;
            int perPod = Mathf.CeilToInt((float)count / pods);

            Vector3[] podCenters = pods == 2 
                ? new[] { new Vector3(-3f, 0, managerPos.z + 4.5f), new Vector3(3f, 0, managerPos.z + 4.5f) }
                : new[] { new Vector3(0, 0, managerPos.z + 4.5f) };

            int assigned = 0;
            for (int p = 0; p < pods; p++)
            {
                int countInThisPod = Mathf.Min(perPod, count - assigned);
                for (int i = 0; i < countInThisPod; i++)
                {
                    float angle = (i / (float)countInThisPod) * Mathf.PI * 2f;
                    float podRadius = 1.4f;
                    Vector3 pos = podCenters[p] + new Vector3(Mathf.Cos(angle) * podRadius, 0, Mathf.Sin(angle) * podRadius);
                    // Face into pod center
                    Vector3 look = (podCenters[p] - pos);
                    look.y = 0;
                    Quaternion rot = look != Vector3.zero ? Quaternion.LookRotation(look) : Quaternion.identity;
                    list.Add(new DeskTransformData(pos, rot));
                    assigned++;
                }
            }

            return list;
        }

        private static List<DeskTransformData> GenerateCoworking(int count, Vector3 managerPos, float spacing)
        {
            // Open, staggered desks with varied angles
            var list = new List<DeskTransformData>();
            float[,] offsets = new float[,]
            {
                { -3.5f, 4.0f, 15f },
                { -1.2f, 3.2f, -10f },
                { 1.8f, 3.5f, 20f },
                { 4.0f, 4.5f, -15f },
                { -2.2f, 6.2f, 5f },
                { 2.5f, 6.5f, -25f }
            };

            for (int i = 0; i < count; i++)
            {
                int lookup = i % 6;
                Vector3 pos = new Vector3(offsets[lookup, 0], 0, managerPos.z + offsets[lookup, 1]);
                Quaternion rot = Quaternion.Euler(0, 180f + offsets[lookup, 2], 0);
                list.Add(new DeskTransformData(pos, rot));
            }
            return list;
        }

        private static List<DeskTransformData> GenerateWarRoom(int count, Vector3 managerPos, float radius)
        {
            // Close round-table circle right in front of manager platform
            var list = new List<DeskTransformData>();
            Vector3 center = managerPos + new Vector3(0, -managerPos.y, 3.8f);
            for (int i = 0; i < count; i++)
            {
                float angle = (i / (float)count) * Mathf.PI * 2f;
                Vector3 pos = center + new Vector3(Mathf.Sin(angle) * radius, 0, Mathf.Cos(angle) * radius);
                Vector3 toCenter = center - pos;
                toCenter.y = 0;
                Quaternion rot = Quaternion.LookRotation(toCenter);
                list.Add(new DeskTransformData(pos, rot));
            }
            return list;
        }
    }
}
