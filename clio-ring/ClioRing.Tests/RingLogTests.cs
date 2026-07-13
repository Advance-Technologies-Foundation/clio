using System;
using System.IO;
using System.Linq;
using ClioRing.Diagnostics;
using ClioRing.Models;
using FluentAssertions;
using NUnit.Framework;

namespace ClioRing.Tests;

/// <summary>
/// Unit tests for the Ring run-log facility (story 10, ADR D6, FR-17): the active logs-folder resolution
/// (appsettings <c>LogsFolder</c> override vs the default <c>Logs</c> folder beside the executable), size
/// rotation with a bounded file count, and redaction of credential-shaped context before it reaches disk
/// (AC-14 / AC-12). Uses a temp folder — no writes to the real logs directory.
/// </summary>
[TestFixture]
[Category("Unit")]
public sealed class RingLogTests {
	private string _folder = string.Empty;

	[SetUp]
	public void SetUp() {
		_folder = Path.Combine(Path.GetTempPath(), "clio-ring-log-" + Guid.NewGuid().ToString("N"));
	}

	[TearDown]
	public void TearDown() {
		try {
			if (Directory.Exists(_folder)) {
				Directory.Delete(_folder, recursive: true);
			}
		}
		catch (IOException) {
			// Best-effort cleanup; a locked temp file must not fail the suite.
		}
	}

	private static AppSettings SettingsWith(string? logsFolder) =>
		new() { WorkspaceFolder = @"C:\ws", LogsFolder = logsFolder };

	// ---- logs-folder resolution (default vs appsettings override) ----

	[Test]
	[Description("With no LogsFolder configured, the active logs folder defaults to a 'Logs' folder next to the executable.")]
	public void ResolveLogsFolder_ShouldDefaultToLogsBesideExecutable_WhenNotConfigured() {
		// Arrange — settings without a LogsFolder (and the null-settings case).
		AppSettings noKey = SettingsWith(null);

		// Act — resolve both the empty-key and null-settings forms.
		string fromEmptyKey = RingLog.ResolveLogsFolder(noKey);
		string fromNull = RingLog.ResolveLogsFolder(null);

		// Assert — both fall back to <app-dir>\Logs.
		string expected = Path.Combine(AppContext.BaseDirectory, "Logs");
		fromEmptyKey.Should().Be(expected, because: "an absent LogsFolder defaults to a Logs folder beside the executable");
		fromNull.Should().Be(expected, because: "unreadable/absent settings default the same way");
	}

	[Test]
	[Description("When appsettings sets LogsFolder, that configured path becomes the active logs folder.")]
	public void ResolveLogsFolder_ShouldUseConfiguredPath_WhenLogsFolderSet() {
		// Arrange — settings with an explicit LogsFolder.
		AppSettings configured = SettingsWith(_folder);

		// Act — resolve the active folder.
		string resolved = RingLog.ResolveLogsFolder(configured);

		// Assert — the configured path wins over the default.
		resolved.Should().Be(_folder, because: "an explicit LogsFolder override is honoured (AC-14)");
	}

	// ---- rotation: size roll + bounded file count ----

	[Test]
	[Description("Rotation starts new files at the size cap and prunes the folder to the retained-file count.")]
	public void Log_ShouldRotateAndCapFileCount_WhenSizeExceeded() {
		// Arrange — a writer with a tiny per-file cap and a 3-file retention limit.
		var writer = new RingLogWriter(_folder, maxBytesPerFile: 40, maxFiles: 3);

		// Act — write far more lines than three small files can hold.
		for (int i = 0; i < 50; i++) {
			writer.Log($"line number {i} with some padding to exceed the byte cap quickly");
		}

		// Assert — rotation rolled multiple files but pruning kept the count at the cap.
		string[] files = Directory.GetFiles(_folder, "ring-*.log");
		files.Length.Should().BeLessThanOrEqualTo(3,
			because: "rotation caps the retained log files at maxFiles (oldest pruned first)");
		files.Length.Should().BeGreaterThan(0, because: "at least one log file is written");
	}

	// ---- redaction: credential-shaped context is scrubbed before disk ----

	[Test]
	[Description("A secret-bearing JSON context line is scrubbed (credential values masked) before it is written to disk (AC-12).")]
	public void Log_ShouldRedactSecretContext_WhenContextCarriesCredentials() {
		// Arrange — a writer and a JSON context carrying a password + token.
		var writer = new RingLogWriter(_folder);
		const string secret = "sup3r-s3cret-value";
		string context = "{\"siteName\":\"creatio-demo\",\"password\":\"" + secret + "\",\"token\":\"" + secret + "\"}";

		// Act — log the context.
		writer.Log("deploy request", context);

		// Assert — the credential values are masked and the raw secret never reaches disk.
		string body = string.Join("\n", Directory.GetFiles(_folder, "ring-*.log").Select(File.ReadAllText));
		body.Should().Contain("****", because: "credential-shaped values are masked by the shared redactor");
		body.Should().NotContain(secret, because: "the raw secret value must never reach disk");
		body.Should().Contain("creatio-demo", because: "non-credential context is preserved for diagnosis");
	}

	// ---- best-effort contract ----

	[Test]
	[Description("The writer is best-effort: logging a message with no context never throws.")]
	public void Log_ShouldNotThrow_WhenNoContext() {
		// Arrange — a writer over the temp folder.
		var writer = new RingLogWriter(_folder);

		// Act — log without context.
		Action act = () => writer.Log("plain message");

		// Assert — a diagnostic must never break the app.
		act.Should().NotThrow(because: "the run-log writer is best-effort");
	}
}
