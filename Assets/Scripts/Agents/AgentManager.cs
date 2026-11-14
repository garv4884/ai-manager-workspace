using System;
using System.Collections.Generic;
using UnityEngine;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Spawns one agent per workstation, tracks the selected agent, and runs group
    /// discussions in the meeting room.
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
        [SerializeField] private Profile[] profiles =
        {
            new Profile("Claude", "Code Writer", new Color(0.86f, 0.50f, 0.34f)),
            new Profile("Nova", "Researcher", new Color(0.22f, 0.66f, 0.60f)),
            new Profile("Iris", "Designer", new Color(0.36f, 0.52f, 0.95f)),
            new Profile("Atlas", "Data Analyst", new Color(0.60f, 0.42f, 0.86f)),
            new Profile("Quill", "Copywriter", new Color(0.93f, 0.70f, 0.22f)),
            new Profile("Probe", "QA Tester", new Color(0.90f, 0.42f, 0.60f)),
        };

        private readonly List<AgentController> agents = new List<AgentController>();

        public IReadOnlyList<AgentController> Agents => agents;
        public IReadOnlyList<Workstation> Workstations => workstations;
        public AgentController Selected { get; private set; }
        public bool MeetingActive { get; private set; }

        public event Action<AgentController> SelectionChanged;
        public event Action AnyAgentChanged;

        private void Start()
        {
            int count = Mathf.Min(profiles.Length, workstations.Length);
            for (int i = 0; i < count; i++)
            {
                Workstation ws = workstations[i];
                GameObject go = Instantiate(agentPrefab, ws.SeatPoint.position, ws.SeatPoint.rotation);
                var agent = go.GetComponent<AgentController>();
                agent.Initialize(new AgentData(profiles[i].name, profiles[i].role, profiles[i].color), ws);
                agent.Changed += _ => AnyAgentChanged?.Invoke();
                agents.Add(agent);
            }
            for (int i = count; i < workstations.Length; i++) workstations[i].gameObject.SetActive(false);
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
            MeetingActive = true;
            int seat = 0;
            foreach (var a in agents)
            {
                if (seat >= meetingRoom.SeatCount) break;
                if (!a.CanTakeTask) continue;
                a.GoToMeeting(meetingRoom.GetSeat(seat++));
            }
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
