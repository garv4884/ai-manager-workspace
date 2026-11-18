using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AIManagerWorkspace
{
    /// <summary>
    /// Room themes (Settings > Room): recolours walls, floor, rugs, windows, ceiling light panels, the sun
    /// and the ambient light. Materials are cloned at start so the project's material assets never change.
    /// </summary>
    public class RoomEnvironment : MonoBehaviour
    {
        [SerializeField] private Transform[] roots;
        [SerializeField] private Light sun;

        private struct Theme
        {
            public Color floor, wall, ceiling, window, panel, rug, rugSoft, wood, concrete;
            public Color sunColor; public float sunIntensity; public Vector3 sunEuler;
            public Color sky, equator, ground;
        }

        private readonly Dictionary<string, Material> clones = new Dictionary<string, Material>();
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public RoomTheme Current { get; private set; }

        private void Start()
        {
            foreach (var root in roots)
            {
                if (root == null) continue;
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (mats[i] == null || !mats[i].name.StartsWith("Env_")) continue;
                        if (!clones.TryGetValue(mats[i].name, out var clone))
                        {
                            clone = new Material(mats[i]) { name = mats[i].name };
                            clones[mats[i].name] = clone;
                        }
                        mats[i] = clone;
                        changed = true;
                    }
                    if (changed) r.sharedMaterials = mats;
                }
            }
            AppSettings.Changed += ApplyFromSettings;
            ApplyFromSettings();
        }

        private void OnDestroy()
        {
            AppSettings.Changed -= ApplyFromSettings;
            foreach (var m in clones.Values) if (m != null) Destroy(m);
        }

        private void ApplyFromSettings()
        {
            RoomTheme t = AppSettings.Current.theme;
            if (t != Current || clones.Count > 0) Apply(t);
        }

        public void Apply(RoomTheme theme)
        {
            Current = theme;
            Theme th = Get(theme);
            Set("Env_Floor", th.floor);
            Set("Env_Wall", th.wall);
            Set("Env_Ceiling", th.ceiling);
            Set("Env_Daylight", th.window);
            Set("Env_CeilingPanel", th.panel);
            Set("Env_Rug", th.rug);
            Set("Env_RugSoft", th.rugSoft);
            Set("Env_Oak", th.wood);
            Set("Env_Concrete", th.concrete);
            if (sun != null)
            {
                sun.color = th.sunColor;
                sun.intensity = th.sunIntensity;
                sun.transform.rotation = Quaternion.Euler(th.sunEuler);
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = th.sky;
            RenderSettings.ambientEquatorColor = th.equator;
            RenderSettings.ambientGroundColor = th.ground;
        }

        private void Set(string name, Color c)
        {
            if (clones.TryGetValue(name, out var m)) m.SetColor(BaseColorId, c);
        }

        private static Theme Get(RoomTheme t) => t switch
        {
            RoomTheme.GoldenHour => new Theme
            {
                floor = new Color(0.78f, 0.66f, 0.55f), wall = new Color(0.96f, 0.87f, 0.78f), ceiling = new Color(0.97f, 0.92f, 0.86f),
                window = new Color(1f, 0.72f, 0.45f), panel = new Color(1f, 0.86f, 0.68f),
                rug = new Color(0.55f, 0.36f, 0.3f), rugSoft = new Color(0.62f, 0.46f, 0.38f), wood = new Color(0.72f, 0.48f, 0.3f),
                concrete = new Color(0.78f, 0.7f, 0.62f),
                sunColor = new Color(1f, 0.74f, 0.5f), sunIntensity = 1.1f, sunEuler = new Vector3(18f, 80f, 0f),
                sky = new Color(1f, 0.86f, 0.74f), equator = new Color(0.86f, 0.7f, 0.58f), ground = new Color(0.6f, 0.48f, 0.4f)
            },
            RoomTheme.NightLoft => new Theme
            {
                floor = new Color(0.2f, 0.17f, 0.15f), wall = new Color(0.17f, 0.18f, 0.21f), ceiling = new Color(0.12f, 0.12f, 0.14f),
                window = new Color(0.16f, 0.24f, 0.5f), panel = new Color(0.95f, 0.78f, 0.55f),
                rug = new Color(0.12f, 0.24f, 0.26f), rugSoft = new Color(0.16f, 0.2f, 0.24f), wood = new Color(0.4f, 0.27f, 0.18f),
                concrete = new Color(0.26f, 0.26f, 0.28f),
                sunColor = new Color(0.55f, 0.65f, 1f), sunIntensity = 0.35f, sunEuler = new Vector3(55f, 40f, 0f),
                sky = new Color(0.36f, 0.4f, 0.55f), equator = new Color(0.3f, 0.28f, 0.3f), ground = new Color(0.2f, 0.18f, 0.18f)
            },
            RoomTheme.NordicCalm => new Theme
            {
                floor = new Color(0.86f, 0.82f, 0.76f), wall = new Color(0.88f, 0.9f, 0.92f), ceiling = new Color(0.95f, 0.96f, 0.97f),
                window = new Color(0.86f, 0.94f, 1f), panel = new Color(0.96f, 0.98f, 1f),
                rug = new Color(0.52f, 0.6f, 0.52f), rugSoft = new Color(0.62f, 0.68f, 0.62f), wood = new Color(0.86f, 0.74f, 0.58f),
                concrete = new Color(0.8f, 0.81f, 0.82f),
                sunColor = new Color(0.94f, 0.97f, 1f), sunIntensity = 1.15f, sunEuler = new Vector3(48f, 60f, 0f),
                sky = new Color(0.94f, 0.97f, 1f), equator = new Color(0.84f, 0.86f, 0.86f), ground = new Color(0.72f, 0.72f, 0.7f)
            },
            _ => new Theme
            {
                floor = new Color(0.80f, 0.77f, 0.72f), wall = new Color(0.93f, 0.91f, 0.87f), ceiling = new Color(0.96f, 0.95f, 0.93f),
                window = new Color(0.82f, 0.91f, 1f), panel = new Color(1f, 0.97f, 0.92f),
                rug = new Color(0.27f, 0.39f, 0.40f), rugSoft = new Color(0.36f, 0.46f, 0.46f), wood = new Color(0.70f, 0.52f, 0.34f),
                concrete = new Color(0.72f, 0.70f, 0.67f),
                sunColor = new Color(1f, 0.95f, 0.86f), sunIntensity = 1.25f, sunEuler = new Vector3(42f, 64f, 0f),
                sky = new Color(0.95f, 0.96f, 1.0f), equator = new Color(0.84f, 0.81f, 0.77f), ground = new Color(0.70f, 0.66f, 0.62f)
            }
        };
    }
}
