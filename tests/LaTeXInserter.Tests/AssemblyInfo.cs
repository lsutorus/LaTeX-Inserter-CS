using Xunit;

// Avalonia Dispatcher and SharpHook native hooks cannot safely run in parallel across test collections.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
