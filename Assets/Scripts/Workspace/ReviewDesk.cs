using UnityEngine;

namespace AIManagerWorkspace
{
    /// <summary>
    /// The manager's desk: where presented pages float for review, where approved pages
    /// are filed, and where review sounds play.
    /// </summary>
    public class ReviewDesk : MonoBehaviour
    {
        public enum Sfx { Arrive, Approve, Changes, Dismiss, Key, Notify, Click }

        [SerializeField] private Transform reviewAnchor;
        [SerializeField] private Transform doneTray;
        [SerializeField] private Transform doneStack;
        [SerializeField] private AudioSource audioSource;

        private AudioClip arrive, approve, changes, dismiss, key, notify, click;

        public Transform ReviewAnchor => reviewAnchor;
        public Transform DoneTray => doneTray;
        public int Completed { get; private set; }

        private void Awake()
        {
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0.6f;
            arrive = Tone("arrive", new[] { 660f, 880f }, 0.12f);
            approve = Tone("approve", new[] { 523f, 659f, 784f }, 0.11f);
            changes = Tone("changes", new[] { 494f, 392f }, 0.14f);
            dismiss = Tone("dismiss", new[] { 330f, 247f }, 0.12f);
            key = Tone("key", new[] { 1400f }, 0.025f);
            notify = Tone("notify", new[] { 988f, 1319f }, 0.09f);
            click = Tone("click", new[] { 1760f }, 0.018f);
            UpdateStack();
        }

        public void Play(Sfx s)
        {
            AudioClip clip = s switch
            {
                Sfx.Arrive => arrive, Sfx.Approve => approve, Sfx.Changes => changes,
                Sfx.Dismiss => dismiss, Sfx.Click => click, _ => key
            };
            if (clip == null) return;
            AppSettings cfg = AppSettings.Current;
            if (s == Sfx.Click && !cfg.uiClicks) return;
            float level = s == Sfx.Key ? 0.35f : s == Sfx.Click ? 0.25f : 0.8f;
            audioSource.PlayOneShot(clip, level * cfg.volume);
        }

        /// <summary>
        /// Soft two-note chime played where an agent is, so you can hear who just finished
        /// and turn towards them (fully spatial, quieter than the desk sounds).
        /// </summary>
        public void PlayNotifyAt(Vector3 position)
        {
            if (notify != null && AppSettings.Current.chimes)
                AudioSource.PlayClipAtPoint(notify, position + Vector3.up * 1.2f, 0.55f * AppSettings.Current.volume);
        }

        /// <summary>Plays a sound at full settings volume (tutorial ticks etc.).</summary>
        public void PlayNotify() { if (notify != null) audioSource.PlayOneShot(notify, 0.7f * AppSettings.Current.volume); }

        public void SetCompleted(int count)
        {
            Completed = Mathf.Max(0, count);
            UpdateStack();
        }

        public void AddCompleted()
        {
            Completed++;
            UpdateStack();
        }

        private void UpdateStack()
        {
            if (doneStack == null) return;
            int sheets = Mathf.Min(Completed, 40);
            doneStack.gameObject.SetActive(sheets > 0);
            Vector3 s = doneStack.localScale;
            doneStack.localScale = new Vector3(s.x, Mathf.Max(0.002f, sheets * 0.003f), s.z);
            Vector3 p = doneStack.localPosition;
            doneStack.localPosition = new Vector3(p.x, doneStack.localScale.y * 0.5f, p.z);
        }

        /// <summary>Short synthesized chime: a sequence of decaying sine notes.</summary>
        private static AudioClip Tone(string name, float[] notes, float noteLength)
        {
            const int rate = 44100;
            int perNote = Mathf.CeilToInt(rate * noteLength);
            int tail = rate / 6;
            float[] data = new float[perNote * notes.Length + tail];
            for (int n = 0; n < notes.Length; n++)
            {
                int start = n * perNote;
                int len = perNote + tail;
                for (int i = 0; i < len && start + i < data.Length; i++)
                {
                    float t = (float)i / rate;
                    float env = Mathf.Exp(-t * 9f) * Mathf.Clamp01(t * 400f);
                    data[start + i] += Mathf.Sin(2f * Mathf.PI * notes[n] * t) * env * 0.28f;
                }
            }
            var clip = AudioClip.Create(name, data.Length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
