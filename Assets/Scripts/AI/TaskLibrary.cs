using System.Collections.Generic;
using UnityEngine;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Demo backlog and offline-simulation text. Each template carries a plan (for
    /// approval tasks), a clarifying question (for input tasks), and the follow-up
    /// deliverable the agent produces once it has an answer or feedback.
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
            public string plan;
            public string followUp;

            public Template(string title, string brief, TaskType type, string question, string plan, string followUp)
            {
                this.title = title; this.brief = brief; this.type = type;
                this.question = question; this.plan = plan; this.followUp = followUp;
            }
        }

        private static readonly List<Template> Templates = new List<Template>
        {
            new Template("Refactor auth service",
                "Split the monolithic AuthService into token, session and password modules.",
                TaskType.NeedsApproval, "",
                "PLAN\n1. Extract TokenIssuer (JWT sign/verify) into its own module.\n2. Move session storage behind ISessionStore (Redis + in-memory).\n3. Isolate password hashing (argon2id) in PasswordVault.\n4. Keep AuthService as a thin facade, so callers do not change.\n5. Add 18 unit tests; no API changes.\n\nRisk: low. Estimated diff: +420 / -510 lines.",
                "DONE\nAuthService split into 3 modules behind the old facade.\n18 new tests pass, coverage 91%.\nNo public API changes. Ready to merge."),
            new Template("Design onboarding emails",
                "Draft a 3-email onboarding sequence for new workspace users.",
                TaskType.NeedsTextInput,
                "Who is the main audience for these emails: solo developers or team leads? And what tone do you want: playful or formal?",
                "PLAN\nEmail 1 (day 0): welcome + one 'first win' action.\nEmail 2 (day 2): invite your team.\nEmail 3 (day 5): templates and layouts.",
                "DRAFT READY\nSubject 1: Your workspace is live - assign your first agent\nSubject 2: Bring your team in (takes 30 seconds)\nSubject 3: 6 layouts to run your AI team your way\nEach email is under 120 words with one call to action."),
            new Template("Research VR text legibility",
                "Summarise best practice for readable world-space text on Quest 3.",
                TaskType.NeedsApproval, "",
                "FINDINGS\n- Keep body text at least 1.2 degrees of visual angle (about 14 mm at 0.7 m).\n- Use SDF fonts with a slight outline on bright backgrounds.\n- Put reading panels 0.6 to 1.0 m away and 10 to 20 degrees below eye level.\n- Avoid pure white on black: use #E8ECF2 on #1A1D23.\n\nNext step: apply this to the agent tags and review pages. Approve?",
                "APPLIED\nTags scale with distance and keep a constant visual angle.\nReview page moved to 0.85 m, 15 degrees below eye level.\nContrast is now 12.6:1."),
            new Template("Database schema for tasks",
                "Propose tables for agents, tasks and task history.",
                TaskType.NeedsTextInput,
                "Should task history be kept forever (audit trail) or pruned after 90 days to save space?",
                "PLAN\nagents(id, name, role, status)\ntasks(id, agent_id, title, status, created_at)\ntask_events(id, task_id, kind, payload, at)",
                "SCHEMA v1\nagents(id uuid pk, name, role, status smallint)\ntasks(id uuid pk, agent_id fk, title, status, revision, created_at)\ntask_events(id bigserial, task_id fk, kind, payload jsonb, at)\nIndex on tasks(agent_id, status). The retention policy you chose is applied as a nightly job."),
            new Template("Write unit tests: payments",
                "Cover the refund and partial-capture paths in PaymentModule.",
                TaskType.NeedsApproval, "",
                "TEST PLAN\n- Refund: full, partial, over-refund (expect error)\n- Partial capture: 1 and 3 captures, then void the rest\n- Webhook signature: valid, expired, tampered\n9 tests, about 140 lines. Uses the existing FakeGateway.",
                "RESULT\n9/9 tests passing (212 ms).\nFound one bug: an over-refund was allowed when the currency differed. Fix included (3 lines).\nBranch coverage 94%."),
            new Template("Competitor landscape brief",
                "One-page brief on other spatial productivity apps.",
                TaskType.NeedsTextInput,
                "Which competitors should I focus on? Give me 2 or 3 names, or say 'you pick'.",
                "PLAN\nCompare features, pricing and platform for each competitor, then list where our VR manager view is unique.",
                "BRIEF READY\n- Most competitors are 2D dashboards; none use spatial queues.\n- Pricing clusters at $10-20 per seat.\n- Our edge: physical review flow plus hands-only approval.\nRecommendation: lead the demo with the queue and the review desk."),
            new Template("Fix flaky CI job",
                "The integration test stage fails about 1 time in 8.",
                TaskType.NeedsApproval, "",
                "DIAGNOSIS\nThe failure comes from a race between DB migration and test start.\n\nPLAN\n1. Add a readiness probe (pg_isready) before tests.\n2. Pin the container image digest.\n3. Retry only the network-bound suite once.\n\nExpected: flake rate below 1 in 100.",
                "FIXED\n50 consecutive CI runs passed.\nThe pipeline is also 40 s faster because the image is cached."),
            new Template("Landing page copy",
                "Hero section and three feature blurbs for the product site.",
                TaskType.NeedsTextInput,
                "What is the single sentence you want visitors to remember?",
                "PLAN\nHero headline, subline, three feature cards, and one call to action.",
                "COPY v1\nHero: Manage your AI team like a real team.\nCards: See every agent at a glance / Approve with a thumbs up / Rearrange your office in one tap.\nCall to action: Step into your workspace."),
            new Template("Analyse retention data",
                "Find why week-2 retention dropped in the last cohort.",
                TaskType.NeedsApproval, "",
                "ANALYSIS\nWeek-2 retention fell from 41% to 33%.\nThe drop is concentrated in users who never assigned a second task (68% of churned users).\n\nPROPOSAL\nAdd a day-1 nudge: 'Your agent is free - give it a task'. Run an A/B test for 2 weeks.",
                "EXPERIMENT LIVE\nA/B split 50/50, 2,140 users per arm.\nThe dashboard link is pinned in #growth. First readout in 7 days."),
        };

        public static Template Random()
        {
            return Templates[UnityEngine.Random.Range(0, Templates.Count)];
        }

        public static TaskData CreateRandomTask()
        {
            Template t = Random();
            return new TaskData(t.title, t.brief, t.type) { template = t };
        }

        /// <summary>Offline text for the current stage of a task.</summary>
        public static AIResult Simulate(AgentData agent, TaskData task)
        {
            Template t = task.template;
            bool firstPass = !task.HasAskedQuestion && task.revision == 1;

            if (firstPass && task.type == TaskType.NeedsTextInput)
            {
                string q = t != null && !string.IsNullOrEmpty(t.question)
                    ? t.question
                    : $"Before I start \"{task.title}\": what does a great result look like to you?";
                return AIResult.Question(q);
            }

            if (task.HasAskedQuestion || task.revision > 1)
            {
                string body = t != null && !string.IsNullOrEmpty(t.followUp)
                    ? t.followUp
                    : $"DELIVERABLE\n{task.title} is complete and ready for review.";
                if (!string.IsNullOrEmpty(task.answer))
                    body += $"\n\nBased on your answer: \"{task.answer}\"";
                if (!string.IsNullOrEmpty(task.feedback))
                    body = $"REVISION {task.revision}\nChanges made: \"{task.feedback}\"\n\n" + body;
                else if (task.revision > 1)
                    body = $"REVISION {task.revision}\nReworked the previous version.\n\n" + body;
                return AIResult.Deliverable(body);
            }

            string plan = t != null && !string.IsNullOrEmpty(t.plan)
                ? t.plan
                : $"PLAN for \"{task.title}\"\n1. Break the request into steps.\n2. Draft the first version.\n3. Check it against your brief:\n\"{task.description}\"\n\nShall I proceed?";
            return AIResult.Deliverable(plan);
        }
    }

    /// <summary>Outcome of one unit of agent work.</summary>
    public struct AIResult
    {
        public bool isQuestion;
        public bool failed;
        public string text;

        public static AIResult Question(string q) => new AIResult { isQuestion = true, text = q };
        public static AIResult Deliverable(string d) => new AIResult { text = d };
        public static AIResult Failure(string e) => new AIResult { failed = true, text = e };
    }
}
