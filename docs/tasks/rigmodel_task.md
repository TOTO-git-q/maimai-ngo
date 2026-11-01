# Task: RigModel backend (rig3.json) for PartnerLive2D

Working directory: `<PROJECT>` (a C# MelonLoader mod for maimai DX: Unity, target .NET Framework 4.7.2, **C# LangVersion 7.3 — no newer syntax**).

## Worklog protocol (mandatory)
Keep `tasks/rigmodel_worklog.md`. If it exists when you start, read it and continue from the last verified step instead of starting over. After each step append: what you did, what you verified (command + result), what is next. Finish with a line `DONE` (or `BLOCKED: <reason>`).

## Rules
- Build ONLY with `powershell -ExecutionPolicy Bypass -File build.ps1` (no `-Install`). Never copy anything into `<GAME_ROOT>`, never start the game, never touch `mcp/` or `deploy/`.
- dotnet is `<TOOLS>\dotnet\dotnet.exe`; always set env DOTNET_CLI_HOME=<TOOLS>\dotnet-home, NUGET_PACKAGES=<TOOLS>\nuget-packages, NUGET_HTTP_CACHE_PATH=<TOOLS>\nuget-http-cache, DOTNET_CLI_TELEMETRY_OPTOUT=1, DOTNET_NOLOGO=1 (build.ps1 already does). If the sandbox blocks the build, write the exact error to the worklog and continue with the code; do not try to work around the sandbox.
- Do not modify `docs/RIG3_FORMAT.md`. If the spec is ambiguous, take the most literal reading and record it in the worklog under "Spec notes".

## Goal
Add a second model backend that loads our own JSON format `*.rig3.json` instead of a Cubism `.moc3`. The format spec is `docs/RIG3_FORMAT.md` — read it fully; every rule is normative (keyform multilinear interpolation with first binding fastest, warp bilinear with linear extrapolation outside the rect, rotation formula with canvas y down, deformer chain applied innermost-first, opacity products incl. part chain, render-order ranks, uv convention, pixel→model-unit conversion, flags).

## Required changes
1. `src/Core/CubismModel.cs`: make `CubismModel` an **abstract base class** with the same public surface as today (counts, Id arrays, canvas props, GetParameterIndex/Value/Minimum/Maximum/Default, Set/Add/MultiplyParameterValue incl. string-id overloads, Save/LoadParameters, GetPartIndex/GetPartOpacity/SetPartOpacity, GetDrawableIndex, the pointer properties ConstantFlags…ScreenColors, Update(), Dispose(), static CoreVersion). Shared logic (clamp/blend, save/load, id dictionaries) in the base; backend specifics abstract/virtual. Move the current moc3 implementation into `src/Core/MocModel.cs` (`public sealed unsafe class MocModel : CubismModel`) with identical behaviour. All existing users keep typing `CubismModel` — do not rename usages.
2. New `src/Core/RigModel.cs`: `public sealed unsafe class RigModel : CubismModel`, built from the rig3.json text. Implement the spec exactly. Expose drawable data through the same pointer properties using unmanaged memory (Marshal.AllocHGlobal): parameter value/min/max/default arrays, part opacities, per-drawable vertex positions (CubismCore.Vector2*), uvs, ushort indices, pointer tables, flag bytes, opacities, render orders, texture indices, mask counts / mask tables, multiply/screen colours. Free all in Dispose. `Update()` must not allocate (precompute parent chains, binding strides, part chains at load). Target: ~100 meshes × ~300 vertices × up to 4 deformer levels at 60 fps without GC churn. For JSON use the existing parser in `src/Framework/Json.cs` if the dependency direction allows; otherwise put the parsing factory where it compiles in both `PartnerLive2D.csproj` and `tests/Headless/Headless.csproj` (the latter compiles `src/Core/*.cs` and `src/Framework/*.cs`).
3. `src/Framework/CubismAvatar.cs` (~line 60 does `new CubismModel(File.ReadAllBytes(...))`): if the Moc file name ends with `.rig3.json` (case-insensitive) → RigModel from UTF-8 text, else MocModel. Loading a rig model must not require Live2DCubismCore.dll.
4. `tests/Headless`:
   - Fixture `tests/Headless/rig_fixture/` with `test.rig3.json`, `test.model3.json`, a 4×4 PNG. Exercise: two parameters; a 2×2-cell warp bound to one parameter (2 keys); a child rotation deformer bound to the other parameter (3 keys); a mesh under the rotation with its own Offsets keyforms; a mesh with an Opacity keyform; a mask; differing DrawOrders. Hand-compute expected vertex positions (model units) for 3 parameter settings — one with a vertex outside the warp rect (extrapolation), one between keys — and assert them (tol 1e-4) in a new `RigChecks()` run before the sample models.
   - CLI mode `Headless.exe rigdump <model3.json> <params.json>`: params.json is `{paramId: value}`; load, set params (no motions/physics), Update(), print to stdout one JSON object `{"meshes": {id: {"opacity": f, "order": rank, "verts": [x,y,...]}}}` with verts in model units.
   - The default Headless run over the 8 Cubism samples (`<TOOLS>\live2d\CubismSdkForNative-5-r.5\Samples\Resources`) must still pass.
5. Do not touch `src/Unity/*` unless it stops compiling.

## Done =
build.ps1 succeeds; Headless default run passes (RigChecks + 8 samples); `rigdump` works on the fixture. Final message (≤25 lines): files changed, spec notes, exact build/test commands and their results.
