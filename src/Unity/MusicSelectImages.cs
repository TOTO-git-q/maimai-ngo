using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace PartnerLive2D.Unity
{
    // Only owns added objects and their PNG resources; stock children are never edited.
    sealed class MusicSelectImages : IDisposable
    {
        sealed class Entry : IDisposable
        {
            public GameObject Object;
            public Image Image;
            public Sprite Sprite;
            public string File;
            public void Dispose()
            {
                if (Object != null) { Object.SetActive(false); UnityEngine.Object.Destroy(Object); }
                if (Sprite != null) UnityEngine.Object.Destroy(Sprite);
            }
        }
        readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();
        readonly List<string> _removed = new List<string>();
        public void Apply(Dictionary<string, MusicSelectImageOptions> images, MusicSelectLayout layout)
        {
            _removed.Clear();
            foreach (var pair in _entries) if (!images.ContainsKey(pair.Key) || !layout.AppliesPath(images[pair.Key].Parent)) _removed.Add(pair.Key);
            foreach (var id in _removed) { _entries[id].Dispose(); _entries.Remove(id); }
            foreach (var pair in images)
            {
                var options = pair.Value;
                if (!layout.AppliesPath(options.Parent)) continue;
                var parent = layout.Resolve(options.Parent, true);
                Entry entry;
                if (_entries.TryGetValue(pair.Key, out entry) && (entry.Object == null || entry.File != options.Sprite))
                { entry.Dispose(); _entries.Remove(pair.Key); entry = null; }
                if (entry == null)
                {
                    entry = new Entry { File = options.Sprite };
                    try
                    {
                        var texture = MusicSelectPngCache.Get(options.Sprite);
                        entry.Sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
                        entry.Object = new GameObject("PartnerLive2D_Image_" + pair.Key, typeof(RectTransform), typeof(Image));
                        entry.Image = entry.Object.GetComponent<Image>(); entry.Image.raycastTarget = false;
                        entry.Image.sprite = entry.Sprite;
                        _entries.Add(pair.Key, entry);
                    }
                    catch { entry.Dispose(); throw; }
                }
                var rect = entry.Image.rectTransform;
                entry.Object.layer = parent.gameObject.layer;
                if (rect.parent != parent) rect.SetParent(parent, false);
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
                rect.localPosition = new Vector3(options.X, options.Y, 0);
                rect.localScale = Vector3.one; rect.localRotation = Quaternion.identity;
                rect.sizeDelta = new Vector2(options.Width, options.Height);
                rect.SetSiblingIndex(options.ResolveSibling(parent.childCount));
                entry.Object.SetActive(!options.Hidden);
            }
        }
        public void Refresh(string id)
        {
            Entry entry; if (_entries.TryGetValue(id, out entry)) { entry.Dispose(); _entries.Remove(id); }
        }
        public void Dispose()
        {
            foreach (var entry in _entries.Values) entry.Dispose();
            _entries.Clear(); _removed.Clear();
        }
    }
}
