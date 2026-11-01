# rig3 Version 2 C# QA report

PASS

Reviewed qa_cs.md, cs_task.md, the full docs/RIG3_FORMAT.md, the worker worklog, preserved baseline and implementation diff, and current evaluator, look integration, Headless tests, fixtures and rigdump path. No confirmed implementation defect was found. No production source fix was necessary.

## Normative review

- Version gating: only Version 1 and 2 load. Camera and Depths additions are read only for Version 2; Version 1 retains its prior path. Missing Camera leaves projection disabled; missing Depths is read as z=0.
- Parameters: camera IDs are resolved at load, missing/null IDs resolve to -1 and read as zero. Set/AddParameterValue clamps through the existing model API. Existing tests exercise out-of-range camera inputs and missing IDs.
- Projection: tx=x*OrbitPx, ty=y*OrbitPx, D=Focal-w*DollyPx. With q=p-C, implementation uses p + [t+(q-t)*D/max(1,D-z)]*Focal/D - q*Focal/max(1,Focal-z). The two Center terms in the specified projection difference cancel. This is the normative formula, including the clamp in each invocation of proj.
- Rest identity: x=y=w=0 bypasses projection entirely, preserving the original floats exactly, including vertices near the depth clamp.
- Order and depth: rest vertices plus interpolated offsets pass through the existing inner-to-root deformer chain; projection follows, then origin subtraction / PPU conversion and y inversion. Depths are indexed per vertex and never transformed by deformers.
- Clamp: max(1,D-z) handles D-z equal to 1, .5, zero and negative values. Tests exercise these cases with distinct per-vertex depths. The reference/rest projection applies the same clamp at Focal-z.
- Render behavior: projection only changes x/y; opacity multiplication, flags, masks, sorting and UVs retain their existing paths. Existing tests verify opacity, order and masks with the camera active.
- Look: ParamCamX receives dragX at factor 1 and ParamCamY receives dragY at factor 1. Missing IDs take the existing -1 no-op path in AddParameterValue. Headless verifies both camera following and the v1 missing-ID case.
- Allocations: Update uses scalar locals, preallocated arrays, indexed loops and stable insertion sorting; it performs no managed or unmanaged allocation. Runtime checks pass for 1,000 camera-active Updates with zero managed bytes; the existing 100-mesh / 300-vertex / 4-deformer stress check also reports zero managed bytes.
- rigdump calls model.Update after applying input parameters and exports those positions, so it includes camera projection.

## Independent tests added

Only tests/Headless/RigChecks.cs was changed by this QA pass. Added RigCameraQaCases, called from RigCameraChecks. Both cases use the fixture's real offset/deformer path, yielding p=(15,15), origin=(5,5), PPU=10. Assertions use the existing 1e-4 tolerance. Expectations are hand computed in source comments, without calling a second implementation of the projection formula.

1. Negative depth: Focal=120, Center=(3,-5), OrbitPx=8, DollyPx=40, z=-20; controls=(.5,-.5,.5). Thus D=100, t=(4,-4), p-C=(12,20). Current projection minus C=(64/5,96/5), rest=(72/7,120/7). Final canvas p'=(613/35,597/35), model=(219/175,-211/175). Both coordinates PASS, as does neutral identity for this negative-depth vertex.
2. Pull-back with different constants: Focal=60, Center=(-5,10), OrbitPx=12, DollyPx=20, z=30; controls=(-.5,.25,-1). Thus D=80, t=(-6,3), p-C=(20,5). Current projection minus C=(267/10,93/20), rest=(40,10). Final canvas p'=(17/10,193/20), model=(-.33,-.465). Both coordinates PASS.

## Commands rerun and results

All commands below ran from <PROJECT>.

1. `powershell -ExecutionPolicy Bypass -File build.ps1`
   - Exit 0. net472 mod and net8 Headless both built with 0 warnings / 0 errors.
   - Log: tasks/v2_qa_build.log.
   - Used only the prescribed build entry point, without -Install.
2. `<TOOLS>\dotnet\dotnet.exe tests\Headless\bin\Release\net8.0\Headless.dll`
   - Exit 0. RigCamera QA PASSED; RigChecks PASSED; ALL 8 MODELS PASSED.
   - Stress check: 0.783 ms/frame, 0 managed bytes on this run.
   - Log: tasks/v2_qa_headless.log.
3. `<TOOLS>\dotnet\dotnet.exe tests\Headless\bin\Release\net8.0\Headless.dll rigdump tests\Headless\rig_fixture\test.model3.json tests\Headless\rig_fixture\params.json`
   - Exit 0. Output: tasks/v2_qa_rigdump_v1.json.
   - Face vertices agree with hand expectations (.52,-.5,.82,-1,-.23,-1.25) within 1e-4.
4. `<TOOLS>\dotnet\dotnet.exe tests\Headless\bin\Release\net8.0\Headless.dll rigdump tests\Headless\rig_fixture_v2\test.model3.json tests\Headless\rig_fixture_v2\params.json`
   - Exit 0. Output: tasks/v2_qa_rigdump_v2.json.
   - Independently checked every DepthMesh vertex at 1e-4. For the fixture's combined controls, hand-simplified model coordinates are X=2.25-100/(100-z), Y=-.375-150/max(1,80-z)+100/(100-z). In particular z=50 exports (.25,-3.375), confirming projected output.
5. Additional read-only verification: inline Python using pathlib/difflib reconstructed the worker's pre-QA RigChecks and checked all four worker code/project diffs against tasks/v2_cs_final.diff; compared preserved spec, build script and v1 rig fixture bytes; compared every source fixture file with its built copy. Inline PowerShell parsed both new rigdump outputs and checked the expectations above.
   - Exit 0; all checks PASS. QA-only source diff: tasks/v2_qa_changes.diff.

## Scope and remaining boundary

FIXED: none required. FAILING: none found in the requested scope. QA changes add executable coverage and evidence; runtime behavior is unchanged. Spec, build script and original v1 rig fixture match the preserved baseline byte for byte. No Git metadata exists, so review used the worker's preserved baseline/diff rather than Git status. The normative equation itself is undefined when D=0 (Focal/D); no new behavior was invented for that unspecified singular input, and it is not reported as an implementation defect. Physical rendering in the game was not part of these prescribed Headless checks.

DONE
