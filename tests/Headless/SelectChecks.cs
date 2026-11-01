using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using PartnerLive2D.Unity;
using Json = PartnerLive2D.Framework.Json;

namespace PartnerLive2D.Headless
{
    internal static unsafe partial class Program
    {
        static void SelectChecks()
        {
            int checks = 0;
            Action<bool, string> check = (ok, why) => { if (!ok) throw new Exception(why); checks++; };
            var c = new MusicSelectSettings();
            check(c.TabLabelColor == "" && c.TabLabelOffsetY == 0 && c.TabTitleOffsetY == 0, "tab label defaults (stock style)");
            check(c.With(Json.Parse("{\"tabLabelColor\":\"#aBc123\"}")).With(Json.Parse("{\"tabLabelColor\":\"\"}")).TabLabelColor == "", "empty tab label colour = game colour");
            var tabs = c.With(Json.Parse("{\"tabLabelColor\":\"#aBc123\",\"tabLabelOffsetY\":-32.5,\"tabTitleOffsetY\":15}"));
            var tabsPartial = tabs.With(Json.Parse("{\"tabTitleOffsetY\":0}"));
            check(tabsPartial.TabLabelColor == "#aBc123" && tabsPartial.TabLabelOffsetY == -32.5f && tabsPartial.TabTitleOffsetY == 0 && c.TabLabelOffsetY == 0, "tab settings parse and merge atomically");
            foreach (var invalidTab in new[] { "{\"tabLabelColor\":\"#123\"}", "{\"tabLabelColor\":\"#GG0000\"}", "{\"tabLabelColor\":null}", "{\"tabLabelColor\":12}", "{\"tabLabelOffsetY\":541}", "{\"tabTitleOffsetY\":-541}", "{\"tabLabelOffsetY\":\"20\"}" })
            {
                bool rejectedTab = false;
                try { c.With(Json.Parse(invalidTab)); } catch (FormatException) { rejectedTab = true; }
                check(rejectedTab && c.TabLabelColor == "", "reject invalid tab setting: " + invalidTab);
            }
            foreach (var key in new[] { "tabLabelOffsetY", "tabTitleOffsetY" })
                foreach (var value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                {
                    bool rejectedTab = false;
                    try { c.With(new Dictionary<string, object> { { key, value } }); } catch (FormatException) { rejectedTab = true; }
                    check(rejectedTab, "reject nonfinite " + key);
                }
            foreach (var path in new[] { "UI_Buttons/Next", "/Canvas/Main/UI_InfoButtons/X", "//UI_Buttons//X", "UI_InfoButtons" })
                check(MusicSelectSettings.IsSpriteLayerPath(path), "sprite layer segment: " + path);
            foreach (var path in new[] { "", "UI_ButtonsExtra/X", "UI_InfoButton/X", "xUI_Buttons/X", "Other/X" })
                check(!MusicSelectSettings.IsSpriteLayerPath(path), "only exact sprite layer segments match: " + path);
            long spritePathStart = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) MusicSelectSettings.IsSpriteLayerPath("Canvas/Main/UI_InfoButtons/X");
            check(GC.GetAllocatedBytesForCurrentThread() == spritePathStart, "sprite layer path checks allocate no managed memory");
            check(!c.Enabled && c.Groups["chains"][0] == .55f && c.Height == 500, "default-off preset");
            check(c.Groups.Count == 12 && c.Nodes.Count == 0 && c.CropBottom == 0, "infrastructure adds no default node or crop change");
            check(c.Groups["tabs"][0] == .45f && c.Groups["tabs"][2] == -140 && c.Groups["selector"][0] == .55f && c.Groups["selector"][2] == -220
                && c.Groups["status"][0] == .55f && c.Groups["status"][2] == -160 && c.Groups["myStatus"][0] == .55f && c.Groups["myStatus"][2] == -220
                && c.BottomX == 0 && c.BottomY == -20 && c.MyStatusX == -200 && c.MyStatusY == -40
                && c.LabelX == 300 && c.LabelY == -30 && c.LabelWidth == 130 && c.LabelHeight == 36, "all original preset defaults preserved");
            foreach (var key in new[] { "difficultyHeader", "achievementRate", "rivalData", "extraInfo", "festaDeco", "challengeBG", "challengeDeco" })
                check(c.Groups[key][0] == 1 && c.Groups[key][1] == 0 && c.Groups[key][2] == 0 && !c.GroupsHidden[key], "stock new group: " + key);
            var infra = c.With(Json.Parse("{\"groups\":{\"chains\":{\"hidden\":true}},\"nodes\":{\"UI_ChainLists/MusicChainList\":{\"scale\":0.8,\"offsetX\":40,\"hidden\":true}},\"partner\":{\"cropBottom\":0.4}}"));
            var nodeUpdate = infra.With(Json.Parse("{\"nodes\":{\"UI_ChainLists/MusicChainList\":{\"offsetY\":-25,\"hidden\":false}}}"));
            check(nodeUpdate.Nodes["UI_ChainLists/MusicChainList"].Transform[0] == .8f && nodeUpdate.Nodes["UI_ChainLists/MusicChainList"].Transform[2] == -25 && !nodeUpdate.Nodes["UI_ChainLists/MusicChainList"].Hidden, "partial node merge");
            check(infra.Nodes["UI_ChainLists/MusicChainList"].Hidden && !c.GroupsHidden["chains"] && c.CropBottom == 0, "deep clone preserves committed nodes and hidden flags");
            float[] actual = { 12, 34, 7, 2, 3, 4 }; int writes = 0;
            var state = new MusicSelectNodeState(actual, v => { actual = v; writes++; });
            for (int i = 0; i < 100; i++) state.Apply(new[] { .5f, 10f, -20f });
            check(actual[0] == 22 && actual[1] == 14 && actual[2] == 7 && actual[3] == 1 && actual[4] == 1.5f && actual[5] == 4, "real node baseline engine applies idempotently and preserves Z");
            state.Apply(new[] { .8f, -5f, 10f });
            check(actual[0] == 7 && actual[1] == 44 && actual[3] == 1.6f, "retuning uses original baseline");
            state.Restore(); int restoredWrites = writes; state.Restore();
            check(actual[0] == 12 && actual[1] == 34 && actual[3] == 2 && writes == restoredWrites, "node restore is exact and idempotent");
            state.Apply(new[] { 1f, 0f, 0f }); check(writes == restoredWrites, "stock untouched transform stays game-owned");
            actual[0] = 999; state.Apply(new[] { 1f, 0f, 0f }, true);
            check(actual[0] == 12 && actual[3] == 2, "explicit identity node overrides legacy pinning from the same original baseline");
            state.Restore();
            state.Apply(new[] { .5f, 0f, 1f }); state.Apply(new[] { 1f, 0f, 0f });
            check(actual[1] == 34 && actual[4] == 3, "returning to identity restores baseline");
            state.Apply(new[] { .5f, 10f, -20f }); state.Restore();
            check(actual[0] == 12 && actual[3] == 2, "reentry does not recapture transformed values");
            foreach (bool originalRaycasts in new[] { true, false })
            {
                float alpha = .37f; bool raycasts = originalRaycasts; int visibilityWrites = 0;
                var hidden = new MusicSelectHiddenState(alpha, raycasts, (a, r) => { alpha = a; raycasts = r; visibilityWrites++; });
                for (int i = 0; i < 100; i++) hidden.Apply(true);
                check(alpha == 0 && !raycasts, "hidden uses zero alpha and disables raycasts");
                hidden.Apply(false); int count = visibilityWrites; hidden.Restore();
                check(alpha == .37f && raycasts == originalRaycasts && count == visibilityWrites, "hidden restore preserves non-default alpha/raycast state and is idempotent");
                hidden.Apply(true); hidden.Restore(); check(alpha == .37f && raycasts == originalRaycasts, "hidden reentry restores original state");
            }
            check(c.UseEquippedPartner && c.SelectPartner == "equipped", "equipped is the default");
            check(!c.With(Json.Parse("{\"selectPartner\":\"hiyori\"}")).UseEquippedPartner, "explicit custom retains legacy meaning");
            check(c.ShouldUseStockPartner(false) && !c.ShouldUseStockPartner(true), "equipped chooses stock only when no custom match");
            var stock = c.With(Json.Parse("{\"selectPartner\":\"stock\"}"));
            check(stock.ShouldUseStockPartner(true) && stock.ShouldUseStockPartner(false), "explicit stock overrides custom match");
            check(!c.With(Json.Parse("{\"selectPartner\":\"hiyori\"}")).ShouldUseStockPartner(false), "explicit custom does not silently fall back to stock");
            check(stock.UseStockPartner && !stock.Enabled && !c.UseStockPartner, "explicit stock choice remains default-off and does not mutate source");
            check(!c.With(Json.Parse("{\"selectPartner\":\"stock-custom\"}")).UseStockPartner, "only reserved stock value selects stock");
            var valid = "{\"enabled\":true,\"selectPartner\":\"hiyori\",\"groups\":{\"chains\":{\"scale\":0.65,\"offsetY\":-180}},\"partner\":{\"height\":480},\"myStatusContent\":{\"offsetY\":-45},\"difficultyLabel\":{\"width\":140},\"phases\":[\"Music\"]}";
            var tuned = c.With(Json.Parse(valid));
            check(tuned.Enabled && tuned.Groups["chains"][0] == .65f && tuned.Height == 480 && tuned.MyStatusY == -45, "valid multi-group update");
            check(!c.Enabled && c.Groups["chains"][0] == .55f, "copy does not mutate committed preset");
            foreach (var invalid in new[]
            {
                "{\"enabled\":true,\"groups\":{\"chains\":{\"scale\":0.7},\"tabs\":{\"scale\":0.1}}}",
                "{\"groups\":{\"chains\":{\"offsetY\":541}}}", "{\"partner\":{\"height\":0}}",
                "{\"partner\":{\"cropBottom\":1.01}}", "{\"partner\":{\"cropBottom\":-0.1}}",
                "{\"nodes\":{\"../Main\":{}}}", "{\"nodes\":{\"Main\":{}}}", "{\"nodes\":{\"Canvas\":{}}}",
                "{\"nodes\":{\"UI_Buttons/Child\":{}}}", "{\"nodes\":{\"UI_TouchLayer\":{}}}",
                "{\"nodes\":{\"UI_ChainLists\":{\"scale\":0}}}", "{\"nodes\":{\"UI_ChainLists\":{\"hidden\":1}}}",
                "{\"groups\":{\"chains\":{\"hidden\":\"true\"}}}",
                "{\"groups\":{\"Main\":{\"scale\":0.5}}}", "{\"enabled\":\"true\"}",
                "{\"phases\":[\"Music\",\"Menu\"]}", "{\"partner\":{\"height\":\"NaN\"}}",
                "{\"selectPartner\":\"hiyori\\nhttp = false\"}", "{\"save\":\"true\"}",
                "{\"difficultyLabel\":{\"width\":50}}", "{\"myStatusContent\":{\"offsetX\":-541}}"
            })
            {
                bool rejected = false;
                try { c.With(Json.Parse(invalid)); } catch (FormatException) { rejected = true; }
                check(rejected && !c.Enabled && c.Groups["chains"][0] == .55f, "reject entire invalid update: " + invalid);
            }
            foreach (float v in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                bool rejected = false;
                try { c.With(new Dictionary<string, object> { { "partner", new Dictionary<string, object> { { "height", v } } } }); }
                catch (FormatException) { rejected = true; }
                check(rejected, "reject nonfinite value");
                foreach (string field in new[] { "cropBottom", "nodeScale" })
                {
                    rejected = false;
                    var payload = field == "cropBottom"
                        ? new Dictionary<string, object> { { "partner", new Dictionary<string, object> { { "cropBottom", v } } } }
                        : new Dictionary<string, object> { { "nodes", new Dictionary<string, object> { { "UI_ChainLists", new Dictionary<string, object> { { "scale", v } } } } } };
                    try { c.With(payload); } catch (FormatException) { rejected = true; }
                    check(rejected, "reject nonfinite " + field);
                }
            }
            string root = Path.Combine(Path.GetTempPath(), "PartnerLive2D-select-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                check(!MusicSelectSettings.Load(root).Enabled, "absent settings stays off");
                tabs.Save(root, JsonSerializer.Serialize(tabs.ToDict()));
                var loadedTabs = MusicSelectSettings.Load(root);
                check(loadedTabs.TabLabelColor == "#aBc123" && loadedTabs.TabLabelOffsetY == -32.5f && loadedTabs.TabTitleOffsetY == 15, "tab settings survive save/load");
                string settings = Path.Combine(root, "settings.ini");
                File.WriteAllText(settings, "# preserve this\nhttp = false\nport = 8799\nselectView = false\nselectView = false\n");
                string json = JsonSerializer.Serialize(tuned.ToDict());
                tuned.Save(root, json);
                var loaded = MusicSelectSettings.Load(root);
                check(loaded.Enabled && loaded.SelectPartner == "hiyori" && loaded.Height == 480 && loaded.Groups["chains"][0] == .65f, "save/load exact values");
                string text = File.ReadAllText(settings);
                check(text.Contains("# preserve this") && text.Contains("http = false") && text.Contains("port = 8799"), "unrelated settings preserved");
                infra.Save(root, JsonSerializer.Serialize(infra.ToDict()));
                loaded = MusicSelectSettings.Load(root);
                check(loaded.GroupsHidden["chains"] && loaded.CropBottom == .4f && loaded.Nodes["UI_ChainLists/MusicChainList"].Hidden && loaded.Nodes["UI_ChainLists/MusicChainList"].Transform[1] == 40, "node, hidden and crop persist through actual save/load");
                c.Save(root, JsonSerializer.Serialize(c.ToDict()));
                check(MusicSelectSettings.Load(root).UseEquippedPartner, "equipped persists through save/load");
                stock.Save(root, JsonSerializer.Serialize(stock.ToDict()));
                loaded = MusicSelectSettings.Load(root);
                check(loaded.UseStockPartner && !loaded.Enabled && loaded.Height == 500, "stock save/load preserves reserved choice and disabled state");
                var enabledStock = stock.With(Json.Parse("{\"enabled\":true,\"partner\":{\"height\":450}}"));
                enabledStock.Save(root, JsonSerializer.Serialize(enabledStock.ToDict()));
                loaded = MusicSelectSettings.Load(root);
                check(loaded.UseStockPartner && loaded.Enabled && loaded.Height == 450, "stock opt-in and common rectangle survive save/load");
                File.WriteAllText(settings, "http = false\n");
                loaded = MusicSelectSettings.Load(root);
                check(!loaded.Enabled && loaded.UseStockPartner, "saved enabled stock preset cannot enable without settings.ini opt-in");
                File.WriteAllText(settings, "selectView = true\nselectPartner = another\n");
                check(MusicSelectSettings.Load(root).SelectPartner == "another", "settings fallback takes priority");
                File.WriteAllText(Path.Combine(root, "select_layout.ini"), "layout = {\"partner\":{\"height\":1e999}}\n");
                bool rejected = false;
                try { MusicSelectSettings.Load(root); } catch (FormatException) { rejected = true; }
                check(rejected, "invalid saved preset rejected");
            }
            finally { Directory.Delete(root, true); }
            Console.WriteLine("Select preset checks passed: " + checks + " (default-off, atomic rejection, finite bounds, persistence, settings preservation)");
            SelectRouterChecks();
            SelectInputChecks();
            SelectFix3Checks();
        }
    }
}
