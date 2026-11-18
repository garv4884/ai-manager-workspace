using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AIManagerWorkspace
{
    /// <summary>Desk card for the agent selected by pointing and pinching.</summary>
    public class AgentDetailPanel : MonoBehaviour
    {
        [SerializeField] private Image accentBar;
        [SerializeField] private Image statusPill;
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private TextMeshProUGUI bodyText;
        [SerializeField] private Button assignButton;
        [SerializeField] private TextMeshProUGUI assignLabel;
        [SerializeField] private Button roleButton;
        [SerializeField] private Button nextButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private Button removeButton;
        [SerializeField] private TextMeshProUGUI removeLabel;
        [SerializeField] private Button brainButton;
        [SerializeField] private TextMeshProUGUI brainLabel;
        private GameObject brainPopup;
        [Header("Agent picker")]
        [SerializeField] private Button pickerButton;
        [SerializeField] private TextMeshProUGUI pickerLabel;
        [SerializeField] private RectTransform pickerList;

        private readonly System.Collections.Generic.List<(Button button, TextMeshProUGUI label, AgentController agent)> rows =
            new System.Collections.Generic.List<(Button, TextMeshProUGUI, AgentController)>();

        private float removeArmedUntil;
        private string notice = "";

        private AgentController agent;

        private void Start()
        {
            GameManager gm = GameManager.Instance;
            gm.Agents.SelectionChanged += Show;
            gm.Agents.AnyAgentChanged += Refresh;
            gm.Queue.Changed += Refresh;
            assignButton.onClick.AddListener(() => gm.OpenAssignTaskKeyboard(agent));
            roleButton.onClick.AddListener(() => gm.OpenRoleKeyboard(agent));
            nextButton.onClick.AddListener(() => { gm.Queue.PromoteToFront(agent); Refresh(); });
            closeButton.onClick.AddListener(() => gm.Agents.Select(null));
            removeButton.onClick.AddListener(OnRemove);
            if (brainButton != null) brainButton.onClick.AddListener(OpenBrainPopup);
            gm.AI.ConfigChanged += Refresh;
            pickerButton.onClick.AddListener(() => SetPickerOpen(!pickerList.gameObject.activeSelf));
            gm.Agents.RosterChanged += () => { if (pickerList.gameObject.activeSelf) BuildRows(); };
            pickerList.gameObject.SetActive(false);
            Show(null);
        }

        // ── Brain: which AI provider this agent thinks with ───────────────────

        private void OpenBrainPopup()
        {
            if (brainPopup != null) { Destroy(brainPopup); brainPopup = null; return; }
            if (agent == null) return;
            GameManager gm = GameManager.Instance;
            var ids = new System.Collections.Generic.List<string> { "", AIServiceManager.Simulation };
            foreach (var id in AIServiceManager.Providers) if (gm.AI.Usable(id)) ids.Add(id);
            var names = ids.ConvertAll(id => id == "" ? $"Team default ({AIServiceManager.DisplayName(gm.AI.IsLive ? gm.AI.DefaultBackend : AIServiceManager.Simulation)})"
                                                     : id == AIServiceManager.Simulation ? "Simulation (demo)" : AIServiceManager.DisplayName(id));
            int current = Mathf.Max(0, ids.IndexOf(agent.Data.backend ?? ""));
            var rt = (RectTransform)brainButton.transform;
            var parent = (RectTransform)rt.parent;
            float total = names.Count * 48f - 4f + 16f;
            Vector2 centre = (Vector2)rt.localPosition - parent.rect.center;
            Vector2 top = centre + new Vector2(0f, rt.rect.height * 0.5f + total + 6f);   // opens upwards
            AgentController target = agent;
            brainPopup = UiKit.Popup(rt.parent, top, Mathf.Max(260f, rt.rect.width * 2.4f), names.ToArray(), current, i =>
            {
                brainPopup = null;
                target.Data.backend = ids[i];
                target.NotifyChanged();
                Refresh();
            });
        }

        // ── Agent picker (dropdown list of the whole team) ─────────────────────

        private void SetPickerOpen(bool open)
        {
            pickerList.gameObject.SetActive(open);
            if (open) BuildRows();
            Refresh();
        }

        private void BuildRows()
        {
            foreach (Transform c in pickerList) Destroy(c.gameObject);
            rows.Clear();
            GameManager gm = GameManager.Instance;
            var team = gm.Agents.Agents;
            float h = 42f, gap = 4f, w = pickerList.rect.size.x - 24f;
            float total = (team.Count + 1) * (h + gap) + 16f;
            UiKit.Box(pickerList, new Vector2(0f, pickerList.rect.size.y * 0.5f - total * 0.5f), new Vector2(pickerList.rect.size.x, total),
                new Color(0.05f, 0.055f, 0.065f, 0.98f), 2.4f, "ListBack").raycastTarget = true;
            float y = pickerList.rect.size.y * 0.5f - 8f - h * 0.5f;
            foreach (var a in team)
            {
                AgentController captured = a;
                var b = UiKit.Button(pickerList, "", new Vector2(0f, y), new Vector2(w, h), UiKit.Control, () =>
                {
                    gm.Agents.Select(captured);
                    SetPickerOpen(false);
                }, out var label, 16f);
                label.alignment = TextAlignmentOptions.Left;
                label.rectTransform.sizeDelta = new Vector2(w - 24f, h);
                rows.Add((b, label, a));
                y -= h + gap;
            }
            if (gm.Agents.CanAdd)
            {
                UiKit.Button(pickerList, "+  Add agent", new Vector2(0f, y), new Vector2(w, h), UiKit.Primary, () =>
                {
                    var added = gm.Agents.AddAgent();
                    if (added != null) gm.Agents.Select(added);
                    SetPickerOpen(false);
                }, out _, 16f);
            }
            RefreshRows();
        }

        private void RefreshRows()
        {
            foreach (var (button, label, a) in rows)
            {
                if (a == null) continue;
                AgentData d = a.Data;
                string colour = ColorUtility.ToHtmlStringRGB(d.accentColor);
                string status = ColorUtility.ToHtmlStringRGB(d.StatusColor);
                label.text = $"<color=#{colour}>|</color>  <b>{d.agentName}</b>  <size=80%><color=#9AA3B2>{d.agentRole}</color></size>" +
                             $"<pos=72%><size=80%><color=#{status}>{d.StatusLabel}</color></size>";
                UiKit.Tint(button, a == agent ? UiKit.Raised : UiKit.Control);
            }
        }

        private void Show(AgentController a)
        {
            agent = a;
            removeArmedUntil = 0f;
            notice = "";
            Refresh();
        }

        private void OnRemove()
        {
            AgentManager team = GameManager.Instance.Agents;
            if (agent == null) return;
            if (!team.CanRemove(agent))
            {
                notice = team.Agents.Count <= 1 ? "The team needs at least one agent." : "Finish reviewing this agent's request first.";
                Refresh();
                return;
            }
            if (Time.unscaledTime > removeArmedUntil) { removeArmedUntil = Time.unscaledTime + 4f; Refresh(); return; }
            team.RemoveAgent(agent);
        }

        private void Refresh()
        {
            bool has = agent != null;
            if (pickerLabel != null)
                pickerLabel.text = has ? $"{agent.Data.agentName}  <size=75%><color=#9AA3B2>{agent.Data.agentRole}</color></size>" : "Choose an agent";
            if (pickerList != null && pickerList.gameObject.activeSelf) RefreshRows();
            assignButton.gameObject.SetActive(has);
            roleButton.gameObject.SetActive(has);
            closeButton.gameObject.SetActive(has);
            removeButton.gameObject.SetActive(has);
            if (brainButton != null) brainButton.gameObject.SetActive(has);

            if (!has)
            {
                nameText.text = "No agent selected";
                statusText.text = "";
                statusPill.color = new Color(0.3f, 0.33f, 0.38f);
                accentBar.color = new Color(0.3f, 0.33f, 0.38f);
                bodyText.text = "Tap the agent list above to pick an agent, or point at a robot or desk and pinch.\nPinch and sweep along the floor to slide a desk.\n\n" +
                                "BRIEF THE TEAM gives everyone one goal. ADD AGENT grows the team; real agents on your PC " +
                                "join through the bridge (Settings).";
                nextButton.gameObject.SetActive(false);
                return;
            }

            AgentData d = agent.Data;
            TaskData t = agent.CurrentTask;
            nameText.text = $"{d.agentName}  <size=70%><color=#9AA3B2>{d.agentRole}</color></size>";
            statusText.text = d.StatusLabel;
            statusPill.color = d.StatusColor;
            accentBar.color = d.accentColor;

            string body = t != null
                ? $"<b>{t.title}</b>\n{TaskLibrary.Trim(t.description, 140)}\n<color=#9AA3B2>{(d.status == AgentStatus.Working ? $"Progress {Mathf.RoundToInt(t.progress * 100f)}%" : d.ActivityLabel)}</color>"
                : !string.IsNullOrEmpty(agent.WorkTitle)
                    ? $"<b>{agent.WorkTitle}</b>\n<color=#9AA3B2>{d.StatusLabel.ToLowerInvariant()}</color>"
                    : $"<color=#9AA3B2>{(d.activity == AgentActivity.InMeeting ? "In group discussion." : "Idle and ready for a task.")}</color>";
            if (d.IsLinked) body = $"<color=#4FD1C5>LIVE {d.linkedKind}</color> <color=#9AA3B2>on your PC, sits at {d.profileName}'s desk</color>\n" + body;
            if (agent.BacklogCount > 0) body += $"\n<color=#4FD1C5>{agent.BacklogCount} more task(s) queued</color>";
            bodyText.text = body + $"\n<color=#9AA3B2>Tasks completed: {d.completedTasks}</color>" +
                            (notice.Length > 0 ? $"\n<color=#FFB347>{notice}</color>" : "");
            if (brainLabel != null) brainLabel.text = "BRAIN: " + GameManager.Instance.AI.BrainLabel(d).Replace(" (team)", "").ToUpperInvariant();
            bool armed = Time.unscaledTime < removeArmedUntil;
            removeLabel.text = armed ? "CONFIRM" : "REMOVE";

            bool idle = t == null && agent.BacklogCount == 0;
            assignLabel.text = idle ? "ASSIGN TASK" : "QUEUE TASK";
            nextButton.gameObject.SetActive(d.activity == AgentActivity.InQueue && d.queuePosition > 0);
        }

        private void Update()
        {
            if (agent == null) return;
            if (agent.Data.status == AgentStatus.Working && Time.frameCount % 15 == 0) Refresh();
            if (removeArmedUntil > 0f && Time.unscaledTime > removeArmedUntil) { removeArmedUntil = 0f; Refresh(); }
        }
    }
}
