using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace AIManagerWorkspace
{
    /// <summary>
    /// The agents' "brains". Out of the box every agent runs on the built-in simulation (demo mode: no keys,
    /// no network). Add one or more providers and pick a team default, and optionally a different brain per
    /// agent (Agents monitor > BRAIN):
    ///
    ///   anthropic  Claude (Messages API, structured JSON output)
    ///   openai     OpenAI (Chat Completions, json_schema response format)
    ///   gemini     Google Gemini (generateContent, JSON response schema)
    ///   custom     Any OpenAI-compatible endpoint: Groq, OpenRouter, Together, Ollama, LM Studio...
    ///
    /// The config file is never part of the build or of git:
    ///   - Quest:  Application.persistentDataPath/config.json
    ///   - Editor: &lt;project&gt;/UserSettings/AIManagerConfig.json
    /// It is re-read every few seconds, so keys pushed over USB apply without a restart.
    /// </summary>
    public class AIServiceManager : MonoBehaviour
    {
        public const string Simulation = "sim";
        public const string DefaultModel = "claude-opus-5-5";
        public static readonly string[] Providers = { "anthropic", "openai", "gemini", "custom" };

        [SerializeField] private string effort = "low";
        [SerializeField] private int maxTokens = 4096;
        [SerializeField] private int requestTimeoutSeconds = 90;
        [SerializeField] private float configPollSeconds = 4f;

        [Serializable]
        public class Backend
        {
            public string id;        // "anthropic" | "openai" | "gemini" | "custom"
            public string apiKey;
            public string model;
            public string baseUrl;   // empty = the provider's public endpoint
        }

        [Serializable]
        public class ConfigData
        {
            public string apiKey;              // legacy single Claude key (still read)
            public string model;
            public string defaultBackend = Simulation;
            public Backend[] backends;
            public string bridgeUrl;
            public string bridgeToken;
        }

        [Serializable] private class WorkOut { public string kind; public string text; public string[] options; }
        [Serializable] private class PlanOut { public PlannedTask[] assignments; }

        private readonly Dictionary<string, Backend> backends = new Dictionary<string, Backend>();
        private string defaultBackend = Simulation;
        private string bridgeUrl = "";
        private string bridgeToken = "";
        private DateTime configStamp;
        private float nextPoll;

        /// <summary>Team default brain is a configured provider (not the simulation).</summary>
        public bool IsLive => Usable(defaultBackend);
        public string DefaultBackend => defaultBackend;
        public string Model => Get(defaultBackend)?.model ?? "";
        public string BridgeUrl => bridgeUrl;
        public string BridgeToken => bridgeToken;
        public Vector2 SimulatedWorkSeconds => AppSettings.Current.SimulatedWorkSeconds;
        public string ModeLabel => IsLive ? $"LIVE  {DisplayName(defaultBackend)} · {Model}" : "DEMO · SIMULATED AGENTS";

        /// <summary>Raised when keys, models, the default brain or the bridge change.</summary>
        public event Action ConfigChanged;

        public static string ConfigPath =>
#if UNITY_EDITOR
            System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "UserSettings", "AIManagerConfig.json"));
#else
            System.IO.Path.Combine(Application.persistentDataPath, "config.json");
#endif

        public static string DisplayName(string id) => id switch
        {
            "anthropic" => "Claude",
            "openai" => "OpenAI",
            "gemini" => "Gemini",
            "custom" => "Custom",
            _ => "Simulation"
        };

        public static string DefaultModelFor(string id) => id switch
        {
            "anthropic" => DefaultModel,
            "openai" => "gpt-4.1",
            "gemini" => "gemini-2.5-flash",
            _ => ""
        };

        public Backend Get(string id) => id != null && backends.TryGetValue(id, out var b) ? b : null;

        /// <summary>A provider can be used: it has a key (custom endpoints may run without one, e.g. local Ollama).</summary>
        public bool Usable(string id)
        {
            var b = Get(id);
            if (b == null) return false;
            if (id == "custom") return !string.IsNullOrEmpty(b.baseUrl) && !string.IsNullOrEmpty(b.model);
            return !string.IsNullOrEmpty(b.apiKey);
        }

        /// <summary>The brain an agent actually uses: its own choice, else the team default; null = simulation.</summary>
        public Backend BrainFor(AgentData agent)
        {
            string id = agent != null && !string.IsNullOrEmpty(agent.backend) ? agent.backend : defaultBackend;
            return id != Simulation && Usable(id) ? Get(id) : null;
        }

        public string BrainLabel(AgentData agent)
        {
            var b = BrainFor(agent);
            string mine = agent != null && !string.IsNullOrEmpty(agent.backend) ? "" : " (team)";
            return (b == null ? "Simulation" : DisplayName(b.id)) + mine;
        }

        // ── Config ────────────────────────────────────────────────────────────

        private void Awake() => ReadConfig(force: true);

        private void Update()
        {
            if (Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + configPollSeconds;
            ReadConfig(force: false);
        }

        private void OnApplicationPause(bool paused) { if (!paused) ReadConfig(force: false); }

        /// <summary>Stores a provider's key / model / URL (null leaves a field unchanged).</summary>
        public void SetBackend(string id, string apiKey = null, string model = null, string baseUrl = null)
        {
            var b = Get(id) ?? new Backend { id = id, model = DefaultModelFor(id) };
            if (apiKey != null) b.apiKey = apiKey.Trim();
            if (model != null) b.model = string.IsNullOrWhiteSpace(model) ? DefaultModelFor(id) : model.Trim();
            if (baseUrl != null) b.baseUrl = baseUrl.Trim().TrimEnd('/');
            backends[id] = b;
            Write();
        }

        public void SetDefaultBackend(string id)
        {
            defaultBackend = string.IsNullOrEmpty(id) ? Simulation : id;
            Write();
        }

        /// <summary>Legacy: the Claude key (headset keyboard, Unity push tool).</summary>
        public void SetApiKey(string key) => SetBackend("anthropic", key ?? "");
        public void ClearApiKey() => SetBackend("anthropic", "");

        public void SetBridge(string url, string token = null)
        {
            url = (url ?? "").Trim();
            if (url.Length > 0 && !url.Contains("://")) url = "http://" + url;
            if (url.Length > 0 && url.LastIndexOf(':') <= url.IndexOf("://", StringComparison.Ordinal) + 2) url += ":8787";
            bridgeUrl = url;
            if (token != null) bridgeToken = token;
            Write();
        }

        private void Write()
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(ConfigPath);
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                var claude = Get("anthropic");
                var data = new ConfigData
                {
                    apiKey = claude?.apiKey ?? "", model = claude?.model ?? DefaultModel,
                    defaultBackend = defaultBackend, backends = new List<Backend>(backends.Values).ToArray(),
                    bridgeUrl = bridgeUrl, bridgeToken = bridgeToken
                };
                System.IO.File.WriteAllText(ConfigPath, JsonUtility.ToJson(data, true));
                configStamp = System.IO.File.GetLastWriteTimeUtc(ConfigPath);
            }
            catch (Exception e) { Debug.LogWarning($"[AIService] Could not save config: {e.Message}"); }
            Debug.Log($"[AIService] {ModeLabel}");
            ConfigChanged?.Invoke();
        }

        private void ReadConfig(bool force)
        {
            string path = ConfigPath;
            if (!System.IO.File.Exists(path))
            {
                if (!force && (backends.Count > 0 || defaultBackend != Simulation))
                {
                    backends.Clear();
                    defaultBackend = Simulation;
                    ConfigChanged?.Invoke();
                }
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
                backends.Clear();
                if (cfg.backends != null)
                    foreach (var b in cfg.backends)
                        if (b != null && !string.IsNullOrEmpty(b.id))
                        {
                            if (string.IsNullOrWhiteSpace(b.model) || (b.id == "anthropic" && b.model.StartsWith("claude-3"))) b.model = DefaultModelFor(b.id);
                            backends[b.id] = b;
                        }
                // Older config files only had one Claude key.
                if (!backends.ContainsKey("anthropic") && !string.IsNullOrWhiteSpace(cfg.apiKey))
                    backends["anthropic"] = new Backend { id = "anthropic", apiKey = cfg.apiKey.Trim(), model = DefaultModel };
                defaultBackend = string.IsNullOrEmpty(cfg.defaultBackend) ? Simulation : cfg.defaultBackend;
                bridgeUrl = (cfg.bridgeUrl ?? "").Trim();
                bridgeToken = (cfg.bridgeToken ?? "").Trim();
                Debug.Log($"[AIService] {ModeLabel}");
                ConfigChanged?.Invoke();
            }
            catch (Exception e) { Debug.LogWarning($"[AIService] Could not parse {path}: {e.Message}"); }
        }

        /// <summary>Sends a tiny request to check a provider's key, model and network. Calls back (ok, message).</summary>
        public void TestConnection(Action<bool, string> done) => TestConnection(defaultBackend == Simulation ? "anthropic" : defaultBackend, done);

        public void TestConnection(string id, Action<bool, string> done)
        {
            var b = Get(id);
            if (!Usable(id)) { done?.Invoke(false, $"{DisplayName(id)}: add a key{(id == "custom" ? ", URL and model" : "")} first."); return; }
            StartCoroutine(Send(b, "Reply with the single word OK.", "Connection test.", null,
                _ => done?.Invoke(true, $"{DisplayName(id)} connected ({b.model})."),
                e => done?.Invoke(false, $"{DisplayName(id)}: " + (e.Contains("401") || e.Contains("403") ? "the key was rejected." : e)), 32));
        }

        // ── Work ──────────────────────────────────────────────────────────────

        /// <summary>Starts work for <paramref name="task"/> on the agent's brain; <paramref name="onDone"/> fires once.</summary>
        public void RequestWork(AgentData agent, TaskData task, Action<AIResult> onDone)
        {
            Backend brain = BrainFor(agent);
            if (brain == null)
            {
                if (task.isTeamPlan && task.plan.Count == 0)
                    task.plan = TaskLibrary.OfflinePlan(task.description, GameManager.Instance.Agents.Agents);
                onDone?.Invoke(TaskLibrary.Simulate(agent, task));
                return;
            }
            if (task.isWrapUp) StartCoroutine(WrapUpRoutine(brain, agent, task, onDone));
            else if (task.isTeamPlan) StartCoroutine(PlanRoutine(brain, agent, task, onDone));
            else StartCoroutine(WorkRoutine(brain, agent, task, onDone));
        }

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

        private IEnumerator WorkRoutine(Backend brain, AgentData agent, TaskData task, Action<AIResult> onDone)
        {
            bool mayAsk = !task.askedOnce && task.revision == 1;
            string system =
                $"You are {agent.agentName}, an AI agent whose role is {agent.agentRole}, working for a manager who reviews " +
                "your work on a floating page in VR. Plain text, short lines, under 160 words, no markdown tables.\n" +
                (mayAsk
                    ? "If the task is ambiguous, ask instead of guessing: use kind 'choice' with 2-3 short options (each under 4 words) " +
                      "when the answer is one of a few directions, or kind 'question' for an open question. Otherwise use kind " +
                      "'deliverable' with a short plan or result for approval."
                    : "Do not ask anything. Use kind 'deliverable' with the finished result.") +
                " Leave 'options' empty unless kind is 'choice'.";

            var sb = new StringBuilder();
            sb.Append("Task: ").Append(task.title).Append('\n').Append(task.description);
            if (task.askedOnce) sb.Append("\n\nYou asked: ").Append(task.question).Append("\nManager answered: ").Append(task.answer);
            if (!string.IsNullOrEmpty(task.planId))
            {
                string goal = GameManager.Instance.Planner.GoalOf(task.planId);
                if (!string.IsNullOrEmpty(goal)) sb.Append("\n\nThis task is part of the team goal: ").Append(goal);
            }
            string memory = GameManager.Instance.Session != null ? GameManager.Instance.Session.RecentWorkDigest(5) : "";
            if (memory.Length > 0) sb.Append("\n\nRecent work your team finished (context, do not repeat it):\n").Append(memory);
            if (task.revision > 1)
            {
                sb.Append("\n\nThe manager asked for changes to your previous version:\n").Append(task.output);
                if (!string.IsNullOrEmpty(task.feedback)) sb.Append("\nFeedback: ").Append(task.feedback);
            }

            string text = null, error = null;
            yield return Send(brain, system, sb.ToString(), WorkSchema, r => text = r, e => error = e);
            if (error != null) { onDone?.Invoke(AIResult.Failure(error)); yield break; }

            WorkOut w = null;
            try { w = JsonUtility.FromJson<WorkOut>(LlmClient.ExtractJson(text)); } catch { /* plain text answer */ }
            if (w == null || string.IsNullOrEmpty(w.text)) { onDone?.Invoke(AIResult.Deliverable(text)); yield break; }

            if (mayAsk && w.kind == "choice" && w.options != null && w.options.Length >= 2) onDone?.Invoke(AIResult.Choice(w.text, w.options));
            else if (mayAsk && w.kind == "question") onDone?.Invoke(AIResult.Question(w.text));
            else onDone?.Invoke(AIResult.Deliverable(w.text));
        }

        // ── Team planning: the lead decides who does what ──────────────────────

        private IEnumerator PlanRoutine(Backend brain, AgentData lead, TaskData task, Action<AIResult> onDone)
        {
            var roster = new StringBuilder();
            foreach (var a in GameManager.Instance.Agents.Agents)
                roster.Append("- ").Append(a.Data.agentName).Append(" (current role: ").Append(a.Data.agentRole)
                      .Append(a.Data.IsLinked ? ", a real agent on the manager's PC" : "").Append(")\n");

            string system =
                $"You are {lead.agentName}, the lead of a team of AI agents. Split the manager's goal into exactly one task per agent " +
                "listed (use the agent names exactly), giving each agent the role that fits best (you may change roles). " +
                "Titles under 6 words, tasks under 30 words. Set 'needs' to 'question' or 'choice' only for tasks where the " +
                "manager's input is genuinely needed first, otherwise 'approval'.";
            string user = $"Goal: {task.description}\n\nTeam:\n{roster}" +
                          (task.revision > 1 && !string.IsNullOrEmpty(task.feedback) ? $"\nManager feedback on your last plan: {task.feedback}" : "");

            string text = null, error = null;
            yield return Send(brain, system, user, PlanSchema, r => text = r, e => error = e);

            PlanOut p = null;
            if (error == null) { try { p = JsonUtility.FromJson<PlanOut>(LlmClient.ExtractJson(text)); } catch { /* fallback below */ } }
            bool live = p?.assignments != null && p.assignments.Length > 0;
            if (!live) Debug.LogWarning($"[AIService] Plan from {DisplayName(brain.id)} unusable ({error ?? "bad JSON"}); using the built-in plan.");
            task.plan = live ? new List<PlannedTask>(p.assignments) : TaskLibrary.OfflinePlan(task.description, GameManager.Instance.Agents.Agents);
            onDone?.Invoke(AIResult.Deliverable(TaskLibrary.PlanText(task)));
        }

        // ── Wrap-up report ────────────────────────────────────────────────────

        private IEnumerator WrapUpRoutine(Backend brain, AgentData lead, TaskData task, Action<AIResult> onDone)
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
                "Plain text, short lines, under 160 words, no markdown tables.";

            string text = null, error = null;
            yield return Send(brain, system, sb.ToString(), null, r => text = r, e => error = e);
            onDone?.Invoke(AIResult.Report(error == null && !string.IsNullOrEmpty(text) ? text : task.output));
        }

        // ── HTTP ──────────────────────────────────────────────────────────────

        private IEnumerator Send(Backend brain, string system, string user, string schema, Action<string> ok, Action<string> fail, int tokens = 0)
        {
            var req = LlmClient.Build(brain, system, user, schema, tokens > 0 ? tokens : maxTokens, effort);
            using var request = new UnityWebRequest(req.url, "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(req.body));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = requestTimeoutSeconds;
            request.SetRequestHeader("content-type", "application/json");
            foreach (var h in req.headers) request.SetRequestHeader(h.Key, h.Value);

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                string detail = LlmClient.ErrorMessage(request.downloadHandler.text);
                Debug.LogWarning($"[AIService] {DisplayName(brain.id)} HTTP {request.responseCode}: {request.error} {detail}");
                fail($"HTTP {request.responseCode}{(detail.Length > 0 ? ": " + detail : "")}");
                yield break;
            }

            string text = LlmClient.ParseText(brain.id, request.downloadHandler.text, out string problem);
            if (problem != null) fail(problem);
            else ok(text);
        }
    }
}
