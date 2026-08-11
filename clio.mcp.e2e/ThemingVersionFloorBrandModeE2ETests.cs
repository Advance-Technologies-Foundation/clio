using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Command.Theming;
using Clio.Common;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;


namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for the LIVE Creatio version floor enforcement of the create-theme brand mode
/// (ENG-93989): against a pre-10 environment the tool must refuse with the stable
/// <c>version-too-old</c> error code before any theme mutation. A real pre-10 stand is not available to
/// the suite, so the fixture registers a loopback HTTP stub (in an isolated <c>CLIO_HOME</c>) that
/// completes clio's forms-auth handshake and reports a pre-10 core version from
/// <c>ApplicationInfoService</c> — the exact probe <see cref="Clio.Common.CreatioVersionProvider"/> runs
/// for the <c>[RequiresCreatioVersion]</c> gate. The gate runs before the executor body, so the refusal is
/// attributable to the environment alone — no build work runs to confuse the cause.
/// The stub-backed live-gate pattern mirrors <see cref="GetCreatioInfoToolE2ETests"/>.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("theming-version-floor")]
[NonParallelizable]
public sealed class ThemingVersionFloorBrandModeE2ETests : McpContractFixtureBase {

	private const string EnvironmentName = "pre10-theming-stub";
	private const string StubCoreVersion = "9.0.0.100";

	// Suppressed: the stub must start inside ConfigureMcpServerSettings (its URI goes into the child
	// process appsettings before the shared server starts), which the analyzer cannot track; it IS
	// disposed in the [OneTimeTearDown] StopPre10StubAsync below.
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Structure", "NUnit1032:An IDisposable field/property should be Disposed in a TearDown method")]
	private Pre10CreatioApplicationStub? _stub;

	/// <inheritdoc />
	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		_stub = Pre10CreatioApplicationStub.Start();
		string clioHome = CreateIsolatedClioHome(
			$$"""
			{
			  "ActiveEnvironmentKey": "{{EnvironmentName}}",
			  "Autoupdate": false,
			  "Features": {},
			  "Environments": {
			    "{{EnvironmentName}}": {
			      "Uri": "{{_stub.ApplicationUri}}",
			      "Login": "Supervisor",
			      "Password": "Supervisor",
			      "IsNetCore": false
			    }
			  }
			}
			""",
			GetType().Name);
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = clioHome;
	}

	// Runs before the base fixture's OneTimeTearDown (NUnit tears down most-derived first), so the stub
	// disappears only after the last test; the shared MCP server outliving it a moment is harmless.
	[OneTimeTearDown]
	public async Task StopPre10StubAsync() {
		if (_stub is not null) {
			await _stub.DisposeAsync();
			_stub = null;
		}
	}

	[Test]
	[AllureTag(CreateThemeTool.ToolName)]
	[AllureName("brand-mode create-theme refuses a pre-10 environment with version-too-old")]
	[Description("Calls create-theme in the brand mode (primary + explicit template version) against the registered pre-10 loopback stub and verifies the structured failure carries the stable version-too-old error code and the advertised floor — the live [RequiresCreatioVersion] gate, not the offline template check (ENG-93989).")]
	public async Task CreateTheme_Should_RefuseWithVersionTooOld_WhenBrandModeTargetsPre10Environment() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			CreateThemeTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = EnvironmentName,
					["caption"] = "Pre10 floor probe",
					["primary"] = "#004fd6"
				}
			},
			context.CancellationTokenSource.Token);
		CreateThemeResult result = EntitySchemaStructuredResultParser.Extract<CreateThemeResult>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an unmet version requirement is an expected, caller-actionable structured failure, not an MCP protocol error");
		result.Success.Should().BeFalse(
			because: $"the brand mode must never create a theme on an environment older than {ThemeServiceRequirement.MinVersion}");
		result.Error.Should().Contain(CreatioVersionRequirementException.VersionTooOldCode,
			because: "the stable version-too-old error code must travel in the message so agents can branch on the failure class");
		result.Error.Should().Contain($"requires Creatio {ThemeServiceRequirement.MinVersion} or later",
			because: "the failure must state the floor the target environment misses");
	}

	/// <summary>
	/// Loopback HTTP stub impersonating a PRE-10 Creatio environment: it completes clio's forms-auth
	/// handshake and answers the ungated <c>ApplicationInfoService</c> probe with a pre-10
	/// <c>coreVersion</c>, so the <c>[RequiresCreatioVersion]</c> gate resolves a REAL (too old) version
	/// instead of failing the probe. Modeled on the <c>NonCreatioApplicationStub</c> nested in
	/// <see cref="GetCreatioInfoToolE2ETests"/>; the cliogate <c>GetSysInfo</c> fallback is left at 404 —
	/// the primary probe already yields a parseable version, so the secondary is never consulted.
	/// </summary>
	private sealed class Pre10CreatioApplicationStub : IAsyncDisposable {
		private readonly CancellationTokenSource _cancellationTokenSource = new();
		private readonly HttpListener _listener;
		private readonly Task _listenerLoop;

		private Pre10CreatioApplicationStub(HttpListener listener, string applicationUri) {
			_listener = listener;
			ApplicationUri = applicationUri;
			_listenerLoop = Task.Run(ListenAsync);
		}

		public string ApplicationUri { get; }

		public static Pre10CreatioApplicationStub Start() {
			for (int attempt = 0; attempt < 5; attempt++) {
				int port = Random.Shared.Next(20_000, 60_000);
				HttpListener listener = new();
				listener.Prefixes.Add($"http://127.0.0.1:{port}/");
				try {
					listener.Start();
					return new Pre10CreatioApplicationStub(listener, $"http://127.0.0.1:{port}");
				} catch (HttpListenerException) {
					listener.Close();
				}
			}
			throw new InvalidOperationException("Unable to start the pre-10 Creatio loopback stub.");
		}

		public async ValueTask DisposeAsync() {
			_cancellationTokenSource.Cancel();
			_listener.Stop();
			try {
				await _listenerLoop.ConfigureAwait(false);
			} catch (OperationCanceledException) {
				// Expected when the fixture stops the listener.
			} finally {
				_listener.Close();
				_cancellationTokenSource.Dispose();
			}
		}

		private async Task ListenAsync() {
			while (!_cancellationTokenSource.IsCancellationRequested) {
				HttpListenerContext context;
				try {
					context = await _listener.GetContextAsync()
						.WaitAsync(_cancellationTokenSource.Token)
						.ConfigureAwait(false);
				} catch (OperationCanceledException) {
					return;
				} catch (HttpListenerException) when (_cancellationTokenSource.IsCancellationRequested) {
					return;
				} catch (ObjectDisposedException) when (_cancellationTokenSource.IsCancellationRequested) {
					return;
				}
				await RespondAsync(context).ConfigureAwait(false);
			}
		}

		private static async Task RespondAsync(HttpListenerContext context) {
			string path = context.Request.Url?.AbsolutePath ?? string.Empty;
			string body;
			if (path.EndsWith("/ServiceModel/AuthService.svc/Login", StringComparison.Ordinal)) {
				context.Response.SetCookie(new Cookie(".ASPXAUTH", "stub-session", "/"));
				context.Response.SetCookie(new Cookie("BPMCSRF", "stub-csrf", "/"));
				context.Response.ContentType = "application/json";
				body = "{\"Code\":0}";
			} else if (path.EndsWith("/ping", StringComparison.Ordinal)) {
				context.Response.ContentType = "application/json";
				body = "{}";
			} else if (path.EndsWith(
					"/ServiceModel/ApplicationInfoService.svc/GetApplicationInfo", StringComparison.Ordinal)) {
				context.Response.ContentType = "application/json";
				body = $"{{\"applicationInfo\":{{\"sysValues\":{{\"coreVersion\":\"{StubCoreVersion}\"}}}}}}";
			} else {
				context.Response.StatusCode = (int)HttpStatusCode.NotFound;
				context.Response.ContentType = "text/plain";
				body = "Not Found";
			}
			byte[] bytes = Encoding.UTF8.GetBytes(body);
			context.Response.ContentLength64 = bytes.Length;
			await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
			context.Response.Close();
		}
	}
}
