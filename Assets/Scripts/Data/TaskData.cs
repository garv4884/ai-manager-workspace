using System;
using UnityEngine;

namespace AIManagerWorkspace
{
    public enum TaskType
    {
        NeedsApproval,
        NeedsTextInput,
        Autonomous
    }

    public enum TaskExecutionStatus
    {
        Pending,
        InProgress,
        WaitingForUser,
        Completed,
        Rejected
    }

    [System.Serializable]
    public class TaskData
    {
        public string taskId;
        public string title;
        [TextArea(2, 4)]
        public string description;
        [TextArea(2, 4)]
        public string userInput;
        [TextArea(3, 8)]
        public string outputResult;
        public TaskType type = TaskType.NeedsApproval;
        public TaskExecutionStatus status = TaskExecutionStatus.Pending;
        [Range(0f, 1f)]
        public float progress = 0f;
        public string assignedAgentId;
        public DateTime createdAt;
        public DateTime completedAt;

        public TaskData()
        {
            taskId = Guid.NewGuid().ToString("N")[..8];
            status = TaskExecutionStatus.Pending;
            progress = 0f;
            createdAt = DateTime.UtcNow;
            userInput = string.Empty;
            outputResult = string.Empty;
        }

        public TaskData(string title, string description, TaskType type)
        {
            taskId = Guid.NewGuid().ToString("N")[..8];
            this.title = title;
            this.description = description;
            this.type = type;
            this.status = TaskExecutionStatus.Pending;
            this.progress = 0f;
            this.createdAt = DateTime.UtcNow;
            this.userInput = string.Empty;
            this.outputResult = string.Empty;
        }
    }
}
