using Xunit;

// Disable parallel test execution so that Avalonia UI tests and Dispatcher don't conflict across threads
[assembly: CollectionBehavior(DisableTestParallelization = true)]

