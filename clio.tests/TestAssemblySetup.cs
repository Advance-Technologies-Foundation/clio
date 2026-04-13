using NUnit.Framework;

// Enable parallel test execution at the fixture level.
// Each test fixture runs on its own thread; tests within a fixture run sequentially.
// This preserves test isolation (SetUp/TearDown per fixture) while maximizing throughput.
// LevelOfParallelism kept at 4 — ConsoleLogger singleton has a background flush thread
// that writes to Console.Out; too many parallel fixtures cause ObjectDisposedException
// when the test host redirects Console.Out per-test.
[assembly: Parallelizable(ParallelScope.Fixtures)]
[assembly: LevelOfParallelism(3)]
