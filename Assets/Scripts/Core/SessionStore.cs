using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AIManagerWorkspace
{
    /// <summary>One finished piece of work, kept for the history view and as team memory for live AI prompts.</summary>
    [Serializable]
    public class HistoryEntry
    {
        public string agent;
        public string role;
        public string title;
        public string outcome;     // "Approved", "Plan approved", "Wrap-up read", "Dropped", ...
        public string text;
        public string time;        // local time, "yyyy-MM-dd HH:mm"
        public string planId;
    }

    /// <summary>
    /// Saves the session to Application.persistentDataPath/session.json and restores it on the next launch:
    /// agent roles and counts, desk positions and layout, demo / hold switches, every agent's current and
    /// queued tasks, team briefs, and the work history. Agents that were waiting for the manager line up
    /// again with the same request; work that was in progress restarts.
    ///
    /// Saved when something changed (at most every <see cref="minSaveInterval"/> seconds), when the headset
    /// goes to sleep, and on quit. The API key is not part of the session (see AIServiceManager).
    /// </summary>
    public class SessionStore : MonoBehaviour
    {
        [SerializeField] private float minSaveInterval = 8f;
        [SerializeField] private int maxHistory = 120;
        [SerializeField] private int maxBriefs = 6;

        private const int Version = 1;

        [Serializable] private class DeskSave { public string agent; public Vector3 position; public float yaw; }
        [Serializable] private class AgentSave
        {
            public string name;
            public string role;
            public int completed;
            public int queueOrder = -1;
            public string backend = "";          // 0 = was presenting, 1.. = place in line, -1 = not waiting
            public List<TaskData> tasks = new List<TaskData>();   // current task first, then the backlog
        }
        [Serializable] private class BriefSave
        {
            public string id, goal, lead;
            public bool executed, wrappedUp;
            public List<TaskData> tasks = new List<TaskData>();
        }
        [Serializable] private class SaveData
        {
            public int version;
            public string savedAt;
            public bool demoMode;
            public bool holdQueue;
            public int layout;
            public int approved;
            public List<DeskSave> desks = new List<DeskSave>();
            public List<AgentSave> agents = new List<AgentSave>();
            public List<BriefSave> briefs = new List<BriefSave>();
            public List<HistoryEntry> history = new List<HistoryEntry>();
        }

        private readonly List<HistoryEntry> history = new List<HistoryEntry>();
        private bool loaded, dirty, suspended;
        private float lastSave;

        public IReadOnlyList<HistoryEntry> History => history;
        public bool RestoredSession { get; private set; }
        public string SavePath => Path.Combine(Application.persistentDataPath, "session.json");

        /// <summary>Raised when a history entry is added or the history is reset.</summary>
        public event Action HistoryChanged;

        private IEnumerator Start()
        {
            // Let AgentManager spawn the team and the layout manager place the desks first.
            yield return null;
            GameManager gm = GameManager.Instance;
            Load();
            loaded = true;
            gm.Agents.AnyAgentChanged += MarkDirty;
            gm.Layout.Changed += MarkDirty;
            gm.Planner.Changed += MarkDirty;
            gm.Queue.Changed += MarkDirty;
        }

        private void MarkDirty() => dirty = true;

        private void Update()
        {
            if (loaded && dirty && Time.unscaledTime - lastSave > minSaveInterval) Save();
        }

        private void OnApplicationPause(bool paused) { if (paused && loaded) Save(); }
        private void OnApplicationQuit() { if (loaded) Save(); }

        // ── History ───────────────────────────────────────────────────────────

        public void Record(AgentController agent, TaskData task, string outcome)
        {
            if (task == null) return;
            string body = task.output;
            if (string.IsNullOrEmpty(body)) body = !string.IsNullOrEmpty(task.question) ? task.question : task.description;
            history.Add(new HistoryEntry
            {
                agent = agent != null ? agent.Data.agentName : "",
                role = agent != null ? agent.Data.agentRole : "",
                title = task.title,
                outcome = outcome,
                text = body ?? "",
                time = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                planId = task.planId
            });
            if (history.Count > maxHistory) history.RemoveRange(0, history.Count - maxHistory);
            dirty = true;
            HistoryChanged?.Invoke();
        }

        /// <summary>Short digest of the latest finished work, given to live agents as shared team memory.</summary>
        public string RecentWorkDigest(int count, int charsEach = 180)
        {
            var sb = new StringBuilder();
            for (int i = history.Count - 1, n = 0; i >= 0 && n < count; i--)
            {
                HistoryEntry h = history[i];
                if (h.outcome == "Dropped") continue;
                sb.Append("- ").Append(h.agent).Append(" (").Append(h.role).Append("), ").Append(h.title).Append(": ")
                  .Append(TaskLibrary.Trim((h.text ?? "").Replace('\n', ' '), charsEach)).Append('\n');
                n++;
            }
            return sb.ToString();
        }

        // ── Save / load ───────────────────────────────────────────────────────

        public void Save()
        {
            if (suspended) return;
            dirty = false;
            lastSave = Time.unscaledTime;
            try
            {
                string json = JsonUtility.ToJson(Capture());
                string tmp = SavePath + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(SavePath)) File.Delete(SavePath);
                File.Move(tmp, SavePath);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Session] Save failed: {e.Message}");
            }
        }

        private SaveData Capture()
        {
            GameManager gm = GameManager.Instance;
            var data = new SaveData
            {
                version = Version,
                savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                demoMode = gm.Dispatcher.AutoDispatch,
                holdQueue = gm.Queue.Hold,
                layout = (int)gm.Layout.Current,
                approved = gm.Queue.Desk.Completed,
                history = new List<HistoryEntry>(history)
            };

            foreach (var a in gm.Agents.Agents)
            {
                Transform ws = a.Workstation.transform;
                data.desks.Add(new DeskSave { agent = a.Data.profileName, position = ws.position, yaw = ws.eulerAngles.y });
            }

            QueueManager q = gm.Queue;
            foreach (var a in gm.Agents.Agents)
            {
                string role = a.Data.IsLinked ? a.Data.linkedRoleBeforeLink : a.Data.agentRole;
                var s = new AgentSave { name = a.Data.profileName, role = role, completed = a.Data.completedTasks, backend = a.Data.backend };
                if (q.Presenter == a) s.queueOrder = 0;
                else { int i = IndexOf(q.Waiting, a); if (i >= 0) s.queueOrder = i + 1; }
                s.tasks.AddRange(a.TasksForSave());
                data.agents.Add(s);
            }

            var briefs = gm.Planner.Briefs;
            for (int i = Mathf.Max(0, briefs.Count - maxBriefs); i < briefs.Count; i++)
            {
                var b = briefs[i];
                var s = new BriefSave
                {
                    id = b.id, goal = b.goal, lead = b.lead != null ? b.lead.Data.profileName : "",
                    executed = b.executed, wrappedUp = b.wrappedUp
                };
                s.tasks.AddRange(b.tasks);
                data.briefs.Add(s);
            }
            return data;
        }

        private void Load()
        {
            if (!File.Exists(SavePath)) return;
            SaveData data;
            try { data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath)); }
            catch (Exception e) { Debug.LogWarning($"[Session] Could not read the saved session: {e.Message}"); return; }
            if (data == null || data.version != Version) return;

            GameManager gm = GameManager.Instance;
            history.Clear();
            if (data.history != null) history.AddRange(data.history);

            // Team size: same agents as last time (added or removed in VR).
            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in data.agents) if (!string.IsNullOrEmpty(s.name)) wanted.Add(s.name);
            if (wanted.Count > 0)
            {
                foreach (var a in new List<AgentController>(gm.Agents.Agents))
                    if (!wanted.Contains(a.Data.profileName)) gm.Agents.RemoveAgent(a, reflow: false);
                foreach (var name in wanted)
                    if (gm.Agents.Find(name) == null) gm.Agents.AddAgent(name, reflow: false);
            }

            var byName = new Dictionary<string, AgentController>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in gm.Agents.Agents) byName[a.Data.profileName] = a;

            // Desks and layout.
            bool allPlaced = true;
            foreach (var a in gm.Agents.Agents)
            {
                DeskSave d = data.desks.Find(x => string.Equals(x.agent, a.Data.profileName, StringComparison.OrdinalIgnoreCase));
                if (d == null) { allPlaced = false; continue; }
                a.Workstation.transform.SetPositionAndRotation(d.position, Quaternion.Euler(0f, d.yaw, 0f));
            }
            var layout = (LayoutType)Mathf.Clamp(data.layout, 0, (int)LayoutType.Custom);
            if (allPlaced) gm.Layout.MarkRestored(layout);
            else gm.Layout.ApplyLayout(layout == LayoutType.Custom ? LayoutType.Grid : layout, true);

            gm.Dispatcher.AutoDispatch = data.demoMode;
            gm.Queue.Desk.SetCompleted(data.approved);

            var liveTasks = new Dictionary<string, TaskData>();
            foreach (var s in data.agents)
            {
                if (!byName.TryGetValue(s.name, out var a)) continue;
                a.Data.agentRole = string.IsNullOrWhiteSpace(s.role) ? a.Data.agentRole : s.role;
                a.Data.completedTasks = s.completed;
                a.Data.backend = s.backend ?? "";
                foreach (var t in s.tasks) if (t != null && !string.IsNullOrEmpty(t.taskId)) liveTasks[t.taskId] = t;
            }

            // Briefs share task objects with the agents' backlogs so progress still counts towards the goal.
            foreach (var b in data.briefs)
            {
                byName.TryGetValue(b.lead ?? "", out var lead);
                var tasks = new List<TaskData>();
                foreach (var t in b.tasks)
                    if (t != null) tasks.Add(t.taskId != null && liveTasks.TryGetValue(t.taskId, out var live) ? live : t);
                gm.Planner.Restore(b.id, b.goal, lead, b.executed, b.wrappedUp, tasks);
            }

            // Tasks: waiting agents line up again in the order they were in.
            var waiting = new List<(int order, AgentController agent)>();
            foreach (var s in data.agents)
            {
                if (!byName.TryGetValue(s.name, out var a)) continue;
                bool waits = a.RestoreTasks(s.tasks);
                if (waits) waiting.Add((s.queueOrder < 0 ? 999 : s.queueOrder, a));
                a.NotifyChanged();
            }
            gm.Queue.Hold = data.holdQueue;     // before lining up, so a held queue stays held
            waiting.Sort((x, y) => x.order.CompareTo(y.order));
            foreach (var w in waiting) gm.Queue.Enqueue(w.agent);

            RestoredSession = true;
            HistoryChanged?.Invoke();
            Debug.Log($"[Session] Restored session from {data.savedAt}: {data.agents.Count} agents, {waiting.Count} waiting, {history.Count} history entries.");
        }

        /// <summary>Deletes the saved session and reloads the scene for a clean start (the API key is kept).</summary>
        public void ResetSession()
        {
            suspended = true;
            try { if (File.Exists(SavePath)) File.Delete(SavePath); }
            catch (Exception e) { Debug.LogWarning($"[Session] Could not delete the save: {e.Message}"); }
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        private static int IndexOf(IReadOnlyList<AgentController> list, AgentController a)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == a) return i;
            return -1;
        }
    }
}
