using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Clio.Command.McpServer.Relay;
using Clio.Common;
using Clio.Common.BrowserSession;
using Clio.Common.McpWorker;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// ENG-103286: page-smoke runs in an MCP worker, and the worker is launched with a cleared environment. These cases
/// compose that environment exactly as the dispatcher and the supervisor do, from a simulated Windows host, and run
/// ChromiumLocator over the result with the Windows candidate paths - so the regression is provable on any OS.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class PageSmokeWorkerBrowserDiscoveryTests {

	private const string ProgramFiles = @"C:\Program Files";
	private const string ProgramFilesX86 = @"C:\Program Files (x86)";
	private const string LocalAppData = @"C:\Users\agent\AppData\Local";

	private static readonly string[] LocatorInputs = ["ProgramFiles", "ProgramFiles(x86)", "ProgramW6432", "CHROME_PATH"];

	private static readonly string[] DiscoveryInputs = [
		"PLAYWRIGHT_BROWSERS_PATH", "PUPPETEER_CACHE_DIR", "XDG_CACHE_HOME", "CLIO_CHROME_FOR_TESTING_MIRROR"
	];

	[Test]
	[Description("ENG-103287: the cache locations of Playwright and Puppeteer and the Chrome for Testing mirror reach the worker, so it offers the browsers the user's machine has and downloads from where the operator pointed it.")]
	public void ComposeEffectiveEnvironment_Should_Carry_The_Browser_Discovery_Inputs_To_The_Worker() {
		// Arrange
		Dictionary<string, string> host = new(StringComparer.Ordinal) {
			["PLAYWRIGHT_BROWSERS_PATH"] = "/opt/pw",
			["PUPPETEER_CACHE_DIR"] = "/opt/pp",
			["XDG_CACHE_HOME"] = "/var/cache/agent",
			["CLIO_CHROME_FOR_TESTING_MIRROR"] = "https://mirror.corp/cft"
		};

		// Act
		IReadOnlyDictionary<string, string> worker = ComposeWorkerEnvironment(host);

		// Assert
		foreach (string name in DiscoveryInputs) {
			worker.Should().Contain(name, host[name], because: $"page-smoke reads {name} inside the worker");
		}
	}

	// The variables a Windows host process has; names are case-insensitive there, as on the real platform.
	private static Dictionary<string, string> WindowsHost(string chromePath = null) {
		Dictionary<string, string> host = new(StringComparer.OrdinalIgnoreCase) {
			["ProgramFiles"] = ProgramFiles,
			["ProgramFiles(x86)"] = ProgramFilesX86,
			["ProgramW6432"] = ProgramFiles,
			["LOCALAPPDATA"] = LocalAppData,
			["SystemRoot"] = @"C:\Windows",
			["CLIO_OIDC_CLIENT_SECRET"] = "must-not-reach-the-worker"
		};
		if (chromePath is not null) {
			host["CHROME_PATH"] = chromePath;
		}
		return host;
	}

	// The worker's environment as the shipped path builds it: the frozen per-call delta, the dispatcher's spawn
	// request, then the supervisor's allowlist read from the host.
	private static IReadOnlyDictionary<string, string> ComposeWorkerEnvironment(IReadOnlyDictionary<string, string> host,
		IReadOnlyCollection<string> allowlist = null) {
		WorkerSpawnRequest request = McpWorkerCallDispatcher.ComposeSpawnRequest(
			McpWorkerEnvironment.ComposeChildEnvironment(
				new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase), McpWorkerLifetime.PerCall,
				name => host.GetValueOrDefault(name)),
			TimeSpan.FromSeconds(120));
		if (allowlist is not null) {
			request = new WorkerSpawnRequest {
				Arguments = request.Arguments,
				Budget = request.Budget,
				WorkingDirectory = request.WorkingDirectory,
				EnvironmentVariables = request.EnvironmentVariables,
				InheritedEnvironmentVariableAllowlist = allowlist
			};
		}
		return WorkerProcessSupervisor.ComposeEffectiveEnvironment(request, name => host.GetValueOrDefault(name));
	}

	// The locator as the worker runs it: reading only the worker's environment (case-insensitively, as Windows does)
	// and probing the Windows install locations, with exactly one browser installed.
	private static ChromiumLocator WorkerLocator(IReadOnlyDictionary<string, string> workerEnvironment,
		string installedBrowser) {
		Dictionary<string, string> windowsView = new(workerEnvironment, StringComparer.OrdinalIgnoreCase);
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		fileSystem.ExistsFile(Arg.Any<string>()).Returns(call =>
			string.Equals(call.Arg<string>(), installedBrowser, StringComparison.OrdinalIgnoreCase));
		return new ChromiumLocator(fileSystem, name => windowsView.GetValueOrDefault(name), OSPlatform.Windows);
	}

	[Test]
	[Description("Every ChromiumLocator input a Windows host has - the three Program Files locations and CHROME_PATH - survives the worker's cleared environment verbatim, while a secret beside them still does not.")]
	public void ComposeEffectiveEnvironment_Should_Carry_The_Locator_Inputs_To_The_Worker() {
		// Arrange
		Dictionary<string, string> host = WindowsHost(chromePath: @"D:\Tools\chrome.exe");

		// Act
		IReadOnlyDictionary<string, string> worker = ComposeWorkerEnvironment(host);

		// Assert
		foreach (string name in LocatorInputs) {
			worker.Should().Contain(name, host[name],
				because: $"ChromiumLocator reads {name} inside the worker, and a value that stops at the parent makes "
					+ "page-smoke answer 'Chromium binary not found' on a machine whose CLI finds the browser");
		}
		worker.Should().NotContainKey("CLIO_OIDC_CLIENT_SECRET",
			because: "widening the allowlist for install locations must not let a secret cross the parent-to-worker channel");
	}

	[TestCase(ProgramFilesX86 + @"\Microsoft\Edge\Application\msedge.exe", TestName = "Edge under Program Files (x86)")]
	[TestCase(ProgramFiles + @"\Microsoft\Edge\Application\msedge.exe", TestName = "Edge under Program Files")]
	[TestCase(ProgramFiles + @"\Google\Chrome\Application\chrome.exe", TestName = "Machine-wide Chrome")]
	[TestCase(ProgramFilesX86 + @"\Google\Chrome\Application\chrome.exe", TestName = "Machine-wide 32-bit Chrome")]
	[TestCase(LocalAppData + @"\Google\Chrome\Application\chrome.exe", TestName = "Per-user Chrome")]
	[Description("Inside the worker, ChromiumLocator finds a browser installed in any standard Windows location, Program Files included.")]
	public void Locate_Should_Find_A_Program_Files_Browser_Inside_The_Worker(string installedBrowser) {
		// Arrange
		ChromiumLocator locator = WorkerLocator(ComposeWorkerEnvironment(WindowsHost()), installedBrowser);

		// Act
		string located = locator.Locate();

		// Assert
		located.Should().Be(installedBrowser,
			because: "the worker sees the same install locations as the host, so it finds the same browser");
	}

	[Test]
	[Description("CHROME_PATH set on the host is honoured inside the worker, ahead of the standard locations.")]
	public void Locate_Should_Honour_The_Hosts_Chrome_Path_Inside_The_Worker() {
		// Arrange
		const string chromePath = @"D:\Tools\Chromium\chrome.exe";
		ChromiumLocator locator = WorkerLocator(ComposeWorkerEnvironment(WindowsHost(chromePath)), chromePath);

		// Act
		string located = locator.Locate();

		// Assert
		located.Should().Be(chromePath, because: "CHROME_PATH is the operator's override and must reach the worker");
	}

	[Test]
	[Description("Control: with the allowlist as it was before ENG-103286, the same worker finds no Edge under Program Files - the regression these cases guard against, so they cannot pass vacuously.")]
	public void Locate_Should_Miss_Edge_When_The_Allowlist_Drops_The_Program_Files_Variables() {
		// Arrange
		string[] previousAllowlist = [.. WorkerProcessSupervisor.DefaultInheritedEnvironmentVariableAllowlist
			.Except(LocatorInputs, StringComparer.Ordinal)];
		ChromiumLocator locator = WorkerLocator(ComposeWorkerEnvironment(WindowsHost(), previousAllowlist),
			ProgramFilesX86 + @"\Microsoft\Edge\Application\msedge.exe");

		// Act
		Action act = () => locator.Locate();

		// Assert
		act.Should().Throw<ChromiumNotFoundException>(
			because: "without ProgramFiles(x86) the candidate path stays '%ProgramFiles(x86)%\\...', which exists nowhere");
	}
}
