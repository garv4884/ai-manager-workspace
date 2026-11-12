using System.Collections.Generic;
using UnityEngine;

namespace AIManagerWorkspace
{
    public class RoomBuilder : MonoBehaviour
    {
        [SerializeField] private Material dividerMaterial;
        [SerializeField] private float wallHeight = 2.4f;
        [SerializeField] private float wallThickness = 0.08f;

        private List<GameObject> placedWalls = new List<GameObject>();

        /// <summary>
        /// Creates a wall cube between start and end, centered at wallHeight/2 on Y.
        /// </summary>
        public void PlaceWall(Vector3 start, Vector3 end)
        {
            Vector3 midPoint = (start + end) / 2f;
            midPoint.y = wallHeight / 2f;

            float length = Vector3.Distance(start, end);
            Vector3 direction = (end - start).normalized;

            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = $"DividerWall_{placedWalls.Count}";
            wall.transform.position = midPoint;
            wall.transform.localScale = new Vector3(length, wallHeight, wallThickness);

            if (direction != Vector3.zero)
                wall.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);

            if (dividerMaterial != null)
            {
                Renderer rend = wall.GetComponent<Renderer>();
                if (rend != null)
                    rend.sharedMaterial = dividerMaterial;
            }

            placedWalls.Add(wall);
        }

        /// <summary>
        /// Destroys the most recently placed wall.
        /// </summary>
        public void RemoveLastWall()
        {
            if (placedWalls.Count == 0) return;

            GameObject last = placedWalls[placedWalls.Count - 1];
            placedWalls.RemoveAt(placedWalls.Count - 1);

            if (last != null)
                Destroy(last);
        }

        /// <summary>
        /// Destroys all placed walls.
        /// </summary>
        public void ClearAllWalls()
        {
            foreach (GameObject wall in placedWalls)
            {
                if (wall != null)
                    Destroy(wall);
            }
            placedWalls.Clear();
        }

        /// <summary>
        /// Creates a 3-wall enclosure (open front) around center with the given size.
        /// </summary>
        public void CreateMeetingRoom(Vector3 center, float size)
        {
            float half = size / 2f;

            // Back wall
            Vector3 backLeft = center + new Vector3(-half, 0f, half);
            Vector3 backRight = center + new Vector3(half, 0f, half);
            PlaceWall(backLeft, backRight);

            // Left wall
            Vector3 frontLeft = center + new Vector3(-half, 0f, -half);
            PlaceWall(backLeft, frontLeft);

            // Right wall
            Vector3 frontRight = center + new Vector3(half, 0f, -half);
            PlaceWall(backRight, frontRight);

            // Front is intentionally open
        }
    }
}
