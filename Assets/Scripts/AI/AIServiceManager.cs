using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Runs one unit of agent work: a live Claude call (raw HTTP; the official Anthropic SDK is
    /// not usable from a Quest IL2CPP build) or the offline simulation in <see cref="TaskLibrary"/>.
    ///
    /// Live replies use structured JSON output, so the model itself decides whether to deliver
    /// work, ask an open question, or offer a set of choices.
    ///
    /// The API key lives in one config file that is never part of the build or of git:
    ///   - Quest:  Application.persistentDataPath/config.json
    ///             (/sdcard/Android/data/&lt;package&gt;/files/config.json)
    ///   - Editor: &lt;project&gt;/UserSettings/AIManagerConfig.json
    /// Format: { "apiKey": "...", "model": "claude-opus-5-5" }
    ///
    /// You never edit it by hand: enter the key in the headset (desk panel, Settings), or in Unity with
    /// Tools > AI Manager > Quest API Key, which writes the editor file and pushes it to the headset over
    /// USB. The file is re-read every few seconds, so a pushed key switches the app to live AI without
    /// a restart.
    /// </summary>
    public class AIServiceManager : MonoBehaviour
    {
        [Header("Claude API")]
        [SerializeField] private string apiEndpoint = "https://api.anthropic.com/v1/messages";
        [SerializeField] private string model = "claude-opus-5-5";
        [SerializeField] private string effort = "low";
        [SerializeField] private int maxTokens = 4096;
        [SerializeField] private int requestTimeoutSeconds = 90;

        [Header("Offline simulation")]
        [SerializeField] private Vector2 simulatedWorkSeconds = new Vector2(5f, 10f);

        [SerializeField] private float configPollSeconds = 4f;

        public const string DefaultModel = "claude-opus-5-5";

        private string apiKey = "";
        private DateTime configStamp;
        private float nextPoll;

        public bool IsLive => !string.IsNullOrEmpty(apiKey);
        public string Model => model;
        public string ModeLabel => IsLive ? $"LIVE AI  {model}" : "OFFLINE SIMULATION";
        public Vector2 SimulatedWorkSeconds => simulatedWorkSeconds;

        /// <summary>Raised when the key or model changes (typed in the headset, pushed over USB, or removed).</summary>
        public event Action ConfigChanged;

        public static string ConfigPath =>
#if UNITY_EDITOR
            System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "UserSettings", "AIManagerConfig.json"));
#else
            System.IO.Path.Combine(Application.persistentDataPath, "config.json");
#endif

        [Serializable] public class ConfigData { public string apiKey; public string model; }
        [Serializable] private class Block { public string type; public string text; }
        [Serializable] private class Response { public Block[] content; public string stop_reason; }
        [Serializable] private class WorkOut { public string kind; public string text; public string[] options; }
        [Serializable] private class PlanOut { public PlannedTask[] assignments; }

        private const string WorkSchema =
            "{\"type\":\"object\",\"properties\":{" +
            "\"kind\":{\"type\":\"string\",\"enum\":[\"deliverable\",\"question\",\"choice\"]}," +
            "\"text\":{\"type\":\"string\"}," +
            "\"options\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}}," +
            "\"required\":[\"kind\",\"text\",\"options\"],\"additionalProperties\":false}";

        private const string PlanSchema =
            "{\"type\":\"object\",\"properties\":{\"assignments\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{" +
            "\"agent\":{\"type\":\"string\"},\"role\":{\"type\":\"string\"},\"title\":{\"type\":\"string\"},\"task\":{\"type\":\"string\"}," +
            "\"needs\":{\"type\":\"string\",\"enum\":[\"approval\",\"question\",\"choice\"]}}," +
            "\"required\":[\"agent\",\"role\",\"title\",\"task\",\"needs\"],\"additionalProperties\":false}}}," +
            "\"required\":[\"assignments\"],\"additionalProperties\":false}";

        private void Awake() => ReadConfig(force: true);

        private void Update()
        {
            if (Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + configPollSeconds;
            ReadConfig(force: false);
        }

        private void OnApplicationPause(bool paused) { if (!paused) ReadConfig(force: false); }

        /// <summary>Saves the key typed in the headset (an empty key switches back to the offline simulation).</summary>
        public void SetApiKey(string key)
        {
            key = (key ?? "").Trim();
            try
            {
                string dir = System.IO.Path.GetDirectoryName(ConfigPath);
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(ConfigPath, JsonUtility.ToJson(new ConfigData { apiKey = key, model = model }, true));
                configStamp = System.IO.File.GetLastWriteTimeUtc(ConfigPath);
            }
            catch (Exception e) { Debug.LogWarning($"[AIService] Could not save config: {e.Message}"); }
            Apply(key, model);
        }

        public void ClearApiKey() => SetApiKey("");

        private void ReadConfig(bool force)
        {
            string path = ConfigPath;
            if (!System.IO.File.Exists(path))
            {
                if (IsLive && !force) Apply("", model);   // file removed: back to offline
                configStamp = default;
                return;
            }
            DateTime stamp = System.IO.File.GetLastWriteTimeUtc(path);
            if (!force && stamp == configStamp) return;
            configStamp = stamp;
            try
            {
                var cfg = JsonUtility.FromJson<ConfigData>(System.IO.File.ReadAllText(path));
                if (cfg == null) return;
                string m = string.IsNullOrWhiteSpace(cfg.model) || cfg.model.StartsWith("claude-3") ? DefaultModel : cfg.model.Trim();
                Apply((cfg.apiKey ?? "").Trim(), m);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AIService] Could not parse {path}: {e.Message}");
            }
        }

        private void Apply(string key, string m)
        {
            bool changed = key != apiKey || m != model;
            apiKey = key;
            model = m;
            if (!changed) return;
            Debug.Log($"[AIService] Mode: {ModeLabel}");
            ConfigChanged?.Invoke();
        }

        /// <summary>Sends a tiny request to check the key and network. Calls back (ok, message).</summary>
        public void TestConnection(Action<bool, string> done)
        {
            if (!IsLive) { done?.Invoke(false, "No API key set."); return; }
            StartCoroutine(Send("Reply with the single word OK.", "Connection test.", null,
                _ => done?.Invoke(true, $"Connected to {model}."),
                e => done?.Invoke(false, e.Contains("401") ? "The API key was rejected (401)." : e), 32));
        }

        /// <summary>Starts work for <paramref name="task"/>; <paramref name="onDone"/> fires once.</summary>
        public void RequestWork(AgentData agent, TaskData task, Action<AIResult> onDone)
        {
            if (task.isWrapUp && IsLive) { StartCoroutine(WrapUpRoutine(agent, task, onDone)); return; }
            if (task.isWrapUp || !IsLive)
            {
                if (task.isTeamPlan && task.plan.Count == 0)
                    task.plan = TaskLibrary.OfflinePlan(task.description, GameManager.Instance.Agents.Agents);
                onDone?.Invoke(TaskLibrary.Simulate(agent, task));
                return;
            }
            StartCoroutine(task.isTeamPlan ? PlanRoutine(agent, task, onDone) : WorkRoutine(agent, task, onDone));
        }

        // ── Work ──────────────────────────────────────────────────────────────

        private IEnumerator WorkRoutine(AgentData agent, TaskData task, Action<AIResult> onDone)
        {
            bool mayAsk = !task.askedOnce && task.revision == 1;
            string system =
                $"You are {agent.agentName}, an AI agent whose role is {agent.agentRole}, working for a manager who reviews " +
                "your work on a floating page in VR. Plain text, short lines, under 140 words, no markdown tables.\n" +
                (mayAsk
                    ? "If the task is ambiguous, ask instead of guessing: use kind 'choice' with 2-3 short options (each under 4 words) " +
                      "when the answer is one of a few directions, or kind 'question' for an open question. Otherwise use kind " +
                      "'deliverable' with a short plan or result for approval."
                    : "Do not ask anything. Use kind 'deliverable' with the finished result.") +
                " Leave 'options' empty unless kind is 'choice'.";

            var sb = new StringBuilder();
            sb.Append("Task: ").Append(task.title).Append('\n').Append(task.description);
            if (task.askedOnce) sb.Append("\n\nYou asked: ").Append(task.question).Append("\nManager answered: ").Append(task.answer);
            string memory = GameManager.Instance.Session != null ? GameManager.Instance.Session.RecentWorkDigest(5) : "";
            if (!string.IsNullOrEmpty(task.planId))
            {
                string goal = GameManager.Instance.Planner.GoalOf(task.planId);
                if (!string.IsNullOrEmpty(goal)) sb.Append("\n\nThis task is part of the team goal: ").Append(goal);
            }
            if (memory.Length > 0) sb.Append("\n\nRecent work your team finished (context, do not repeat it):\n").Append(memory);
            if (task.revision > 1)
            {
                sb.Append("\n\nThe manager asked for changes to your previous version:\n").Append(task.output);
                if (!string.IsNullOrEmpty(task.feedback)) sb.Append("\nFeedback: ").Append(task.feedback);
            }

            string text = null, error = null;
            yield return Send(system, sb.ToString(), WorkSchema, r => text = r, e => error = e);
            if (error != null) { onDone?.Invoke(AIResult.Failure(error)); yield break; }

            WorkOut w = null;
            try { w = JsonUtility.FromJson<WorkOut>(text); } catch { /* fall through */ }
            if (w == null || string.IsNullOrEmpty(w.text)) { onDone?.Invoke(AIResult.Deliverable(text)); yield break; }

            if (mayAsk && w.kind == "choice" && w.options != null && w.options.Length >= 2) onDone?.Invoke(AIResult.Choice(w.text, w.options));
            else if (mayAsk && w.kind == "question") onDone?.Invoke(AIResult.Question(w.text));
            else onDone?.Invoke(AIResult.Deliverable(w.text));
        }

        // ── Team planning ─────────────────────────────────────────────────────

        private IEnumerator PlanRoutine(AgentData lead, TaskData task, Action<AIResult> onDone)
        {
            var roster = new StringBuilder();
            foreach (var a in GameManager.Instance.Agents.Agents)
                roster.Append("- ").Append(a.Data.agentName).Append(" (current role: ").Append(a.Data.agentRole).Append(")\n");

            string system =
                $"You are {lead.agentName}, the lead of a team of AI agents. Split the manager's goal into exactly one task per agent " +
                "listed, giving each agent the role that fits best (you may change roles). Titles under 6 words, tasks under 30 words. " +
                "Set 'needs' to 'question' or 'choice' only for tasks where the manager's input is genuinely needed first.";
            string user = $"Goal: {task.description}\n\nTeam:\n{roster}" +
                          (task.revision > 1 && !string.IsNullOrEmpty(task.feedback) ? $"\nManager feedback on your last plan: {task.feedback}" : "");

            string text = null, error = null;
            yield return Send(system, user, PlanSchema, r => text = r, e => error = e);

            PlanOut p = null;
            if (error == null) { try { p = JsonUtility.FromJson<PlanOut>(text); } catch { /* fallback below */ } }
            task.plan = p?.assignments != null && p.assignments.Length > 0
                ? new List<PlannedTask>(p.assignments)
                : TaskLibrary.OfflinePlan(task.description, GameManager.Instance.Agents.Agents);
            onDone?.Invoke(AIResult.Deliverable(TaskLibrary.PlanText(task)));
        }

        // ── Wrap-up report ────────────────────────────────────────────────────

        private IEnumerator WrapUpRoutine(AgentData lead, TaskData task, Action<AIResult> onDone)
        {
            var sb = new StringBuilder("Goal: ").Append(task.description).Append("\n\nWhat the team handed in:\n");
            var tasks = GameManager.Instance.Planner.TasksOf(task.planId);
            if (tasks != null)
                foreach (var t in tasks)
                {
                    bool dropped = t.status == TaskExecutionStatus.Cancelled;
                    sb.Append("- ").Append(t.title).Append(dropped ? " (dropped by the manager)" : "").Append(": ")
                      .Append(TaskLibrary.Trim((t.output ?? "").Replace('\n', ' '), 400)).Append('\n');
                }
            string system =
                $"You are {lead.agentName}, lead of an AI team. Write the wrap-up report the manager reads on a floating page in VR: " +
                "what was delivered towards the goal, anything dropped or still open, and one suggested next step. " +
                "Plain text, short lines, under 140 words, no markdown tables.";

            string text = null, error = null;
            yield return Send(system, sb.ToString(), null, r => text = r, e => error = e);
            onDone?.Invoke(AIResult.Report(error == null && !string.IsNullOrEmpty(text) ? text : task.output));
        }

        // ── HTTP ──────────────────────────────────────────────────────────────

        private IEnumerator Send(string system, string user, string schema, Action<string> ok, Action<string> fail, int tokens = 0)
        {
            string output = schema != null
                ? $"{{\"effort\":{Json(effort)},\"format\":{{\"type\":\"json_schema\",\"schema\":{schema}}}}}"
                : $"{{\"effort\":{Json(effort)}}}";
            string body =
                "{" +
                $"\"model\":{Json(model)}," +
                $"\"max_tokens\":{(tokens > 0 ? tokens : maxTokens)}," +
                $"\"system\":{Json(system)}," +
                $"\"messages\":[{{\"role\":\"user\",\"content\":{Json(user)}}}]," +
                $"\"output_config\":{output}," +
                "\"fallbacks\":\"default\"" +
                "}";

            using var request = new UnityWebRequest(apiEndpoint, "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = requestTimeoutSeconds;
            request.SetRequestHeader("content-type", "application/json");
            request.SetRequestHeader("x-api-key", apiKey);
            request.SetRequestHeader("anthropic-version", "2023-06-01");
            request.SetRequestHeader("anthropic-beta", "server-side-fallback-2026-07-01");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[AIService] HTTP {request.responseCode}: {request.error}\n{request.downloadHandler.text}");
                fail($"HTTP {request.responseCode}: {request.error}");
                yield break;
            }

            Response resp = null;
            try { resp = JsonUtility.FromJson<Response>(request.downloadHandler.text); }
            catch (Exception e) { Debug.LogError($"[AIService] Parse error: {e.Message}"); }

            if (resp == null) { fail("Could not read the response."); yield break; }
            if (resp.stop_reason == "refusal") { fail("The model declined this task."); yield break; }

            var text = new StringBuilder();
            if (resp.content != null)
                foreach (var block in resp.content)
                    if (block != null && block.type == "text") text.Append(block.text);

            string result = text.ToString().Trim();
            if (string.IsNullOrEmpty(result)) fail("Empty response.");
            else ok(result);
        }

        private static string Json(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
