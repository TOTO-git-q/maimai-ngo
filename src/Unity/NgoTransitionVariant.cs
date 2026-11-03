using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace PartnerLive2D.Unity
{
    // NGO skin: the screen-change animation (FadeProcess / track start: the zooming medallion 01_01 and the banners
    // Sub_01, Sub_02, sub_03) shows Ame or KAngel at random. The Ame art is installed in the game's textures; the KAngel
    // art sits in LocalAssets/PartnerLive2D/ngo_transi_kangel/<texture>.png. The first of these images enabled in a new
    // transition rolls once; the others enabled within the same few seconds follow that roll. KAngel's version also
    // recolours the tinted white parts of the circle (its solid ground and the stripe/shape parts) to her sky blue,
    // pink, white and pale yellow; Ame's version puts the prefab colours back.
    [HarmonyPatch(typeof(Image), "OnEnable")]
    static class NgoTransitionVariant
    {
        static readonly string[] Names = { "01_01", "Sub_01", "Sub_02", "sub_03" };
        static readonly Dictionary<int, Color> Stock = new Dictionary<int, Color>();          // image id -> prefab colour

        // KAngel colour for a tinted part of ChangeScreen_01, by its object name (null: not a recoloured part)
        static Color? KangelColor(Image img)
        {
            var n = NameCache.Of(img);
            var sp = img.sprite;
            var tex = sp != null && sp.texture != null ? NameCache.Of(sp.texture) : "";
            if (n == "01_01" && tex != "01_01" && img.transform.parent != null && NameCache.Of(img.transform.parent).StartsWith("ChangeScreen", StringComparison.Ordinal))   // no sprite or a plain one
                return new Color(.37f, .71f, .93f);                                   // the solid ground: sky blue
            if (!tex.StartsWith("UI_CMN_Parts_0", StringComparison.Ordinal)) return null;
            if (n.StartsWith("08_01", StringComparison.Ordinal)) return new Color(1f, .49f, .78f);       // pink band
            if (n.StartsWith("08_", StringComparison.Ordinal)) return Color.white;
            if (n == "06_01_03") return new Color(1f, .61f, .82f);
            if (n == "06_01_02" || n == "06_02_01") return Color.white;
            if (n.StartsWith("06_", StringComparison.Ordinal)) return new Color(1f, .95f, .63f);         // pale yellow
            if (n.StartsWith("07_", StringComparison.Ordinal)) return new Color(.75f, .9f, 1f);          // pale blue
            return null;
        }

        static void Recolour(Image img)
        {
            var k = KangelColor(img);
            if (k == null) return;
            int id = img.GetInstanceID();
            if (!Stock.ContainsKey(id)) Stock[id] = img.color;
            var c = _kangel ? k.Value : Stock[id];
            img.color = new Color(c.r, c.g, c.b, img.color.a);   // the animation owns the alpha
        }
        static readonly Dictionary<Sprite, Sprite> Made = new Dictionary<Sprite, Sprite>();   // stock -> KAngel
        static readonly Dictionary<Sprite, Sprite> Back = new Dictionary<Sprite, Sprite>();   // KAngel -> stock
        static readonly System.Random Dice = new System.Random();
        static bool _kangel;
        static float _rolledAt = -100f;

        static string Folder => Path.Combine(Mod.AssetRoot, "ngo_transi_kangel");
        static bool Prepare() => Directory.Exists(Folder);

        static void Postfix(Image __instance)
        {
            long t0 = PlayStats.Now();
            try { Apply(__instance); }
            catch (Exception e) { Mod.Error("ngo transition variant: " + e.Message); }
            finally { PlayStats.Spent(PlayStats.Part.Transition, t0); }
        }

        static void Apply(Image img)
        {
            var s = img.sprite;
            if (s == null)
            {
                // the circle's solid ground is a plain colour image without a sprite
                if (KangelColor(img) != null) { Roll(); Recolour(img); }
                return;
            }
            Sprite stock;
            bool wasKangel = Back.TryGetValue(s, out stock);
            if (!wasKangel)
            {
                var tex = s.texture;
                if (tex == null) return;
                if (Array.IndexOf(Names, NameCache.Of(tex)) < 0)
                {
                    // a tinted part of the transition: follows this transition's roll, or rolls if it comes first
                    if (Time.realtimeSinceStartup - _rolledAt <= 3f) Recolour(img);
                    else if (KangelColor(img) != null) { Roll(); Recolour(img); }
                    return;
                }
                stock = s;
            }
            Roll();
            var want = _kangel ? Kangel(stock) : stock;
            if (want != null && want != s) img.sprite = want;
        }

        // one roll per transition: the images of one transition come on within ~2 s of the first, so they follow the
        // roll made then; the clock is not refreshed by them (it was, and a busy screen kept the first roll forever)
        static void Roll()
        {
            if (Time.realtimeSinceStartup - _rolledAt <= 3f) return;
            _kangel = Dice.Next(2) == 1;
            _rolledAt = Time.realtimeSinceStartup;
            Mod.Msg("ngo transition: " + (_kangel ? "KAngel" : "Ame"));
        }

        // the KAngel twin of a stock sprite: same rect, pivot, size and border, from the PNG of the same name
        static Sprite Kangel(Sprite stock)
        {
            Sprite k;
            if (Made.TryGetValue(stock, out k)) return k;
            var tex = Mod.LoadImage(Path.Combine(Folder, stock.texture.name + ".png"));
            if (tex == null || tex.width != stock.texture.width || tex.height != stock.texture.height) { Made[stock] = null; return null; }
            var r = stock.rect;
            k = Sprite.Create(tex, r, new Vector2(stock.pivot.x / r.width, stock.pivot.y / r.height), stock.pixelsPerUnit, 0, SpriteMeshType.FullRect, stock.border);
            k.name = stock.name + "_kangel";
            UnityEngine.Object.DontDestroyOnLoad(k);
            Made[stock] = k; Back[k] = stock;
            return k;
        }
    }
}
