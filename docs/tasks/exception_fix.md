# Startup exception: cause, fix, and evidence

## Finding

This is a real dispatcher lifetime bug. The observed failure is a secondary completion-signal exception after a request has timed out; it does not by itself establish a game crash or a failed select-view operation.

The supplied `<WORK_ROOT>\select_poc\exception.txt` shows the control page starting at 22:48:49.667 and the exception at 22:48:55.347. Its stack is:

```
SafeHandle.DangerousAddRef
NativeEventCalls.SetEvent
EventWaitHandle.Set
MainThread.<Call>b__0
MainThread.Drain
```

Previously `MainThread.Call<T>` created a `ManualResetEvent`, queued a closure, waited up to 5000 ms, then disposed the event on leaving its `using` block. Timeout does not remove the closure from the queue. When Unity subsequently drains that task (or an already-running task finishes after timeout), its `finally` calls `done.Set()` on the disposed event. This explains the exact stack and is consistent with the approximately 5.68-second startup interval. The excerpt cannot identify which endpoint requested the task or why Unity exceeded the timeout.

`CallAsync<T>` had the same hazard: it closed the event after waiting, while screenshot startup/completion callbacks could still signal it later.

## Scope investigation

- HTTP input streams are read/disposed on request worker threads; response writes and closes are guarded in `SendBytes`. The supplied stack identifies a wait event in the queued dispatcher closure, not an HTTP stream or listener context.
- Select-view status/tuning endpoints call the existing dispatcher. The new select code can therefore encounter this shared defect, but the stack does not establish that select-view code caused the startup delay. Select layout/session/debug disposal handles Unity objects and restores layout; it is not the wait handle in this trace.
- File reads in status/model metadata do not match `EventWaitHandle.Set` in the supplied stack. No file-handle or select-view change is needed for this failure.

## Fix

- Extracted the existing `MainThread` class from `ControlServer.cs` to `src/Unity/MainThread.cs`, allowing Headless to compile the actual dispatcher without the HTTP/Unity implementation. A comparison against the preserved original confirmed that `ControlServer.cs` changed only by removing this class.
- Added a private completion wrapper owning the wait event. Its `Set` and `Dispose` operations use the same lock; disposal marks the wrapper closed before closing its event, and late signals return without touching that event. This closes the check/dispose race rather than merely catching the resulting exception.
- Used the wrapper in both `Call` and `CallAsync`. The latter now also disposes through `using` on exceptional wait exits.
- Preserved method signatures, default five-second waits, timeout messages, result/error propagation, request timeout-to-503 mapping, queued work execution, and screenshot callbacks. Timeout still does not cancel queued operations; this preserves existing behavior.

Only dispatcher lifetime handling and its regression harness changed. There are no dependency, endpoint, select-view, settings, or deployment changes.

## Verification

Build command (only build entry point used):

```
powershell -ExecutionPolicy Bypass -File build.ps1
```

Exit 0. Both the net472 mod and net8.0 Headless build completed with zero warnings and zero errors. See `tasks/exception_fix_build.log`. Output: `bin/PartnerLive2D.dll`.

Harness command:

```
$env:DOTNET_ROOT = '<TOOLS>\dotnet'
& '.\tests\Headless\bin\Release\net8.0\Headless.exe'
```

Exit 0. See `tasks/exception_fix_headless.log`:

- 13 dispatcher lifetime assertions passed, including deterministic timeout-before-drain, late sync failure, late async callbacks (including repeated callbacks), late async startup failure, success values, and exception identity propagation.
- 100 bounded cross-thread completion/timeout iterations completed without dispatcher errors.
- Existing 23 select-preset and 17 select-router assertions passed; stationary router allocation remained zero across 1000 frames.
- Existing rig/math checks passed; `ALL 8 MODELS PASSED`.

The deterministic timeout-before-drain cases exercise the ordering that caused the supplied failure; no wall-clock startup delay is required to trigger that ordering. Tests compile the real dispatcher with a logging double that records any exceptions escaping tasks through `Drain`.

Preserved affected originals: `tasks/exception_fix_before/`. Reviewable source/test comparison: `tasks/exception_fix_changes.diff`. Progress appended to `tasks/select_poc_worklog.md`.

## Remaining evidence boundary

No game was started and no installation was performed. The required build reads existing game reference assemblies; no writes were made to `<GAME_ROOT>`. The actual startup latency, triggering endpoint, Mono runtime execution, and in-game visual behavior were not tested. The fix addresses the evidenced closed-wait-handle failure; it does not claim to eliminate main-thread loading timeouts.

DONE
