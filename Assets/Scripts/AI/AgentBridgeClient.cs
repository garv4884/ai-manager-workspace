using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Connects the workspace to the AI Manager Bridge (Bridge/aim_bridge.py on your PC), so real agents
    /// (Claude Code sessions, scripts, CI jobs, MCP clients) appear at desks in VR.
    ///
    ///   - A real agent that registers takes over a free desk (or a new desk is added) and is shown with a
    ///     halo and a LIVE tag. Its reported status drives the desk: typing while it works.
    ///   - Its requests (approve / question / choice / report / error) are walked up to you like any page;
    ///     your decision goes straight back to the agent waiting on it.
    ///   - Tasks you give a linked agent (desk card, or "Brief the team") are sent to it over the bridge.
    ///
    /// Bridge URL comes from the config file ("bridgeUrl"); default http://127.0.0.1:8787, which reaches
    /// the PC over USB after `adb reverse tcp:8787 tcp:8787` (the Unity Quest tool does this for you).
    /// </summary>
    public class AgentBridgeClient : MonoBehaviour
    {
        [SerializeField] private string defaultUrl = "http://127.0.0.1:8787";
        [SerializeField] private float retrySeconds = 5f;

        [Serializable] private class AgentInfo { public string name; public string role; public string kind; public string state; public string title; public float progress = -1f; }
        [Serializable] private class RequestInfo { public string id; public string agent; public string kind; public string title; public string text; public string[] options; public string task; public string state; }

        private string url = "";
        private string token = "";
        private int seq;
        private Coroutine loop;
        private readonly Dictionary<string, AgentController> links = new Dictionary<string, AgentController>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, AgentInfo> known = new Dictionary<string, AgentInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly List<(AgentInfo agent, RequestInfo request)> unplaced = new List<(AgentInfo, RequestInfo)>();

        public bool Connected { get; private set; }
        public string Url => url;
        public int LinkedCount => links.Count;
        public string LastError { get; private set; } = "";
        public event Action StateChanged;

        private void Start()
        {
            GameManager.Instance.AI.ConfigChanged += Reconfigure;
            Reconfigure();
        }

        private void Reconfigure()
        {
            AIServiceManager ai = GameManager.Instance.AI;
            string next = string.IsNullOrWhiteSpace(ai.BridgeUrl) ? defaultUrl : ai.BridgeUrl.Trim().TrimEnd('/');
            if (next == url && token == ai.BridgeToken && loop != null) return;
            url = next;
            token = ai.BridgeToken ?? "";
            seq = 0;
            if (loop != null) StopCoroutine(loop);
            loop = StartCoroutine(PollLoop());
        }

        // ── Polling ───────────────────────────────────────────────────────────

        private IEnumerator PollLoop()
        {
            while (true)
            {
                using var req = UnityWebRequest.Get($"{url}/api/events?since={seq}&wait=20");
                req.timeout = 35;
                Auth(req);
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    SetConnected(false, req.responseCode == 401 ? "Bridge needs the token (LAN mode)." : "Bridge not reachable.");
                    yield return new WaitForSecondsRealtime(retrySeconds);
                    continue;
                }
                SetConnected(true, "");
                try { Handle(req.downloadHandler.text); }
                catch (Exception e) { Debug.LogWarning($"[Bridge] Bad event batch: {e.Message}"); }
                TryPlaceWaiting();
            }
        }

        private void SetConnected(bool value, string error)
        {
            LastError = error;
            if (Connected == value) return;
            Connected = value;
            Debug.Log(value ? $"[Bridge] Connected to {url}" : $"[Bridge] Disconnected ({error})");
            if (!value) seq = 0;    // full snapshot on reconnect
            StateChanged?.Invoke();
        }

        /// <summary>Parses {"seq":N,"snapshot":bool,"events":[{"seq","type","data":{...}}]} without a JSON library.</summary>
        private void Handle(string json)
        {
            var batch = MiniJson.Parse(json) as Dictionary<string, object>;
            if (batch == null) return;
            bool snapshot = batch.TryGetValue("snapshot", out var s) && s is bool b && b;
            if (snapshot) OnSnapshot(batch["events"] as List<object>);
            seq = Convert.ToInt32(batch["seq"]);
            if (!(batch["events"] is List<object> events)) return;
            foreach (var o in events)
            {
                if (!(o is Dictionary<string, object> e)) continue;
                var data = e.TryGetValue("data", out var d) ? d as Dictionary<string, object> : null;
                if (data == null) continue;
                switch (e["type"] as string)
                {
                    case "agent": OnAgent(ToAgent(data)); break;
                    case "agent_gone": OnAgentGone(Str(data, "name")); break;
                    case "request": OnRequest(ToRequest(data)); break;
                    case "cancel": OnCancel(Str(data, "id"), Str(data, "agent")); break;
                    case "decided": OnCancel(Str(data, "id"), Str(data, "agent")); break;   // decided elsewhere (dashboard)
                }
            }
            StateChanged?.Invoke();
        }

        /// <summary>Reconnected: unlink desks whose agent the bridge no longer knows (bridge restarted).</summary>
        private void OnSnapshot(List<object> events)
        {
            unplaced.Clear();
            var alive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (events != null)
                foreach (var o in events)
                    if (o is Dictionary<string, object> e && e["type"] as string == "agent" && e["data"] is Dictionary<string, object> d)
                        alive.Add(Str(d, "name"));
            foreach (var name in new List<string>(links.Keys))
                if (!alive.Contains(name)) OnAgentGone(name);
        }

        // ── Agents ────────────────────────────────────────────────────────────

        private void OnAgent(AgentInfo info)
        {
            if (string.IsNullOrEmpty(info.name)) return;
            known[info.name] = info;
            AgentController desk = Desk(info.name, place: true);
            if (desk == null) return;
            if (!string.IsNullOrEmpty(info.role) && info.role != desk.Data.agentRole) { desk.Data.agentRole = info.role; desk.NotifyChanged(); }
            if (!string.IsNullOrEmpty(info.kind)) desk.Data.linkedKind = info.kind;
            desk.SetExternalState(info.state, info.title, info.progress);
        }

        private void OnAgentGone(string name)
        {
            known.Remove(name);
            if (links.TryGetValue(name, out var desk) && desk != null) desk.Unlink();
            links.Remove(name);
        }

        /// <summary>The desk that stands in for a bridge agent; links a free desk (or adds one) when needed.</summary>
        private AgentController Desk(string name, bool place)
        {
            if (links.TryGetValue(name, out var linked) && linked != null) return linked;
            links.Remove(name);
            if (!place) return null;

            AgentManager team = GameManager.Instance.Agents;
            AgentController pick = null;
            foreach (var a in team.Agents)
                if (!a.Data.IsLinked && a.CanTakeTask) { pick = a; break; }
            if (pick == null && team.CanAdd) pick = team.AddAgent();
            if (pick == null) return null;     // every desk busy: placed later (see TryPlaceWaiting)

            known.TryGetValue(name, out var info);
            pick.LinkTo(name, info?.kind ?? "agent", info?.role);
            links[name] = pick;
            Debug.Log($"[Bridge] {name} is now at {pick.Data.profileName}'s desk.");
            return pick;
        }

        private void TryPlaceWaiting()
        {
            for (int i = unplaced.Count - 1; i >= 0; i--)
            {
                var (agent, request) = unplaced[i];
                AgentController desk = Desk(agent.name, place: true);
                if (desk == null) continue;
                unplaced.RemoveAt(i);
                desk.ReceiveExternalRequest(ToTask(request));
            }
            foreach (var kv in known)
                if (!links.ContainsKey(kv.Key)) OnAgent(kv.Value);
        }

        // ── Requests ──────────────────────────────────────────────────────────

        private void OnRequest(RequestInfo r)
        {
            if (string.IsNullOrEmpty(r.id) || r.state != "pending") return;
            AgentController desk = Desk(r.agent, place: true);
            if (desk == null)
            {
                known.TryGetValue(r.agent, out var info);
                unplaced.Add((info ?? new AgentInfo { name = r.agent }, r));
                return;
            }
            desk.ReceiveExternalRequest(ToTask(r));
        }

        private void OnCancel(string id, string agent)
        {
            unplaced.RemoveAll(u => u.request.id == id);
            if (links.TryGetValue(agent ?? "", out var desk) && desk != null) desk.CancelExternalRequest(id);
        }

        private static TaskData ToTask(RequestInfo r)
        {
            var t = new TaskData(string.IsNullOrEmpty(r.title) ? "Request" : r.title, r.text ?? "", TaskType.NeedsApproval)
            {
                isExternal = true,
                externalId = r.id,
                externalTaskId = r.task ?? ""
            };
            switch (r.kind)
            {
                case "question": t.request = RequestKind.Question; t.question = r.text; break;
                case "choice":
                    t.request = RequestKind.Choice; t.question = r.text;
                    if (r.options != null) t.options.AddRange(r.options);
                    break;
                case "error": t.request = RequestKind.Error; t.error = r.text; break;
                case "report": t.request = RequestKind.Report; t.output = r.text; break;
                default: t.request = RequestKind.Approval; t.output = r.text; break;
            }
            return t;
        }

        // ── Outgoing ──────────────────────────────────────────────────────────

        public void SendDecision(string requestId, string action, string text) =>
            StartCoroutine(Post("/api/decisions", "{" + $"\"id\":{MiniJson.Quote(requestId)},\"action\":{MiniJson.Quote(action)},\"text\":{MiniJson.Quote(text)}" + "}"));

        public void SendTask(AgentController agent, TaskData task)
        {
            string goal = string.IsNullOrEmpty(task.planId) ? "" : GameManager.Instance.Planner.GoalOf(task.planId) ?? "";
            StartCoroutine(Post("/api/tasks", "{" +
                $"\"agent\":{MiniJson.Quote(agent.Data.linkedTo)},\"id\":{MiniJson.Quote(task.taskId)}," +
                $"\"title\":{MiniJson.Quote(task.title)},\"text\":{MiniJson.Quote(task.description)},\"goal\":{MiniJson.Quote(goal)}" + "}"));
        }

        private IEnumerator Post(string path, string body)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                using var req = new UnityWebRequest(url + path, "POST");
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = 10;
                Auth(req);
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success) yield break;
                Debug.LogWarning($"[Bridge] POST {path} failed: {req.error}");
                yield return new WaitForSecondsRealtime(1.5f);
            }
        }

        private void Auth(UnityWebRequest req)
        {
            if (!string.IsNullOrEmpty(token)) req.SetRequestHeader("X-AIM-Token", token);
        }

        // ── Parsing helpers ───────────────────────────────────────────────────

        private static string Str(Dictionary<string, object> d, string k) => d.TryGetValue(k, out var v) && v != null ? v.ToString() : "";

        private static AgentInfo ToAgent(Dictionary<string, object> d) => new AgentInfo
        {
            name = Str(d, "name"), role = Str(d, "role"), kind = Str(d, "kind"), state = Str(d, "state"), title = Str(d, "title"),
            progress = d.TryGetValue("progress", out var p) && p != null ? Convert.ToSingle(p) : -1f
        };

        private static RequestInfo ToRequest(Dictionary<string, object> d)
        {
            var r = new RequestInfo { id = Str(d, "id"), agent = Str(d, "agent"), kind = Str(d, "kind"), title = Str(d, "title"),
                                      text = Str(d, "text"), task = Str(d, "task"), state = Str(d, "state") };
            if (d.TryGetValue("options", out var o) && o is List<object> list)
                r.options = list.ConvertAll(x => x?.ToString() ?? "").ToArray();
            return r;
        }
    }
}
