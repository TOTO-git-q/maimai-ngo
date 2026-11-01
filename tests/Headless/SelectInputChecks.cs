using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using PartnerLive2D.Unity;

namespace PartnerLive2D.Headless
{
    internal static unsafe partial class Program
    {
        sealed class CategoryInputDouble
        {
            public readonly MusicSelectCategoryWindow Window = new MusicSelectCategoryWindow();
            public bool RightHeld, LeftHeld, Eligible = true;
            public int Tick(double ms, bool right = false, bool left = false, bool a2 = false, bool a7 = false)
                => Window.Sample(ms, Eligible, right, left, RightHeld, LeftHeld, a2, a7);
        }
        static void SelectInputChecks()
        {
            int count = 0;
            Action<bool, string> check = (ok, why) => { if (!ok) throw new Exception(why); count++; };
            foreach (bool right in new[] { true, false })
            {
                var input = new CategoryInputDouble();
                check(input.Tick(0, right, !right) == 0, "single press starts delay");
                check(input.Tick(60) == 0 && input.Tick(69.99) == 0, "single waits full 70 ms");
                check(input.Tick(70) == (right ? 1 : -1), "released tap dispatches correct direction at 70 ms");
                check(input.Tick(80) == 0 && input.Tick(1000) == 0, "no repeat after released tap");
                input = new CategoryInputDouble { RightHeld = right, LeftHeld = !right };
                input.Tick(0, right, !right);
                check(input.Tick(70) == (right ? 1 : -1) && input.Tick(500) == 0 && input.Tick(1000) == 0, "hold triggers once");
                foreach (double gap in new[] { 0, 1, 16, 49, 50, 60, 69, 70 })
                {
                    input = new CategoryInputDouble();
                    if (gap == 0) check(input.Tick(0, true, true) == 0, "simultaneous combo untouched");
                    else
                    {
                        input.Tick(0, right, !right);
                        check(input.Tick(gap, !right, right) == 0, "opposite cancels even when first released, gap=" + gap);
                    }
                    check(input.Tick(150) == 0 && input.Tick(500) == 0, "combo never leaves delayed category");
                }
            }
            var i = new CategoryInputDouble();
            check(i.Tick(0, a2: true) == 1 && i.Tick(1) == 0 && i.Tick(2, a7: true) == -1, "touch maps immediately, once per down");
            check(i.Tick(3, a2: true, a7: true) == 0, "opposite simultaneous touch cancels");
            i.Tick(5, right: true);
            check(i.Tick(10, a2: true) == 1 && i.Tick(100) == 0, "touch clears matching pending physical press");
            check(i.Tick(110, right: true, a2: true) == 1 && i.Tick(200) == 0, "same-frame button/touch coalesces");
            i.Tick(210, left: true); i.Eligible = false;
            check(i.Tick(220) == 0, "inactive phase cancels pending"); i.Eligible = true;
            check(i.Tick(400) == 0, "phase reentry has no stale input");
            i.Eligible = false;
            check(i.Tick(410, right: true, a7: true) == 0, "disabled or locked input is ignored"); i.Eligible = true;
            check(i.Tick(500) == 0, "ignored input is not replayed");
            i.Tick(510, right: true); i.Window.Reset(); check(i.Tick(600) == 0, "higher-priority stock branch resets pending");
            var p1 = new CategoryInputDouble(); var p2 = new CategoryInputDouble();
            p1.Tick(0, right: true); p2.Tick(5, left: true);
            check(p1.Tick(70) == 1 && p2.Tick(75) == -1, "player sides have independent windows");
            check(p1.Tick(80, right: true) == 0 && p1.Tick(150) == 1, "new down after release can trigger again");
            var settings = new MusicSelectSettings();
            // Exact stock sampling gates: category/card/mode/mission/Freedom are
            // deliberately absent. Branch priority remains enforced by stock Update.
            for (int mask = 0; mask < 16; mask++)
            {
                bool enabled = (mask & 1) != 0, valid = (mask & 2) != 0;
                bool sharedLocked = (mask & 4) != 0, playerLocked = (mask & 8) != 0;
                bool allowed = MusicSelectCategoryWindow.CanSample(enabled, valid, sharedLocked, playerLocked);
                check(allowed == (mask == 3), "stock sampling truth table " + mask);
                var window = new MusicSelectCategoryWindow();
                check(window.Sample(0, allowed, false, false, false, false, true, false) == (allowed ? 1 : 0), "stock gates control A2 " + mask);
                window.Sample(0, true, true, false, false, false, false, false);
                window.Sample(10, allowed, false, false, false, false, false, false);
                check(window.Sample(70, true, false, false, false, false, false, false) == (allowed ? 1 : 0), "stock gates cancel pending button " + mask);
            }
            Func<string, Dictionary<string, object>, Dictionary<string, object>> edit = (path, values) =>
                new Dictionary<string, object> { { "nodes", new Dictionary<string, object> { { path, values } } } };
            Action<Action, string> rejects = (action, why) =>
            {
                bool rejected = false; try { action(); } catch (FormatException) { rejected = true; }
                check(rejected, why);
            };
            foreach (string layer in new[] { "UI_Buttons", "UI_InfoButtons" })
            foreach (string prefix in new[] { "", "MusicSelectProcess(Clone)/Canvas/Main/" })
            {
                string path = prefix + layer + "/UI_Button04/FlatButton(Clone)/IMG_Button";
                var sprite = new Dictionary<string, object> { { "sprite", "next_flat.png" } };
                var accepted = settings.With(edit(path, sprite));
                check(accepted.Nodes[path].Sprite == "next_flat.png" && accepted.Nodes[path].IsSpriteOnly, "protected leaf sprite accepted " + path);
                var restored = settings.With(PartnerLive2D.Framework.Json.Parse(JsonSerializer.Serialize(accepted.ToDict())));
                check(restored.Nodes[path].Sprite == "next_flat.png", "protected sprite schema roundtrip " + path);
                check(accepted.With(edit(path, new Dictionary<string, object> { { "sprite", null } })).Nodes[path].Sprite == null, "protected sprite clear " + path);
                foreach (var field in new Dictionary<string, object> { { "scale", 1f }, { "offsetX", 0f }, { "offsetY", 0f }, { "hidden", false } })
                {
                    var mixed = new Dictionary<string, object>(sprite) { { field.Key, field.Value } };
                    rejects(() => accepted.With(edit(path, mixed)), "mixed protected " + field.Key + " rejected even identity");
                    rejects(() => accepted.With(edit(path, new Dictionary<string, object> { { field.Key, field.Value } })), "protected transform/hidden alone rejected");
                }
                rejects(() => accepted.With(edit(path, new Dictionary<string, object>())), "empty protected edit rejected");
                rejects(() => settings.With(edit(prefix + layer, sprite)), "layer itself protected");
                rejects(() => settings.With(edit(path + "/../Other", sprite)), "sprite traversal rejected");
                MusicSelectSettings.ValidateInputLayerAccess(layer, true, true, true);
                check(true, "live leaf Image policy matches parser");
                rejects(() => MusicSelectSettings.ValidateInputLayerAccess(layer, true, true, false), "nonleaf or non-Image protected");
                rejects(() => MusicSelectSettings.ValidateInputLayerAccess(layer, false, true, true), "live layer itself protected");
                rejects(() => MusicSelectSettings.ValidateInputLayerAccess(layer, true, false, true), "live transform protected");
                string temp = Path.Combine(Path.GetTempPath(), "select-fix2-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temp);
                try
                {
                    accepted.Save(temp, JsonSerializer.Serialize(accepted.ToDict()));
                    check(MusicSelectSettings.Load(temp).Nodes[path].Sprite == "next_flat.png", "protected sprite save/load");
                }
                finally { Directory.Delete(temp, true); }
                check(settings.Nodes.Count == 0 && accepted.Nodes[path].Sprite == "next_flat.png", "failed protected edits preserve presets");
            }
            rejects(() => settings.With(edit("TouchInput/IMG_Button", new Dictionary<string, object> { { "sprite", "next_flat.png" } })), "unrelated touch/input layer remains protected");
            rejects(() => settings.With(edit("UI_Buttons/TouchInput/IMG_Button", new Dictionary<string, object> { { "sprite", "next_flat.png" } })), "nested input layer remains protected");
            check(settings.CategoryKeys && !settings.Enabled, "category keys default true while select remains opt-in");
            var node = new Dictionary<string, object> { { "nodes", new Dictionary<string, object> { { "Art", new Dictionary<string, object> { { "sprite", "edited.png" } } } } }, { "categoryKeys", false } };
            var updated = settings.With(node);
            check(!updated.CategoryKeys && updated.Nodes["Art"].Sprite == "edited.png" && settings.Nodes.Count == 0, "sprite/category settings copy atomically");
            var merged = updated.With(new Dictionary<string, object> { { "nodes", new Dictionary<string, object> { { "Art", new Dictionary<string, object> { { "hidden", true } } } } } });
            check(merged.Nodes["Art"].Sprite == "edited.png" && !updated.Nodes["Art"].Hidden, "partial node edits retain sprite without mutation");
            var cleared = merged.With(new Dictionary<string, object> { { "nodes", new Dictionary<string, object> { { "Art", new Dictionary<string, object> { { "sprite", null } } } } } });
            check(cleared.Nodes["Art"].Sprite == null && cleared.Nodes["Art"].Hidden && merged.Nodes["Art"].Sprite == "edited.png", "null clears only sprite");
            foreach (object bad in new object[] { "../x.png", "..\\x.png", "C:x.png", "/x.png", "x.jpg", "", "x.png\n", "a/b.png", "x?.png", 1, true })
            {
                bool rejected = false;
                try { settings.With(new Dictionary<string, object> { { "nodes", new Dictionary<string, object> { { "Art", new Dictionary<string, object> { { "sprite", bad } } } } } }); }
                catch (FormatException) { rejected = true; }
                check(rejected && settings.Nodes.Count == 0, "invalid sprite filename/type rejected atomically: " + bad);
            }
            string root = Path.Combine(Path.GetTempPath(), "select-input-sprite-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                updated.Save(root, JsonSerializer.Serialize(updated.ToDict()));
                var loaded = MusicSelectSettings.Load(root);
                check(!loaded.CategoryKeys && loaded.Nodes["Art"].Sprite == "edited.png", "sprite and category keys persist");
                File.WriteAllText(Path.Combine(root, "settings.ini"), "selectView = true\ncategoryKeys = true\n");
                check(MusicSelectSettings.Load(root).CategoryKeys, "startup categoryKeys can override preset");
                cleared.Save(root, JsonSerializer.Serialize(cleared.ToDict()));
                check(MusicSelectSettings.Load(root).Nodes["Art"].Sprite == null, "cleared sprite persists");
            }
            finally { Directory.Delete(root, true); }
            Console.WriteLine("Select input/sprite checks passed: " + count + " (70 ms input doubles, combo cancellation, per-side isolation, schema and persistence)");
        }
    }
}
