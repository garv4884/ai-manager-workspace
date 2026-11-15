using UnityEngine;

namespace AIManagerWorkspace
{
    /// <summary>
    /// The manager's spot on the platform. Fits the desk to the user's height (seated or
    /// standing) and brings the user back to the desk after teleporting around the office.
    /// </summary>
    public class ManagerStation : MonoBehaviour
    {
        [SerializeField] private Transform deskAssembly;
        [SerializeField] private Transform standPoint;
        [SerializeField] private Transform rigRoot;
        [SerializeField] private float platformTopY = 0.9f;
        [SerializeField] private float deskBelowEyes = 0.5f;
        [SerializeField] private float defaultDeskHeight = 0.78f;
        [SerializeField] private Vector2 deskHeightRange = new Vector2(0.62f, 1.12f);

        private float baseDeskY;

        private void Awake() => baseDeskY = deskAssembly.localPosition.y;

        private System.Collections.IEnumerator Start()
        {
            // Wait for head tracking, then put the user behind the desk and size the desk to them.
            yield return new WaitForSeconds(1.5f);
            if (!Application.isEditor) { ReturnToDesk(); FitDeskToHead(); }
        }

        private Transform Head => Camera.main != null ? Camera.main.transform : null;

        /// <summary>Moves the whole desk (console, keyboard, panels, review spot) to suit the current head height.</summary>
        public void FitDeskToHead()
        {
            if (Head == null) return;
            float eyeAbovePlatform = Head.position.y - platformTopY;
            float desk = Mathf.Clamp(eyeAbovePlatform - deskBelowEyes, deskHeightRange.x, deskHeightRange.y);
            Vector3 p = deskAssembly.localPosition;
            deskAssembly.localPosition = new Vector3(p.x, baseDeskY + (desk - defaultDeskHeight), p.z);
        }

        /// <summary>Teleports the user back behind the review desk, facing the team.</summary>
        public void ReturnToDesk()
        {
            if (rigRoot == null || Head == null) return;
            Vector3 headFlat = Head.position; headFlat.y = rigRoot.position.y;
            float yawDelta = standPoint.eulerAngles.y - Head.eulerAngles.y;
            rigRoot.RotateAround(headFlat, Vector3.up, yawDelta);
            Vector3 offset = rigRoot.position - new Vector3(Head.position.x, rigRoot.position.y, Head.position.z);
            rigRoot.position = new Vector3(standPoint.position.x, standPoint.position.y, standPoint.position.z) + offset;
        }
    }
}
