using System;
using UnityEngine;

namespace AIManagerWorkspace
{
    public enum AgentStatus
    {
        Free,
        Working,
        WaitingForApproval,
        WaitingForInput,
        Walking,
        InQueue,
        Presenting,
        Error
    }

    [System.Serializable]
    public class AgentData
    {
        public string agentId;
        public string agentName;
        public string agentRole;
        public AgentStatus currentStatus = AgentStatus.Free;
        [TextArea(2, 5)]
        public string currentTaskDescription;
        [TextArea(3, 8)]
        public string currentOutput;
        public int deskIndex = -1;

        public AgentData()
        {
            agentId = System.Guid.NewGuid().ToString();
            currentStatus = AgentStatus.Free;
            currentTaskDescription = "Awaiting assignment";
            currentOutput = string.Empty;
        }

        public AgentData(string id, string name, string role, int desk = -1)
        {
            agentId = id;
            agentName = name;
            agentRole = role;
            currentStatus = AgentStatus.Free;
            currentTaskDescription = "Awaiting assignment";
            currentOutput = string.Empty;
            deskIndex = desk;
        }

        public Color GetStatusColor()
        {
            return currentStatus switch
            {
                AgentStatus.Free => new Color(0.2f, 0.85f, 0.3f),          // Bright Green
                AgentStatus.Working => new Color(0.2f, 0.6f, 1.0f),       // Vibrant Blue
                AgentStatus.WaitingForApproval => new Color(1.0f, 0.65f, 0.0f), // Orange
                AgentStatus.WaitingForInput => new Color(0.95f, 0.9f, 0.15f),  // Yellow
                AgentStatus.Walking => new Color(0.7f, 0.4f, 0.9f),       // Purple
                AgentStatus.InQueue => new Color(0.9f, 0.5f, 0.8f),       // Magenta
                AgentStatus.Presenting => new Color(0.1f, 1.0f, 0.9f),    // Cyan
                AgentStatus.Error => new Color(0.95f, 0.2f, 0.2f),         // Red
                _ => Color.white
            };
        }

        public string GetStatusDisplayString()
        {
            return currentStatus switch
            {
                AgentStatus.Free => "Free",
                AgentStatus.Working => "Working...",
                AgentStatus.WaitingForApproval => "Waiting for Approval",
                AgentStatus.WaitingForInput => "Waiting for Input",
                AgentStatus.Walking => "On the Move",
                AgentStatus.InQueue => "In Queue",
                AgentStatus.Presenting => "Reviewing with Manager",
                AgentStatus.Error => "Blocked / Error",
                _ => "Unknown"
            };
        }
    }
}
