# RigModel worklog

## Step 1 - orientation and backend split
- Read the task and full normative spec; inspected Core, avatar load path, JSON parser, project files and headless harness. No prior worklog and no Git checkout. Saved scoped original files in tasks/rigmodel_baseline for final diff review.
- Verified: Get-Content / rg inspection; both projects compile Core and Framework together, so RigModel can reuse Framework.Json without a project dependency change. Existing native-only construction occurs in CubismAvatar.
- Implemented abstract shared base and moved native allocation/evaluation to MocModel; avatar selects backend by case-insensitive suffix.
- Next: implement precomputed rig evaluation and unmanaged drawable storage, then fixture and regression checks.

## Spec notes
- Nearest-integer DrawOrder ties use Math.Round with MidpointRounding.ToEven (the spec does not specify a tie rule).
- CanvasWidth/Height and OriginX/Y retain the existing public API in model units; rig geometry remains in pixels until the final vertex conversion.
- Build-only-via-build.ps1 is interpreted literally: add the headless build to that script so both changed targets are compiled with the required single entry point and cache environment.

## Step 2 - rig evaluator and fixture
- Added RigModel: unmanaged parameter, part and drawable buffers; precomputed binding strides and mesh deformer/part chains; reusable interpolation buffers and stable insertion sort for render ranks. Constructor failures and repeated Dispose release allocations safely.
- Added a 2-parameter fixture with 2x2-cell warp, 3-key child rotation, 2-binding mesh offsets (first binding fastest), opacity mesh, mask, blend flags and 4x4 PNG. Added rigdump and RigChecks before native sample checks.
- Verified: source inspection and Python fixture construction; hand calculations recorded in RigChecks for rest, extrapolated endpoint and between-key settings. PNG generated with standard PNG chunks and CRCs.
- Next: run the mandatory build command, then default Headless and fixture rigdump. Additional checks cover defaults/single-key bindings, negative rotation, scale/Y translation, opacity/rank flags and allocation measurements at 100 meshes x 300 vertices x four deformer levels.

## Step 3 - first build and runtime verification
- Command: powershell -ExecutionPolicy Bypass -File build.ps1
- Result: both PartnerLive2D (.NET Framework 4.7.2 / C# 7.3) and Headless (.NET 8 / C# 7.3) built successfully, 0 warnings, 0 errors. No sandbox block occurred.
- Runtime environment for all test commands: DOTNET_ROOT=<TOOLS>\dotnet; DOTNET_CLI_HOME=<TOOLS>\dotnet-home; NUGET_PACKAGES=<TOOLS>\nuget-packages; NUGET_HTTP_CACHE_PATH=<TOOLS>\nuget-http-cache; DOTNET_CLI_TELEMETRY_OPTOUT=1; DOTNET_NOLOGO=1.
- Command: & '.\tests\Headless\bin\Release\net8.0\Headless.exe'
- Result: exit 0, RigChecks PASSED, ALL 8 MODELS PASSED. Stress: 0.592 ms/frame at 100 meshes x 300 vertices x four deformer levels, zero managed bytes over 300 updates. Full output: tasks/rigmodel_headless.log.
- Command: & '.\tests\Headless\bin\Release\net8.0\Headless.exe' rigdump 'tests\Headless\rig_fixture\test.model3.json' 'tests\Headless\rig_fixture\params.json'
- Result: exit 0, exactly one JSON object; Face opacity 0.1/rank 0 with expected extrapolated vertices, Mask opacity 0.8/rank 1. Output: tasks/rigmodel_rigdump.json.
- Next: cohesive final review and bounded remediation, then final build/runtime verification including isolated runtime folder without native Cubism DLL.

## Step 4 - consolidated review and remediation
- Reviewed diffs against tasks/rigmodel_baseline plus the new evaluator and checks; native path retains parameter/part semantics and drawable accessors. No src/Unity changes, spec edits, game writes or launches, and no mcp/deploy access.
- Changed final pixel conversion to the literal (pixel-originPixel)/PPU operation order to avoid needless cancellation; retained public origins in model units. Cleaned native constructor indentation and unused imports.
- Added checks for key-interval endpoint clamping and nearest-even DrawOrder rounding, including rank ties.
- Next: one final build and verification pass, then record completion.

## Step 5 - final verification and handoff
- Command: powershell -ExecutionPolicy Bypass -File build.ps1
- Result: exit 0; PartnerLive2D and Headless built successfully with 0 warnings / 0 errors after consolidated remediation.
- Command: & '.\tests\Headless\bin\Release\net8.0\Headless.exe'
- Result: exit 0; RigChecks PASSED and ALL 8 MODELS PASSED. Final stress result: 0.589 ms/frame, zero managed bytes for 300 updates of 100 meshes x 300 vertices x four deformers. This is a headless evaluator measurement, not game-rendering performance.
- Command: & '.\tests\Headless\bin\Release\net8.0\Headless.exe' rigdump 'tests\Headless\rig_fixture\test.model3.json' 'tests\Headless\rig_fixture\params.json'
- Result: exit 0, one JSON object; verified expected mesh IDs, opacity, ranks and all Face vertex coordinates within 1e-4. Output: tasks/rigmodel_rigdump.json.
- Copied only Headless.exe, Headless.dll, Headless.runtimeconfig.json and Headless.deps.json into tasks/rigmodel_no_native; explicitly checked no Live2DCubismCore.dll is present there.
- Command: & '.\tasks\rigmodel_no_native\Headless.exe' rigdump 'tests\Headless\rig_fixture\test.model3.json' 'tests\Headless\rig_fixture\params.json'
- Result: exit 0; same verified rigdump JSON without a native Cubism DLL in the application folder. Output: tasks/rigmodel_rigdump_no_native.json.
- Command: Python artifact verification (json.loads, expected numeric assertions, PNG IHDR/CRC checks, difflib review artifact).
- Result: both dump artifacts match hand calculations; fixture PNG is 4x4 with valid CRCs; final scoped review saved in tasks/rigmodel_final.diff. docs/RIG3_FORMAT.md was not edited. No Unity files or game installation contents were changed.
- Files changed: src/Core/CubismModel.cs; new src/Core/MocModel.cs and RigModel.cs; src/Framework/CubismAvatar.cs; tests/Headless/Program.cs and Headless.csproj; new tests/Headless/RigChecks.cs and rig_fixture/{test.rig3.json,test.model3.json,test.png,params.json}; build.ps1; this worklog and scoped review/verification artifacts under tasks/.
- Remaining limitations: no game launch or Unity visual rendering check (explicit task constraint); no comparison to external Python reference source was requested or performed. Normative behavior is checked against hand-computed fixture values and targeted headless assertions.
DONE
