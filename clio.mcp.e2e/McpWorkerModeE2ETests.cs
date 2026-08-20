using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Common.McpWorker;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Creatio;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Stage 3 of the MCP worker execution boundary (ENG-95262), end to end against a real
/// <c>clio mcp-server --worker</c> child: no secret reaches the worker's command line or environment block
/// (TC-E-301), a bearer principal is observed AT THE CREATIO END as that principal (TC-E-302), a worker given
/// unusable material refuses instead of falling back to a default identity (TC-E-303), and the worker's tool
/// generation comes from the parent rather than from <c>appsettings.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>This fixture owns the child process itself</b> rather than letting the SDK's stdio transport spawn it, for
/// two reasons that are both assertions rather than preferences. The process identifier is required to read the
/// worker's command line and environment block back from the operating system, and the child's environment must
/// be composed the way the production supervisor composes it — cleared, then the inherited-variable allowlist,
/// then the parent's explicit variables — so that nothing can hide in an inherited variable the test never
/// looked at. <see cref="StreamClientTransport"/> then speaks MCP over the streams of the process this fixture
/// started.
/// </para>
/// <para>
/// Nothing here is wired to clio's default dispatch path: Stage 4b/6 does the routing. These tests spawn the
/// worker explicitly, exactly as the parent will once routing lands.
/// </para>
/// </remarks>
[TestFixture]
[AllureNUnit]
[NonParallelizable]
public sealed class McpWorkerModeE2ETests {

	private const string EnvironmentName = "worker-mode-stub-e2e";
	private const string ListPagesToolName = PageListTool.ToolName;
	private const string ProcessDesignerGatedToolName = "list-user-tasks";
	private const string ProcessDesignerFeature = "process-designer";

	private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(90);

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Starts a real clio mcp-server --worker against a deterministic Creatio stub, has it serve a list-pages call that genuinely authenticates with the registered login and password, and then reads the worker's command line and environment block back from the operating system to prove neither carries the secret: a stdio worker receives only the environment NAME and reads appsettings.json itself.")]
	[AllureFeature(ListPagesToolName)]
	[AllureTag(ListPagesToolName)]
	[AllureName("A worker authenticates from appsettings.json, and no secret appears in its command line or environment block")]
	[AllureDescription("Registers an environment whose password is a per-run marker, spawns clio mcp-server --worker with the supervisor's own inherited-variable allowlist plus the parent's explicit variables, and drives one list-pages call through the child over stdio. Asserts first that the stub actually observed the call — a worker that never authenticated would satisfy a 'no secret in argv' assertion vacuously — and then that the marker appears in neither the argument vector, the composed environment, the operating system's view of the child's command line, nor its environment block where that is readable.")]
	public async Task Worker_Should_CarryNoSecret_InItsCommandLineOrEnvironment() {
		// Arrange
		string secret = $"wkr-pwd-{Guid.NewGuid():N}";
		string login = $"wkr-login-{Guid.NewGuid():N}";
		await using CreatioWedgeStubServer stub = CreatioWedgeStubServer.Start();
		using WorkerHome home = WorkerHome.Create();
		using TemporaryClioSettingsOverride settingsOverride = home.ReplaceSettings($$"""
			{
			  "ActiveEnvironmentKey": "{{EnvironmentName}}",
			  "Environments": {
			    "{{EnvironmentName}}": {
			      "Uri": "{{stub.BaseUrl}}",
			      "Login": "{{login}}",
			      "Password": "{{secret}}",
			      "IsNetCore": false
			    }
			  }
			}
			""");
		settingsOverride.AppSettingsPath.Should().StartWith(home.Path,
			because: "the replaced settings file must live in this fixture's own clio home, never the "
				+ "assembly-shared one every other fixture depends on");
		using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(3));

		// Act
		await using WorkerProcess worker = await home.StartWorkerAsync(
			McpWorkerEnvironment.ComposeChildEnvironment(
				new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
				McpWorkerLifetime.PerCall),
			cancellation.Token);
		CallToolResult callResult = await worker.CallToolAsync(
			ListPagesToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> { ["environment-name"] = EnvironmentName }
			},
			cancellation.Token);
		string? observedCommandLine = WorkerProcessInspector.TryReadCommandLine(worker.ProcessId);
		string? observedEnvironmentBlock = WorkerProcessInspector.TryReadEnvironmentBlock(worker.ProcessId);

		// Assert
		string diagnostics = $" Worker: {worker.Describe()} Stub: {stub.DescribeState()} "
			+ $"Result: {DescribeResult(callResult)}";
		await AllureApi.Step("The worker really did authenticate with the secret it read from disk", () => {
			stub.UnexpectedHandlerFailures.Should().BeEmpty(
				because: $"a broken stub would make every counter below meaningless.{diagnostics}");
			stub.LoginCount.Should().BeGreaterThan(0,
				because: $"the worker must have USED the login and password from appsettings.json; a worker that "
					+ $"never authenticated would satisfy the assertions below vacuously.{diagnostics}");
			stub.ObservedLoginPrincipals.Should().Contain(login,
				because: $"the worker read the REGISTERED principal off disk — it received only the environment "
					+ $"NAME — and did not fall back to the Supervisor default.{diagnostics}");
			return Task.CompletedTask;
		});
		await AllureApi.Step("The secret is absent from the argument vector and the composed environment", () => {
			worker.Arguments.Should().NotContainMatch($"*{secret}*",
				because: $"secret material must never reach a command line (R-1); a stdio worker receives only "
					+ $"the environment NAME.{diagnostics}");
			worker.Arguments.Should().NotContainMatch($"*{login}*",
				because: $"the principal name is credential material too and has no reason to be in argv.{diagnostics}");
			worker.ComposedEnvironment.Values.Should().NotContain(value =>
					value.Contains(secret, StringComparison.Ordinal),
				because: $"the parent hands the child a frozen generation and deadline variables, never "
					+ $"credentials.{diagnostics}");
			return Task.CompletedTask;
		});
		await AllureApi.Step("The operating system's own view of the child carries no secret either", () => {
			// "where readable" is deliberate: a process command line and environment block are read through
			// platform-specific interfaces, and the assertion is skipped rather than faked where the platform
			// does not expose one. The argument vector above is the deterministic half.
			if (observedCommandLine is not null) {
				observedCommandLine.Should().NotContain(secret,
					because: $"the process listing is what a co-located user or an agent host can actually "
						+ $"read.{diagnostics}");
				observedCommandLine.Should().Contain(McpWorkerEnvironment.WorkerFlag,
					because: $"the command line read back must be the WORKER's, or the assertion above proves "
						+ $"nothing about it.{diagnostics}");
			}
			if (observedEnvironmentBlock is not null) {
				observedEnvironmentBlock.Should().NotContain(secret,
					because: $"the environment block is the other place a secret could have been smuggled "
						+ $"into.{diagnostics}");
			}
			return Task.CompletedTask;
		});
	}

	// ═══════════════════════════════════════════════════════════════════════════════════════════════════
	// IDENTITY-UNIFICATION FLIP POINT — switch this to BearerIdentityExpectation.AfterIdentityUnification
	// once a bearer-only environment actually presents its token on the MCP per-call path. Keep
	// MasterToday as the record of what shipped; nothing else in this fixture encodes the column.
	// ═══════════════════════════════════════════════════════════════════════════════════════════════════
	private static readonly BearerIdentityExpectation ExpectedBearerIdentity =
		BearerIdentityExpectation.MasterToday;

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Fail-first identity assertion (TC-E-302): asserts WHO CREATIO SAW, never that the call succeeded. Measures the identity a bearer-only environment presents through a real worker child, and — the part that is specifically about this stage — that a worker and an ordinary host present the IDENTICAL identity, so running a call in a worker adds no second client-construction path.")]
	[AllureFeature(ListPagesToolName)]
	[AllureTag(ListPagesToolName)]
	[AllureName("A worker presents exactly the identity a host presents, and the bearer identity matches the expected column")]
	[AllureDescription("Registers an environment carrying ONLY an access token — no login, no password — and drives the same list-pages call twice against the same deterministic Creatio stub: once through clio mcp-server --worker and once through an ordinary clio mcp-server. The stub records the Authorization header of every SelectQuery and the UserName of every forms-auth login, which is the only witness that distinguishes a delegated principal from a silent fallback: the fallback answers success:true just as well. Two things are asserted. The identities observed for the worker and for the host must be IDENTICAL — that is the Stage 3 guarantee, that a worker introduces no second construction site. And the absolute identity must match the expected column; on master today that column records the measured defect, a forms-auth login as Supervisor with no bearer header ever presented.")]
	public async Task Worker_Should_PresentTheSameIdentityAsTheHost_AtTheCreatioEnd() {
		// Arrange
		string accessToken = $"wkr-bearer-{Guid.NewGuid():N}";
		await using CreatioWedgeStubServer stub = CreatioWedgeStubServer.Start();
		using WorkerHome home = WorkerHome.Create();
		using TemporaryClioSettingsOverride settingsOverride = home.ReplaceSettings($$"""
			{
			  "ActiveEnvironmentKey": "{{EnvironmentName}}",
			  "Environments": {
			    "{{EnvironmentName}}": {
			      "Uri": "{{stub.BaseUrl}}",
			      "AccessToken": "{{accessToken}}",
			      "IsNetCore": false
			    }
			  }
			}
			""");
		settingsOverride.AppSettingsPath.Should().StartWith(home.Path,
			because: "the bearer-only environment must be registered in this fixture's own clio home");
		using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(4));

		// Act
		ObservedIdentity workerIdentity = await MeasureIdentityAsync(home, stub, asWorker: true, cancellation.Token);
		ObservedIdentity hostIdentity = await MeasureIdentityAsync(home, stub, asWorker: false, cancellation.Token);

		// Assert
		string diagnostics = $" worker: {workerIdentity} | host: {hostIdentity} | stub: {stub.DescribeState()}";
		await AllureApi.Step("The instrument saw a request at all in both runs", () => {
			stub.UnexpectedHandlerFailures.Should().BeEmpty(
				because: $"a broken stub would make every identity observation below meaningless.{diagnostics}");
			workerIdentity.SelectRequests.Should().BeGreaterThan(0,
				because: $"an identity assertion needs a request to inspect; zero would mean the worker failed "
					+ $"before presenting any credential.{diagnostics}");
			hostIdentity.SelectRequests.Should().BeGreaterThan(0,
				because: $"the host arm is the comparison baseline and must have reached the stub too.{diagnostics}");
			return Task.CompletedTask;
		});
		await AllureApi.Step("The worker presents exactly the identity the host presents", () => {
			// The Stage 3 guarantee. It is deliberately independent of WHICH identity is correct: a worker must
			// never be the component that changes who Creatio sees, and a second client-construction path is
			// exactly how a bearer caller silently became Supervisor in this codebase once before.
			workerIdentity.AuthorizationHeaders.Should().Equal(hostIdentity.AuthorizationHeaders,
				because: $"a worker must reach Creatio through the same construction path as the host; a "
					+ $"different presented credential means a second site exists.{diagnostics}");
			workerIdentity.LoginPrincipals.Should().Equal(hostIdentity.LoginPrincipals,
				because: $"the authenticated principal must not depend on whether the call ran in a child "
					+ $"process.{diagnostics}");
			return Task.CompletedTask;
		});
		await AllureApi.Step($"The bearer identity matches the '{ExpectedBearerIdentity.Name}' column", () => {
			if (ExpectedBearerIdentity.ExpectBearerHeader) {
				workerIdentity.AuthorizationHeaders.Should().Contain($"Bearer {accessToken}",
					because: $"the delegated principal must reach Creatio AS ITSELF in the "
						+ $"'{ExpectedBearerIdentity.Name}' column.{diagnostics}");
				workerIdentity.LoginPrincipals.Should().BeEmpty(
					because: $"a bearer environment that presents its token never needs a forms-auth "
						+ $"login.{diagnostics}");
				return Task.CompletedTask;
			}

			// MEASURED ON MASTER, 2026-08-17, and recorded rather than tolerated: an environment carrying ONLY
			// an AccessToken performs a forms-auth login as Supervisor and answers success:true. The token is
			// never presented. This is the same class of defect as ENG-93208 B1, it is NOT introduced by the
			// worker boundary (Stage 3 adds no client-construction site), and no assertion on the call's
			// outcome could have found it. Unifying the inline construction site onto ApplicationClientFactory
			// is separate work; when it lands, flip the constant above and this arm disappears.
			workerIdentity.AuthorizationHeaders.Should().NotContain(header =>
					header.Contains(accessToken, StringComparison.Ordinal),
				because: $"the '{ExpectedBearerIdentity.Name}' column records that the access token is never "
					+ $"presented; a token reaching the backend means the defect is FIXED and this fixture's "
					+ $"expectation constant must be flipped.{diagnostics}");
			workerIdentity.LoginPrincipals.Should().Contain(ExpectedBearerIdentity.FallbackPrincipal,
				because: $"the '{ExpectedBearerIdentity.Name}' column records the measured fallback: a "
					+ $"bearer-only environment authenticates as "
					+ $"'{ExpectedBearerIdentity.FallbackPrincipal}'.{diagnostics}");
			return Task.CompletedTask;
		});
	}

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("TC-E-303: a worker asked to serve a call for an environment that is not registered REFUSES, and reaches no backend at all — it never falls back to the registered environment's credentials, to the active environment, or to a default identity.")]
	[AllureFeature(ListPagesToolName)]
	[AllureTag(ListPagesToolName)]
	[AllureName("A worker given unusable material refuses and never falls back to registry credentials")]
	[AllureDescription("Registers one usable environment pointed at the stub, then asks a real worker child to serve a call for a DIFFERENT, unregistered environment name. Asserts the call fails and — the load-bearing half — that the stub observed neither a login nor a SelectQuery: a silent fallback to the registered environment or to the active-environment default would have been indistinguishable from success at the MCP layer.")]
	public async Task Worker_Should_Refuse_WhenTheEnvironmentIsNotRegistered() {
		// Arrange
		string unregisteredEnvironment = $"worker-unregistered-{Guid.NewGuid():N}";
		await using CreatioWedgeStubServer stub = CreatioWedgeStubServer.Start();
		using WorkerHome home = WorkerHome.Create();
		using TemporaryClioSettingsOverride settingsOverride = home.ReplaceSettings($$"""
			{
			  "ActiveEnvironmentKey": "{{EnvironmentName}}",
			  "Environments": {
			    "{{EnvironmentName}}": {
			      "Uri": "{{stub.BaseUrl}}",
			      "Login": "registered-principal",
			      "Password": "registered-secret",
			      "IsNetCore": false
			    }
			  }
			}
			""");
		settingsOverride.AppSettingsPath.Should().StartWith(home.Path,
			because: "the registered environment the worker must NOT fall back to lives in this fixture's home");
		using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(3));

		// Act
		await using WorkerProcess worker = await home.StartWorkerAsync(
			McpWorkerEnvironment.ComposeChildEnvironment(
				new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
				McpWorkerLifetime.PerCall),
			cancellation.Token);
		CallToolResult callResult = await worker.CallToolAsync(
			ListPagesToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> { ["environment-name"] = unregisteredEnvironment }
			},
			cancellation.Token);

		// Assert
		string diagnostics = $" Worker: {worker.Describe()} Stub: {stub.DescribeState()} "
			+ $"Result: {DescribeResult(callResult)}";
		await AllureApi.Step("The call did not answer with data", () => {
			IsSuccessfulAnswer(callResult).Should().BeFalse(
				because: $"an unregistered environment cannot be served, so a success envelope would mean the "
					+ $"worker served something else.{diagnostics}");
			return Task.CompletedTask;
		});
		await AllureApi.Step("No identity was used against any backend", () => {
			stub.LoginCount.Should().Be(0,
				because: $"a fallback to the registered environment's credentials would authenticate through "
					+ $"AuthService.svc/Login.{diagnostics}");
			stub.SelectCount.Should().Be(0,
				because: $"a worker that refuses must not touch a backend at all — reaching the stub would mean "
					+ $"it resolved SOME environment for a name it was never given.{diagnostics}");
			return Task.CompletedTask;
		});
	}

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("The worker's tool generation comes from the parent, not from appsettings.json: with the same settings file declaring process-designer ENABLED, a worker frozen with the flag OFF cannot reach a process-designer tool at all, while a worker frozen with it ON can — so a mid-session settings change cannot move a worker's tool set.")]
	[AllureFeature(ClioRunTool.ToolName)]
	[AllureTag(ClioRunTool.ToolName)]
	[AllureName("A worker's tool set follows the parent's frozen generation, not the settings file")]
	[AllureDescription("Writes a settings file whose features block enables process-designer, then starts two real worker children against that SAME file: one frozen with process-designer=0, one with process-designer=1. Dispatches a process-designer-gated tool through clio-run in each. The frozen-off worker must report the tool as not registered — proving it ignored the settings file — while the frozen-on worker must reach the tool and fail for some other reason. Feature-gated primitives are filtered out of registration before the transport is attached, so this is the only observable form of the guarantee.")]
	public async Task Worker_Should_UseTheParentsFrozenGeneration_NotAppSettings() {
		// Arrange
		await using CreatioWedgeStubServer stub = CreatioWedgeStubServer.Start();
		using WorkerHome home = WorkerHome.Create();
		using TemporaryClioSettingsOverride settingsOverride = home.ReplaceSettings($$"""
			{
			  "ActiveEnvironmentKey": "{{EnvironmentName}}",
			  "Environments": {
			    "{{EnvironmentName}}": {
			      "Uri": "{{stub.BaseUrl}}",
			      "Login": "registered-principal",
			      "Password": "registered-secret",
			      "IsNetCore": false
			    }
			  },
			  "features": {
			    "{{ProcessDesignerFeature}}": true
			  }
			}
			""");
		settingsOverride.AppSettingsPath.Should().StartWith(home.Path,
			because: "the settings file the workers must DISAGREE with lives in this fixture's own clio home");
		using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(4));
		Dictionary<string, object?> gatedCall = new() {
			["command"] = ProcessDesignerGatedToolName,
			["args"] = new Dictionary<string, object?> { ["environment-name"] = EnvironmentName }
		};

		// Act
		string frozenOffAnswer;
		await using (WorkerProcess frozenOff = await home.StartWorkerAsync(
				McpWorkerEnvironment.ComposeChildEnvironment(
					new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
						[ProcessDesignerFeature] = false
					},
					McpWorkerLifetime.PerCall),
				cancellation.Token)) {
			frozenOffAnswer = DescribeResult(await frozenOff.CallToolAsync(
				ClioRunTool.ToolName, gatedCall, cancellation.Token));
		}
		string frozenOnAnswer;
		await using (WorkerProcess frozenOn = await home.StartWorkerAsync(
				McpWorkerEnvironment.ComposeChildEnvironment(
					new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
						[ProcessDesignerFeature] = true
					},
					McpWorkerLifetime.PerCall),
				cancellation.Token)) {
			frozenOnAnswer = DescribeResult(await frozenOn.CallToolAsync(
				ClioRunTool.ToolName, gatedCall, cancellation.Token));
		}

		// Assert
		string diagnostics = $" frozen-off answer: {frozenOffAnswer} | frozen-on answer: {frozenOnAnswer}";
		await AllureApi.Step("The frozen-off worker does not have the gated tool, even though the settings file enables it", () => {
			frozenOffAnswer.Should().Contain($"unknown tool '{ProcessDesignerGatedToolName}'",
				because: $"the worker must resolve the PARENT's generation; reaching the tool here would mean it "
					+ $"read the settings file and disagreed with the parent about its own surface.{diagnostics}");
			return Task.CompletedTask;
		});
		await AllureApi.Step("The frozen-on worker does have it, so the flag is what moved and not the environment", () => {
			frozenOnAnswer.Should().NotContain($"unknown tool '{ProcessDesignerGatedToolName}'",
				because: $"this arm is the control: without it, a worker that registered NO long-tail tools at "
					+ $"all would satisfy the assertion above for the wrong reason.{diagnostics}");
			return Task.CompletedTask;
		});
	}

	// ─────────────────────────────────────────────────────────────────────────────────────────────────
	// Identity measurement
	// ─────────────────────────────────────────────────────────────────────────────────────────────────

	/// <summary>
	/// Drives one <c>list-pages</c> call through a child clio (worker or ordinary host) and returns what the
	/// backend observed about the caller's identity — never whether the call succeeded.
	/// </summary>
	private static async Task<ObservedIdentity> MeasureIdentityAsync(
		WorkerHome home,
		CreatioWedgeStubServer stub,
		bool asWorker,
		CancellationToken cancellationToken) {
		stub.ResetCounters();
		await using WorkerProcess child = await home.StartWorkerAsync(
			McpWorkerEnvironment.ComposeChildEnvironment(
				new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
				McpWorkerLifetime.PerCall),
			cancellationToken,
			asWorker);
		CallToolResult callResult = await child.CallToolAsync(
			ListPagesToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> { ["environment-name"] = EnvironmentName }
			},
			cancellationToken);
		return new ObservedIdentity(
			asWorker ? "worker" : "host",
			stub.SelectCount,
			stub.ObservedSelectAuthorizationHeaders,
			[.. stub.ObservedLoginPrincipals],
			DescribeResult(callResult));
	}

	/// <summary>What the backend observed about one call's caller.</summary>
	/// <param name="Role">Whether the call ran in a worker or in an ordinary host.</param>
	/// <param name="SelectRequests">How many <c>SelectQuery</c> requests reached the backend.</param>
	/// <param name="AuthorizationHeaders">The <c>Authorization</c> headers presented, in arrival order.</param>
	/// <param name="LoginPrincipals">The principals presented to forms auth, in arrival order.</param>
	/// <param name="Answer">The call's answer, for diagnostics only — never asserted as identity evidence.</param>
	private sealed record ObservedIdentity(
		string Role,
		int SelectRequests,
		IReadOnlyList<string> AuthorizationHeaders,
		IReadOnlyList<string> LoginPrincipals,
		string Answer) {
		public override string ToString() =>
			$"{Role}: select={SelectRequests}, authorization=[{string.Join(", ", AuthorizationHeaders)}], "
			+ $"login-principals=[{string.Join(", ", LoginPrincipals)}], answer={Answer}";
	}

	/// <summary>
	/// The two columns of the bearer-identity observation: what master presents today, and the acceptance shape.
	/// </summary>
	/// <param name="Name">Column name, quoted in every assertion message.</param>
	/// <param name="ExpectBearerHeader">Whether the access token must reach Creatio as a bearer header.</param>
	/// <param name="FallbackPrincipal">The principal a forms-auth fallback presents, when one happens.</param>
	private sealed record BearerIdentityExpectation(
		string Name,
		bool ExpectBearerHeader,
		string FallbackPrincipal) {

		/// <summary>
		/// Measured on master (2026-08-17): an environment carrying only an <c>AccessToken</c> performs a
		/// forms-auth login as <c>Supervisor</c> and answers <c>success:true</c>; the token is never presented.
		/// </summary>
		public static BearerIdentityExpectation MasterToday { get; } =
			new("master today (the identity defect)", ExpectBearerHeader: false, FallbackPrincipal: "Supervisor");

		/// <summary>
		/// After the client-construction sites are unified: the token is presented and no login happens at all.
		/// </summary>
		public static BearerIdentityExpectation AfterIdentityUnification { get; } =
			new("after identity unification", ExpectBearerHeader: true, FallbackPrincipal: string.Empty);
	}

	// ─────────────────────────────────────────────────────────────────────────────────────────────────
	// Result shapes
	// ─────────────────────────────────────────────────────────────────────────────────────────────────

	private static bool IsSuccessfulAnswer(CallToolResult callResult) =>
		callResult.IsError != true
		&& callResult.StructuredContent is JsonElement structured
		&& structured.ValueKind == JsonValueKind.Object
		&& structured.TryGetProperty("success", out JsonElement success)
		&& success.ValueKind == JsonValueKind.True;

	private static string DescribeResult(CallToolResult callResult) {
		StringBuilder builder = new();
		builder.Append($"isError={callResult.IsError} ");
		if (callResult.StructuredContent is JsonElement structured) {
			builder.Append(structured.GetRawText());
		}
		foreach (TextContentBlock block in callResult.Content.OfType<TextContentBlock>()) {
			builder.Append(' ').Append(block.Text);
		}
		return builder.ToString().Replace('\r', ' ').Replace('\n', ' ');
	}

	// ─────────────────────────────────────────────────────────────────────────────────────────────────
	// The worker child: an isolated clio home, a process this fixture owns, and MCP over its streams
	// ─────────────────────────────────────────────────────────────────────────────────────────────────

	/// <summary>
	/// An isolated clio home for one test, plus the ability to start real worker children against it.
	/// </summary>
	/// <remarks>
	/// The home isolation is the same shape the wedge fixture uses and for the same reason: the settings
	/// override resolves its path by asking a child clio process, so an inherited assembly-shared
	/// <c>CLIO_HOME</c> would make it rewrite the settings file every other fixture reads.
	/// </remarks>
	private sealed class WorkerHome : IDisposable {

		private readonly McpE2ESettings _settings;

		private WorkerHome(string path, McpE2ESettings settings) {
			Path = path;
			_settings = settings;
		}

		public string Path { get; }

		public static WorkerHome Create() {
			string path = System.IO.Path.Combine(
				System.IO.Path.GetTempPath(), $"clio-worker-mode-e2e-{Guid.NewGuid():N}");
			Directory.CreateDirectory(path);
			McpE2ESettings settings = TestConfiguration.Load();
			settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
			string homeVariableName = OperatingSystem.IsWindows() ? "LOCALAPPDATA" : "HOME";
			settings.ProcessEnvironmentVariables[homeVariableName] = path;
			settings.ProcessEnvironmentVariables["CLIO_HOME"] = path;
			return new WorkerHome(path, settings);
		}

		public TemporaryClioSettingsOverride ReplaceSettings(string content) =>
			TemporaryClioSettingsOverride.ReplaceContent(
				content, _settings.ClioProcessPath, _settings.ProcessEnvironmentVariables);

		public async Task<WorkerProcess> StartWorkerAsync(
			IReadOnlyDictionary<string, string> composedEnvironment,
			CancellationToken cancellationToken,
			bool asWorker = true) {
			ClioProcessDescriptor descriptor = asWorker
				? ClioExecutableResolver.Resolve(_settings, "mcp-server", McpWorkerEnvironment.WorkerFlag)
				: ClioExecutableResolver.Resolve(_settings, "mcp-server");
			ProcessStartInfo startInfo = new() {
				FileName = descriptor.Command,
				WorkingDirectory = descriptor.WorkingDirectory,
				UseShellExecute = false,
				RedirectStandardInput = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			foreach (string argument in descriptor.Arguments) {
				startInfo.ArgumentList.Add(argument);
			}
			// Compose the environment the way the production supervisor does: clear the inherited block, apply
			// its own allowlist, then the parent's explicit variables. Anything the worker sees is therefore
			// something this fixture put there and can assert about.
			startInfo.Environment.Clear();
			foreach (string name in WorkerProcessSupervisor.DefaultInheritedEnvironmentVariableAllowlist) {
				string? value = Environment.GetEnvironmentVariable(name);
				if (value is not null) {
					startInfo.Environment[name] = value;
				}
			}
			Dictionary<string, string> composed = new(StringComparer.Ordinal);
			foreach (KeyValuePair<string, string> pair in composedEnvironment) {
				composed[pair.Key] = pair.Value;
			}
			composed["CLIO_HOME"] = Path;
			composed[OperatingSystem.IsWindows() ? "LOCALAPPDATA" : "HOME"] = Path;
			composed["CLIO_NO_UPDATE_CHECK"] = "true";
			foreach (KeyValuePair<string, string> pair in composed) {
				startInfo.Environment[pair.Key] = pair.Value;
			}

			Process process = Process.Start(startInfo)
				?? throw new InvalidOperationException("Unable to start the clio MCP worker child process.");
			WorkerProcess worker = new(process, [.. descriptor.Arguments], composed);
			await worker.ConnectAsync(cancellationToken);
			return worker;
		}

		public void Dispose() {
			try {
				if (Directory.Exists(Path)) {
					Directory.Delete(Path, recursive: true);
				}
			} catch (IOException) {
				// Best-effort cleanup.
			} catch (UnauthorizedAccessException) {
				// Best-effort cleanup.
			}
		}
	}

	/// <summary>One live <c>clio mcp-server --worker</c> child and an MCP client speaking over its streams.</summary>
	private sealed class WorkerProcess : IAsyncDisposable {

		private readonly Process _process;
		private readonly StringBuilder _standardError = new();
		private McpClient? _client;
		private Task? _standardErrorPump;

		public WorkerProcess(
			Process process,
			IReadOnlyList<string> arguments,
			IReadOnlyDictionary<string, string> composedEnvironment) {
			_process = process;
			Arguments = arguments;
			ComposedEnvironment = composedEnvironment;
		}

		public int ProcessId => _process.Id;

		public IReadOnlyList<string> Arguments { get; }

		public IReadOnlyDictionary<string, string> ComposedEnvironment { get; }

		public async Task ConnectAsync(CancellationToken cancellationToken) {
			// Drain standard error continuously. An undrained pipe eventually blocks the child, which would
			// surface as an unexplained hang rather than as a failed assertion.
			_standardErrorPump = Task.Run(async () => {
				string? line;
				while ((line = await _process.StandardError.ReadLineAsync()) is not null) {
					lock (_standardError) {
						_standardError.AppendLine(line);
					}
				}
			});
			StreamClientTransport transport = new(
				_process.StandardInput.BaseStream,
				_process.StandardOutput.BaseStream,
				NullLoggerFactory.Instance);
			_client = await McpClient.CreateAsync(
				transport,
				new McpClientOptions {
					ClientInfo = new Implementation { Name = "clio.mcp.e2e.worker-parent", Version = "1.0.0" }
				},
				NullLoggerFactory.Instance,
				cancellationToken);
		}

		public async Task<CallToolResult> CallToolAsync(
			string toolName,
			IReadOnlyDictionary<string, object?> arguments,
			CancellationToken cancellationToken) {
			using CancellationTokenSource callBudget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			callBudget.CancelAfter(CallBudget);
			McpClient client = _client ?? throw new InvalidOperationException(
				"ConnectAsync has to run before a tool call: without it this fixture would report a transport "
				+ "defect as a worker failure.");
			return await client.CallToolAsync(toolName, arguments, cancellationToken: callBudget.Token);
		}

		public string Describe() {
			string standardError;
			lock (_standardError) {
				standardError = _standardError.ToString();
			}
			string shortened = standardError.Length <= 400 ? standardError : standardError[..400];
			return $"pid={ProcessId}, args=[{string.Join(" ", Arguments)}], "
				+ $"stderr=[{shortened.Replace('\r', ' ').Replace('\n', ' ')}]";
		}

		public async ValueTask DisposeAsync() {
			if (_client is not null) {
				try {
					await _client.DisposeAsync();
				} catch (Exception) {
					// Teardown must not hide an assertion failure.
				}
			}
			try {
				if (!_process.HasExited) {
					_process.Kill(entireProcessTree: true);
				}
			} catch (Exception) {
				// The child already exited.
			}
			if (_standardErrorPump is not null) {
				try {
					await _standardErrorPump;
				} catch (Exception) {
					// The pipe was torn down with the process.
				}
			}
			_process.Dispose();
		}
	}

	/// <summary>
	/// Reads a live process's command line and environment block back from the operating system.
	/// </summary>
	/// <remarks>
	/// Platform-specific by necessity — the assertion's subject IS what the operating system exposes about the
	/// child — and returns <see langword="null"/> rather than guessing where a platform exposes nothing. On
	/// Windows the environment block of another process is not readable without debug-level access, so only the
	/// command line is available there.
	/// </remarks>
	private static class WorkerProcessInspector {

		public static string? TryReadCommandLine(int processId) {
			if (OperatingSystem.IsLinux()) {
				return TryReadProcFile(processId, "cmdline");
			}
			if (OperatingSystem.IsMacOS()) {
				return TryRunCapture("/bin/ps", ["-o", "command=", "-p", processId.ToString()]);
			}
			if (OperatingSystem.IsWindows()) {
				return TryRunCapture("powershell", [
					"-NoProfile",
					"-Command",
					$"(Get-CimInstance Win32_Process -Filter \"ProcessId={processId}\").CommandLine"
				]);
			}
			return null;
		}

		public static string? TryReadEnvironmentBlock(int processId) {
			if (OperatingSystem.IsLinux()) {
				return TryReadProcFile(processId, "environ");
			}
			if (OperatingSystem.IsMacOS()) {
				// `ps eww` prints the environment of a process owned by the same user.
				return TryRunCapture("/bin/ps", ["eww", "-o", "command=", "-p", processId.ToString()]);
			}
			return null;
		}

		private static string? TryReadProcFile(int processId, string fileName) {
			try {
				string path = $"/proc/{processId}/{fileName}";
				return File.Exists(path) ? File.ReadAllText(path).Replace('\0', ' ') : null;
			} catch (IOException) {
				return null;
			} catch (UnauthorizedAccessException) {
				return null;
			}
		}

		private static string? TryRunCapture(string fileName, string[] arguments) {
			try {
				ProcessStartInfo startInfo = new() {
					FileName = fileName,
					UseShellExecute = false,
					RedirectStandardOutput = true,
					RedirectStandardError = true
				};
				foreach (string argument in arguments) {
					startInfo.ArgumentList.Add(argument);
				}
				using Process? process = Process.Start(startInfo);
				if (process is null) {
					return null;
				}
				string output = process.StandardOutput.ReadToEnd();
				process.WaitForExit(10_000);
				return string.IsNullOrWhiteSpace(output) ? null : output;
			} catch (Exception) {
				// An inspection interface that is unavailable is reported as unavailable, never as "clean".
				return null;
			}
		}
	}
}
