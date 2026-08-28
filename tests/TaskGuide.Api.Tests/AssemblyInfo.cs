using Xunit;

// TaskEndpointsTests sets a process-wide environment variable (Storage__DataDir) around each
// WebApplicationFactory boot, because that's the only override path that reaches Program.cs's
// pre-Build() configuration read (see the remarks on TaskEndpointsTests). Parallel test classes
// in this assembly would race on that global mutable state.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
