using System.Collections.Generic;
using UnityEngine;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Feeds work to the team. Auto mode pulls demo tasks from the backlog for free agents.
    /// The manager can also type a task for a selected agent.
    /// </summary>
    public class TaskDispatcher : MonoBehaviour
    {
        [SerializeField] private bool autoDispatch = true;
        [SerializeField] private Vector2 intervalSeconds = new Vector2(6f, 12f);
        [SerializeField] private float firstTaskDelay = 2f;
        [Tooltip("Auto mode pauses while this many agents are already waiting for the manager.")]
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

            AgentController agent = candidates[Random.Range(0, candidates.Count)];
            agent.AssignTask(TaskLibrary.CreateRandomTask());
        }

        /// <summary>Assigns a task the manager typed. Returns false if the agent is busy.</summary>
        public bool AssignCustom(AgentController agent, string text)
        {
            if (agent == null || string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();
            string[] words = text.Split(' ');
            string title = words.Length <= 5 ? text : string.Join(" ", words, 0, 5) + "...";
            TaskType type = Random.value < 0.35f ? TaskType.NeedsTextInput : TaskType.NeedsApproval;
            return agent.AssignTask(new TaskData(title, text, type));
        }
    }
}
