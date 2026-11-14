using System.Collections.Generic;
using UnityEngine;

namespace AIManagerWorkspace
{
    public enum LayoutType
    {
        Grid,       // classroom rows facing you, centre aisle for the queue
        Circle,     // you at the centre, team all around you
        UShape,     // open end towards you
        HubSpoke,   // small collaboration pods facing each other
        Coworking,  // relaxed, staggered open floor
        WarRoom,    // tight ring hugging your platform
        Custom      // arranged by hand
    }

    public struct DeskPose
    {
        public Vector3 position;
        public Quaternion rotation;
        public DeskPose(Vector3 p, Quaternion r) { position = p; rotation = r; }
    }

    /// <summary>
    /// Generates workstation poses around the manager platform at <c>center</c>.
    /// The queue lane runs from the platform along +Z, so every template keeps
    /// |x| &lt; 1.6 clear in front of the manager. A pose's forward axis is the
    /// direction the seated agent faces.
    /// </summary>
    public static class WorkspaceLayout
    {
        public static string DisplayName(LayoutType t) => t switch
        {
            LayoutType.Grid => "Classroom",
            LayoutType.Circle => "Circle",
            LayoutType.UShape => "U-Shape",
            LayoutType.HubSpoke => "Pods",
            LayoutType.Coworking => "Coworking",
            LayoutType.WarRoom => "War Room",
            _ => "Custom"
        };

        public static List<DeskPose> Generate(LayoutType type, int count, Vector3 center)
        {
            var list = new List<DeskPose>(count);
            switch (type)
            {
                case LayoutType.Grid: Grid(list, count, center); break;
                case LayoutType.Circle: Ring(list, count, center, Mathf.Max(6.2f, count * 1.05f), 25f); break;
                case LayoutType.UShape: UShape(list, count, center); break;
                case LayoutType.HubSpoke: Pods(list, count, center); break;
                case LayoutType.Coworking: Coworking(list, count, center); break;
                case LayoutType.WarRoom: Ring(list, count, center, 4.0f, 32f); break;
                default: Grid(list, count, center); break;
            }
            return list;
        }

        public static Quaternion Facing(Vector3 from, Vector3 target)
        {
            Vector3 d = target - from;
            d.y = 0f;
            return d.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(d) : Quaternion.identity;
        }

        private static void Grid(List<DeskPose> list, int count, Vector3 c)
        {
            float[] xs = { -2.2f, 2.2f, -4.6f, 4.6f, -7.0f, 7.0f };
            int perRow = count > 8 ? 6 : 4;
            for (int i = 0; i < count; i++)
            {
                int row = i / perRow, col = i % perRow;
                Vector3 p = c + new Vector3(xs[col], 0f, 5.8f + row * 3.0f);
                list.Add(new DeskPose(p, Facing(p, c)));
            }
        }

        private static void Ring(List<DeskPose> list, int count, Vector3 c, float radius, float gapDegrees)
        {
            float span = 360f - 2f * gapDegrees;
            for (int i = 0; i < count; i++)
            {
                float a = (gapDegrees + (count > 1 ? span * i / (count - 1) : span * 0.5f)) * Mathf.Deg2Rad;
                Vector3 p = c + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * radius;
                list.Add(new DeskPose(p, Facing(p, c)));
            }
        }

        private static void UShape(List<DeskPose> list, int count, Vector3 c)
        {
            int side = Mathf.Max(1, Mathf.RoundToInt(count / 3f));
            int back = count - side * 2;
            Vector3 focus = c + new Vector3(0f, 0f, 7.5f);
            for (int i = 0; i < side; i++)
            {
                Vector3 p = c + new Vector3(-5.4f, 0f, 5.0f + i * 2.6f);
                list.Add(new DeskPose(p, Facing(p, new Vector3(c.x, 0f, p.z))));
            }
            for (int i = 0; i < back; i++)
            {
                float x = back == 1 ? 0f : Mathf.Lerp(-2.4f, 2.4f, i / (float)(back - 1));
                Vector3 p = c + new Vector3(x, 0f, 5.0f + side * 2.6f + 0.6f);
                list.Add(new DeskPose(p, Facing(p, c)));
            }
            for (int i = 0; i < side; i++)
            {
                Vector3 p = c + new Vector3(5.4f, 0f, 5.0f + i * 2.6f);
                list.Add(new DeskPose(p, Facing(p, new Vector3(c.x, 0f, p.z))));
            }
        }

        private static void Pods(List<DeskPose> list, int count, Vector3 c)
        {
            Vector3[] centres =
            {
                c + new Vector3(-5.2f, 0f, 7.2f), c + new Vector3(5.2f, 0f, 7.2f),
                c + new Vector3(-6.0f, 0f, -1.6f), c + new Vector3(5.6f, 0f, -1.2f)
            };
            const int perPod = 3;
            for (int i = 0; i < count; i++)
            {
                Vector3 pc = centres[(i / perPod) % centres.Length];
                int slot = i % perPod;
                float a = (slot * 120f + 180f) * Mathf.Deg2Rad;
                Vector3 p = pc + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 1.4f;
                list.Add(new DeskPose(p, Facing(p, pc)));
            }
        }

        private static void Coworking(List<DeskPose> list, int count, Vector3 c)
        {
            Vector3[] spots =
            {
                new Vector3(-3.0f, 0f, 5.2f), new Vector3(3.2f, 0f, 4.6f), new Vector3(-6.6f, 0f, 8.4f),
                new Vector3(6.2f, 0f, 7.8f), new Vector3(-2.4f, 0f, 11.0f), new Vector3(3.0f, 0f, 11.6f),
                new Vector3(-8.0f, 0f, 2.0f), new Vector3(8.4f, 0f, 1.6f)
            };
            float[] twist = { 18f, -14f, 30f, -26f, 8f, -12f, 40f, -38f };
            for (int i = 0; i < count; i++)
            {
                int k = i % spots.Length;
                Vector3 p = c + spots[k] + (i >= spots.Length ? new Vector3(0f, 0f, 3f) : Vector3.zero);
                list.Add(new DeskPose(p, Facing(p, c) * Quaternion.Euler(0f, twist[k], 0f)));
            }
        }
    }
}
