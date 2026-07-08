using Xunit;

// UIA drives global desktop state (focus, cursor, keyboard); never run in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
