using System;
using System.Collections.Generic;
using UnityEngine;

namespace AIManagerWorkspace
{
    /// <summary>
    /// "Brief the team": the manager states one goal; a lead agent proposes a plan that gives
    /// every agent a role and a task (presented for approval like any other work). On approval
    /// the tasks are handed out. When every task is done or dropped, the lead brings back a
    /// wrap-up report.
    /// </summary>
    public class TeamPlanner : MonoBehaviour
    {
        public class Brief
        {
            public string id;
            public string goal;
            public AgentController lead;
            public readonly List<TaskData> tasks = new List<TaskData>();
            public bool executed;
            public bool wrappedUp;

            public int Finished
            {
                get
                {
                    int n = 0;
                    foreach (var t in tasks)
                        if (t.status == TaskExecutionStatus.Completed || t.status == TaskExecutionStatus.Cancelled) n++;
                    return n;
                }
            }
        }

        private readonly List<Brief> briefs = new List<Brief>();

        public IReadOnlyList<Brief> Briefs => briefs;

        public Brief Active => briefs.Count > 0 && !briefs[briefs.Count - 1].wrappedUp ? briefs[briefs.Count - 1] : null;
        public event Action Changed;

        /// <summary>Starts a team brief. Demo mode is switched off so only the manager's work runs.</summary>
        public void StartBrief(string goal)
        {
            if (string.IsNullOrWhiteSpace(goal)) return;
            GameManager gm = GameManager.Instance;
            gm.Dispatcher.AutoDispatch = false;

            AgentController lead = PickLead(gm.Agents.Agents);
            var brief = new Brief { id = Guid.NewGuid().ToString("N").Substring(0, 8), goal = goal.Trim(), lead = lead };
            briefs.Add(brief);

            var planTask = new TaskData($"Team plan: {TaskLibrary.Trim(brief.goal, 30)}", brief.goal, TaskType.NeedsApproval)
            {
                isTeamPlan = true,
                planId = brief.id
            };
            lead.AssignTask(planTask);
            Changed?.Invoke();
        }

        /// <summary>Called when the manager approves a plan: hands out one task per agent.</summary>
        public void Execute(TaskData planTask)
        {
            Brief brief = Find(planTask.planId);
            if (brief == null || brief.executed) return;
            brief.executed = true;

            var agents = GameManager.Instance.Agents.Agents;
            var used = new HashSet<AgentController>();
            foreach (PlannedTask p in planTask.plan)
            {
                AgentController agent = null;
                foreach (var a in agents)
                    if (!used.Contains(a) && string.Equals(a.Data.agentName, p.agent, StringComparison.OrdinalIgnoreCase)) { agent = a; break; }
                if (agent == null)
                    foreach (var a in agents) if (!used.Contains(a)) { agent = a; break; }
                if (agent == null) break;
                used.Add(agent);

                if (!string.IsNullOrWhiteSpace(p.role)) agent.SetRole(p.role);
                var task = new TaskData(string.IsNullOrWhiteSpace(p.title) ? p.role : p.title, p.task, TaskLibrary.TypeFor(p.needs))
                {
                    planId = brief.id
                };
                brief.tasks.Add(task);
                agent.AssignTask(task);
            }
            Changed?.Invoke();
        }

        /// <summary>Called whenever a task ends (approved, acknowledged or dropped).</summary>
        public void OnTaskFinished(TaskData task)
        {
            if (task == null || string.IsNullOrEmpty(task.planId) || task.isTeamPlan) return;
            Brief brief = Find(task.planId);
            if (brief == null) return;

            if (task.isWrapUp) { brief.wrappedUp = true; Changed?.Invoke(); return; }

            Changed?.Invoke();
            if (!brief.executed || brief.Finished < brief.tasks.Count) return;

            var wrap = new TaskData($"Wrap-up: {TaskLibrary.Trim(brief.goal, 30)}", brief.goal, TaskType.NeedsApproval)
            {
                isWrapUp = true,
                planId = brief.id,
                output = TaskLibrary.WrapUpText(brief.goal, brief.tasks)
            };
            if (brief.lead == null || !GameManager.Instance.Agents.Contains(brief.lead))
                brief.lead = PickLead(GameManager.Instance.Agents.Agents);
            if (brief.lead == null) { brief.wrappedUp = true; Changed?.Invoke(); return; }
            brief.lead.AssignTask(wrap);
        }

        /// <summary>Ends a brief early (its plan was dropped or its lead left before planning).</summary>
        public void CloseBrief(string planId)
        {
            Brief brief = Find(planId);
            if (brief == null) return;
            brief.wrappedUp = true;
            Changed?.Invoke();
        }

        /// <summary>Tasks handed out for a brief (used for the wrap-up report).</summary>
        public IReadOnlyList<TaskData> TasksOf(string planId) => Find(planId)?.tasks;

        public string GoalOf(string planId) => Find(planId)?.goal;

        /// <summary>Re-creates a brief from a saved session.</summary>
        public void Restore(string id, string goal, AgentController lead, bool executed, bool wrappedUp, List<TaskData> tasks)
        {
            if (string.IsNullOrEmpty(id) || Find(id) != null) return;
            if (lead == null) lead = PickLead(GameManager.Instance.Agents.Agents);
            var b = new Brief { id = id, goal = goal, lead = lead, executed = executed, wrappedUp = wrappedUp };
            if (tasks != null) b.tasks.AddRange(tasks);
            briefs.Add(b);
            Changed?.Invoke();
        }

        private Brief Find(string id)
        {
            foreach (var b in briefs) if (b.id == id) return b;
            return null;
        }

        private static AgentController PickLead(IReadOnlyList<AgentController> agents)
        {
            AgentController best = null;
            int bestLoad = int.MaxValue;
            foreach (var a in agents)
            {
                int load = a.BacklogCount + (a.CurrentTask != null ? 1 : 0);
                if (a.CanTakeTask) load = -1;
                if (a.Data.IsLinked) load += 1000;
                if (load < bestLoad) { bestLoad = load; best = a; }
            }
            return best;
        }
    }
}
