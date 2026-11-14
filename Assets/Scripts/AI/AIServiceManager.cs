using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Runs one unit of agent work: either a live Claude call (raw HTTP; the official
    /// Anthropic SDK is not usable from a Quest IL2CPP build) or an offline simulation.
    ///
    /// Config is read from (first match wins):
    ///   1. Application.persistentDataPath/config.json  (on Quest: /sdcard/Android/data/&lt;package&gt;/files/)
    ///   2. StreamingAssets/config.json                  (do not ship real keys inside an APK)
    /// Format: { "apiKey": "...", "model": "claude-opus-5-5" }
    /// </summary>
    public class AIServiceManager : MonoBehaviour
    {
        [Header("Claude API")]
        [SerializeField] private string apiEndpoint = "https://api.anthropic.com/v1/messages";
        [SerializeField] private string model = "claude-opus-5-5";
        [SerializeField] private string effort = "low";
        [SerializeField] private int maxTokens = 2048;
        [SerializeField] private int requestTimeoutSeconds = 60;

        [Header("Offline simulation")]
        [SerializeField] private Vector2 simulatedWorkSeconds = new Vector2(5f, 10f);

        private string apiKey = "";

        public bool IsLive => !string.IsNullOrEmpty(apiKey);
        public string ModeLabel => IsLive ? $"LIVE AI  {model}" : "OFFLINE SIMULATION";
        public Vector2 SimulatedWorkSeconds => simulatedWorkSeconds;

        [Serializable] private class ConfigData { public string apiKey; public string model; }

        // ── Request / response shapes for JsonUtility ──────────────────────────
        [Serializable] private class Msg { public string role; public string content; }
        [Serializable] private class OutputConfig { public string effort; }
        [Serializable] private class Request
        {
            public string model;
            public int max_tokens;
            public string system;
            public Msg[] messages;
            public OutputConfig output_config;
            public string fallbacks = "default";
        }
        [Serializable] private class Block { public string type; public string text; }
        [Serializable] private class Response { public Block[] content; public string stop_reason; }

        private void Awake()
        {
            StartCoroutine(LoadConfig());
        }

        public void SetApiKey(string key) => apiKey = key?.Trim() ?? "";

        private IEnumerator LoadConfig()
        {
            string persistent = System.IO.Path.Combine(Application.persistentDataPath, "config.json");
            string json = null;
            if (System.IO.File.Exists(persistent))
            {
                json = System.IO.File.ReadAllText(persistent);
            }
            else
            {
                string streaming = System.IO.Path.Combine(Application.streamingAssetsPath, "config.json");
                if (streaming.Contains("://"))
                {
                    using var req = UnityWebRequest.Get(streaming);   // Android: inside the APK
                    yield return req.SendWebRequest();
                    if (req.result == UnityWebRequest.Result.Success) json = req.downloadHandler.text;
                }
                else if (System.IO.File.Exists(streaming))
                {
                    json = System.IO.File.ReadAllText(streaming);
                }
            }

            if (string.IsNullOrEmpty(json)) yield break;
            try
            {
                var cfg = JsonUtility.FromJson<ConfigData>(json);
                if (cfg != null)
                {
                    if (!string.IsNullOrWhiteSpace(cfg.apiKey)) apiKey = cfg.apiKey.Trim();
                    if (!string.IsNullOrWhiteSpace(cfg.model)) model = cfg.model.Trim();
                }
                Debug.Log($"[AIService] Config loaded. Mode: {ModeLabel}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AIService] Could not parse config.json: {e.Message}");
            }
        }

        /// <summary>Starts work for <paramref name="task"/>; <paramref name="onDone"/> fires once.</summary>
        public void RequestWork(AgentData agent, TaskData task, Action<AIResult> onDone)
        {
            if (IsLive) StartCoroutine(SendClaude(agent, task, onDone));
            else onDone?.Invoke(TaskLibrary.Simulate(agent, task));
        }

        private IEnumerator SendClaude(AgentData agent, TaskData task, Action<AIResult> onDone)
        {
            bool mayAsk = !task.HasAskedQuestion && task.revision == 1;
            string system =
                $"You are {agent.agentName}, an AI agent whose role is {agent.agentRole}, working for a manager " +
                "who reviews your work on a floating page in VR. Keep every reply under 140 words, in plain text " +
                "with short lines (no markdown tables). " +
                (mayAsk
                    ? "If the task is too ambiguous to do well, reply with exactly one line starting with 'QUESTION:' " +
                      "and ask one clarifying question. Otherwise reply with a short plan or deliverable for approval."
                    : "Do not ask questions. Reply with the finished deliverable.");

            var sb = new StringBuilder();
            sb.Append("Task: ").Append(task.title).Append('\n').Append(task.description);
            if (task.HasAskedQuestion) sb.Append("\n\nYou asked: ").Append(task.question).Append("\nManager answered: ").Append(task.answer);
            if (task.revision > 1)
            {
                sb.Append("\n\nThe manager asked for changes to your previous version:\n").Append(task.output);
                if (!string.IsNullOrEmpty(task.feedback)) sb.Append("\nFeedback: ").Append(task.feedback);
            }

            var body = new Request
            {
                model = model,
                max_tokens = maxTokens,
                system = system,
                messages = new[] { new Msg { role = "user", content = sb.ToString() } },
                output_config = new OutputConfig { effort = effort }
            };

            using var request = new UnityWebRequest(apiEndpoint, "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(body)));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = requestTimeoutSeconds;
            request.SetRequestHeader("content-type", "application/json");
            request.SetRequestHeader("x-api-key", apiKey);
            request.SetRequestHeader("anthropic-version", "2023-06-01");
            request.SetRequestHeader("anthropic-beta", "server-side-fallback-2026-07-01");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                string err = $"HTTP {request.responseCode}: {request.error}";
                Debug.LogError($"[AIService] {err}\n{request.downloadHandler.text}");
                onDone?.Invoke(AIResult.Failure(err));
                yield break;
            }

            Response resp = null;
            try { resp = JsonUtility.FromJson<Response>(request.downloadHandler.text); }
            catch (Exception e) { Debug.LogError($"[AIService] Parse error: {e.Message}"); }

            if (resp == null)
            {
                onDone?.Invoke(AIResult.Failure("Could not read the response."));
                yield break;
            }
            if (resp.stop_reason == "refusal")
            {
                onDone?.Invoke(AIResult.Failure("The model declined this task."));
                yield break;
            }

            var text = new StringBuilder();
            if (resp.content != null)
                foreach (var block in resp.content)
                    if (block != null && block.type == "text") text.Append(block.text);

            string result = text.ToString().Trim();
            if (string.IsNullOrEmpty(result))
            {
                onDone?.Invoke(AIResult.Failure("Empty response."));
                yield break;
            }

            if (mayAsk && result.StartsWith("QUESTION:", StringComparison.OrdinalIgnoreCase))
                onDone?.Invoke(AIResult.Question(result.Substring("QUESTION:".Length).Trim()));
            else
                onDone?.Invoke(AIResult.Deliverable(result));
        }
    }
}
