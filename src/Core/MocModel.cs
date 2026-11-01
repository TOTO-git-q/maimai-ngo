using System;
using System.Runtime.InteropServices;

namespace PartnerLive2D.Core
{
    // Native Cubism backend; owns the moc and model allocations.
    public sealed unsafe class MocModel : CubismModel
    {
        IntPtr _mocRaw, _modelRaw, _moc, _model;
        public MocModel(byte[] moc3)
        {
            try
            {
                var size = (uint)moc3.Length;
                var mocAddr = CubismCore.AllocAligned(moc3.Length, CubismCore.AlignofMoc, out _mocRaw);
                Marshal.Copy(moc3, 0, mocAddr, moc3.Length);

                if (CubismCore.csmHasMocConsistency(mocAddr, size) == 0)
                    throw new InvalidOperationException("moc3 failed the consistency check");
                _moc = CubismCore.csmReviveMocInPlace(mocAddr, size);
                if (_moc == IntPtr.Zero)
                    throw new InvalidOperationException("moc3 could not be revived (unsupported version?)");

                var modelSize = CubismCore.csmGetSizeofModel(_moc);
                var modelAddr = CubismCore.AllocAligned((int)modelSize, CubismCore.AlignofModel, out _modelRaw);
                _model = CubismCore.csmInitializeModelInPlace(_moc, modelAddr, modelSize);
                if (_model == IntPtr.Zero)
                    throw new InvalidOperationException("model could not be initialized");

                ParameterCount = CubismCore.csmGetParameterCount(_model);
                PartCount = CubismCore.csmGetPartCount(_model);
                DrawableCount = CubismCore.csmGetDrawableCount(_model);

                ParameterIds = ReadIds(CubismCore.csmGetParameterIds(_model), ParameterCount);
                PartIds = ReadIds(CubismCore.csmGetPartIds(_model), PartCount);
                DrawableIds = ReadIds(CubismCore.csmGetDrawableIds(_model), DrawableCount);

                _paramValues = CubismCore.csmGetParameterValues(_model);
                _paramMin = CubismCore.csmGetParameterMinimumValues(_model);
                _paramMax = CubismCore.csmGetParameterMaximumValues(_model);
                _paramDefault = CubismCore.csmGetParameterDefaultValues(_model);
                _partOpacities = CubismCore.csmGetPartOpacities(_model);

                CubismCore.Vector2 sizePx, originPx;
                float ppu;
                CubismCore.csmReadCanvasInfo(_model, out sizePx, out originPx, out ppu);
                PixelsPerUnit = ppu;
                CanvasWidth = sizePx.X / ppu;
                CanvasHeight = sizePx.Y / ppu;
                OriginX = originPx.X / ppu;
                OriginY = originPx.Y / ppu;

                InitializeIds();
                Update();
            }
            catch { Dispose(); throw; }
        }

        static string[] ReadIds(IntPtr* ids, int count)
        {
            var result = new string[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = CubismCore.PtrToUtf8(ids[i]);
            }
            return result;
        }

        public override byte* ConstantFlags => CubismCore.csmGetDrawableConstantFlags(_model);
        public override byte* DynamicFlags => CubismCore.csmGetDrawableDynamicFlags(_model);
        public override int* TextureIndices => CubismCore.csmGetDrawableTextureIndices(_model);
        public override int* RenderOrders => CubismCore.csmGetRenderOrders(_model);
        public override float* Opacities => CubismCore.csmGetDrawableOpacities(_model);
        public override int* MaskCounts => CubismCore.csmGetDrawableMaskCounts(_model);
        public override int** Masks => CubismCore.csmGetDrawableMasks(_model);
        public override int* VertexCounts => CubismCore.csmGetDrawableVertexCounts(_model);
        public override CubismCore.Vector2** VertexPositions => CubismCore.csmGetDrawableVertexPositions(_model);
        public override CubismCore.Vector2** VertexUvs => CubismCore.csmGetDrawableVertexUvs(_model);
        public override int* IndexCounts => CubismCore.csmGetDrawableIndexCounts(_model);
        public override ushort** Indices => CubismCore.csmGetDrawableIndices(_model);
        public override CubismCore.Vector4* MultiplyColors => CubismCore.csmGetDrawableMultiplyColors(_model);
        public override CubismCore.Vector4* ScreenColors => CubismCore.csmGetDrawableScreenColors(_model);

        public override void Update()
        {
            CubismCore.csmResetDrawableDynamicFlags(_model);
            CubismCore.csmUpdateModel(_model);
        }

        public override void Dispose()
        {
            if (_modelRaw != IntPtr.Zero) { Marshal.FreeHGlobal(_modelRaw); _modelRaw = IntPtr.Zero; }
            if (_mocRaw != IntPtr.Zero) { Marshal.FreeHGlobal(_mocRaw); _mocRaw = IntPtr.Zero; }
            _model = _moc = IntPtr.Zero;
        }
    }
}
