using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Handles HTTP calls to the Claude or OpenAI APIs via UnityWebRequest.
    /// Wire up events to receive responses or errors per agent.
    /// </summary>
    public class AIServiceManager : MonoBehaviour
    {
        // ── Provider enum ─────────────────────────────────────────────────────
        public enum AIProvider { Claude, OpenAI }

        // ── Inspector ─────────────────────────────────────────────────────────
        [Header("Provider Settings")]
        [SerializeField] private AIProvider provider        = AIProvider.Claude;
        [SerializeField] private string     apiEndpoint     = "https://api.anthropic.com/v1/messages";
        [SerializeField] private string     defaultModel    = "claude-opus-4-5";
        [SerializeField] private int        maxTokens       = 1024;
        [SerializeField] private float      requestTimeout  = 30f;

        /// <summary>API key — set at runtime via SetApiKey(); hidden in Inspector to avoid accidental exposure.</summary>
        [HideInInspector] public string apiKey = "";

        // ── Events ────────────────────────────────────────────────────────────
        /// <summary>Fired when a successful response is received. (agentId, responseText)</summary>
        public event Action<string, string> OnResponseReceived;

        /// <summary>Fired when a request fails. (agentId, errorMessage)</summary>
        public event Action<string, string> OnRequestError;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Sets the API key used for all subsequent requests.</summary>
        public void SetApiKey(string key)
        {
            apiKey = key;
        }

        /// <summary>Switches the provider and optionally overrides the endpoint URL.</summary>
        public void SetProvider(AIProvider prov, string endpoint = null)
        {
            provider = prov;

            if (!string.IsNullOrEmpty(endpoint))
            {
                apiEndpoint = endpoint;
            }
            else
            {
                // Reset to sensible defaults when no explicit endpoint is given
                apiEndpoint = prov == AIProvider.Claude
                    ? "https://api.anthropic.com/v1/messages"
                    : "https://api.openai.com/v1/chat/completions";
            }
        }

        /// <summary>
        /// Sends a request to the configured AI provider.
        /// Responses arrive via <see cref="OnResponseReceived"/> or <see cref="OnRequestError"/>.
        /// </summary>
        public void SendRequest(string agentId, string systemPrompt, string userMessage)
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                Debug.LogError("[AIServiceManager] API key is not set.");
                OnRequestError?.Invoke(agentId, "API key not set.");
                return;
            }

            if (provider == AIProvider.Claude)
                StartCoroutine(SendClaudeRequest(agentId, systemPrompt, userMessage));
            else
                StartCoroutine(SendOpenAIRequest(agentId, systemPrompt, userMessage));
        }

        // ── Coroutines ────────────────────────────────────────────────────────

        private IEnumerator SendClaudeRequest(string agentId, string system, string user)
        {
            string payload = BuildClaudePayload(system, user);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(payload);

            using UnityWebRequest request = new UnityWebRequest(apiEndpoint, "POST");
            request.uploadHandler   = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout         = Mathf.RoundToInt(requestTimeout);

            request.SetRequestHeader("Content-Type",       "application/json");
            request.SetRequestHeader("x-api-key",          apiKey);
            request.SetRequestHeader("anthropic-version",  "2023-06-01");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                string responseText = ParseClaudeResponse(request.downloadHandler.text);
                if (responseText != null)
                    OnResponseReceived?.Invoke(agentId, responseText);
                else
                    OnRequestError?.Invoke(agentId, "Failed to parse Claude response: " + request.downloadHandler.text);
            }
            else
            {
                string error = $"[Claude] HTTP {request.responseCode}: {request.error} — {request.downloadHandler.text}";
                Debug.LogError($"[AIServiceManager] {error}");
                OnRequestError?.Invoke(agentId, error);
            }
        }

        private IEnumerator SendOpenAIRequest(string agentId, string system, string user)
        {
            string payload = BuildOpenAIPayload(system, user);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(payload);

            using UnityWebRequest request = new UnityWebRequest(apiEndpoint, "POST");
            request.uploadHandler   = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout         = Mathf.RoundToInt(requestTimeout);

            request.SetRequestHeader("Content-Type",  "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + apiKey);

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                string responseText = ParseOpenAIResponse(request.downloadHandler.text);
                if (responseText != null)
                    OnResponseReceived?.Invoke(agentId, responseText);
                else
                    OnRequestError?.Invoke(agentId, "Failed to parse OpenAI response: " + request.downloadHandler.text);
            }
            else
            {
                string error = $"[OpenAI] HTTP {request.responseCode}: {request.error} — {request.downloadHandler.text}";
                Debug.LogError($"[AIServiceManager] {error}");
                OnRequestError?.Invoke(agentId, error);
            }
        }

        // ── Payload Builders ──────────────────────────────────────────────────

        /// <summary>Builds the JSON body for the Anthropic Messages API.</summary>
        private string BuildClaudePayload(string system, string user)
        {
            // Escape special characters in user content
            string safeSystem = EscapeJson(system);
            string safeUser   = EscapeJson(user);

            return $@"{{
  ""model"": ""{defaultModel}"",
  ""max_tokens"": {maxTokens},
  ""system"": ""{safeSystem}"",
  ""messages"": [
    {{
      ""role"": ""user"",
      ""content"": ""{safeUser}""
    }}
  ]
}}";
        }

        /// <summary>Builds the JSON body for the OpenAI Chat Completions API.</summary>
        private string BuildOpenAIPayload(string system, string user)
        {
            string safeSystem = EscapeJson(system);
            string safeUser   = EscapeJson(user);

            return $@"{{
  ""model"": ""{defaultModel}"",
  ""max_tokens"": {maxTokens},
  ""messages"": [
    {{
      ""role"": ""system"",
      ""content"": ""{safeSystem}""
    }},
    {{
      ""role"": ""user"",
      ""content"": ""{safeUser}""
    }}
  ]
}}";
        }

        // ── Response Parsers ──────────────────────────────────────────────────

        /// <summary>
        /// Extracts content[0].text from Claude's response JSON.
        /// Uses lightweight string parsing to avoid JsonUtility limitations with nested arrays.
        /// </summary>
        private string ParseClaudeResponse(string json)
        {
            // Claude response shape: { "content": [ { "type": "text", "text": "..." } ] }
            const string textKey = "\"text\":";
            int textIdx = json.IndexOf(textKey, StringComparison.Ordinal);
            if (textIdx < 0) return null;

            int start = json.IndexOf('"', textIdx + textKey.Length);
            if (start < 0) return null;
            start++; // skip opening quote

            int end = FindJsonStringEnd(json, start);
            if (end < 0) return null;

            return UnescapeJson(json.Substring(start, end - start));
        }

        /// <summary>
        /// Extracts choices[0].message.content from OpenAI's response JSON.
        /// </summary>
        private string ParseOpenAIResponse(string json)
        {
            // OpenAI response shape: { "choices": [ { "message": { "content": "..." } } ] }
            const string contentKey = "\"content\":";
            int contentIdx = json.IndexOf(contentKey, StringComparison.Ordinal);
            if (contentIdx < 0) return null;

            int start = json.IndexOf('"', contentIdx + contentKey.Length);
            if (start < 0) return null;
            start++; // skip opening quote

            int end = FindJsonStringEnd(json, start);
            if (end < 0) return null;

            return UnescapeJson(json.Substring(start, end - start));
        }

        // ── JSON Utilities ────────────────────────────────────────────────────

        /// <summary>Escapes a string for safe embedding in a JSON literal.</summary>
        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\r")
                    .Replace("\t", "\\t");
        }

        /// <summary>Reverses basic JSON string escaping.</summary>
        private static string UnescapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\n",  "\n")
                    .Replace("\\r",  "\r")
                    .Replace("\\t",  "\t")
                    .Replace("\\\"", "\"")
                    .Replace("\\\\", "\\");
        }

        /// <summary>
        /// Finds the index of the closing quote of a JSON string starting at <paramref name="start"/>
        /// (the character right after the opening quote), respecting backslash escapes.
        /// Returns -1 if not found.
        /// </summary>
        private static int FindJsonStringEnd(string json, int start)
        {
            for (int i = start; i < json.Length; i++)
            {
                if (json[i] == '\\')
                {
                    i++; // skip escaped character
                    continue;
                }
                if (json[i] == '"')
                    return i;
            }
            return -1;
        }
    }
}
