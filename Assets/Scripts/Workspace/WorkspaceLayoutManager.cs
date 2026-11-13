using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.AI.Navigation;

namespace AIManagerWorkspace
{
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
            if (deskTransforms != null)
            {
                foreach (var t in deskTransforms)
                {
                    if (t != null && t.gameObject.activeSelf) activeCount++;
                }
            }

            Vector3 managerPos = managerPlatformTransform != null ? managerPlatformTransform.position : Vector3.zero;
            List<DeskTransformData> targets = WorkspaceLayout.GenerateLayout(type, activeCount, managerPos, deskSpacing, circleRadius);

            if (activeTransition != null)
                StopCoroutine(activeTransition);

            activeTransition = StartCoroutine(AnimateDesksToLayout(targets));
        }

        private IEnumerator AnimateDesksToLayout(List<DeskTransformData> targets)
        {
            List<Transform> activeDesks = new List<Transform>();
            if (deskTransforms != null)
            {
                foreach (var t in deskTransforms)
                {
                    if (t != null && t.gameObject.activeSelf) activeDesks.Add(t);
                }
            }

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
            if (deskTransforms == null) return;
            count = Mathf.Clamp(count, 0, deskTransforms.Length);
            for (int i = 0; i < deskTransforms.Length; i++)
            {
                if (deskTransforms[i] != null)
                    deskTransforms[i].gameObject.SetActive(i < count);
            }
        }

        public LayoutType GetCurrentLayout() => currentLayout;
    }
}
