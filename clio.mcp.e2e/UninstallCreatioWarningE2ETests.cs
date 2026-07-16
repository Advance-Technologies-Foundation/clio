using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Progress;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

// [AllureNUnit] is intentionally omitted. This destructive fixture performs a long async MCP call and uses
// explicit Allure metadata; fixture-level AOP can block async continuations in this suite.
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureFeature("uninstall-creatio")]
[NonParallelizable]
public sealed class UninstallCreatioWarningE2ETests {
	private const string ToolName = UninstallCreatioTool.UninstallCreatioToolName;

	[Test]
	[Description("Uninstalls an explicitly opted-in disposable Windows sandbox while its registered application-pool profile contains a locked file, verifying warning propagation and successful MCP completion.")]
	[AllureTag(ToolName)]
	[AllureName("Uninstall Creatio reports a locked application-pool profile as success with warnings")]
	[AllureDescription("Locks a file inside the sandbox IIS virtual-account profile, invokes the real uninstall-creatio tool over stdio MCP, and verifies WarningMessage, exit 0, IsError=false, warning stage, and success-with-warnings terminal before releasing the lock and cleaning the residual profile.")]
	public async Task UninstallCreatio_ShouldCompleteWithWarnings_WhenAppPoolProfileContainsLockedFile() {
		// Arrange
		if (!OperatingSystem.IsWindows()) {
			Assert.Ignore("Application-pool profile deletion is a Windows-only E2E scenario.");
			return;
		}
		McpE2ESettings settings = TestConfiguration.Load();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Set McpE2E:AllowDestructiveMcpTests=true for the disposable uninstall sandbox.");
		}
		if (string.IsNullOrWhiteSpace(settings.Sandbox.EnvironmentName)) {
			Assert.Fail("Configure McpE2E:Sandbox:EnvironmentName for the explicitly opted-in disposable uninstall sandbox.");
		}
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentPath = string.IsNullOrWhiteSpace(settings.Sandbox.EnvironmentPath)
			? ClioEnvironmentCommandResolver.ResolveEnvironmentPath(settings, settings.Sandbox.EnvironmentName!)
			: Path.GetFullPath(settings.Sandbox.EnvironmentPath);
		string appPoolName = ResolveAppPoolName(environmentPath);
		using ServiceProvider services = new ServiceCollection()
			.AddSingleton<IWindowsUserProfileApi, WindowsUserProfileApi>()
			.BuildServiceProvider();
		IWindowsUserProfileApi profileApi = services.GetRequiredService<IWindowsUserProfileApi>();
		WindowsProfileRegistration registration = profileApi.Resolve(appPoolName);
		registration.Should().NotBeNull(
			because: "the opted-in sandbox must have a registered IIS virtual-account profile to exercise the warning path");
		Directory.CreateDirectory(registration.ProfilePath);
		string lockPath = Path.Combine(registration.ProfilePath, $"clio-e2e-lock-{Guid.NewGuid():N}.tmp");
		await File.WriteAllTextAsync(lockPath, "locked by uninstall warning E2E");
		bool uninstallCompleted = false;

		try {
			using CancellationTokenSource cts = new(TimeSpan.FromMinutes(5));
			await using McpServerSession session = await McpServerSession.StartAsync(settings, cts.Token);
			session.StartCapturingProgressNotifications();
			ProgressToken progressToken = new($"uninstall-warning-{Guid.NewGuid():N}");
			// Act
			CallToolResult callResult;
			await using (FileStream profileLock = new(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
				callResult = await session.CallToolWithRawProgressAsync(
					ToolName,
					new Dictionary<string, object?> {
						["args"] = new Dictionary<string, object?> {
							["environment-name"] = settings.Sandbox.EnvironmentName
						}
					},
					progressToken,
					cts.Token);
			}
			IReadOnlyList<JsonNode> rawProgress = await session.WaitForCapturedProgressAsync(
				progressToken, HasWarningTerminalStream, TimeSpan.FromMinutes(1), cts.Token);
			uninstallCompleted = HasWarningTerminalStream(rawProgress) && !AppPoolExists(appPoolName);

			// Assert
			CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);
			execution.ExitCode.Should().Be(0,
				because: "a locked best-effort profile must never make uninstall return a non-zero exit code");
			callResult.IsError.Should().NotBeTrue(
				because: "successful uninstall with warnings must keep the MCP protocol result non-error");
			execution.Output.Should().Contain(message => message.MessageType == LogDecoratorType.Warning
				&& message.Value != null && message.Value.Contains(registration.ProfilePath),
				because: "the successful command result must preserve the friendly typed WarningMessage");
			execution.Output.Should().Contain(message => message.MessageType == LogDecoratorType.Info,
				because: "successful command output must retain its normal informational completion messages");

			IReadOnlyList<ClioStageEvent> events = ExtractStageEvents(rawProgress);
			events.Should().Contain(stageEvent => stageEvent.Stage != null
				&& stageEvent.Stage.StageId == StageIds.DeleteApppoolProfile
				&& stageEvent.Stage.Status == ClioStageEventContract.StageStatuses.Warning
				&& stageEvent.Stage.ErrorCode == WindowsAppPoolProfileCleaner.ProfileDeleteFailedErrorCode,
				because: "Ring needs the explicit warning stage and stable error code");
			events[^1].RunCompleted!.Outcome.Should().Be(ClioStageEventContract.RunOutcomes.SuccessWithWarnings,
				because: "the terminal contract must distinguish successful completion with retained warnings");
			if (settings.Sandbox.RequireDbHubLifecycle) {
				string[] manifest = events[0].Stages!.Select(stage => stage.StageId).ToArray();
				manifest.Should().ContainInOrder(
					[StageIds.DeleteFiles, StageIds.RemoveDbHubSource, StageIds.Unregister],
					because: "the opted-in dbHub lifecycle proof requires removal after cleanup and before unregister");
				events.Should().Contain(stageEvent => stageEvent.Stage != null
					&& stageEvent.Stage.StageId == StageIds.RemoveDbHubSource
					&& stageEvent.Stage.Status == ClioStageEventContract.StageStatuses.Done,
					because: "the live dbHub server must hot-reload the exact source removal successfully");
			}
		}
		finally {
			if (!uninstallCompleted) {
				TestContext.Error.WriteLine(
					$"Profile cleanup was intentionally skipped because uninstall did not reach a verified successful terminal or app pool '{appPoolName}' still exists.");
			}
			else {
				if (File.Exists(lockPath)) {
					File.Delete(lockPath);
				}
				int cleanupError = DeleteResidualProfile(profileApi, registration);
				cleanupError.Should().Be(0,
					because: "the disposable E2E must remove the intentionally retained profile after restoring delete access");
				Directory.Exists(registration.ProfilePath).Should().BeFalse(
					because: "native success is insufficient when a residual profile directory remains");
			}
		}
	}

	private static bool AppPoolExists(string appPoolName) {
		string appCmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
			"System32", "inetsrv", "appcmd.exe");
		ProcessStartInfo startInfo = new(appCmd) {
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true
		};
		foreach (string argument in new[] { "list", "apppool", appPoolName }) {
			startInfo.ArgumentList.Add(argument);
		}
		using Process process = Process.Start(startInfo)
			?? throw new InvalidOperationException("Could not start IIS appcmd.exe.");
		string output = process.StandardOutput.ReadToEnd();
		string error = process.StandardError.ReadToEnd();
		process.WaitForExit();
		if (process.ExitCode == 1) {
			return false;
		}
		if (process.ExitCode != 0) {
			throw new InvalidOperationException($"appcmd.exe failed with exit code {process.ExitCode}: {error}");
		}
		return !string.IsNullOrWhiteSpace(output);
	}

	private static int DeleteResidualProfile(IWindowsUserProfileApi profileApi,
		WindowsProfileRegistration registration) {
		int errorCode = 0;
		for (int attempt = 0; attempt < 3; attempt++) {
			errorCode = profileApi.Delete(registration);
			if (errorCode == 0 || !Directory.Exists(registration.ProfilePath)) {
				return 0;
			}
			Thread.Sleep(250);
		}
		if (Directory.Exists(registration.ProfilePath)) {
			DeleteDirectoryWithoutFollowingReparsePoints(registration.ProfilePath);
			errorCode = profileApi.Delete(registration);
			if (errorCode == 0 || !Directory.Exists(registration.ProfilePath)) {
				return 0;
			}
		}
		return errorCode;
	}

	private static void DeleteDirectoryWithoutFollowingReparsePoints(string path) {
		FileAttributes rootAttributes = File.GetAttributes(path);
		if (rootAttributes.HasFlag(FileAttributes.ReparsePoint)) {
			throw new InvalidOperationException($"Refusing to traverse reparse-point cleanup root '{path}'.");
		}
		foreach (string entry in Directory.EnumerateFileSystemEntries(path)) {
			FileAttributes attributes = File.GetAttributes(entry);
			if (attributes.HasFlag(FileAttributes.Directory)) {
				if (attributes.HasFlag(FileAttributes.ReparsePoint)) {
					Directory.Delete(entry, recursive: false);
				}
				else {
					DeleteDirectoryWithoutFollowingReparsePoints(entry);
				}
				continue;
			}
			if (attributes.HasFlag(FileAttributes.ReadOnly)) {
				File.SetAttributes(entry, attributes & ~FileAttributes.ReadOnly);
			}
			File.Delete(entry);
		}
		Directory.Delete(path, recursive: false);
	}

	private static string ResolveAppPoolName(string environmentPath) {
		string appCmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
			"System32", "inetsrv", "appcmd.exe");
		string vdirsXml = RunAppCmd(appCmd, "list", "vdir", "/xml");
		XElement root = XElement.Parse(vdirsXml);
		string normalizedTarget = Path.GetFullPath(environmentPath).TrimEnd(Path.DirectorySeparatorChar);
		XElement vdir = root.Elements("VDIR").Single(element => {
			string physicalPath = element.Attribute("physicalPath")?.Value ?? string.Empty;
			return string.Equals(Path.GetFullPath(physicalPath).TrimEnd(Path.DirectorySeparatorChar),
				normalizedTarget, StringComparison.OrdinalIgnoreCase);
		});
		string appName = vdir.Attribute("APP.NAME")?.Value
			?? throw new InvalidOperationException("The sandbox IIS virtual directory has no APP.NAME.");
		return RunAppCmd(appCmd, "list", "app", appName, "/text:applicationPool").Trim();
	}

	private static string RunAppCmd(string appCmd, params string[] arguments) {
		return RunProcess(appCmd, arguments);
	}

	private static string RunProcess(string executable, IEnumerable<string> arguments) {
		ProcessStartInfo startInfo = new(executable) {
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true
		};
		foreach (string argument in arguments) {
			startInfo.ArgumentList.Add(argument);
		}
		using Process process = Process.Start(startInfo)
			?? throw new InvalidOperationException("Could not start IIS appcmd.exe.");
		string output = process.StandardOutput.ReadToEnd();
		string error = process.StandardError.ReadToEnd();
		process.WaitForExit();
		if (process.ExitCode != 0) {
			throw new InvalidOperationException($"{Path.GetFileName(executable)} failed with exit code {process.ExitCode}: {error}");
		}
		return output;
	}

	private static bool HasWarningTerminalStream(IReadOnlyList<JsonNode> rawParams) {
		IReadOnlyList<ClioStageEvent> events = ExtractStageEvents(rawParams);
		return events.Any(stageEvent => stageEvent.Stage?.Status == ClioStageEventContract.StageStatuses.Warning)
			&& events.Any(stageEvent => stageEvent.RunCompleted?.Outcome ==
				ClioStageEventContract.RunOutcomes.SuccessWithWarnings);
	}

	private static IReadOnlyList<ClioStageEvent> ExtractStageEvents(IReadOnlyList<JsonNode> rawParams) =>
		[.. rawParams
			.Select(node => node["_meta"]?["clioStageEvent"]?.Deserialize<ClioStageEvent>(
				ClioStageEventContract.SerializerOptions))
			.Where(stageEvent => stageEvent is not null)
			.Cast<ClioStageEvent>()
			.OrderBy(stageEvent => stageEvent.Sequence)];
}
