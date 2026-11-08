using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PartnerLive2D.Unity
{
    // settings.ini `rankBadges = off` (owner 2026-11-07): no course rank (初心者, _udemaeImage) and no class badge (B5,
    // rankImage) on the player's information bar, so the nameplate art shows there. The images are only switched off,
    // not removed or moved; the rest of the bar keeps its place. The game sets both only in SetUdemae / SetRank, which
    // every screen's bar (UserInformationController and its subclasses) goes through.
    // The course rank sat inside the white name box (UI_Name/IMG_Bg, a left-pivoted 9-slice), so that box also ends
    // right after the name now: name's left gap + name width + the same gap on the right, never wider than the stock box.
    static class NgoHideRankBadges
    {
        static bool Wanted()
        {
            string v;
            return Mod.ReadSettings(Path.Combine(Mod.AssetRoot, "settings.ini")).TryGetValue("rankBadges", out v) && v == "off";
        }

        static void Hide(Image image) { if (image != null) image.enabled = false; }

        static readonly Dictionary<int, float> StockWidth = new Dictionary<int, float>();
        static readonly Vector3[] Corners = new Vector3[4];

        static void FitNameBox(TextMeshProUGUI name)
        {
            if (name == null || name.transform.parent == null) return;
            var bg = name.transform.parent.Find("IMG_Bg") as RectTransform;
            if (bg == null) return;
            float stock;
            if (!StockWidth.TryGetValue(bg.GetInstanceID(), out stock)) StockWidth[bg.GetInstanceID()] = stock = bg.sizeDelta.x;
            name.rectTransform.GetWorldCorners(Corners);
            float gap = bg.InverseTransformPoint(Corners[0]).x + bg.pivot.x * bg.rect.width;   // name's left edge from the box's left edge
            float width = name.text.Length == 0 ? stock : Mathf.Clamp(gap + name.preferredWidth + gap, 2 * gap + 20f, stock);
            bg.sizeDelta = new Vector2(width, bg.sizeDelta.y);
        }

        [HarmonyPatch(typeof(UserInformationController), "SetUdemae")]
        static class ClassBadge
        {
            static bool Prepare() => Wanted();
            static void Postfix(Image ___rankImage) => Hide(___rankImage);
        }

        // both SetUserData paths set the name first and call SetRank last
        [HarmonyPatch(typeof(UserInformationController), "SetRank")]
        static class CourseRank
        {
            static bool Prepare() => Wanted();
            static void Postfix(Image ____udemaeImage, TextMeshProUGUI ___userNameText)
            {
                Hide(____udemaeImage);
                try { FitNameBox(___userNameText); }
                catch (System.Exception e) { Mod.Warn("name box fit: " + e.Message); }
            }
        }
    }
}
