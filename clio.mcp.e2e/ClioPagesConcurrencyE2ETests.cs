using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Common;
using Clio.Common.BrowserSession;
using Clio.Mcp.E2E.Support.Configuration;
using FluentAssertions;

namespace Clio.Mcp.E2E;

/// <summary>
/// ENG-95262 story 9: the files clio shares between processes must survive concurrent access.
/// <para>
/// The premise these tests defend is a present-tense one, not a future regression guard. The
/// <c>CwdLock</c> monitor that was believed to serialise <c>.clio-pages</c> writes covers only the
/// anchor-path computation and is released before every file touch, so two clio processes in one
/// workspace — a CLI <c>update-page</c> beside a running MCP server, or two MCP calls on different
/// tenant keys — could already interleave a <c>meta.json</c> read-modify-write and lose one update
/// silently, because every I/O failure on that path was swallowed.
/// </para>
/// <para>
/// TC-E-901 proves the page baseline survives two REAL clio processes saving one schema. TC-E-902
/// proves the browser-session cache's documented last-write-wins policy never exposes a torn read.
/// The <c>appsettings.json</c> case is a REGRESSION test: that path was already correct before this
/// story (cross-process lock, atomic replace, read-share, optimistic concurrency), and this fixture
/// pins those guarantees so a later change cannot quietly remove them.
/// </para>
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("interprocess-file-gate")]
[NonParallelizable]
public sealed class ClioPagesConcurrencyE2ETests {

	// The page seeded on the sandbox stand for the page fixtures; reused here so this test needs no
	// schema creation of its own.
	private const string SeededPageSchemaName = "ClioMcp_BlankPageToSave";
	private const string FallbackEnvironmentName = "d2";

	[Test]
	[Category("McpE2E.Sandbox")]
	[Description("TC-E-901: two concurrent real clio update-page processes saving one schema must leave a whole, parseable meta.json whose baseline points at a post-save checksum — neither write may be silently lost.")]
	[AllureTag("interprocess-file-gate")]
	[AllureName("Concurrent clio processes leave one whole page baseline")]
	[AllureDescription("Materialises .clio-pages/{schema}/ with a real CLI get-page, then runs two clio update-page processes concurrently against the same schema and asserts the resulting meta.json is whole, parseable, and carries a baseline — the interleaved read-modify-write must not truncate the file or drop the baseline block.")]
	public async Task ClioPagesMetaJson_Should_StayWhole_When_TwoProcessesSaveOneSchema() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(10));
		string environmentName = await ResolveReachableEnvironmentAsync(settings, cancellation.Token);
		string anchor = CreateTemporaryDirectory("clio-pages-concurrency-e2e");
		try {
			// A real CLI get-page writes body.js + meta.json (with the conflict baseline) under the anchor.
			// Without this the update-page runs would have no baseline to race on and the test would pass
			// for the wrong reason.
			ClioCliCommandResult read = await ClioCliCommandRunner.RunAsync(
				settings,
				["get-page", "--schema-name", SeededPageSchemaName, "-e", environmentName,
					"--output-directory", anchor],
				cancellationToken: cancellation.Token);
			read.ExitCode.Should().Be(0,
				because: $"the arrange step must materialise the page files before the race can be observed. stderr: {read.StandardError}");
			string schemaDirectory = Path.Combine(anchor, ".clio-pages", SeededPageSchemaName);
			string bodyFile = Path.Combine(schemaDirectory, "body.js");
			string metaFile = Path.Combine(schemaDirectory, "meta.json");
			File.Exists(metaFile).Should().BeTrue(because: "get-page must persist the conflict baseline for the race to touch");

			// Act — two independent clio processes save the same schema at the same time. Both pass --force
			// so the second is not stopped by a LEGITIMATE conflict: the subject here is the meta.json
			// read-modify-write, and a blocked second save would never reach it.
			IReadOnlyList<string> saveArguments = [
				"update-page", "--schema-name", SeededPageSchemaName, "--body-file", bodyFile,
				"--force", "true", "-e", environmentName
			];
			Task<ClioCliCommandResult> firstSave = ClioCliCommandRunner.RunAsync(
				settings, saveArguments, anchor, cancellation.Token);
			Task<ClioCliCommandResult> secondSave = ClioCliCommandRunner.RunAsync(
				settings, saveArguments, anchor, cancellation.Token);
			ClioCliCommandResult[] saves = await Task.WhenAll(firstSave, secondSave);

			// Assert
			foreach (ClioCliCommandResult save in saves) {
				save.ExitCode.Should().Be(0,
					because: $"a concurrent save must not fail: the gate queues the disk touch, it does not reject it. stdout: {save.StandardOutput}. stderr: {save.StandardError}");
			}
			string metaContent = File.ReadAllText(metaFile);
			metaContent.Should().NotBeNullOrWhiteSpace(
				because: "an interleaved read-modify-write must never leave the baseline file empty");
			Action parse = () => JsonDocument.Parse(metaContent).Dispose();
			parse.Should().NotThrow(
				because: "the loser of an interleaved write used to be able to truncate the file; the gate plus the atomic replace must leave exactly one whole document");
			using JsonDocument meta = JsonDocument.Parse(metaContent);
			meta.RootElement.TryGetProperty("baseline", out JsonElement baseline).Should().BeTrue(
				because: "both saves succeeded, so the surviving file must still carry a baseline block rather than having lost it to the race");
			baseline.TryGetProperty("checksum", out JsonElement checksum).Should().BeTrue(
				because: "the surviving baseline must carry the post-save checksum the next save will compare against");
			checksum.GetString().Should().NotBeNullOrWhiteSpace(
				because: "a blank checksum would disarm conflict detection for this page — the silent loss this story removes");
		} finally {
			DeleteDirectoryQuietly(anchor);
		}
	}

	[Test]
	[Category("McpE2E.NoEnvironment")]
	[Description("TC-E-902: the browser-session cache follows its documented last-write-wins policy — concurrent writers may overwrite each other, but a concurrent reader must never observe a torn, unparseable session file.")]
	[AllureTag("browser-session-cache")]
	[AllureName("Browser-session cache never exposes a torn read")]
	[AllureDescription("Rewrites one cached storageState file from several concurrent writers while a reader loop parses it, and asserts every successful read yielded whole JSON — the atomic replacement, not a lock, is what makes the documented last-write-wins policy safe for Playwright to consume.")]
	public void BrowserSessionCache_Should_NeverExposeATornRead_When_WrittenConcurrently() {
		// Arrange
		string root = CreateTemporaryDirectory("clio-session-concurrency-e2e");
		try {
			IFileSystem fileSystem = new FileSystem(new System.IO.Abstractions.FileSystem());
			IBrowserSessionCache cache = new BrowserSessionCache(fileSystem, new FileSecurityHardening());
			string sessionPath = Path.Combine(root, "concurrent.storageState.json");
			// Distinct payload SIZES on purpose: a non-atomic rewrite of a long document by a short one
			// leaves trailing bytes from the previous content, which is the shape of the torn read this
			// asserts against.
			string[] payloads = [
				BuildStorageState(cookieCount: 1),
				BuildStorageState(cookieCount: 40),
				BuildStorageState(cookieCount: 200)
			];
			cache.Write("concurrent", payloads[0], sessionPath);

			ConcurrentBag<string> unparseableReads = [];
			int successfulReads = 0;
			int sharingViolations = 0;
			using CancellationTokenSource stopReading = new();

			Task reader = Task.Run(() => {
				while (!stopReading.IsCancellationRequested) {
					try {
						string content = File.ReadAllText(sessionPath);
						try {
							JsonDocument.Parse(content).Dispose();
							Interlocked.Increment(ref successfulReads);
						} catch (JsonException) {
							unparseableReads.Add(content.Length > 200 ? content[..200] : content);
						}
					} catch (IOException) {
						// A momentary sharing violation is a different outcome from a torn document: the
						// reader simply did not get the file, so it cannot have observed a broken one.
						Interlocked.Increment(ref sharingViolations);
					}
				}
			});

			// Act
			Task[] writers = new Task[6];
			for (int writerIndex = 0; writerIndex < writers.Length; writerIndex++) {
				int seed = writerIndex;
				writers[writerIndex] = Task.Run(() => {
					for (int iteration = 0; iteration < 30; iteration++) {
						cache.Write("concurrent", payloads[(seed + iteration) % payloads.Length], sessionPath);
					}
				});
			}
			Task.WaitAll(writers, TimeSpan.FromMinutes(2)).Should().BeTrue(
				because: "the writers must all finish; a hung writer would mean the atomic replace deadlocked");
			stopReading.Cancel();
			reader.Wait(TimeSpan.FromSeconds(30)).Should().BeTrue(because: "the reader loop must stop when cancelled");

			// Assert
			unparseableReads.Should().BeEmpty(
				because: "the documented policy is last-write-wins, which is only safe if a reader can never catch a half-written session — Playwright loads this file directly and fails on truncated JSON");
			successfulReads.Should().BeGreaterThan(0,
				because: $"the reader must actually have read the file for the assertion above to mean anything (sharing violations observed: {sharingViolations})");
			string finalContent = File.ReadAllText(sessionPath);
			payloads.Should().Contain(finalContent,
				because: "last write wins: the surviving file must be exactly one writer's payload, never a blend of two");
			Directory.GetFiles(root, "*.tmp*", SearchOption.AllDirectories).Should().BeEmpty(
				because: "the temporary files used for the atomic replacement must not be left behind next to the cached session");
		} finally {
			DeleteDirectoryQuietly(root);
		}
	}

	[Test]
	[Category("McpE2E.NoEnvironment")]
	[Description("AC-04 regression: appsettings.json already had a cross-process lock, atomic replace and read-share before this story — a concurrent read during real reg-web-app writes must keep yielding a whole, valid environment catalog.")]
	[AllureTag("appsettings-catalog")]
	[AllureName("Concurrent read during reg-web-app yields a whole catalog")]
	[AllureDescription("Runs real clio reg-web-app processes against an isolated clio home while reading appsettings.json in a loop, then reads the catalog back with clio list-environments; every successful read must parse and the final catalog must contain every registered environment.")]
	public async Task AppSettings_Should_YieldAWholeCatalog_When_ReadDuringRegWebAppWrites() {
		// Arrange — an ISOLATED clio home, redirected through CLIO_HOME. Setting HOME/LOCALAPPDATA alone
		// does NOT isolate: TestConfiguration.Load puts the suite-owned CLIO_HOME into every spawned
		// process and CLIO_HOME wins outright, so the six registrations below would land in the SHARED
		// catalog that every other fixture resolves environments against — beside fixtures that
		// deliberately install an unresolvable ActiveEnvironmentKey there. See IsolatedClioHome.
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string home = IsolatedClioHome.CreateAndRedirect(settings, "clio-appsettings-concurrency-e2e");
		using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(10));
		try {
			string appSettingsPath = TemporaryClioSettingsOverride.GetClioAppSettingsPath(
				settings.ClioProcessPath, settings.ProcessEnvironmentVariables);
			// Ask clio itself where it will write, and refuse to run unless that is inside this test's own
			// home. Without this guard the isolation can regress to inert and the only symptom is six junk
			// environments appearing in the shared catalog — which reads as an unrelated failure, in a
			// different fixture, much later in the run.
			appSettingsPath.Should().StartWith(home,
				because: "clio must resolve its settings inside this test's private home; a path outside it means the redirect is inert and these registrations would damage the catalog the rest of the suite depends on");
			string[] environmentNames = ["cat-a", "cat-b", "cat-c", "cat-d", "cat-e", "cat-f"];

			ConcurrentBag<string> unparseableReads = [];
			int successfulReads = 0;
			using CancellationTokenSource stopReading = new();
			Task reader = Task.Run(() => {
				while (!stopReading.IsCancellationRequested) {
					try {
						string content = File.ReadAllText(appSettingsPath);
						try {
							JsonDocument.Parse(content).Dispose();
							Interlocked.Increment(ref successfulReads);
						} catch (JsonException) {
							unparseableReads.Add(content.Length > 200 ? content[..200] : content);
						}
					} catch (IOException) {
						// Not yet created, or momentarily unavailable — neither is a torn catalog.
					}
				}
			});

			// Act — real clio processes registering environments one after another. `--IsNetCore true` keeps
			// runtime auto-detection (a network probe) out of a test about file concurrency.
			foreach (string environmentName in environmentNames) {
				ClioCliCommandResult registration = await ClioCliCommandRunner.RunAsync(
					settings,
					["reg-web-app", environmentName, "-u", "http://localhost/concurrency-probe",
						"-l", "Supervisor", "-p", "Supervisor", "--IsNetCore", "true"],
					cancellationToken: cancellation.Token);
				registration.ExitCode.Should().Be(0,
					because: $"registering '{environmentName}' must succeed for the read side to have something to race with. stderr: {registration.StandardError}");
			}
			stopReading.Cancel();
			reader.Wait(TimeSpan.FromSeconds(30)).Should().BeTrue(because: "the reader loop must stop when cancelled");

			// Assert
			unparseableReads.Should().BeEmpty(
				because: "the settings writer replaces appsettings.json atomically and reads with sharing, so a concurrent reader must never see a partial catalog");
			successfulReads.Should().BeGreaterThan(0,
				because: "the reader must actually have read the catalog for the assertion above to mean anything");
			string finalCatalog = File.ReadAllText(appSettingsPath);
			using JsonDocument catalog = JsonDocument.Parse(finalCatalog);
			catalog.RootElement.TryGetProperty("Environments", out JsonElement environments).Should().BeTrue(
				because: "the catalog must still be a valid clio settings document after six concurrent-read-exposed writes");
			foreach (string environmentName in environmentNames) {
				environments.TryGetProperty(environmentName, out _).Should().BeTrue(
					because: $"'{environmentName}' was registered successfully, so no write may have been lost to the reads");
			}
			ClioCliCommandResult list = await ClioCliCommandRunner.RunAsync(
				settings, ["list-environments"], cancellationToken: cancellation.Token);
			list.ExitCode.Should().Be(0,
				because: $"clio itself must still be able to read the catalog it just wrote. stderr: {list.StandardError}");
		} finally {
			DeleteDirectoryQuietly(home);
		}
	}

	// A storageState-shaped payload whose length varies with the cookie count, so a non-atomic rewrite
	// would leave observable trailing bytes from the previous, longer document.
	private static string BuildStorageState(int cookieCount) {
		List<object> cookies = [];
		for (int index = 0; index < cookieCount; index++) {
			cookies.Add(new {
				name = $".ASPXAUTH_{index}",
				value = new string('a', 64),
				domain = "localhost",
				path = "/"
			});
		}
		return JsonSerializer.Serialize(new { cookies, origins = Array.Empty<object>() });
	}

	private static string CreateTemporaryDirectory(string prefix) {
		string path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
		Directory.CreateDirectory(path);
		return path;
	}

	private static void DeleteDirectoryQuietly(string path) {
		try {
			if (Directory.Exists(path)) {
				Directory.Delete(path, recursive: true);
			}
		} catch (IOException) {
			// A leftover temporary directory must never fail a test run.
		} catch (UnauthorizedAccessException) {
			// Same reasoning as above.
		}
	}

	private static async Task<string> ResolveReachableEnvironmentAsync(
		McpE2ESettings settings, CancellationToken cancellationToken) {
		string? configured = settings.Sandbox.EnvironmentName;
		if (!string.IsNullOrWhiteSpace(configured)
			&& await ClioCliCommandRunner.IsEnvironmentReachableAsync(settings, configured, cancellationToken)) {
			return configured;
		}
		if (await ClioCliCommandRunner.IsEnvironmentReachableAsync(
				settings, FallbackEnvironmentName, cancellationToken)) {
			return FallbackEnvironmentName;
		}
		Assert.Ignore(
			$"TC-E-901 needs a reachable Creatio environment. Neither the configured sandbox environment '{configured}' nor the fallback '{FallbackEnvironmentName}' answered ping-app.");
		return string.Empty;
	}
}
