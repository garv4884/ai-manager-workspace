using System;
using System.Collections.Generic;
using UnityEngine;

namespace AIManagerWorkspace
{
    /// <summary>
    /// The team: spawns agents at workstations, adds and removes agents (desks re-flow into the
    /// current layout), tracks the selected agent, and runs group discussions in the meeting room.
    /// </summary>
    public class AgentManager : MonoBehaviour
    {
        [Serializable]
        public class Profile
        {
            public string name;
            public string role;
            public Color color;
            public Profile(string n, string r, Color c) { name = n; role = r; color = c; }
        }

        [SerializeField] private GameObject agentPrefab;
        [SerializeField] private Workstation[] workstations;
        [SerializeField] private MeetingRoom meetingRoom;
        [SerializeField] private int startAgents = 6;
        [SerializeField] private Profile[] profiles =
        {
            new Profile("Claude", "Code Writer", new Color(0.86f, 0.50f, 0.34f)),
            new Profile("Nova", "Researcher", new Color(0.22f, 0.66f, 0.60f)),
            new Profile("Iris", "Designer", new Color(0.36f, 0.52f, 0.95f)),
            new Profile("Atlas", "Data Analyst", new Color(0.60f, 0.42f, 0.86f)),
            new Profile("Quill", "Copywriter", new Color(0.93f, 0.70f, 0.22f)),
            new Profile("Probe", "QA Tester", new Color(0.90f, 0.42f, 0.60f)),
            new Profile("Echo", "DevOps Engineer", new Color(0.40f, 0.78f, 0.36f)),
            new Profile("Sage", "Product Manager", new Color(0.95f, 0.52f, 0.42f)),
            new Profile("Pixel", "Frontend Developer", new Color(0.30f, 0.76f, 0.92f)),
            new Profile("Vega", "Security Reviewer", new Color(0.72f, 0.74f, 0.80f)),
        };

        private readonly List<AgentController> agents = new List<AgentController>();

        public IReadOnlyList<AgentController> Agents => agents;
        public IReadOnlyList<Workstation> Workstations => workstations;
        public AgentController Selected { get; private set; }
        public bool MeetingActive { get; private set; }
        public int MaxAgents => Mathf.Min(profiles.Length, workstations.Length);
        public bool CanAdd => agents.Count < MaxAgents;

        public event Action<AgentController> SelectionChanged;
        public event Action AnyAgentChanged;
        /// <summary>Raised after an agent joins or leaves the team.</summary>
        public event Action RosterChanged;

        private void Start()
        {
            foreach (var ws in workstations) ws.gameObject.SetActive(false);
            int count = Mathf.Clamp(startAgents, 1, MaxAgents);
            for (int i = 0; i < count; i++) Spawn(profiles[i]);
            GameManager.Instance.Layout.ApplyStartLayout();
        }

        // ── Roster ────────────────────────────────────────────────────────────

        /// <summary>Adds an agent (the named profile, or the next free one). Desks re-flow into the layout.</summary>
        public AgentController AddAgent(string profileName = null, bool reflow = true)
        {
            if (!CanAdd) return null;
            Profile profile = null;
            foreach (var p in profiles)
            {
                if (InUse(p.name)) continue;
                if (profileName == null || string.Equals(p.name, profileName, StringComparison.OrdinalIgnoreCase)) { profile = p; break; }
            }
            if (profile == null) return null;
            AgentController agent = Spawn(profile);
            if (agent == null) return null;
            if (reflow) GameManager.Instance.Layout.Reflow();
            RosterChanged?.Invoke();
            AnyAgentChanged?.Invoke();
            return agent;
        }

        /// <summary>Whether the agent can leave right now (not waiting in line or presenting).</summary>
        public bool CanRemove(AgentController agent) =>
            agent != null && agents.Count > 1 && !GameManager.Instance.Queue.IsInLine(agent);

        /// <summary>Removes an agent: unfinished work is dropped, its desk is packed away, the rest re-flow.</summary>
        public bool RemoveAgent(AgentController agent, bool reflow = true)
        {
            if (!CanRemove(agent)) return false;
            if (Selected == agent) Select(null);
            agents.Remove(agent);
            agent.Retire();
            if (reflow) GameManager.Instance.Layout.Reflow();
            RosterChanged?.Invoke();
            AnyAgentChanged?.Invoke();
            return true;
        }

        public bool Contains(AgentController agent) => agent != null && agents.Contains(agent);

        public AgentController Find(string name)
        {
            foreach (var a in agents)
                if (string.Equals(a.Data.profileName, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a.Data.agentName, name, StringComparison.OrdinalIgnoreCase)) return a;
            return null;
        }

        private bool InUse(string profileName)
        {
            foreach (var a in agents) if (a.Data.profileName == profileName) return true;
            return false;
        }

        private AgentController Spawn(Profile profile)
        {
            Workstation ws = null;
            foreach (var w in workstations) if (!w.gameObject.activeSelf) { ws = w; break; }
            if (ws == null) return null;
            ws.gameObject.SetActive(true);
            GameObject go = Instantiate(agentPrefab, ws.SeatPoint.position, ws.SeatPoint.rotation);
            var agent = go.GetComponent<AgentController>();
            agent.Initialize(new AgentData(profile.name, profile.role, profile.color), ws);
            agent.Changed += _ => AnyAgentChanged?.Invoke();
            agents.Add(agent);
            return agent;
        }

        public void Select(AgentController agent)
        {
            if (Selected == agent) return;
            if (Selected != null) Selected.SetSelected(false);
            Selected = agent;
            if (Selected != null) Selected.SetSelected(true);
            SelectionChanged?.Invoke(Selected);
        }

        public int Count(AgentStatus status)
        {
            int n = 0;
            foreach (var a in agents) if (a.Data.status == status) n++;
            return n;
        }

        // ── Group discussion ──────────────────────────────────────────────────

        public void ToggleMeeting()
        {
            if (MeetingActive) EndMeeting();
            else StartMeeting();
        }

        /// <summary>Sends every free, seated agent to the discussion table.</summary>
        public void StartMeeting()
        {
            if (meetingRoom == null || MeetingActive) return;
            int seat = 0;
            foreach (var a in agents)
            {
                if (seat >= meetingRoom.SeatCount) break;
                if (!a.CanTakeTask) continue;
                a.GoToMeeting(meetingRoom.GetSeat(seat++));
            }
            MeetingActive = seat > 0;   // nobody free: no meeting (and demo mode keeps running)
            AnyAgentChanged?.Invoke();
        }

        public void EndMeeting()
        {
            if (!MeetingActive) return;
            MeetingActive = false;
            foreach (var a in agents)
            {
                if (a.Data.activity == AgentActivity.InMeeting || a.Data.activity == AgentActivity.WalkingToMeeting)
                    a.ReturnToDesk();
            }
            AnyAgentChanged?.Invoke();
        }
    }
}
