using System;
using TMPro;
using UnityEngine;

namespace AIManagerWorkspace
{
    /// <summary>Hands fonts, the rounded sprite and icons (from the Meta Interaction SDK sample kit) to <see cref="UiKit"/>.</summary>
    [DefaultExecutionOrder(-500)]
    public class UiKitAssets : MonoBehaviour
    {
        [Serializable] public class NamedIcon { public string name; public Texture2D texture; }

        [SerializeField] private TMP_FontAsset medium;
        [SerializeField] private TMP_FontAsset bold;
        [SerializeField] private Sprite rounded;
        [SerializeField] private NamedIcon[] icons;

        private void Awake()
        {
            UiKit.Medium = medium;
            UiKit.Bold = bold;
            UiKit.Rounded = rounded;
            UiKit.Icons.Clear();
            if (icons != null) foreach (var i in icons) if (i != null && i.texture != null) UiKit.Icons[i.name] = i.texture;
        }
    }
}
