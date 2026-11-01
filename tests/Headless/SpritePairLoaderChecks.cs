using System;
using System.IO;
using System.Threading;
using PartnerLive2D.Core;
using PartnerLive2D.Unity;

namespace PartnerLive2D.Headless
{
    internal static unsafe partial class Program
    {
        static void SpritePairLoaderChecks()
        {
            var root = Path.Combine(Path.GetTempPath(), "sprite-loader-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var manifest = new SpritePairManifest();
                string[] names = { "ame", "k", "tap", "result" };
                string[] groups = { "Idle", "Idle", "Tap", "ResultTopLoop" };
                for (int i = 0; i < names.Length; i++)
                {
                    var c = new SpritePairManifest.Clip { Name = names[i], Character = i == 0 ? "Ame" : "K", Group = groups[i], Width = 4, Height = 4,
                        Drawings = new[] { new SpritePairManifest.Drawing { Offset = 0, Length = 16 }, new SpritePairManifest.Drawing { Offset = 16, Length = 16 } } };
                    manifest.Clips.Add(c);
                    var data = new byte[32]; for (int j = 0; j < data.Length; j++) data[j] = (byte)(i * 10 + j / 16 + 1);
                    File.WriteAllBytes(Path.Combine(root, names[i] + ".bin"), data);
                }
                UnityEngine.Texture2D.UploadOrder.Clear();
                var loader = new SpritePairLoader(manifest, root, "Idle", true);
                var started = DateTime.UtcNow;
                while (loader.Loaded != loader.Total && loader.Error == null && (DateTime.UtcNow - started).TotalSeconds < 5)
                { loader.Pump(3); Thread.Sleep(1); }
                if (loader.Error != null) throw new Exception("loader worker failed", loader.Error);
                if (loader.Total != 6 || loader.Loaded != 6 || loader.Bytes != 96) throw new Exception("loader page selection/upload count");
                var order = UnityEngine.Texture2D.UploadOrder;
                if (order.Count != 6 || order[0] != 1 || order[1] != 11) throw new Exception("loader loop first frames not prioritized");
                var t = loader.Clips[0].Textures[1];
                if (t.Raw[0] != 2 || t.Raw[15] != 2) throw new Exception("loader worker buffer contents");
                loader.Dispose();
                if (!t.Destroyed) throw new Exception("loader did not destroy textures");
                Console.WriteLine("Sprite loader checks passed: page filtering, loop priority, worker data and disposal (Unity doubles)");
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
