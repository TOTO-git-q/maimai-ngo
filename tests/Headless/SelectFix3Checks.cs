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
        static void SelectFix3Checks()
        {
            int checks = 0;
            Action<bool, string> check = (ok, why) => { if (!ok) throw new Exception(why); checks++; };
            float[] stock = { -608, 194, 7, 1, 2, 3 }, rendered = new float[6];
            int writes = 0;
            var state = new MusicSelectNodeState(stock, v => { Array.Copy(v, rendered, 6); writes++; });
            var overrideValues = new[] { .5f, 100f, -20f };
            state.Apply(overrideValues, true);
            stock[0] = 999; // Capture is a copy, never an animated reference.
            for (int frame = 0; frame < 100; frame++)
            {
                rendered[0] = frame; rendered[3] = 9; // Animator runs after process Update.
                state.Reapply();
                check(rendered[0] == -508 && rendered[1] == 174 && rendered[2] == 7 && rendered[3] == .5f && rendered[4] == 1 && rendered[5] == 3,
                    "late replay wins after Animator and preserves immutable baseline/Z");
            }
            state.Apply(overrideValues, true); state.Reapply(); state.Restore();
            check(rendered[0] == -608 && rendered[3] == 1, "restore uses stock, never last animated/overridden sample");
            int count = writes; state.Reapply(); state.Restore();
            check(writes == count, "late callbacks after restore are no-ops");
            state.Apply(new[] { 1f, 0f, 0f }); state.Reapply();
            check(writes == count, "unmodified groups are never pinned by late replay");
            state.Apply(new[] { 1f, 0f, 0f }, true); rendered[0] = 1; state.Reapply();
            check(rendered[0] == -608, "explicit identity node still wins");
            state.Restore(); state.Apply(overrideValues, true);
            for (int i = 0; i < 100; i++) { state.Apply(overrideValues, true); state.Reapply(); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) { state.Apply(overrideValues, true); state.Reapply(); }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            check(allocated == 0, "1000 Apply + late replay frames allocate zero managed bytes");
            state.Restore(); state.Apply(new[] { .8f, 2f, 3f }, true); state.Reapply();
            check(rendered[0] == -606 && rendered[3] == .8f, "reentry/retuning retain the first baseline");

            const string parent = "Null_SelectorBackgroundTarget/UI_CMN_SelectorBG(Clone)/Null_CanvasGroup";
            string create = "{\"id\":\"board\",\"sprite\":\"board.png\",\"parent\":\"" + parent + "\",\"siblingIndex\":\"first\",\"x\":12,\"y\":-34,\"width\":800,\"height\":500,\"hidden\":false}";
            var original = new MusicSelectSettings();
            var preset = original.WithImage(Json.Parse(create));
            var board = preset.Images["board"];
            check(original.Images.Count == 0 && !preset.Enabled && board.Parent == parent && board.Sprite == "board.png", "create is atomic and preserves opt-in/parent/file");
            check(board.X == 12 && board.Y == -34 && board.Width == 800 && board.Height == 500 && !board.Hidden && board.ResolveSibling(9) == 0, "rect and first child ordering");
            var hidden = preset.WithImage(Json.Parse("{\"id\":\"board\",\"hidden\":true,\"siblingIndex\":\"last\"}"));
            check(hidden.Images["board"].Hidden && !board.Hidden && hidden.Images["board"].Width == 800 && hidden.Images["board"].ResolveSibling(9) == 8, "partial update deep copy, hidden and last ordering");
            var indexed = preset.WithImage(Json.Parse("{\"id\":\"board\",\"siblingIndex\":3}"));
            check(indexed.Images["board"].ResolveSibling(9) == 3 && indexed.Images["board"].ResolveSibling(2) == 1, "integer sibling index clamps to parent's current child count");
            var maxIndex = preset.WithImage(Json.Parse("{\"id\":\"board\",\"siblingIndex\":2147483647}"));
            check(maxIndex.Images["board"].ResolveSibling(5) == 4, "full nonnegative integer range is supported without float rounding");
            var reparented = preset.WithImage(Json.Parse("{\"id\":\"board\",\"parent\":\"Other/Container\",\"sprite\":\"other.png\"}"));
            check(reparented.Images.Count == 1 && reparented.Images["board"].Parent == "Other/Container" && board.Parent == parent, "same id updates parent and resource without duplicating preset entries");
            var deleted = preset.WithImage(Json.Parse("{\"id\":\"board\",\"delete\":true}"));
            check(deleted.Images.Count == 0 && preset.Images.Count == 1, "delete is atomic and removes persisted entry");
            check(deleted.WithImage(Json.Parse("{\"id\":\"board\",\"delete\":true}")).Images.Count == 0, "delete is idempotent");
            foreach (var invalid in new[] {
                "{}", "{\"id\":1}", "{\"id\":\"../bad\",\"delete\":true}", "{\"id\":\"\",\"delete\":true}",
                "{\"id\":\"new\"}", "{\"id\":\"board\",\"sprite\":null}", "{\"id\":\"board\",\"sprite\":\"../board.png\"}",
                "{\"id\":\"board\",\"sprite\":\"board.jpg\"}", "{\"id\":\"board\",\"parent\":\"Main\"}",
                "{\"id\":\"board\",\"parent\":\"../Other\"}", "{\"id\":\"board\",\"parent\":\"UI_Buttons/Child\"}",
                "{\"id\":\"board\",\"parent\":null}", "{\"id\":\"board\",\"siblingIndex\":-1}",
                "{\"id\":\"board\",\"siblingIndex\":1.5}", "{\"id\":\"board\",\"siblingIndex\":\"3\"}",
                "{\"id\":\"board\",\"siblingIndex\":16777216.5}", "{\"id\":\"board\",\"siblingIndex\":2147483648}",
                "{\"id\":\"board\",\"siblingIndex\":null}", "{\"id\":\"board\",\"hidden\":1}",
                "{\"id\":\"board\",\"width\":0}", "{\"id\":\"board\",\"height\":8193}",
                "{\"id\":\"board\",\"x\":10001}", "{\"id\":\"board\",\"y\":\"NaN\"}",
                "{\"id\":\"board\",\"delete\":\"true\"}", "{\"id\":\"board\",\"delete\":true,\"sprite\":\"board.png\"}",
                "{\"id\":\"board\",\"extra\":1}" })
            {
                bool rejected = false;
                try { preset.WithImage(Json.Parse(invalid)); } catch (FormatException) { rejected = true; }
                check(rejected && preset.Images.Count == 1 && !board.Hidden && board.Width == 800, "reject invalid image without mutation: " + invalid);
            }
            foreach (string field in new[] { "x", "y", "width", "height", "siblingIndex" }) foreach (float nonfinite in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                bool rejected = false;
                try { preset.WithImage(new Dictionary<string, object> { { "id", "board" }, { field, nonfinite } }); }
                catch (FormatException) { rejected = true; }
                check(rejected, "nonfinite image " + field);
            }
            var roundtrip = original.With(Json.Parse(JsonSerializer.Serialize(hidden.ToDict())));
            check(roundtrip.Images["board"].Hidden && Equals(roundtrip.Images["board"].SiblingIndex, "last"), "preset JSON roundtrip includes images");
            string root = Path.Combine(Path.GetTempPath(), "PartnerLive2D-fix3-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                hidden.Save(root, JsonSerializer.Serialize(hidden.ToDict()));
                var loaded = MusicSelectSettings.Load(root);
                check(loaded.Images["board"].Parent == parent && loaded.Images["board"].Hidden && loaded.Images["board"].X == 12, "actual INI save/load preserves image");
                deleted.Save(root, JsonSerializer.Serialize(deleted.ToDict()));
                check(MusicSelectSettings.Load(root).Images.Count == 0, "deleted image stays deleted after save/load");
            }
            finally { Directory.Delete(root, true); }
            check(new MusicSelectSettings().Images.Count == 0, "reset defaults contain no images");
            var limit = preset;
            for (int i = 1; i < 64; i++) limit = limit.WithImage(Json.Parse(create.Replace("\"board\"", "\"board" + i + "\"")));
            bool tooMany = false;
            try { limit.WithImage(Json.Parse(create.Replace("\"board\"", "\"extra\""))); } catch (FormatException) { tooMany = true; }
            check(tooMany && limit.Images.Count == 64, "bounded image count rejects atomically");
            bool mixedRejected = false;
            try { preset.With(Json.Parse("{\"enabled\":true,\"images\":{\"board\":{\"width\":0}}}")); } catch (FormatException) { mixedRejected = true; }
            check(mixedRejected && !preset.Enabled && board.Width == 800, "invalid mixed layout/image update cannot partially enable feature");
            Console.WriteLine("Select fix3 checks passed: " + checks + "; Apply/late replay allocated " + allocated + " bytes / 1000 frames");
        }
    }
}
