using System;
using System.Runtime.InteropServices;

namespace PartnerLive2D.Core
{
    // P/Invoke surface of Live2DCubismCore (Cubism SDK for Native 5-r.5, Windows x64).
    // Only the calls this mod uses are declared. All pointers returned by the core
    // stay owned by the core and remain valid as long as the model memory lives.
    public static unsafe class CubismCore
    {
        const string Dll = "Live2DCubismCore";
        const CallingConvention CC = CallingConvention.Cdecl;

        public const int AlignofMoc = 64;
        public const int AlignofModel = 16;

        // csmConstantFlags
        public const byte BlendAdditive = 1 << 0;
        public const byte BlendMultiplicative = 1 << 1;
        public const byte IsDoubleSided = 1 << 2;
        public const byte IsInvertedMask = 1 << 3;

        // csmDynamicFlags
        public const byte IsVisible = 1 << 0;
        public const byte VisibilityDidChange = 1 << 1;
        public const byte OpacityDidChange = 1 << 2;
        public const byte DrawOrderDidChange = 1 << 3;
        public const byte RenderOrderDidChange = 1 << 4;
        public const byte VertexPositionsDidChange = 1 << 5;
        public const byte BlendColorDidChange = 1 << 6;

        [StructLayout(LayoutKind.Sequential)]
        public struct Vector2 { public float X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct Vector4 { public float X, Y, Z, W; }

        [DllImport(Dll, CallingConvention = CC)] public static extern uint csmGetVersion();
        [DllImport(Dll, CallingConvention = CC)] public static extern uint csmGetLatestMocVersion();
        [DllImport(Dll, CallingConvention = CC)] public static extern uint csmGetMocVersion(IntPtr address, uint size);
        [DllImport(Dll, CallingConvention = CC)] public static extern int csmHasMocConsistency(IntPtr address, uint size);
        [DllImport(Dll, CallingConvention = CC)] public static extern IntPtr csmReviveMocInPlace(IntPtr address, uint size);
        [DllImport(Dll, CallingConvention = CC)] public static extern uint csmGetSizeofModel(IntPtr moc);
        [DllImport(Dll, CallingConvention = CC)] public static extern IntPtr csmInitializeModelInPlace(IntPtr moc, IntPtr address, uint size);
        [DllImport(Dll, CallingConvention = CC)] public static extern void csmUpdateModel(IntPtr model);
        [DllImport(Dll, CallingConvention = CC)] public static extern void csmReadCanvasInfo(IntPtr model, out Vector2 sizeInPixels, out Vector2 originInPixels, out float pixelsPerUnit);

        [DllImport(Dll, CallingConvention = CC)] public static extern int csmGetParameterCount(IntPtr model);
        [DllImport(Dll, CallingConvention = CC)] public static extern IntPtr* csmGetParameterIds(IntPtr model);
        [DllImport(Dll, CallingConvention = CC)] public static extern float* csmGetParameterMinimumValues(IntPtr model);
        [DllImport(Dll, CallingConvention = CC)] public static extern float* csmGetParameterMaximumValues(IntPtr model);
        [DllImport(Dll, CallingConvention = CC)] public static extern float* csmGetParameterDefaultValues(IntPtr model);
        [DllImport(Dll, CallingConvention = CC)] public static extern float* csmGetParameterValues(IntPtr model);
        [DllImport(Dll, CallingConvention = CC)] public static extern int* csmGetParameterRepeats(IntPtr model);

        [DllImport(Dll, CallingConvention = CC)] public static extern int csmGetPartCount(IntPtr model);
        [DllImport(Dll, CallingConvention = CC)] public static extern IntPtr* csmGetPartIds(IntPtr model);
        [DllImport(Dll, CallingConvention = CC)] public static extern float* csmGetPartOpacities(IntPtr model);
        [DllImport(Dll, CallingConvention = CC)] public static extern int* csmGetPartParentPartIndices(IntPtr model);

        [DllImport(Dll, CallingConvention = CC)] public static extern int csmGetDrawableCount(IntPtr model);
        [DllImport(Dll, CallingConvention = CC)] public static extern IntPtr* csmGetDrawableIds(IntPtr model);
        [DllImport(Dll, CallingConvention = CC)] public static extern byte* csmGetDrawableConstantFlags(IntPtr model);
        [DllImport(Dll, CallingConvention = CC)] public static extern byte* csmGetDrawableDynamicFlags(IntPtr model);
        [DllImport(Dll, CallingConvention = CC)] public static extern int* csmGetDrawableTextureIndices(IntPtr model);
        [DllImport(Dll, CallingConvention = CC)] public static extern int* csmGetDrawableDrawOrders(IntPtr model);
        [DllImport(Dll, CallingConvention = CC)] public static extern int* csmGetRenderOrders(IntPtr model);
        [DllImport(Dll, CallingConvention = CC)] public static extern float* csmGetDrawableOpacities(IntPtr model);
        [DllImport(Dll, CallingConvention = CC)] public static extern int* csmGetDrawableMaskCounts(IntPtr model);
        [DllImport(Dll, CallingConvention = CC)] public static extern int** csmGetDrawableMasks(IntPtr model);
        [DllImport(Dll, CallingConvention = CC)] public static extern int* csmGetDrawableVertexCounts(IntPtr model);
        [DllImport(Dll, CallingConvention = CC)] public static extern Vector2** csmGetDrawableVertexPositions(IntPtr model);
        [DllImport(Dll, CallingConvention = CC)] public static extern Vector2** csmGetDrawableVertexUvs(IntPtr model);
        [DllImport(Dll, CallingConvention = CC)] public static extern int* csmGetDrawableIndexCounts(IntPtr model);
        [DllImport(Dll, CallingConvention = CC)] public static extern ushort** csmGetDrawableIndices(IntPtr model);
        [DllImport(Dll, CallingConvention = CC)] public static extern Vector4* csmGetDrawableMultiplyColors(IntPtr model);
        [DllImport(Dll, CallingConvention = CC)] public static extern Vector4* csmGetDrawableScreenColors(IntPtr model);
        [DllImport(Dll, CallingConvention = CC)] public static extern void csmResetDrawableDynamicFlags(IntPtr model);

        // Allocates unmanaged memory aligned to 'align'. Free with FreeAligned(raw).
        public static IntPtr AllocAligned(int size, int align, out IntPtr raw)
        {
            raw = Marshal.AllocHGlobal(size + align);
            long p = raw.ToInt64();
            long aligned = (p + align - 1) & ~((long)align - 1);
            return new IntPtr(aligned);
        }

        public static string PtrToUtf8(IntPtr p)
        {
            if (p == IntPtr.Zero) return null;
            int len = 0;
            byte* b = (byte*)p;
            while (b[len] != 0) len++;
            return System.Text.Encoding.UTF8.GetString(b, len);
        }
    }
}
