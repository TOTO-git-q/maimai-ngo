using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PartnerLive2D.Unity
{
    // Main-thread only. Retain old versions while live sprites may still reference them.
    static class MusicSelectPngCache
    {
        struct Key : IEquatable<Key>
        {
            public string Path;
            public long Length, WriteTicks;
            public bool Equals(Key other) => StringComparer.OrdinalIgnoreCase.Equals(Path, other.Path) && Length == other.Length && WriteTicks == other.WriteTicks;
            public override bool Equals(object other) => other is Key && Equals((Key)other);
            public override int GetHashCode() => (StringComparer.OrdinalIgnoreCase.GetHashCode(Path) * 397 ^ Length.GetHashCode()) * 397 ^ WriteTicks.GetHashCode();
        }
        static readonly Dictionary<Key, Texture2D> Textures = new Dictionary<Key, Texture2D>();
        static long BigEndian(byte[] b, int offset) => ((long)b[offset] << 24) | ((long)b[offset + 1] << 16) | ((long)b[offset + 2] << 8) | b[offset + 3];

        public static Texture2D Get(string file)
        {
            string path = System.IO.Path.GetFullPath(MusicSelectSprites.FilePath(file));
            var info = new FileInfo(path);
            if (!info.Exists) throw new ApiError(404, "sprite PNG not found: " + file);
            var key = new Key { Path = path, Length = info.Length, WriteTicks = info.LastWriteTimeUtc.Ticks };
            if (key.Length > 32 * 1024 * 1024) throw new ApiError(400, "sprite PNG exceeds 32 MiB");
            Texture2D cached;
            if (Textures.TryGetValue(key, out cached) && cached != null) return cached;
            var bytes = File.ReadAllBytes(path);
            info.Refresh();
            if (!info.Exists || info.Length != key.Length || info.LastWriteTimeUtc.Ticks != key.WriteTicks || bytes.LongLength != key.Length)
                throw new ApiError(400, "sprite PNG changed while reading: " + file);
            if (bytes.Length < 24 || bytes[0] != 137 || bytes[1] != 80 || bytes[2] != 78 || bytes[3] != 71
                || bytes[4] != 13 || bytes[5] != 10 || bytes[6] != 26 || bytes[7] != 10
                || BigEndian(bytes, 8) != 13 || bytes[12] != 73 || bytes[13] != 72 || bytes[14] != 68 || bytes[15] != 82)
                throw new ApiError(400, "sprite file is not PNG: " + file);
            long w = BigEndian(bytes, 16), h = BigEndian(bytes, 20);
            if (w < 1 || h < 1 || w > 8192 || h > 8192 || w * h > 16777216)
                throw new ApiError(400, "sprite PNG dimensions exceed 8192 or 16M pixels");
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texture.LoadImage(bytes) || texture.width != w || texture.height != h)
                    throw new ApiError(400, "cannot decode sprite PNG: " + file);
                Textures[key] = texture;
                return texture;
            }
            catch { UnityEngine.Object.Destroy(texture); throw; }
        }
        public static void Clear()
        {
            foreach (var texture in Textures.Values) if (texture != null) UnityEngine.Object.Destroy(texture);
            Textures.Clear();
        }
    }
}
