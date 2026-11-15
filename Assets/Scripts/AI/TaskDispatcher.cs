using System.Collections.Generic;
using UnityEngine;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Feeds work to the team. Demo mode pulls tasks from the built-in backlog for idle agents
    /// (good for pitches). The manager can also type a task for one agent, or brief the whole
    /// team through <see cref="TeamPlanner"/>; doing so switches demo mode off.
    /// </summary>
    public class TaskDispatcher : MonoBehaviour
    {
        [SerializeField] private bool autoDispatch = true;
        [SerializeField] private Vector2 intervalSeconds = new Vector2(6f, 12f);
        [SerializeField] private float firstTaskDelay = 2f;
        [Tooltip("Demo mode pauses while this many agents are already waiting for the manager.")]
        [SerializeField] private int maxPendingReviews = 4;

        private float nextTime;
        private readonly List<AgentController> candidates = new List<AgentController>();

        public bool AutoDispatch
        {
            get => autoDispatch;
            set => autoDispatch = value;
        }

        private void Start() => nextTime = Time.time + firstTaskDelay;

        private void Update()
        {
            if (!autoDispatch || Time.time < nextTime) return;
            nextTime = Time.time + Random.Range(intervalSeconds.x, intervalSeconds.y);

            GameManager gm = GameManager.Instance;
            if (gm.Queue.PendingCount >= maxPendingReviews || gm.Agents.MeetingActive) return;

            candidates.Clear();
            foreach (var a in gm.Agents.Agents) if (a.CanTakeTask) candidates.Add(a);
            if (candidates.Count == 0) return;

            candidates[Random.Range(0, candidates.Count)].AssignTask(TaskLibrary.CreateRandomTask());
        }

        /// <summary>Builds a task from text the manager typed.</summary>
        public TaskData CreateCustomTask(string text)
        {
            text = text.Trim();
            string[] words = text.Split(' ');
            string title = words.Length <= 5 ? text : string.Join(" ", words, 0, 5) + "...";
            if (title.Length > 0) title = char.ToUpperInvariant(title[0]) + title.Substring(1);
            float r = Random.value;
            TaskType type = r < 0.25f ? TaskType.NeedsTextInput : r < 0.45f ? TaskType.NeedsChoice : TaskType.NeedsApproval;
            return new TaskData(title, text, type);
        }

        /// <summary>Gives the agent a typed task (queued if the agent is busy).</summary>
        public void AssignCustom(AgentController agent, string text)
        {
            if (agent == null || string.IsNullOrWhiteSpace(text)) return;
            agent.AssignTask(CreateCustomTask(text));
        }
    }
}
