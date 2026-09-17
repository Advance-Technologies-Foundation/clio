using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Clio.Mcp.E2E.Support.Configuration;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio.Mcp.E2E.Support.Mcp;

/// <summary>
/// The arrange half every process-designer E2E fixture repeats: resolve a fresh clio, require a reachable
/// sandbox environment, open one MCP session bounded by a token, and call a tool through it.
/// </summary>
/// <remarks>
/// Lifted out of the per-element fixtures because copying it copied more than code: the Formula fixture
/// inherited the Approval fixture's skip message ("to run the Approval MCP E2E tests") and its comment about
/// "four call sites" on a fixture with three, so a developer who hit the skip was sent to the wrong feature.
/// The pieces most worth owning once are the ones a reader cannot see are shared: the three-minute token and
/// the rule that an unreachable environment IGNORES rather than fails.
/// <para>The subject and the minimum package version are parameters rather than constants, because they are the
/// only two things that differ per fixture - and the version is a gate rather than a caption, so a stand that is
/// merely behind skips instead of failing inside the build.</para>
/// <para>Both process-designer element fixtures are on it: leaving the Approval one on its own copy would have
/// left the next fixture free to copy from either, which is how the first copy travelled.</para>
/// </remarks>
internal static class ProcessDesignerE2EArrange {

	/// <summary>
	/// Opens a session against the configured sandbox, or ignores the test when none is configured or reachable.
	/// </summary>
	/// <param name="subject">What the skip message should call these tests, e.g. "Formula".</param>
	/// <param name="minimumPackageVersion">
	/// The CrtProcessBuilder the environment must carry. This is a GATE, not only message text: the installed
	/// version is read and a lower one IGNORES the test. A stand that is merely behind should not look like a
	/// broken feature — without the read, an older package fails inside the build with a descriptor error and
	/// sends the developer to the test instead of to the stand.
	/// </param>
	internal static async Task<ProcessDesignerArrangeContext> StartAsync(string subject,
			string minimumPackageVersion) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = settings.Sandbox.EnvironmentName;
		if (string.IsNullOrWhiteSpace(environmentName)) {
			Assert.Ignore($"Configure McpE2E:Sandbox:EnvironmentName (with a CrtProcessBuilder "
				+ $"{minimumPackageVersion} or later) to run the {subject} MCP E2E tests.");
		}
		if (!await ClioCliCommandRunner.IsEnvironmentReachableAsync(settings, environmentName)) {
			Assert.Ignore($"{subject} MCP E2E requires a reachable configured sandbox environment. "
				+ $"'{environmentName}' was not reachable.");
		}
		await EnsurePackageIsNewEnoughAsync(settings, environmentName, subject, minimumPackageVersion);
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ProcessDesignerArrangeContext(session, cancellationTokenSource, environmentName);
	}

	/// <summary>
	/// Ignores the test when the environment's CrtProcessBuilder is older than the feature under test.
	/// </summary>
	/// <remarks>
	/// Fails OPEN on purpose. A version that cannot be read — an older clio whose <c>list-packages</c> shape
	/// differs, a package list that does not name it, a non-zero exit — lets the test run: the alternative is a
	/// silent skip that reads exactly like a pass, which is worse than the confusing failure this gate exists to
	/// replace. Only a version that IS read and IS lower skips.
	/// </remarks>
	private static async Task EnsurePackageIsNewEnoughAsync(McpE2ESettings settings, string environmentName,
			string subject, string minimumPackageVersion) {
		if (!Version.TryParse(minimumPackageVersion, out Version required)) {
			return;
		}
		ClioCliCommandResult listed;
		try {
			listed = await ClioCliCommandRunner.RunAsync(settings,
				["list-packages", "-e", environmentName, "--Json", "true"]);
		} catch (Exception) {
			return;
		}
		if (listed.ExitCode != 0 || !TryReadPackageVersion(listed.StandardOutput, PackageName, out Version installed)) {
			return;
		}
		if (installed < required) {
			Assert.Ignore($"{subject} MCP E2E needs {PackageName} {minimumPackageVersion} or later on "
				+ $"'{environmentName}'; it carries {installed}. Install it with install-process-builder.");
		}
	}

	/// <summary>Reads one package's version out of the JSON <c>list-packages</c> prints.</summary>
	/// <remarks>
	/// Shape-tolerant by design: it walks every object in the document and matches on a name property rather
	/// than on a path, so a change to the envelope around the list degrades to "could not read" — which this
	/// gate treats as "run the test" — instead of to a wrong answer.
	/// </remarks>
	private static bool TryReadPackageVersion(string json, string packageName, out Version version) {
		version = null;
		if (string.IsNullOrWhiteSpace(json)) {
			return false;
		}
		JsonNode document;
		try {
			document = JsonNode.Parse(json);
		} catch (JsonException) {
			return false;
		}
		foreach (JsonObject candidate in Objects(document)) {
			string name = candidate.TryGetPropertyValue("Name", out JsonNode named)
				|| candidate.TryGetPropertyValue("name", out named)
					? named?.GetValue<string>()
					: null;
			if (!string.Equals(name, packageName, StringComparison.OrdinalIgnoreCase)) {
				continue;
			}
			foreach (string key in new[] { "Version", "version", "PackageVersion", "packageVersion" }) {
				if (candidate.TryGetPropertyValue(key, out JsonNode value)
						&& Version.TryParse(value?.ToString(), out version)) {
					return true;
				}
			}
		}
		return false;
	}

	/// <summary>Every object in the document, nested ones included.</summary>
	private static IEnumerable<JsonObject> Objects(JsonNode node) {
		switch (node) {
			case JsonObject o:
				yield return o;
				foreach (KeyValuePair<string, JsonNode> property in o) {
					foreach (JsonObject nested in Objects(property.Value)) {
						yield return nested;
					}
				}
				break;
			case JsonArray a:
				foreach (JsonNode item in a) {
					foreach (JsonObject nested in Objects(item)) {
						yield return nested;
					}
				}
				break;
		}
	}

	/// <summary>The package every process-designer tool requires.</summary>
	private const string PackageName = "CrtProcessBuilder";

	/// <summary>Calls a tool, first asserting it is discoverable — a missing tool is a different failure.</summary>
	internal static async Task<CallToolResult> CallToolAsync(ProcessDesignerArrangeContext context,
			string toolName, Dictionary<string, object?> args) {
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);
		toolNames.Should().Contain(toolName,
			because: "the tool must be discoverable before the end-to-end call");
		return await context.Session.CallToolAsync(
			toolName, new Dictionary<string, object?> { ["args"] = args }, context.CancellationTokenSource.Token);
	}

	/// <summary>Reads a process back by code through <c>describe-business-process</c>.</summary>
	internal static async Task<CallToolResult> DescribeAsync(ProcessDesignerArrangeContext context,
			string processCode) =>
		await context.Session.CallToolAsync(
			Clio.Command.McpServer.Tools.ProcessDesigner.DescribeProcessTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = context.EnvironmentName,
					["process-name"] = processCode
				}
			},
			context.CancellationTokenSource.Token);

}

/// <summary>One MCP session plus the environment it is bound to, disposed together.</summary>
internal sealed record ProcessDesignerArrangeContext(
	McpServerSession Session,
	CancellationTokenSource CancellationTokenSource,
	string EnvironmentName) : IAsyncDisposable {

	/// <inheritdoc />
	public async ValueTask DisposeAsync() {
		await Session.DisposeAsync();
		CancellationTokenSource.Dispose();
	}
}
