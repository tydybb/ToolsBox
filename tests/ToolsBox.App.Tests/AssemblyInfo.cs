// WPF's process-wide BAML/Pack resource cache is shared by the STA test threads.
// Parallel window construction can corrupt its requested-stream list during LoadComponent.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
