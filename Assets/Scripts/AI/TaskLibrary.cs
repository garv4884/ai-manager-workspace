using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Demo backlog and offline-simulation text. Used whenever no API key is configured,
    /// so the app stays fully demoable without a network connection.
    /// </summary>
    public static class TaskLibrary
    {
        [System.Serializable]
        public class Template
        {
            public string title;
            public string brief;
            public TaskType type;
            public string question;
            public string[] options;
            public string plan;
            public string followUp;

            public Template(string title, string brief, TaskType type, string question, string[] options, string plan, string followUp)
            {
                this.title = title; this.brief = brief; this.type = type; this.question = question;
                this.options = options; this.plan = plan; this.followUp = followUp;
            }
        }

        private static readonly List<Template> Templates = new List<Template>
        {
            new Template("Refactor auth service",
                "Split the monolithic AuthService into token, session and password modules.",
                TaskType.NeedsApproval, "", null,
                "PLAN\n1. Extract TokenIssuer (JWT sign/verify) into its own module.\n2. Move session storage behind ISessionStore.\n3. Isolate password hashing (argon2id) in PasswordVault.\n4. Keep AuthService as a thin facade so callers do not change.\n5. Add 18 unit tests; no API changes.\n\nRisk: low. Diff: +420 / -510 lines.",
                "DONE\nAuthService split into 3 modules behind the old facade.\n18 new tests pass, coverage 91%.\nNo public API changes. Ready to merge."),
            new Template("Design onboarding emails",
                "Draft a 3-email onboarding sequence for new workspace users.",
                TaskType.NeedsTextInput,
                "Who is the main audience for these emails, and what tone do you want?", null,
                "",
                "DRAFT READY\n1: Your workspace is live - assign your first agent\n2: Bring your team in (takes 30 seconds)\n3: Six layouts to run your AI team your way\nEach email is under 120 words with one call to action."),
            new Template("Research VR text legibility",
                "Summarise best practice for readable world-space text on Quest 3.",
                TaskType.NeedsApproval, "", null,
                "FINDINGS\n- Body text at least 1.2 degrees of visual angle (about 14 mm at 0.7 m).\n- SDF fonts, slight outline on bright backgrounds.\n- Reading panels 0.6-1.0 m away, 10-20 degrees below eye level.\n- Avoid pure white on black.\n\nNext: apply this to agent tags and review pages. Approve?",
                "APPLIED\nTags keep a constant visual angle.\nReview page at 0.85 m, 15 degrees below eye level.\nContrast is now 12.6:1."),
            new Template("Database schema for tasks",
                "Propose tables for agents, tasks and task history.",
                TaskType.NeedsChoice,
                "How long should task history be kept?",
                new[] { "Forever (audit trail)", "Prune after 90 days", "Prune after 1 year" },
                "",
                "SCHEMA v1\nagents(id, name, role, status)\ntasks(id, agent_id, title, status, revision, created_at)\ntask_events(id, task_id, kind, payload jsonb, at)\nIndex on tasks(agent_id, status). Retention policy applied as a nightly job."),
            new Template("Write unit tests: payments",
                "Cover the refund and partial-capture paths in PaymentModule.",
                TaskType.NeedsApproval, "", null,
                "TEST PLAN\n- Refund: full, partial, over-refund (expect error)\n- Partial capture: 1 and 3 captures, then void the rest\n- Webhook signature: valid, expired, tampered\n9 tests, about 140 lines, using the existing FakeGateway.",
                "RESULT\n9/9 tests passing (212 ms).\nFound one bug: over-refund allowed when currency differed. Fix included.\nBranch coverage 94%."),
            new Template("Competitor landscape brief",
                "One-page brief on other spatial productivity apps.",
                TaskType.NeedsTextInput,
                "Which competitors should I focus on? Name 2 or 3, or tap 'You decide'.", null,
                "",
                "BRIEF READY\n- Most competitors are 2D dashboards; none use spatial queues.\n- Pricing clusters at $10-20 per seat.\n- Our edge: physical review flow plus hands-only approval."),
            new Template("Fix flaky CI job",
                "The integration test stage fails about 1 time in 8.",
                TaskType.NeedsApproval, "", null,
                "DIAGNOSIS\nRace between DB migration and test start.\n\nPLAN\n1. Add a readiness probe before tests.\n2. Pin the container image digest.\n3. Retry only the network-bound suite once.\n\nExpected flake rate: below 1 in 100.",
                "FIXED\n50 consecutive CI runs passed.\nPipeline is 40 s faster thanks to image caching."),
            new Template("Landing page hero",
                "Hero section and three feature blurbs for the product site.",
                TaskType.NeedsChoice,
                "Which headline direction should I develop?",
                new[] { "Manage AI like a team", "Your AI office in VR", "Approve with a thumbs up" },
                "",
                "COPY v1\nHero headline and subline written in the direction you chose.\nCards: See every agent at a glance / Approve with a gesture / Rearrange your office in one tap.\nCall to action: Step into your workspace."),
            new Template("Analyse retention data",
                "Find why week-2 retention dropped in the last cohort.",
                TaskType.NeedsApproval, "", null,
                "ANALYSIS\nWeek-2 retention fell from 41% to 33%.\n68% of churned users never assigned a second task.\n\nPROPOSAL\nAdd a day-1 nudge: 'Your agent is free - give it a task'. A/B test for 2 weeks.",
                "EXPERIMENT LIVE\nA/B split 50/50, 2,140 users per arm.\nFirst readout in 7 days."),
        };

        public static TaskData CreateRandomTask()
        {
            Template t = Templates[Random.Range(0, Templates.Count)];
            return new TaskData(t.title, t.brief, t.type) { template = t };
        }

        // ── Offline work simulation ───────────────────────────────────────────

        public static AIResult Simulate(AgentData agent, TaskData task)
        {
            if (task.isTeamPlan) return AIResult.Deliverable(PlanText(task));
            if (task.isWrapUp) return AIResult.Report(task.output);

            Template t = task.template;
            if (t != null && string.IsNullOrEmpty(t.title)) t = null;   // empty after a JSON round-trip
            bool firstPass = !task.askedOnce && task.revision == 1;

            if (firstPass && task.type == TaskType.NeedsChoice)
            {
                string[] opts = t?.options != null && t.options.Length >= 2 ? t.options : RoleOptions(agent.agentRole);
                string q = t != null && !string.IsNullOrEmpty(t.question) ? t.question : $"Which direction should I take for \"{task.title}\"?";
                return AIResult.Choice(q, opts);
            }
            if (firstPass && task.type == TaskType.NeedsTextInput)
            {
                string q = t != null && !string.IsNullOrEmpty(t.question)
                    ? t.question
                    : $"Before I start \"{task.title}\": what does a great result look like to you?";
                return AIResult.Question(q);
            }

            bool finalPass = task.askedOnce || task.revision > 1;
            string body;
            if (finalPass)
                body = t != null && !string.IsNullOrEmpty(t.followUp) ? t.followUp : RoleDeliverable(agent, task);
            else
                body = t != null && !string.IsNullOrEmpty(t.plan) ? t.plan : RolePlan(agent, task);

            if (!string.IsNullOrEmpty(task.answer)) body += $"\n\nUsing your direction: \"{task.answer}\"";
            if (task.revision > 1)
                body = $"REVISION {task.revision}\n" + (string.IsNullOrEmpty(task.feedback) ? "Reworked the previous version.\n\n" : $"Changes made: \"{task.feedback}\"\n\n") + body;
            return AIResult.Deliverable(body);
        }

        private static string RolePlan(AgentData agent, TaskData task) =>
            $"PLAN ({agent.agentRole})\n1. Break \"{task.title}\" into steps.\n2. Draft a first version.\n3. Check it against your brief:\n\"{Trim(task.description, 140)}\"\n\nShall I proceed?";

        private static string RoleDeliverable(AgentData agent, TaskData task) =>
            $"DELIVERABLE\n\"{task.title}\" is complete.\n{agent.agentName} ({agent.agentRole}) checked it against the brief and it is ready for review.";

        private static string[] RoleOptions(string role)
        {
            string r = role.ToLowerInvariant();
            if (r.Contains("design")) return new[] { "Minimal and clean", "Bold and playful", "Corporate" };
            if (r.Contains("copy") || r.Contains("writ")) return new[] { "Friendly", "Professional", "Punchy" };
            if (r.Contains("test") || r.Contains("qa")) return new[] { "Unit tests first", "End-to-end first", "Both, lighter" };
            if (r.Contains("data") || r.Contains("analy")) return new[] { "Last 30 days", "Last quarter", "All time" };
            return new[] { "Fast and simple", "Thorough", "Balanced" };
        }

        // ── Team briefs ───────────────────────────────────────────────────────

        private static readonly (string role, string verb, string needs)[] RoleTemplates =
        {
            ("Researcher", "Research users, competitors and constraints for", "question"),
            ("Designer", "Design the experience and visuals for", "choice"),
            ("Code Writer", "Build a working prototype of", "approval"),
            ("Copywriter", "Write the messaging and copy for", "choice"),
            ("Data Analyst", "Define success metrics and tracking for", "approval"),
            ("QA Tester", "Write and run a test plan for", "approval"),
            ("Project Lead", "Coordinate milestones and risks for", "approval"),
        };

        /// <summary>Splits a goal across the team, keeping each agent's existing role when it fits.</summary>
        public static List<PlannedTask> OfflinePlan(string goal, IReadOnlyList<AgentController> agents)
        {
            var plan = new List<PlannedTask>();
            var used = new HashSet<int>();
            foreach (var a in agents)
            {
                int pick = -1;
                for (int i = 0; i < RoleTemplates.Length; i++)
                    if (!used.Contains(i) && RoleTemplates[i].role == a.Data.agentRole) { pick = i; break; }
                if (pick < 0)
                    for (int i = 0; i < RoleTemplates.Length; i++)
                        if (!used.Contains(i)) { pick = i; break; }
                if (pick < 0) pick = plan.Count % RoleTemplates.Length;
                used.Add(pick);

                var rt = RoleTemplates[pick];
                plan.Add(new PlannedTask
                {
                    agent = a.Data.agentName,
                    role = rt.role,
                    title = $"{rt.role}: {Trim(goal, 28)}",
                    task = $"{rt.verb} \"{goal}\".",
                    needs = rt.needs
                });
            }
            return plan;
        }

        public static string PlanText(TaskData task)
        {
            var sb = new StringBuilder();
            sb.Append("TEAM PLAN\nGoal: ").Append(Trim(task.description, 120)).Append("\n\n");
            foreach (var p in task.plan)
                sb.Append("- ").Append(p.agent).Append(" (").Append(p.role).Append("): ").Append(Trim(p.task, 70)).Append('\n');
            if (task.revision > 1 && !string.IsNullOrEmpty(task.feedback))
                sb.Append("\nAdjusted for: \"").Append(task.feedback).Append('"');
            sb.Append("\nApprove to hand out the work.");
            return sb.ToString();
        }

        public static string WrapUpText(string goal, List<TaskData> finished)
        {
            var sb = new StringBuilder();
            sb.Append("TEAM GOAL COMPLETE\n").Append(Trim(goal, 120)).Append("\n\n");
            foreach (var t in finished)
                sb.Append(t.status == TaskExecutionStatus.Completed ? "Done: " : "Dropped: ").Append(t.title).Append('\n');
            sb.Append("\nAll deliverables are in your done tray.");
            return sb.ToString();
        }

        public static TaskType TypeFor(string needs) => needs switch
        {
            "question" => TaskType.NeedsTextInput,
            "choice" => TaskType.NeedsChoice,
            _ => TaskType.NeedsApproval
        };

        public static string Trim(string s, int max) => string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max - 3) + "...";
    }

    /// <summary>Outcome of one unit of agent work.</summary>
    public struct AIResult
    {
        public RequestKind kind;
        public string text;
        public string[] options;

        public bool Failed => kind == RequestKind.Error;

        public static AIResult Question(string q) => new AIResult { kind = RequestKind.Question, text = q };
        public static AIResult Choice(string q, string[] options) => new AIResult { kind = RequestKind.Choice, text = q, options = options };
        public static AIResult Deliverable(string d) => new AIResult { kind = RequestKind.Approval, text = d };
        public static AIResult Report(string d) => new AIResult { kind = RequestKind.Report, text = d };
        public static AIResult Failure(string e) => new AIResult { kind = RequestKind.Error, text = e };
    }
}
