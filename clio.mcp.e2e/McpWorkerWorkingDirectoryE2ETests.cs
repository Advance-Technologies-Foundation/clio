using System.Diagnostics;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Relay;
using Clio.Common;
using Clio.Common.McpWorker;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Mcp.E2E;

/// <summary>
/// ENG-95262 Stage 6, blast radius: a worker child must start in the HOST's working directory, and this
/// asserts it on the directory a real child process reports about ITSELF.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a real child and not the spawn request.</b> "The request stated a directory" and "the child
/// started there" are different claims, and the defect this pins lived precisely in the gap: the request
/// left the field null, the supervisor's fallback chain then chose the directory the clio ASSEMBLY lives
/// in, and a cohort <c>get-page</c> wrote a user's <c>.clio-pages/{schema}/</c> tree into the clio
/// installation while answering <c>success: true</c>. Nothing reported it — the files were simply
/// somewhere else. A unit test on the request alone would have gone green the same way.
/// </para>
/// <para>
/// <b>The discriminator is the decoy.</b> The launch descriptor states a directory that stands in for
/// the install tree; only a request that explicitly carries the host's directory can beat it, because
/// the supervisor's precedence is request → descriptor → parent. So deleting the working-directory line
/// from <see cref="McpWorkerCallDispatcher.ComposeSpawnRequest"/> makes this fixture report the decoy
/// and fail — which is the whole point of composing the request through production code here rather than
/// building a look-alike.
/// </para>
/// </remarks>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("mcp-worker-execution-boundary")]
[NonParallelizable]
public sealed class McpWorkerWorkingDirectoryE2ETests {

	private const string ReportWorkingDirectoryArgument = "--report-working-directory";
	private static readonly TimeSpan ReportWait = TimeSpan.FromSeconds(30);

	private string _scratchDirectory = string.Empty;

	[SetUp]
	public void SetUp() {
		// Under the test directory rather than the system temp root: on macOS the temp root is reached
		// through a symlink, and comparing a path the child resolved against one the test did not would
		// fail on the link instead of on the behaviour.
		_scratchDirectory = Path.Combine(TestContext.CurrentContext.TestDirectory,
			$"clio-worker-cwd-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_scratchDirectory);
	}

	[TearDown]
	public void TearDown() {
		if (Directory.Exists(_scratchDirectory)) {
			try {
				Directory.Delete(_scratchDirectory, recursive: true);
			} catch (IOException) {
				// A scratch directory left behind must not fail a working-directory assertion.
			}
		}
	}

	[Test]
	[Description("A worker child spawned from the production spawn request starts in the HOST's working directory and not in the directory the launch descriptor names — the install-tree default that made a cohort get-page write a user's .clio-pages files into the clio installation and still answer success.")]
	[AllureTag("mcp-worker")]
	[AllureName("A worker child starts in the host's working directory")]
	public async Task Worker_ShouldStartInTheHostsWorkingDirectory_NotWhereTheExecutableLives() {
		// Arrange
		string decoyInstallDirectory = Path.Combine(_scratchDirectory, "pretend-install-tree");
		Directory.CreateDirectory(decoyInstallDirectory);
		string reportPath = Path.Combine(_scratchDirectory, "child-working-directory.txt");
		string hostWorkingDirectory = Environment.CurrentDirectory;
		hostWorkingDirectory.Should().NotBe(decoyInstallDirectory,
			because: "the two candidates have to differ for the child's answer to distinguish them at all");
		IWorkerProcessSupervisor supervisor = CreateSupervisor();
		// Composed by PRODUCTION code, then pointed at a fixture that can report about itself. The
		// descriptor's directory is what the supervisor falls back to when the request states none.
		WorkerSpawnRequest request = McpWorkerCallDispatcher.ComposeSpawnRequest(
			new Dictionary<string, string>(StringComparer.Ordinal), TimeSpan.FromSeconds(20)) with {
			LaunchOverride = new ClioWorkerLaunchDescriptor(ResolveFixtureExecutable(),
				[ReportWorkingDirectoryArgument, reportPath], decoyInstallDirectory)
		};

		// Act
		using IWorkerLease lease = await supervisor.SpawnContainedAsync(request, CancellationToken.None);
		WorkerRunResult result = await supervisor.WaitWithinBudgetAsync(lease, CancellationToken.None);
		string reported = await ReadReportAsync(reportPath);

		// Assert
		result.Status.Should().Be(WorkerRunStatus.Completed,
			because: "the fixture reports its directory and exits at once, so anything but a clean completion means the report was never written by the process under test");
		Canonical(reported).Should().Be(Canonical(hostWorkingDirectory),
			because: "the child must see the same 'here' the host does: `.clio-pages/{schema}/` is anchored on the process current directory, so a worker started anywhere else relocates a user's page files silently");
		Canonical(reported).Should().NotBe(Canonical(decoyInstallDirectory),
			because: "the descriptor's directory stands in for the clio installation, and it is what wins the moment the spawn request stops stating the host's directory — the exact defect observed live twice on Stage 6");
	}

	private static async Task<string> ReadReportAsync(string path) {
		DateTime deadline = DateTime.UtcNow + ReportWait;
		while (DateTime.UtcNow < deadline) {
			if (File.Exists(path)) {
				string content = await File.ReadAllTextAsync(path);
				if (!string.IsNullOrWhiteSpace(content)) {
					return content.Trim();
				}
			}
			await Task.Delay(50);
		}
		throw new TimeoutException($"The fixture did not report its working directory at '{path}'.");
	}

	// Compared as canonical paths so a trailing separator or a differently-spelled but identical
	// directory cannot make an equal pair look unequal — the assertion is about WHICH directory, not
	// about how it was spelled.
	private static string Canonical(string path) =>
		Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

	private static IWorkerProcessSupervisor CreateSupervisor() {
		IFileSystem fileSystem = new System.IO.Abstractions.FileSystem();
		// Rooted in the scratch area, never in the developer's clio home: this supervisor kills what it
		// spawns, and it must not read a real host's worker records to decide what to kill.
		string registryRoot = Path.Combine(Path.GetTempPath(), $"clio-worker-registry-{Guid.NewGuid():N}");
		IStaleWorkerRegistry registry = new StaleWorkerRegistry(fileSystem,
			new InterprocessFileGate(fileSystem), registryRoot);
		IProcessContainment containment = OperatingSystem.IsWindows()
			? new WindowsJobObjectContainment()
			: new UnixProcessGroupContainment();
		return new WorkerProcessSupervisor(ConsoleLogger.Instance, new ProcessExecutor(ConsoleLogger.Instance),
			containment, new ClioExecutablePathProvider(fileSystem), registry, concurrencyCap: 2);
	}

	private static string ResolveFixtureExecutable() {
		DirectoryInfo testDirectory = new(TestContext.CurrentContext.TestDirectory);
		string targetFramework = testDirectory.Name;
		string configuration = testDirectory.Parent?.Name
			?? throw new InvalidOperationException("The test configuration directory could not be resolved.");
		string repositoryRoot = Path.GetFullPath(Path.Combine(testDirectory.FullName, "..", "..", "..", ".."));
		string fixtureExecutable = Path.Combine(repositoryRoot, "clio.process.fixture", "bin", configuration,
			targetFramework, OperatingSystem.IsWindows() ? "git.exe" : "git");
		return File.Exists(fixtureExecutable)
			? fixtureExecutable
			: throw new FileNotFoundException("The process fixture was not built.", fixtureExecutable);
	}
}
