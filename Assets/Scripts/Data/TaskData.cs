using System;
using System.Collections.Generic;

namespace AIManagerWorkspace
{
    /// <summary>Hint for how the first pass of a task ends (offline simulation follows it; live AI decides).</summary>
    public enum TaskType
    {
        NeedsApproval,
        NeedsTextInput,
        NeedsChoice
    }

    /// <summary>What an agent brings to the manager. Each kind offers its own set of actions.</summary>
    public enum RequestKind
    {
        None,
        Approval,   // plan or deliverable: approve / request changes / reject
        Question,   // open question: answer / let the agent decide / drop task
        Choice,     // pick one of the agent's options / something else / drop task
        Error,      // agent is blocked: retry / drop task
        Report      // FYI wrap-up: acknowledge / follow up
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

    /// <summary>One line of a team plan: which agent does what.</summary>
    [Serializable]
    public class PlannedTask
    {
        public string agent;
        public string role;
        public string title;
        public string task;
        public string needs;     // "approval" | "question" | "choice"
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

        /// <summary>The request currently waiting on the manager.</summary>
        public RequestKind request = RequestKind.None;
        /// <summary>Question text (Question / Choice).</summary>
        public string question = "";
        /// <summary>Options offered with a Choice request.</summary>
        public List<string> options = new List<string>();
        /// <summary>The manager's answer or chosen option.</summary>
        public string answer = "";
        /// <summary>Feedback from the latest "request changes".</summary>
        public string feedback = "";
        /// <summary>Plan / deliverable / report text.</summary>
        public string output = "";
        public string error = "";
        public int revision = 1;
        public bool askedOnce;

        /// <summary>Team brief this task belongs to (null for standalone tasks).</summary>
        public string planId;
        /// <summary>True for the lead's "split the goal into roles" task.</summary>
        public bool isTeamPlan;
        /// <summary>True for the lead's final wrap-up report.</summary>
        public bool isWrapUp;
        public List<PlannedTask> plan = new List<PlannedTask>();

        /// <summary>Offline-simulation content, filled from the task library.</summary>
        public TaskLibrary.Template template;

        public DateTime createdAt = DateTime.UtcNow;
        public DateTime completedAt;

        /// <summary>For JSON loading (session save files).</summary>
        public TaskData() { }

        public TaskData(string title, string description, TaskType type)
        {
            taskId = Guid.NewGuid().ToString("N").Substring(0, 8);
            this.title = title;
            this.description = description;
            this.type = type;
        }
    }
}
