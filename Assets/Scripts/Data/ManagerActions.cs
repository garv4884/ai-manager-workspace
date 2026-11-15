using System.Collections.Generic;
using UnityEngine;

namespace AIManagerWorkspace
{
    public enum ManagerActionType
    {
        Approve,
        RequestChanges,
        Reject,
        Answer,
        LetAgentDecide,
        ChooseOption,
        Retry,
        Acknowledge,
        FollowUp
    }

    /// <summary>One decision the manager can make on the current request.</summary>
    public struct ManagerAction
    {
        public ManagerActionType type;
        public string label;
        public Color color;
        public int optionIndex;
        /// <summary>Opens the keyboard before acting.</summary>
        public bool needsText;
        /// <summary>Text may be left empty (e.g. optional feedback).</summary>
        public bool textOptional;
        public string textPrompt;
        /// <summary>Fired by thumbs up.</summary>
        public bool isPrimary;
        /// <summary>Fired by thumbs down.</summary>
        public bool isRework;
        /// <summary>Fired by palm push.</summary>
        public bool isDismiss;

        private static readonly Color Green = new Color(0.18f, 0.60f, 0.38f);
        private static readonly Color Amber = new Color(0.80f, 0.52f, 0.16f);
        private static readonly Color Gold = new Color(0.72f, 0.60f, 0.12f);
        private static readonly Color Blue = new Color(0.22f, 0.45f, 0.75f);
        private static readonly Color Muted = new Color(0.42f, 0.25f, 0.27f);

        /// <summary>The actions that make sense for a request; never offers "approve" for a question.</summary>
        public static List<ManagerAction> For(TaskData task, string agentName)
        {
            var list = new List<ManagerAction>();
            if (task == null) return list;

            switch (task.request)
            {
                case RequestKind.Approval:
                    list.Add(new ManagerAction { type = ManagerActionType.Approve, label = task.isTeamPlan ? "APPROVE PLAN" : "APPROVE", color = Green, isPrimary = true });
                    list.Add(new ManagerAction { type = ManagerActionType.RequestChanges, label = "CHANGES", color = Amber, needsText = true, textOptional = true, isRework = true,
                        textPrompt = $"What should {agentName} change? (optional)" });
                    list.Add(new ManagerAction { type = ManagerActionType.Reject, label = "REJECT", color = Muted, isDismiss = true });
                    break;

                case RequestKind.Question:
                    list.Add(new ManagerAction { type = ManagerActionType.Answer, label = "ANSWER", color = Gold, needsText = true,
                        textPrompt = $"{agentName} asks: {task.question}" });
                    list.Add(new ManagerAction { type = ManagerActionType.LetAgentDecide, label = "YOU DECIDE", color = Blue, isPrimary = true });
                    list.Add(new ManagerAction { type = ManagerActionType.Reject, label = "DROP TASK", color = Muted, isDismiss = true });
                    break;

                case RequestKind.Choice:
                    for (int i = 0; i < task.options.Count && i < 3; i++)
                        list.Add(new ManagerAction { type = ManagerActionType.ChooseOption, label = Short(task.options[i]), color = Gold, optionIndex = i });
                    list.Add(new ManagerAction { type = ManagerActionType.Answer, label = "OTHER...", color = Blue, needsText = true,
                        textPrompt = $"{agentName} asks: {task.question}" });
                    list.Add(new ManagerAction { type = ManagerActionType.Reject, label = "DROP TASK", color = Muted, isDismiss = true });
                    break;

                case RequestKind.Error:
                    list.Add(new ManagerAction { type = ManagerActionType.Retry, label = "RETRY", color = Green, isPrimary = true });
                    list.Add(new ManagerAction { type = ManagerActionType.Reject, label = "DROP TASK", color = Muted, isDismiss = true });
                    break;

                case RequestKind.Report:
                    list.Add(new ManagerAction { type = ManagerActionType.Acknowledge, label = "GOT IT", color = Green, isPrimary = true, isDismiss = true });
                    list.Add(new ManagerAction { type = ManagerActionType.FollowUp, label = "FOLLOW UP", color = Blue, needsText = true,
                        textPrompt = $"Follow-up task for {agentName}" });
                    break;
            }
            return list;
        }

        /// <summary>Short gesture/prompt line for the bottom of the page.</summary>
        public static string GestureHint(List<ManagerAction> actions)
        {
            var parts = new List<string>();
            foreach (var a in actions)
            {
                if (a.isPrimary) parts.Add($"Thumbs up: {a.label.ToLowerInvariant()}");
                if (a.isRework) parts.Add($"Thumbs down: {a.label.ToLowerInvariant()}");
                if (a.isDismiss && !a.isPrimary) parts.Add($"Palm out: {a.label.ToLowerInvariant()}");
            }
            return parts.Count > 0 ? string.Join("     ", parts) : "Choose on your desk console";
        }

        private static string Short(string s) => s.Length <= 16 ? s.ToUpperInvariant() : s.Substring(0, 15).ToUpperInvariant() + ".";
    }
}
