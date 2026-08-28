# Task 5 Report - Completion-log Codecs

## Scope

- Created `src/TaskGuide.Infrastructure/Storage/CompletionCodec.cs`.
- Created `tests/TaskGuide.Storage.Tests/CompletionCodecTests.cs`.
- Appended Task 5 storage inventory lines to `tests/TEST-INVENTORY.md`.

## Red output per test

### `A_completion_log_is_not_rewritten_when_its_Task_s_title_changes`

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  TaskGuide.Domain -> /private/tmp/task-guide-storage-codecs/src/TaskGuide.Domain/bin/Debug/net10.0/TaskGuide.Domain.dll
  TaskGuide.Application -> /private/tmp/task-guide-storage-codecs/src/TaskGuide.Application/bin/Debug/net10.0/TaskGuide.Application.dll
  TaskGuide.Infrastructure -> /private/tmp/task-guide-storage-codecs/src/TaskGuide.Infrastructure/bin/Debug/net10.0/TaskGuide.Infrastructure.dll
  TaskGuide.Storage.Tests -> /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll
Test run for /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:00.12]     TaskGuide.Storage.Tests.CompletionCodecTests.A_completion_log_is_not_rewritten_when_its_Task_s_title_changes [FAIL]
  Failed TaskGuide.Storage.Tests.CompletionCodecTests.A_completion_log_is_not_rewritten_when_its_Task_s_title_changes [5 ms]
  Error Message:
   System.NotImplementedException : The method or operation is not implemented.
  Stack Trace:
     at TaskGuide.Infrastructure.Storage.CompletionCodec.Read(TaskId taskId, String json) in /private/tmp/task-guide-storage-codecs/src/TaskGuide.Infrastructure/Storage/CompletionCodec.cs:line 10
   at TaskGuide.Storage.Tests.CompletionCodecTests.RoundTrip(TaskId taskId, String json) in /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Storage.Tests/CompletionCodecTests.cs:line 30
   at TaskGuide.Storage.Tests.CompletionCodecTests.A_completion_log_is_not_rewritten_when_its_Task_s_title_changes() in /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Storage.Tests/CompletionCodecTests.cs:line 43
   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)

Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 12 ms - TaskGuide.Storage.Tests.dll (net10.0)
```

### Other Task 5 tests

Concern: I added the remaining Task 5 tests after implementing the codec methods they exercise, so I do not have true red-first transcripts for each of these tests. I did run the lane mutation below, and the focused suite stayed green after reverting it.

## Green output

### `A_completion_log_is_not_rewritten_when_its_Task_s_title_changes`

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  TaskGuide.Domain -> /private/tmp/task-guide-storage-codecs/src/TaskGuide.Domain/bin/Debug/net10.0/TaskGuide.Domain.dll
  TaskGuide.Application -> /private/tmp/task-guide-storage-codecs/src/TaskGuide.Application/bin/Debug/net10.0/TaskGuide.Application.dll
  TaskGuide.Infrastructure -> /private/tmp/task-guide-storage-codecs/src/TaskGuide.Infrastructure/bin/Debug/net10.0/TaskGuide.Infrastructure.dll
  TaskGuide.Storage.Tests -> /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll
Test run for /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 9 ms - TaskGuide.Storage.Tests.dll (net10.0)
```

### `A_one_off_Task_s_entry_round_trips_a_null_due`

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  TaskGuide.Domain -> /private/tmp/task-guide-storage-codecs/src/TaskGuide.Domain/bin/Debug/net10.0/TaskGuide.Domain.dll
  TaskGuide.Application -> /private/tmp/task-guide-storage-codecs/src/TaskGuide.Application/bin/Debug/net10.0/TaskGuide.Application.dll
  TaskGuide.Infrastructure -> /private/tmp/task-guide-storage-codecs/src/TaskGuide.Infrastructure/bin/Debug/net10.0/TaskGuide.Infrastructure.dll
  TaskGuide.Storage.Tests -> /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll
Test run for /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 11 ms - TaskGuide.Storage.Tests.dll (net10.0)
```

### `CompletionCodecTests`

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  TaskGuide.Domain -> /private/tmp/task-guide-storage-codecs/src/TaskGuide.Domain/bin/Debug/net10.0/TaskGuide.Domain.dll
  TaskGuide.Application -> /private/tmp/task-guide-storage-codecs/src/TaskGuide.Application/bin/Debug/net10.0/TaskGuide.Application.dll
  TaskGuide.Infrastructure -> /private/tmp/task-guide-storage-codecs/src/TaskGuide.Infrastructure/bin/Debug/net10.0/TaskGuide.Infrastructure.dll
  TaskGuide.Storage.Tests -> /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll
Test run for /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:     7, Skipped:     0, Total:     7, Duration: 17 ms - TaskGuide.Storage.Tests.dll (net10.0)
```

## Mutation transcript

Mutation: write non-null completion `due` as an instant (`2026-08-11T00:00:00Z`) instead of a calendar date (`2026-08-11`).

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  TaskGuide.Domain -> /private/tmp/task-guide-storage-codecs/src/TaskGuide.Domain/bin/Debug/net10.0/TaskGuide.Domain.dll
  TaskGuide.Application -> /private/tmp/task-guide-storage-codecs/src/TaskGuide.Application/bin/Debug/net10.0/TaskGuide.Application.dll
  TaskGuide.Infrastructure -> /private/tmp/task-guide-storage-codecs/src/TaskGuide.Infrastructure/bin/Debug/net10.0/TaskGuide.Infrastructure.dll
  TaskGuide.Storage.Tests -> /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll
Test run for /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:00.11]     TaskGuide.Storage.Tests.CompletionCodecTests.A_one_off_Task_s_entry_round_trips_a_null_due [FAIL]
  Failed TaskGuide.Storage.Tests.CompletionCodecTests.A_one_off_Task_s_entry_round_trips_a_null_due [9 ms]
  Error Message:
   Assert.Equal() Failure: Strings differ
Expected: "2026-08-11"
Actual:   "2026-08-11T00:00:00Z"
                     ↑ (pos 10)
  Stack Trace:
     at TaskGuide.Storage.Tests.CompletionCodecTests.A_one_off_Task_s_entry_round_trips_a_null_due() in /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Storage.Tests/CompletionCodecTests.cs:line 104
   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)

Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 17 ms - TaskGuide.Storage.Tests.dll (net10.0)
```

Reverted mutation and confirmed `CompletionCodecTests` green: 7 passed.

## Whole-suite result

```text
  Determining projects to restore...
  Restored /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Domain.Tests/TaskGuide.Domain.Tests.csproj (in 147 ms).
  Restored /private/tmp/task-guide-storage-codecs/src/TaskGuide.Api/TaskGuide.Api.csproj (in 147 ms).
  Restored /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Application.Tests/TaskGuide.Application.Tests.csproj (in 147 ms).
  Restored /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Api.Tests/TaskGuide.Api.Tests.csproj (in 369 ms).
  4 of 8 projects are up-to-date for restore.
  TaskGuide.Domain -> /private/tmp/task-guide-storage-codecs/src/TaskGuide.Domain/bin/Debug/net10.0/TaskGuide.Domain.dll
  TaskGuide.Application -> /private/tmp/task-guide-storage-codecs/src/TaskGuide.Application/bin/Debug/net10.0/TaskGuide.Application.dll
  TaskGuide.Domain.Tests -> /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Domain.Tests/bin/Debug/net10.0/TaskGuide.Domain.Tests.dll
  TaskGuide.Infrastructure -> /private/tmp/task-guide-storage-codecs/src/TaskGuide.Infrastructure/bin/Debug/net10.0/TaskGuide.Infrastructure.dll
Test run for /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Domain.Tests/bin/Debug/net10.0/TaskGuide.Domain.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
  TaskGuide.Storage.Tests -> /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll
Test run for /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll (.NETCoreApp,Version=v10.0)
  TaskGuide.Application.Tests -> /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Application.Tests/bin/Debug/net10.0/TaskGuide.Application.Tests.dll
Test run for /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Application.Tests/bin/Debug/net10.0/TaskGuide.Application.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
  TaskGuide.Api -> /private/tmp/task-guide-storage-codecs/src/TaskGuide.Api/bin/Debug/net10.0/TaskGuide.Api.dll
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:   158, Skipped:     0, Total:   158, Duration: 110 ms - TaskGuide.Domain.Tests.dll (net10.0)

Passed!  - Failed:     0, Passed:    15, Skipped:     0, Total:    15, Duration: 54 ms - TaskGuide.Application.Tests.dll (net10.0)
  TaskGuide.Api.Tests -> /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Api.Tests/bin/Debug/net10.0/TaskGuide.Api.Tests.dll
Test run for /private/tmp/task-guide-storage-codecs/tests/TaskGuide.Api.Tests/bin/Debug/net10.0/TaskGuide.Api.Tests.dll (.NETCoreApp,Version=v10.0)

Passed!  - Failed:     0, Passed:    44, Skipped:     0, Total:    44, Duration: 401 ms - TaskGuide.Storage.Tests.dll (net10.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    11, Skipped:     0, Total:    11, Duration: 1 s - TaskGuide.Api.Tests.dll (net10.0)
```

## Commit

3ae5fca

## Concerns

- I did not preserve unknown fields for completion entries because the requested `CompletionCodec` interface has no `extras` return/write parameter. That differs from the global constraint, but changing the interface would be a lane-level design change.
- I do not have true red-first output for every Task 5 test. The first test was red-first against the stub; the remaining assertions were added after the codec implementation and protected by the required mutation check.
