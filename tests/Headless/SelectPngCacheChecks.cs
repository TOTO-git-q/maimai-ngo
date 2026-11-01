using System;
using System.IO;
using PartnerLive2D.Unity;

// PNG cache tests use a decode double; actual Unity decoding and rendering require the game.
namespace UnityEngine
{
    enum TextureFormat { RGBA32, BC7 }
    enum TextureWrapMode { Clamp }
    enum FilterMode { Bilinear }
    class Object { public bool Destroyed; public static void Destroy(Object o) { o.Destroyed = true; } }
    class Texture2D : Object
    {
        public static int Decodes;
        public int width, height;
        public TextureWrapMode wrapMode; public FilterMode filterMode;
        public byte[] Raw;
        public static readonly System.Collections.Generic.List<byte> UploadOrder = new System.Collections.Generic.List<byte>();
        public void LoadRawTextureData(byte[] data) { Raw = (byte[])data.Clone(); UploadOrder.Add(data[0]); }
        public void Apply(bool mipmaps, bool noLongerReadable) { }
        public Texture2D(int w, int h, TextureFormat f, bool mipmaps) { width = w; height = h; }
        public bool LoadImage(byte[] b)
        {
            Decodes++;
            if (b.Length < 25 || b[24] == 0) return false;
            width = b[16] << 24 | b[17] << 16 | b[18] << 8 | b[19];
            height = b[20] << 24 | b[21] << 16 | b[22] << 8 | b[23];
            return true;
        }
    }
}
namespace PartnerLive2D.Unity
{
    class ApiError : Exception { public ApiError(int status, string message) : base(message) { } }
    static class MusicSelectSprites { public static string Root; public static string FilePath(string file) => Path.Combine(Root, file); }
}
namespace PartnerLive2D.Headless
{
    internal static unsafe partial class Program
    {
        static void SelectPngCacheChecks()
        {
            string dir = Path.Combine(Path.GetTempPath(), "select-png-cache-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir); MusicSelectSprites.Root = dir;
            string path = Path.Combine(dir, "test.png");
            byte[] bytes = { 137,80,78,71,13,10,26,10, 0,0,0,13, 73,72,68,82, 0,0,0,2, 0,0,0,3, 1 };
            int checks = 0;
            Action<bool, string> check = (ok, message) => { checks++; if (!ok) throw new Exception("PNG cache: " + message); };
            try
            {
                File.WriteAllBytes(path, bytes);
                var first = MusicSelectPngCache.Get("test.png");
                check(object.ReferenceEquals(first, MusicSelectPngCache.Get("test.png")) && UnityEngine.Texture2D.Decodes == 1, "one decode for unchanged version");
                File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(5));
                var second = MusicSelectPngCache.Get("test.png");
                check(!object.ReferenceEquals(first, second) && UnityEngine.Texture2D.Decodes == 2 && !first.Destroyed, "new timestamp reloads without destroying old texture");
                Array.Resize(ref bytes, 26); File.WriteAllBytes(path, bytes);
                var third = MusicSelectPngCache.Get("test.png");
                check(!object.ReferenceEquals(second, third) && UnityEngine.Texture2D.Decodes == 3, "size change reloads");
                bytes[24] = 0; File.WriteAllBytes(path, bytes); File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(10));
                bool failed = false; try { MusicSelectPngCache.Get("test.png"); } catch (ApiError) { failed = true; }
                check(failed && UnityEngine.Texture2D.Decodes == 4, "decode failure rejected");
                bytes[16] = 1; File.WriteAllBytes(path, bytes); File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(10));
                failed = false; try { MusicSelectPngCache.Get("test.png"); } catch (ApiError) { failed = true; }
                check(failed && UnityEngine.Texture2D.Decodes == 4, "oversized header rejected before decode");
                MusicSelectPngCache.Clear();
                check(first.Destroyed && second.Destroyed && third.Destroyed, "shutdown destroys cached versions");
                Console.WriteLine("Select PNG cache checks passed: " + checks + " (decode double; no Unity runtime)");
            }
            finally { MusicSelectPngCache.Clear(); if (File.Exists(path)) File.Delete(path); Directory.Delete(dir); }
        }
    }
}
