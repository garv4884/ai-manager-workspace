using System;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Hint for how the first pass of a task ends. The live AI may still decide to ask
    /// a question; offline simulation follows the hint.
    /// </summary>
    public enum TaskType
    {
        NeedsApproval,
        NeedsTextInput
    }

    public enum TaskExecutionStatus
    {
        Pending,
        InProgress,
        WaitingForUser,
        Completed,
        Cancelled,
        Failed
    }

    [Serializable]
    public class TaskData
    {
        public string taskId;
        public string title;
        public string description;
        public TaskType type = TaskType.NeedsApproval;
        public TaskExecutionStatus status = TaskExecutionStatus.Pending;
        public float progress;

        /// <summary>The question the agent asked (when waiting for input).</summary>
        public string question = "";
        /// <summary>The manager's answer to <see cref="question"/>.</summary>
        public string answer = "";
        /// <summary>Feedback from the latest "request changes".</summary>
        public string feedback = "";
        /// <summary>The deliverable / plan shown on the page.</summary>
        public string output = "";
        public string error = "";
        public int revision = 1;

        /// <summary>Offline-simulation content, filled from the task library.</summary>
        public TaskLibrary.Template template;

        public DateTime createdAt = DateTime.UtcNow;
        public DateTime completedAt;

        public TaskData(string title, string description, TaskType type)
        {
            taskId = Guid.NewGuid().ToString("N").Substring(0, 8);
            this.title = title;
            this.description = description;
            this.type = type;
        }

        public bool HasAskedQuestion => !string.IsNullOrEmpty(question);
    }
}
