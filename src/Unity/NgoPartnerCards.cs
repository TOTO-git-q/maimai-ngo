using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace PartnerLive2D.Unity
{
    // NGO skin: partner cards (UI_CharactorSlot) whose picture is one of the owner's full illustrations. The stock
    // slot clips a 200 px square picture with a capsule mask under the frame, so a picture with its own background only
    // fills the middle of the window and nothing can reach over the frame. For the textures listed in
    // LocalAssets/PartnerLive2D/ngo_open_cards.txt the capsule mask is switched off, the picture is drawn above the
    // frame and the neighbouring cards and enlarged to 300 px; the texture carries its own window shape
    // (tools/ngo_partner_cards.py), so a figure can lean out over the frame. Any other partner in a reused slot gets
    // the stock setup back.
    [HarmonyPatch(typeof(Image), "sprite", MethodType.Setter)]
    static class NgoPartnerCards
    {
        public const float Enlarge = 1.5f;   // must match DISPLAY / 200 in tools/ngo_partner_cards.py
        static HashSet<string> _open;
        static readonly HashSet<string> Front = new HashSet<string>(StringComparer.Ordinal);
        static readonly Dictionary<int, Vector3> StockScale = new Dictionary<int, Vector3>();

        // no ngo_open_cards.txt -> the hook is not installed at all
        static bool Prepare() => File.Exists(Path.Combine(Mod.AssetRoot, "ngo_open_cards.txt"));

        static HashSet<string> Open()
        {
            if (_open != null) return _open;
            _open = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in File.ReadAllLines(Path.Combine(Mod.AssetRoot, "ngo_open_cards.txt")))
            {
                var t = line.Trim();
                if (t.Length == 0 || t.StartsWith(";") || t.StartsWith("#")) continue;
                var parts = t.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);   // "<texture> [front]"
                _open.Add(parts[0]);
                if (parts.Length > 1 && parts[1] == "front") Front.Add(parts[0]);
            }
            Mod.Msg("ngo open cards: " + _open.Count + " pictures");
            return _open;
        }

        static void Postfix(Image __instance)
        {
            try { Apply(__instance); }
            catch (Exception e) { Mod.Error("ngo open cards: " + e.Message); }
        }

        static void Apply(Image img)
        {
            var name = NameCache.Of(img);
            if (name == "IMG_Leader") { LiftLeader(img); return; }
            if (name != "IMG_Charactor") return;
            var mask = img.transform.parent;
            var slot = mask != null ? mask.parent : null;
            if (slot == null || !slot.name.StartsWith("UI_CharactorSlot", StringComparison.Ordinal)) return;
            var frame = slot.Find("IMG_Frame");
            if (frame == null) return;
            int id = img.GetInstanceID();
            if (!StockScale.ContainsKey(id)) StockScale[id] = img.transform.localScale;
            var sprite = img.sprite;
            bool open = sprite != null && sprite.texture != null && Open().Contains(sprite.texture.name);

            var m = mask.GetComponent<Mask>();
            if (m != null) m.enabled = !open;
            var g = mask.GetComponent<Graphic>();   // the capsule shape the mask used; it would draw once unmasked
            if (g != null) g.enabled = !open;
            img.transform.localScale = open ? StockScale[id] * Enlarge : StockScale[id];
            bool above = mask.GetSiblingIndex() > frame.GetSiblingIndex();
            if (open != above) mask.SetSiblingIndex(frame.GetSiblingIndex());   // swaps the two: above <-> below the frame
            // a figure leaning out must not slip under the next card: the picture gets its own canvas sorted above
            // the one the cards are on, a "front" picture (one that leans out) above the other pictures too. The
            // sprite is often set before the slot is placed under its canvas, so the order is kept by a component
            var sort = mask.GetComponent<NgoCardSort>();
            if (open)
            {
                if (sort == null) sort = mask.gameObject.AddComponent<NgoCardSort>();
                sort.Lift = Front.Contains(sprite.texture.name) ? 2 : 1;
                sort.enabled = true;
            }
            else if (sort != null) sort.enabled = false;
            // the card's own marks (stars, LV, level number, reborn star) stay readable above every picture
            foreach (var n in Marks)
            {
                var t = slot.Find(n);
                if (t == null) continue;
                if (n == "Lv" || n == "IMG_Level") LowerIntoToolStrip(t);
                var s = t.GetComponent<NgoCardSort>();
                if (open)
                {
                    if (s == null) s = t.gameObject.AddComponent<NgoCardSort>();
                    s.Lift = 3; s.enabled = true;
                }
                else if (s != null) s.enabled = false;
            }
        }

        static readonly string[] Marks = { "Skill_Type01", "Lv", "IMG_Level", "IMG_RebornSter" };

        // the leader portrait (UI_LeaderCharaSlot/.../UI_Mask/IMG_Leader) sits on the same top-screen canvas as the
        // cards; the collection screen draws its preview layer over that canvas, which hid the leader while the lifted
        // cards stayed. The leader's mask gets its own canvas one above the host, like the cards.
        static void LiftLeader(Image img)
        {
            var mask = img.transform.parent;
            if (mask == null || mask.name != "UI_Mask") return;
            var t = mask.parent;
            while (t != null && !t.name.StartsWith("UI_LeaderCharaSlot", StringComparison.Ordinal)) t = t.parent;
            if (t == null) return;
            var sort = mask.GetComponent<NgoCardSort>() ?? mask.gameObject.AddComponent<NgoCardSort>();
            sort.Lift = 1; sort.enabled = true;
        }

        // the NGO card frame (tools/ngo_partner_frame_fit.py) carries the "LV" and level digits on a tool strip under
        // its title bar, 5 px lower than the stock frame's band; every card uses that frame, so the move is unconditional
        public const float LvDrop = 5f;
        static readonly Dictionary<int, Vector3> StockPos = new Dictionary<int, Vector3>();

        static void LowerIntoToolStrip(Transform t)
        {
            int id = t.GetInstanceID();
            if (!StockPos.ContainsKey(id)) StockPos[id] = t.localPosition;
            t.localPosition = StockPos[id] + new Vector3(0f, -LvDrop, 0f);
        }
    }

    sealed class NgoCardSort : MonoBehaviour
    {
        public int Lift = 1;
        Canvas _c;

        void LateUpdate()
        {
            var host = transform.parent != null ? transform.parent.GetComponentInParent<Canvas>() : null;
            if (host == null) return;
            if (_c == null) { _c = GetComponent<Canvas>(); if (_c == null) _c = gameObject.AddComponent<Canvas>(); }
            _c.overrideSorting = true;
            if (_c.sortingLayerID != host.sortingLayerID) _c.sortingLayerID = host.sortingLayerID;
            if (_c.sortingOrder != host.sortingOrder + Lift) _c.sortingOrder = host.sortingOrder + Lift;
        }

        void OnDisable() { if (_c != null) _c.overrideSorting = false; }
    }
}
