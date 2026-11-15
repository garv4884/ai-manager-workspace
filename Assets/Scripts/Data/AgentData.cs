using System;
using UnityEngine;

namespace AIManagerWorkspace
{
    /// <summary>
    /// What the agent needs from the manager. This is what the head tag shows,
    /// and it does not change while the agent walks around or waits in line.
    /// </summary>
    public enum AgentStatus
    {
        Free,
        Working,
        WaitingForApproval,
        WaitingForInput,
        Error,
        HasUpdate
    }

    /// <summary>
    /// Where the agent physically is, kept apart from <see cref="AgentStatus"/> so that
    /// walking to the queue never hides "waiting for your input".
    /// </summary>
    public enum AgentActivity
    {
        AtDesk,
        WalkingToDesk,
        WalkingToQueue,
        InQueue,
        Presenting,
        InMeeting,
        WalkingToMeeting
    }

    [Serializable]
    public class AgentData
    {
        public string agentId;
        public string agentName;
        public string agentRole;
        public Color accentColor = Color.white;
        public AgentStatus status = AgentStatus.Free;
        public AgentActivity activity = AgentActivity.AtDesk;
        public int queuePosition = -1;
        public int completedTasks;

        public AgentData(string name, string role, Color accent)
        {
            agentId = Guid.NewGuid().ToString("N");
            agentName = name;
            agentRole = role;
            accentColor = accent;
        }

        public bool NeedsManager =>
            status == AgentStatus.WaitingForApproval ||
            status == AgentStatus.WaitingForInput ||
            status == AgentStatus.Error ||
            status == AgentStatus.HasUpdate;

        public Color StatusColor => ColorFor(status);

        public static Color ColorFor(AgentStatus s) => s switch
        {
            AgentStatus.Free => new Color(0.30f, 0.82f, 0.45f),
            AgentStatus.Working => new Color(0.30f, 0.62f, 1.00f),
            AgentStatus.WaitingForApproval => new Color(1.00f, 0.62f, 0.15f),
            AgentStatus.WaitingForInput => new Color(1.00f, 0.86f, 0.20f),
            AgentStatus.Error => new Color(0.95f, 0.28f, 0.28f),
            AgentStatus.HasUpdate => new Color(0.31f, 0.82f, 0.77f),
            _ => Color.white
        };

        public string StatusLabel => status switch
        {
            AgentStatus.Free => "FREE",
            AgentStatus.Working => "WORKING",
            AgentStatus.WaitingForApproval => "NEEDS APPROVAL",
            AgentStatus.WaitingForInput => "NEEDS INPUT",
            AgentStatus.Error => "BLOCKED",
            AgentStatus.HasUpdate => "HAS UPDATE",
            _ => "?"
        };

        public string ActivityLabel => activity switch
        {
            AgentActivity.AtDesk => status == AgentStatus.Free ? "At desk" : "",
            AgentActivity.WalkingToDesk => "Returning to desk",
            AgentActivity.WalkingToQueue => "Walking to you",
            AgentActivity.InQueue => queuePosition >= 0 ? $"#{queuePosition + 1} in line" : "In line",
            AgentActivity.Presenting => "Presenting to you",
            AgentActivity.InMeeting => "In group discussion",
            AgentActivity.WalkingToMeeting => "Heading to discussion",
            _ => ""
        };
    }
}
