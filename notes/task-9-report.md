# Task 9 Report: Fire Retention

## Scope

- Worktree: `/Users/phil/dev/task-guide-task9`
- Base: `storage-sequential` HEAD `3235730`
- Inventory section: `fires older than 30 days are unlinked as whole files`
- Commit sha: final SHA cannot be embedded in this committed report without changing the commit hash; see the delivery note for the final SHA.

## Red And Green Output Per Test

### `Fires_older_than_30_days_are_unlinked_as_whole_files`

Red, from the required throwing stub:

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  TaskGuide.Domain -> /Users/phil/dev/task-guide-task9/src/TaskGuide.Domain/bin/Debug/net10.0/TaskGuide.Domain.dll
  TaskGuide.Application -> /Users/phil/dev/task-guide-task9/src/TaskGuide.Application/bin/Debug/net10.0/TaskGuide.Application.dll
  TaskGuide.Infrastructure -> /Users/phil/dev/task-guide-task9/src/TaskGuide.Infrastructure/bin/Debug/net10.0/TaskGuide.Infrastructure.dll
  TaskGuide.Storage.Tests -> /Users/phil/dev/task-guide-task9/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll
Test run for /Users/phil/dev/task-guide-task9/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:00.11]     TaskGuide.Storage.Tests.FireRetentionTests.Fires_older_than_30_days_are_unlinked_as_whole_files [FAIL]
  Failed TaskGuide.Storage.Tests.FireRetentionTests.Fires_older_than_30_days_are_unlinked_as_whole_files [4 ms]
  Error Message:
   System.NotImplementedException : The method or operation is not implemented.
  Stack Trace:
     at TaskGuide.Infrastructure.Storage.FireRetention.Sweep(String dataDir, DateOnly today) in /Users/phil/dev/task-guide-task9/src/TaskGuide.Infrastructure/Storage/FireRetention.cs:line 5
   at TaskGuide.Storage.Tests.FireRetentionTests.Fires_older_than_30_days_are_unlinked_as_whole_files() in /Users/phil/dev/task-guide-task9/tests/TaskGuide.Storage.Tests/FireRetentionTests.cs:line 31
   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)

Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 11 ms - TaskGuide.Storage.Tests.dll (net10.0)
```

Green:

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  TaskGuide.Domain -> /Users/phil/dev/task-guide-task9/src/TaskGuide.Domain/bin/Debug/net10.0/TaskGuide.Domain.dll
  TaskGuide.Application -> /Users/phil/dev/task-guide-task9/src/TaskGuide.Application/bin/Debug/net10.0/TaskGuide.Application.dll
  TaskGuide.Infrastructure -> /Users/phil/dev/task-guide-task9/src/TaskGuide.Infrastructure/bin/Debug/net10.0/TaskGuide.Infrastructure.dll
  TaskGuide.Storage.Tests -> /Users/phil/dev/task-guide-task9/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll
Test run for /Users/phil/dev/task-guide-task9/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 10 ms - TaskGuide.Storage.Tests.dll (net10.0)
```

### `A_fire_file_exactly_30_days_old_is_kept_the_boundary_must_not_drift`

Red, with temporary boundary mutation `<= 30` to `< 30`:

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  TaskGuide.Domain -> /Users/phil/dev/task-guide-task9/src/TaskGuide.Domain/bin/Debug/net10.0/TaskGuide.Domain.dll
  TaskGuide.Application -> /Users/phil/dev/task-guide-task9/src/TaskGuide.Application/bin/Debug/net10.0/TaskGuide.Application.dll
  TaskGuide.Infrastructure -> /Users/phil/dev/task-guide-task9/src/TaskGuide.Infrastructure/bin/Debug/net10.0/TaskGuide.Infrastructure.dll
  TaskGuide.Storage.Tests -> /Users/phil/dev/task-guide-task9/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll
Test run for /Users/phil/dev/task-guide-task9/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:00.11]     TaskGuide.Storage.Tests.FireRetentionTests.A_fire_file_exactly_30_days_old_is_kept_the_boundary_must_not_drift [FAIL]
[xUnit.net 00:00:00.12]     TaskGuide.Storage.Tests.FireRetentionTests.Fires_older_than_30_days_are_unlinked_as_whole_files [FAIL]
  Failed TaskGuide.Storage.Tests.FireRetentionTests.A_fire_file_exactly_30_days_old_is_kept_the_boundary_must_not_drift [7 ms]
  Error Message:
   Assert.Equal() Failure: Values differ
Expected: 0
Actual:   1
  Stack Trace:
     at TaskGuide.Storage.Tests.FireRetentionTests.A_fire_file_exactly_30_days_old_is_kept_the_boundary_must_not_drift() in /Users/phil/dev/task-guide-task9/tests/TaskGuide.Storage.Tests/FireRetentionTests.cs:line 45
   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
  Failed TaskGuide.Storage.Tests.FireRetentionTests.Fires_older_than_30_days_are_unlinked_as_whole_files [< 1 ms]
  Error Message:
   Assert.Equal() Failure: Values differ
Expected: 1
Actual:   2
  Stack Trace:
     at TaskGuide.Storage.Tests.FireRetentionTests.Fires_older_than_30_days_are_unlinked_as_whole_files() in /Users/phil/dev/task-guide-task9/tests/TaskGuide.Storage.Tests/FireRetentionTests.cs:line 33
   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)

Failed!  - Failed:     2, Passed:     0, Skipped:     0, Total:     2, Duration: 22 ms - TaskGuide.Storage.Tests.dll (net10.0)
```

Green after reverting the mutation:

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  TaskGuide.Domain -> /Users/phil/dev/task-guide-task9/src/TaskGuide.Domain/bin/Debug/net10.0/TaskGuide.Domain.dll
  TaskGuide.Application -> /Users/phil/dev/task-guide-task9/src/TaskGuide.Application/bin/Debug/net10.0/TaskGuide.Application.dll
  TaskGuide.Infrastructure -> /Users/phil/dev/task-guide-task9/src/TaskGuide.Infrastructure/bin/Debug/net10.0/TaskGuide.Infrastructure.dll
  TaskGuide.Storage.Tests -> /Users/phil/dev/task-guide-task9/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll
Test run for /Users/phil/dev/task-guide-task9/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:     2, Skipped:     0, Total:     2, Duration: 14 ms - TaskGuide.Storage.Tests.dll (net10.0)
```

### `A_file_in_fires_whose_name_is_not_a_date_is_left_untouched`

Red, with temporary mutation that deletes files whose names do not parse:

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  TaskGuide.Domain -> /Users/phil/dev/task-guide-task9/src/TaskGuide.Domain/bin/Debug/net10.0/TaskGuide.Domain.dll
  TaskGuide.Application -> /Users/phil/dev/task-guide-task9/src/TaskGuide.Application/bin/Debug/net10.0/TaskGuide.Application.dll
  TaskGuide.Infrastructure -> /Users/phil/dev/task-guide-task9/src/TaskGuide.Infrastructure/bin/Debug/net10.0/TaskGuide.Infrastructure.dll
  TaskGuide.Storage.Tests -> /Users/phil/dev/task-guide-task9/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll
Test run for /Users/phil/dev/task-guide-task9/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:00.11]     TaskGuide.Storage.Tests.FireRetentionTests.A_file_in_fires_whose_name_is_not_a_date_is_left_untouched [FAIL]
  Failed TaskGuide.Storage.Tests.FireRetentionTests.A_file_in_fires_whose_name_is_not_a_date_is_left_untouched [1 ms]
  Error Message:
   Assert.Equal() Failure: Values differ
Expected: 0
Actual:   1
  Stack Trace:
     at TaskGuide.Storage.Tests.FireRetentionTests.A_file_in_fires_whose_name_is_not_a_date_is_left_untouched() in /Users/phil/dev/task-guide-task9/tests/TaskGuide.Storage.Tests/FireRetentionTests.cs:line 59
   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)

Failed!  - Failed:     1, Passed:     2, Skipped:     0, Total:     3, Duration: 19 ms - TaskGuide.Storage.Tests.dll (net10.0)
```

Green after reverting the mutation:

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  TaskGuide.Domain -> /Users/phil/dev/task-guide-task9/src/TaskGuide.Domain/bin/Debug/net10.0/TaskGuide.Domain.dll
  TaskGuide.Application -> /Users/phil/dev/task-guide-task9/src/TaskGuide.Application/bin/Debug/net10.0/TaskGuide.Application.dll
  TaskGuide.Infrastructure -> /Users/phil/dev/task-guide-task9/src/TaskGuide.Infrastructure/bin/Debug/net10.0/TaskGuide.Infrastructure.dll
  TaskGuide.Storage.Tests -> /Users/phil/dev/task-guide-task9/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll
Test run for /Users/phil/dev/task-guide-task9/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:     3, Skipped:     0, Total:     3, Duration: 14 ms - TaskGuide.Storage.Tests.dll (net10.0)
```

### `The_sweep_on_an_absent_fires_directory_is_a_no_op_not_an_error`

Red, with temporary mutation removing the `Directory.Exists` guard:

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  TaskGuide.Domain -> /Users/phil/dev/task-guide-task9/src/TaskGuide.Domain/bin/Debug/net10.0/TaskGuide.Domain.dll
  TaskGuide.Application -> /Users/phil/dev/task-guide-task9/src/TaskGuide.Application/bin/Debug/net10.0/TaskGuide.Application.dll
  TaskGuide.Infrastructure -> /Users/phil/dev/task-guide-task9/src/TaskGuide.Infrastructure/bin/Debug/net10.0/TaskGuide.Infrastructure.dll
  TaskGuide.Storage.Tests -> /Users/phil/dev/task-guide-task9/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll
Test run for /Users/phil/dev/task-guide-task9/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:00.11]     TaskGuide.Storage.Tests.FireRetentionTests.The_sweep_on_an_absent_fires_directory_is_a_no_op_not_an_error [FAIL]
  Failed TaskGuide.Storage.Tests.FireRetentionTests.The_sweep_on_an_absent_fires_directory_is_a_no_op_not_an_error [< 1 ms]
  Error Message:
   System.IO.DirectoryNotFoundException : Could not find a part of the path '/var/folders/3f/n3z06n912lq606lbcsnwzhnr0000gn/T/taskguide-storage-tests-SBXn5L/fires'.
  Stack Trace:
     at System.IO.Enumeration.FileSystemEnumerator`1.CreateDirectoryHandle(String path, Boolean ignoreNotFound)
   at System.IO.Enumeration.FileSystemEnumerator`1.Init()
   at System.IO.Enumeration.FileSystemEnumerable`1..ctor(String directory, FindTransform transform, EnumerationOptions options, Boolean isNormalized, String expression)
   at System.IO.Enumeration.FileSystemEnumerableFactory.UserFiles(String directory, String expression, EnumerationOptions options)
   at System.IO.Directory.InternalEnumeratePaths(String path, String searchPattern, SearchTarget searchTarget, EnumerationOptions enumerationOptions)
   at TaskGuide.Infrastructure.Storage.FireRetention.Sweep(String dataDir, DateOnly today) in /Users/phil/dev/task-guide-task9/src/TaskGuide.Infrastructure/Storage/FireRetention.cs:line 10
   at TaskGuide.Storage.Tests.FireRetentionTests.The_sweep_on_an_absent_fires_directory_is_a_no_op_not_an_error() in /Users/phil/dev/task-guide-task9/tests/TaskGuide.Storage.Tests/FireRetentionTests.cs:line 66
   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)

Failed!  - Failed:     1, Passed:     3, Skipped:     0, Total:     4, Duration: 17 ms - TaskGuide.Storage.Tests.dll (net10.0)
```

Green after reverting the mutation:

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  TaskGuide.Domain -> /Users/phil/dev/task-guide-task9/src/TaskGuide.Domain/bin/Debug/net10.0/TaskGuide.Domain.dll
  TaskGuide.Application -> /Users/phil/dev/task-guide-task9/src/TaskGuide.Application/bin/Debug/net10.0/TaskGuide.Application.dll
  TaskGuide.Infrastructure -> /Users/phil/dev/task-guide-task9/src/TaskGuide.Infrastructure/bin/Debug/net10.0/TaskGuide.Infrastructure.dll
  TaskGuide.Storage.Tests -> /Users/phil/dev/task-guide-task9/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll
Test run for /Users/phil/dev/task-guide-task9/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:     4, Skipped:     0, Total:     4, Duration: 14 ms - TaskGuide.Storage.Tests.dll (net10.0)
```

## Additional Mutation Transcripts

### Mutation: skip the actual unlink

Changed `File.Delete(path);` to `_ = path;`. Result: red.

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  TaskGuide.Domain -> /Users/phil/dev/task-guide-task9/src/TaskGuide.Domain/bin/Debug/net10.0/TaskGuide.Domain.dll
  TaskGuide.Application -> /Users/phil/dev/task-guide-task9/src/TaskGuide.Application/bin/Debug/net10.0/TaskGuide.Application.dll
  TaskGuide.Infrastructure -> /Users/phil/dev/task-guide-task9/src/TaskGuide.Infrastructure/bin/Debug/net10.0/TaskGuide.Infrastructure.dll
  TaskGuide.Storage.Tests -> /Users/phil/dev/task-guide-task9/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll
Test run for /Users/phil/dev/task-guide-task9/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:00.11]     TaskGuide.Storage.Tests.FireRetentionTests.Fires_older_than_30_days_are_unlinked_as_whole_files [FAIL]
  Failed TaskGuide.Storage.Tests.FireRetentionTests.Fires_older_than_30_days_are_unlinked_as_whole_files [1 ms]
  Error Message:
   Assert.False() Failure
Expected: False
Actual:   True
  Stack Trace:
     at TaskGuide.Storage.Tests.FireRetentionTests.Fires_older_than_30_days_are_unlinked_as_whole_files() in /Users/phil/dev/task-guide-task9/tests/TaskGuide.Storage.Tests/FireRetentionTests.cs:line 34
   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)

Failed!  - Failed:     1, Passed:     3, Skipped:     0, Total:     4, Duration: 15 ms - TaskGuide.Storage.Tests.dll (net10.0)
```

Reverted and confirmed green:

```text
Passed!  - Failed:     0, Passed:     4, Skipped:     0, Total:     4, Duration: 14 ms - TaskGuide.Storage.Tests.dll (net10.0)
```

### Mutation: do not increment returned unlink count

Removed `removed++;`. Result: red.

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  TaskGuide.Domain -> /Users/phil/dev/task-guide-task9/src/TaskGuide.Domain/bin/Debug/net10.0/TaskGuide.Domain.dll
  TaskGuide.Application -> /Users/phil/dev/task-guide-task9/src/TaskGuide.Application/bin/Debug/net10.0/TaskGuide.Application.dll
  TaskGuide.Infrastructure -> /Users/phil/dev/task-guide-task9/src/TaskGuide.Infrastructure/bin/Debug/net10.0/TaskGuide.Infrastructure.dll
  TaskGuide.Storage.Tests -> /Users/phil/dev/task-guide-task9/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll
Test run for /Users/phil/dev/task-guide-task9/tests/TaskGuide.Storage.Tests/bin/Debug/net10.0/TaskGuide.Storage.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:00.11]     TaskGuide.Storage.Tests.FireRetentionTests.Fires_older_than_30_days_are_unlinked_as_whole_files [FAIL]
  Failed TaskGuide.Storage.Tests.FireRetentionTests.Fires_older_than_30_days_are_unlinked_as_whole_files [1 ms]
  Error Message:
   Assert.Equal() Failure: Values differ
Expected: 1
Actual:   0
  Stack Trace:
     at TaskGuide.Storage.Tests.FireRetentionTests.Fires_older_than_30_days_are_unlinked_as_whole_files() in /Users/phil/dev/task-guide-task9/tests/TaskGuide.Storage.Tests/FireRetentionTests.cs:line 33
   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)

Failed!  - Failed:     1, Passed:     3, Skipped:     0, Total:     4, Duration: 16 ms - TaskGuide.Storage.Tests.dll (net10.0)
```

## Verification

Storage project:

```text
Passed!  - Failed:     0, Passed:    57, Skipped:     0, Total:    57, Duration: 199 ms - TaskGuide.Storage.Tests.dll (net10.0)
```

Whole suite:

```text
Passed!  - Failed:     0, Passed:   166, Skipped:     0, Total:   166, Duration: 90 ms - TaskGuide.Domain.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    15, Skipped:     0, Total:    15, Duration: 36 ms - TaskGuide.Application.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    57, Skipped:     0, Total:    57, Duration: 341 ms - TaskGuide.Storage.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    11, Skipped:     0, Total:    11, Duration: 1 s - TaskGuide.Api.Tests.dll (net10.0)
```

Whole-suite totals: Failed 0, Passed 249, Skipped 0, Total 249.

## Concerns

- `dotnet test` under the managed sandbox failed before test execution because MSBuild could not create named pipes. All useful test runs were executed with approved escalation.
- I did not change `FireCodec` or `CodecPrimitives`. Retention only needed the existing `FireCodec.DateFromFileName(string)` and `FireCodec.FileNameFor(DateOnly)` members.
- I did not wire `FireRetention.Sweep` into startup or liveness, because Task 9's file lane only created the retention class and tests. That integration appears to belong to the startup/liveness lanes.
- The report cannot truthfully contain its own final commit SHA inside the same commit. Amending the SHA into the report changes the SHA again.
