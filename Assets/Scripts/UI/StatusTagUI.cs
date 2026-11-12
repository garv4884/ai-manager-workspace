using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AIManagerWorkspace
{
    public class StatusTagUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Canvas canvas;
        [SerializeField] private Image backgroundImage;
        [SerializeField] private Image statusIndicatorDot;
        [SerializeField] private TextMeshProUGUI agentNameText;
        [SerializeField] private TextMeshProUGUI agentRoleText;
        [SerializeField] private TextMeshProUGUI statusText;

        [Header("Settings")]
        [SerializeField] private Vector3 heightOffset = new Vector3(0, 2.1f, 0);
        [SerializeField] private float colorLerpSpeed = 5f;
        [SerializeField] private bool billboardToCamera = true;

        private Transform cameraTransform;
        private Color targetStatusColor = Color.white;
        private Transform targetFollow;

        private void Awake()
        {
            if (canvas == null) canvas = GetComponent<Canvas>();
            if (Camera.main != null) cameraTransform = Camera.main.transform;
        }

        private void Start()
        {
            if (cameraTransform == null && Camera.main != null)
            {
                cameraTransform = Camera.main.transform;
            }
        }

        public void Initialize(Transform agentTransform, string name, string role)
        {
            targetFollow = agentTransform;
            if (agentNameText != null) agentNameText.text = name;
            if (agentRoleText != null) agentRoleText.text = role;
            transform.position = agentTransform.position + heightOffset;
        }

        public void UpdateStatus(AgentData data)
        {
            if (data == null) return;

            targetStatusColor = data.GetStatusColor();

            if (statusText != null)
            {
                statusText.text = data.GetStatusDisplayString();
                statusText.color = targetStatusColor;
            }

            if (statusIndicatorDot != null)
            {
                statusIndicatorDot.color = targetStatusColor;
            }

            if (backgroundImage != null)
            {
                // Semi-transparent dark background tinted with status color
                Color tintedBg = Color.Lerp(new Color(0.08f, 0.08f, 0.12f, 0.88f), targetStatusColor, 0.12f);
                backgroundImage.color = tintedBg;
            }
        }

        private void LateUpdate()
        {
            // Position follow
            if (targetFollow != null)
            {
                transform.position = targetFollow.position + heightOffset;
            }

            // Billboard towards player camera
            if (billboardToCamera)
            {
                if (cameraTransform == null && Camera.main != null)
                {
                    cameraTransform = Camera.main.transform;
                }

                if (cameraTransform != null)
                {
                    Vector3 lookDir = transform.position - cameraTransform.position;
                    lookDir.y = 0; // Keep vertical orientation straight
                    if (lookDir != Vector3.zero)
                    {
                        transform.rotation = Quaternion.LookRotation(lookDir);
                    }
                }
            }
        }
    }
}
