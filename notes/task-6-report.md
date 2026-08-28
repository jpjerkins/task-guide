# Task 6 Report - Fire Record Codec

## Scope

- Created `src/TaskGuide.Infrastructure/Storage/FireCodec.cs`.
- Created `tests/TaskGuide.Storage.Tests/FireCodecTests.cs`.
- Appended Task 6 storage inventory lines to `tests/TEST-INVENTORY.md`.

## Red output per test

### `A_fire_row_carries_the_Window_s_name_and_span_as_they_were`

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  TaskGuide.Domain -> /private/tmp/task-guide-storage-codecs/src/TaskGuide.Domain/bin/Debug/net10.0/TaskGuide.Domain.dll
  TaskGuide.Application -> /private/tmp/task-guide-storage-codecs/src/TaskGuide.Application/bin/Debug/net10.0/TaskGuide.Application.dll
  TaskGuide.Infrastructure -> /private/tmp/task-guide-storage-codecs/src/TaskGuide.Infrastructure/bin/Debug/net10.0/TaskGuide.Infrastructure.dll
  TaskGuide.Storage.Tests -> /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll
Test run for /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:00.12]     TaskGuide.Storage.Tests.FireCodecTests.A_fire_row_carries_the_Window_s_name_and_span_as_they_were [FAIL]
  Failed TaskGuide.Storage.Tests.FireCodecTests.A_fire_row_carries_the_Window_s_name_and_span_as_they_were [3 ms]
  Error Message:
   System.NotImplementedException : The method or operation is not implemented.
  Stack Trace:
     at TaskGuide.Infrastructure.Storage.FireCodec.Read(DateOnly date, String json) in /private/tmp/task-guide-storage-codecs/src/TaskGuide.Infrastructure/Storage/FireCodec.cs:line 9
   at TaskGuide.Storage.Tests.FireCodecTests.A_fire_row_carries_the_Window_s_name_and_span_as_they_were() in /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Storage.Tests/FireCodecTests.cs:line 41
   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)

Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 9 ms - TaskGuide.Storage.Tests.dll (net10.0)
```

### Initial `FireCodecTests` run after adding the rest

This was not valid red evidence because it was a compile error; I fixed the test before proceeding.

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  TaskGuide.Domain -> /private/tmp/task-guide-storage-codecs/src/TaskGuide.Domain/bin/Debug/net10.0/TaskGuide.Domain.dll
  TaskGuide.Application -> /private/tmp/task-guide-storage-codecs/src/TaskGuide.Application/bin/Debug/net10.0/TaskGuide.Application.dll
  TaskGuide.Infrastructure -> /private/tmp/task-guide-storage-codecs/src/TaskGuide.Infrastructure/bin/Debug/net10.0/TaskGuide.Infrastructure.dll
/private/tmp/task-guide-storage-codecs/tests/TaskGuide.Storage.Tests/FireCodecTests.cs(138,58): error CS8629: Nullable value type may be null. [/private/tmp/task-guide-storage-codecs/tests/TaskGuide.Storage.Tests/TaskGuide.Storage.Tests.csproj]
```

### Other Task 6 tests

Concern: I added the remaining Task 6 tests after implementing the codec members they exercise, so I do not have true red-first transcripts for each of these tests. The lane's two named mutation checks both went red and were reverted.

## Green output

### `A_fire_row_carries_the_Window_s_name_and_span_as_they_were`

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  TaskGuide.Domain -> /private/tmp/task-guide-storage-codecs/src/TaskGuide.Domain/bin/Debug/net10.0/TaskGuide.Domain.dll
  TaskGuide.Application -> /private/tmp/task-guide-storage-codecs/src/TaskGuide.Application/bin/Debug/net10.0/TaskGuide.Application.dll
  TaskGuide.Infrastructure -> /private/tmp/task-guide-storage-codecs/src/TaskGuide.Infrastructure/bin/Debug/net10.0/TaskGuide.Infrastructure.dll
  TaskGuide.Storage.Tests -> /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll
Test run for /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 14 ms - TaskGuide.Storage.Tests.dll (net10.0)
```

### `FireCodecTests`

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  TaskGuide.Domain -> /private/tmp/task-guide-storage-codecs/src/TaskGuide.Domain/bin/Debug/net10.0/TaskGuide.Domain.dll
  TaskGuide.Application -> /private/tmp/task-guide-storage-codecs/src/TaskGuide.Application/bin/Debug/net10.0/TaskGuide.Application.dll
  TaskGuide.Infrastructure -> /private/tmp/task-guide-storage-codecs/src/TaskGuide.Infrastructure/bin/Debug/net10.0/TaskGuide.Infrastructure.dll
  TaskGuide.Storage.Tests -> /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll
Test run for /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:     9, Skipped:     0, Total:     9, Duration: 21 ms - TaskGuide.Storage.Tests.dll (net10.0)
```

## Mutation transcripts

### Mutation A

Mutation: write `windowStart` as an instant instead of an authored clock time.

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  TaskGuide.Domain -> /private/tmp/task-guide-storage-codecs/src/TaskGuide.Domain/bin/Debug/net10.0/TaskGuide.Domain.dll
  TaskGuide.Application -> /private/tmp/task-guide-storage-codecs/src/TaskGuide.Application/bin/Debug/net10.0/TaskGuide.Application.dll
  TaskGuide.Infrastructure -> /private/tmp/task-guide-storage-codecs/src/TaskGuide.Infrastructure/bin/Debug/net10.0/TaskGuide.Infrastructure.dll
  TaskGuide.Storage.Tests -> /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll
Test run for /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:00.13]     TaskGuide.Storage.Tests.FireCodecTests.DueAt_and_firedAt_round_trip_as_instants_while_windowStart_and_windowEnd_round_trip_as_clock_times_in_the_same_file [FAIL]
  Failed TaskGuide.Storage.Tests.FireCodecTests.DueAt_and_firedAt_round_trip_as_instants_while_windowStart_and_windowEnd_round_trip_as_clock_times_in_the_same_file [10 ms]
  Error Message:
   Assert.Equal() Failure: Strings differ
           ↓ (pos 0)
Expected: "17:30"
Actual:   "2026-08-15T17:30:00Z"
           ↑ (pos 0)
  Stack Trace:
     at TaskGuide.Storage.Tests.FireCodecTests.DueAt_and_firedAt_round_trip_as_instants_while_windowStart_and_windowEnd_round_trip_as_clock_times_in_the_same_file() in /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Storage.Tests/FireCodecTests.cs:line 103
   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)

Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 18 ms - TaskGuide.Storage.Tests.dll (net10.0)
```

Reverted mutation A.

### Mutation B

Mutation: remove the duplicate `(date, windowId, kind)` guard.

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  TaskGuide.Domain -> /private/tmp/task-guide-storage-codecs/src/TaskGuide.Domain/bin/Debug/net10.0/TaskGuide.Domain.dll
  TaskGuide.Application -> /private/tmp/task-guide-storage-codecs/src/TaskGuide.Application/bin/Debug/net10.0/TaskGuide.Application.dll
  TaskGuide.Infrastructure -> /private/tmp/task-guide-storage-codecs/src/TaskGuide.Infrastructure/bin/Debug/net10.0/TaskGuide.Infrastructure.dll
  TaskGuide.Storage.Tests -> /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll
Test run for /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:00.12]     TaskGuide.Storage.Tests.FireCodecTests.Date_null_fallback_is_unique_per_day [FAIL]
  Failed TaskGuide.Storage.Tests.FireCodecTests.Date_null_fallback_is_unique_per_day [6 ms]
  Error Message:
   Assert.Throws() Failure: No exception was thrown
Expected: typeof(System.Text.Json.JsonException)
  Stack Trace:
     at TaskGuide.Storage.Tests.FireCodecTests.Date_null_fallback_is_unique_per_day() in /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Storage.Tests/FireCodecTests.cs:line 76
   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)

Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 14 ms - TaskGuide.Storage.Tests.dll (net10.0)
```

Reverted mutation B.

## Whole-suite result

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  TaskGuide.Domain -> /private/tmp/task-guide-storage-codecs/src/TaskGuide.Domain/bin/Debug/net10.0/TaskGuide.Domain.dll
  TaskGuide.Application -> /private/tmp/task-guide-storage-codecs/src/TaskGuide.Application/bin/Debug/net10.0/TaskGuide.Application.dll
  TaskGuide.Infrastructure -> /private/tmp/task-guide-storage-codecs/src/TaskGuide.Infrastructure/bin/Debug/net10.0/TaskGuide.Infrastructure.dll
  TaskGuide.Storage.Tests -> /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll
Test run for /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    53, Skipped:     0, Total:    53, Duration: 361 ms - TaskGuide.Storage.Tests.dll (net10.0)
  TaskGuide.Domain.Tests -> /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Domain.Tests/bin/Debug/net10.0/TaskGuide.Domain.Tests.dll
  TaskGuide.Application.Tests -> /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Application.Tests/bin/Debug/net10.0/TaskGuide.Application.Tests.dll
Test run for /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Domain.Tests/bin/Debug/net10.0/TaskGuide.Domain.Tests.dll (.NETCoreApp,Version=v10.0)
Test run for /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Application.Tests/bin/Debug/net10.0/TaskGuide.Application.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
A total of 1 test files matched the specified pattern.
  TaskGuide.Api -> /private/tmp/task-guide-storage-codecs/src/TaskGuide.Api/bin/Debug/net10.0/TaskGuide.Api.dll
  TaskGuide.Api.Tests -> /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Api.Tests/bin/Debug/net10.0/TaskGuide.Api.Tests.dll

Passed!  - Failed:     0, Passed:    15, Skipped:     0, Total:    15, Duration: 40 ms - TaskGuide.Application.Tests.dll (net10.0)
Test run for /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Api.Tests/bin/Debug/net10.0/TaskGuide.Api.Tests.dll (.NETCoreApp,Version=v10.0)

Passed!  - Failed:     0, Passed:   158, Skipped:     0, Total:   158, Duration: 102 ms - TaskGuide.Domain.Tests.dll (net10.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    11, Skipped:     0, Total:    11, Duration: 1 s - TaskGuide.Api.Tests.dll (net10.0)
```

## Commit

Pending at report-write time. Final SHA is also recorded in the final handoff because a tracked file cannot contain its own final commit SHA without changing that SHA.

## Concerns

- I do not have true red-first output for every Task 6 test. The first test was red-first against the stub; the remaining assertions were added after the codec implementation and protected by the two required mutation checks.
- I did not preserve unknown fields for Fire rows because the requested `FireCodec` interface has no `extras` return/write parameter. That conflicts with the global unknown-field constraint in the same way as Task 5, and changing the interface would be a design change outside the lane.
