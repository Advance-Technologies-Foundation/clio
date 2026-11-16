using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using Clio.Package;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E.Support.Creatio;

/// <summary>
/// The Freedom UI page <c>ClioMcp_BlankPageToSave</c> that the page write fixtures share on the stand, and the
/// one place that brings it back to its blank body.
/// </summary>
/// <remarks>
/// <para>
/// <c>PageSyncToolE2ETests</c> and <c>PageUpdateToolE2ETests</c> write this one page from about fifteen tests
/// and build every probe body from the body they read first. Each test used to trust the cleanup of the test
/// before it, so one restore that did not take effect failed up to seven later tests with errors about a
/// missing empty marker pair (TeamCity builds 16330337 and 16330482). A test now takes a
/// <see cref="Lease"/>: it resets the page before the test when the page is not blank, and the fixture's
/// <c>[TearDown]</c> resets it again afterwards. Both resets go through <c>get-page</c> and
/// <c>sync-pages</c>, assert that the save succeeded and that the next read shows the blank body, and put the
/// tool's own error and a description of the page's stored layers in the failure message.
/// </para>
/// <para>
/// The reset lives in <c>[TearDown]</c> rather than in a <c>finally</c> block of the test: an exception
/// thrown in <c>finally</c> replaces the assertion that failed in the test body, while NUnit reports a
/// teardown failure next to it.
/// </para>
/// </remarks>
internal static class SeededBlankPage {

	/// <summary>The schema name of the shared page.</summary>
	internal const string SchemaName = "ClioMcp_BlankPageToSave";

	/// <summary>
	/// How many reads the reset waits for its own write, the same short window
	/// <c>docs/knowledge/McpServer/a-completed-sync-pages-write-is-not-always-visible-to-the-next-get-page.md</c>
	/// records. It bounds the ARRANGEMENT only; the test that verifies the visibility of a completed write
	/// keeps its own budget and its own assertion.
	/// </summary>
	internal const int ResetObservationAttempts = 6;

	private static readonly TimeSpan ResetObservationPollInterval = TimeSpan.FromSeconds(1);
	private static readonly TimeSpan OperationTimeout = TimeSpan.FromMinutes(3);
	private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
	private const int DiagnosticSnippetLength = 400;
	private const string ClientUnitSchemaManagerName = "ClientUnitSchemaManager";

	// Every section whose content a probe fills. SCHEMA_ARGS is left out on purpose: the blank body carries
	// "()" there and no test writes it.
	private static readonly string[] SectionMarkers = [
		"SCHEMA_DEPS",
		"SCHEMA_VIEW_CONFIG_DIFF",
		"SCHEMA_VIEW_MODEL_CONFIG_DIFF",
		"SCHEMA_MODEL_CONFIG_DIFF",
		"SCHEMA_HANDLERS",
		"SCHEMA_CONVERTERS",
		"SCHEMA_VALIDATORS"
	];

	private const string LastSectionMarker = "/**SCHEMA_VALIDATORS*/";

	// The stored layers are listed once per test process, on the first lease, so a build that later fails
	// already has the state the stand started from in its log.
	private static int _layersReported;

	/// <summary>
	/// The blank body the reset writes: the body clio itself gives a replacing schema it creates
	/// (<see cref="PageUpdateCommand.BuildEmptyReplacingBody"/>), which is also what <c>get-page</c> returns
	/// for a page that has no editable schema yet.
	/// </summary>
	/// <returns>The blank web page body of <see cref="SchemaName"/>.</returns>
	internal static string BuildBlankBody() =>
		PageUpdateCommand.BuildEmptyReplacingBody(SchemaName, PageSchemaType.Web);

	/// <summary>
	/// Decides whether <paramref name="body"/> is the blank page: every section between a marker pair is
	/// empty and nothing but the closing brackets of the module follows the last section.
	/// </summary>
	/// <param name="body">The body <c>get-page</c> wrote to <c>body.js</c>.</param>
	/// <param name="reason">What makes the body not blank; <c>null</c> when it is blank.</param>
	/// <returns><c>true</c> when a probe can be built from the body without discarding content.</returns>
	internal static bool IsBlank(string? body, out string? reason) {
		if (string.IsNullOrWhiteSpace(body)) {
			reason = "the body is empty";
			return false;
		}
		foreach (string markerName in SectionMarkers) {
			string marker = Regex.Escape($"/**{markerName}*/");
			Match section = Regex.Match(body, $"{marker}(?<content>[\\s\\S]*?){marker}",
				RegexOptions.CultureInvariant, RegexTimeout);
			if (!section.Success) {
				reason = $"the {markerName} marker pair is missing";
				return false;
			}
			string content = RemoveWhitespace(section.Groups["content"].Value);
			if (content is not ("" or "[]" or "{}")) {
				reason = $"the {markerName} section is not empty: {Shorten(content)}";
				return false;
			}
		}
		string tail = RemoveWhitespace(body[(body.LastIndexOf(LastSectionMarker, StringComparison.Ordinal)
			+ LastSectionMarker.Length)..]);
		if (tail.Any(character => character is not ('}' or ')' or ';'))) {
			reason = $"content follows the module: {Shorten(tail)}";
			return false;
		}
		reason = null;
		return true;
	}

	/// <summary>
	/// Resets the page when it is not blank and returns the lease the fixture's <c>[TearDown]</c> ends.
	/// Call it only from a test that is allowed to write the stand.
	/// </summary>
	/// <param name="session">The MCP server session of the fixture.</param>
	/// <param name="environmentName">The sandbox environment the test writes.</param>
	/// <returns>The lease to hand to <see cref="Lease.EndAsync"/> in <c>[TearDown]</c>.</returns>
	internal static async Task<Lease> LeaseAsync(McpServerSession session, string environmentName) {
		ArgumentNullException.ThrowIfNull(session);
		if (Interlocked.Exchange(ref _layersReported, 1) == 0) {
			using CancellationTokenSource reportCts = new(OperationTimeout);
			TestContext.Progress.WriteLine(
				$"[seed-page] stored layers of '{SchemaName}' before the first write of this run:{Environment.NewLine}"
				+ await DescribeStoredLayersAsync(session, environmentName, reportCts.Token));
		}
		await EnsureBlankAsync(session, environmentName, "before the test");
		return new Lease(session, environmentName);
	}

	/// <summary>
	/// Reads the page and, when it is not blank, writes the blank body with <c>sync-pages</c> and asserts
	/// that the save succeeded and that <c>get-page</c> shows it.
	/// </summary>
	/// <param name="session">The MCP server session of the fixture.</param>
	/// <param name="environmentName">The sandbox environment.</param>
	/// <param name="phase">When the reset runs, for the failure message.</param>
	internal static async Task EnsureBlankAsync(McpServerSession session, string environmentName, string phase) {
		// Not the test's token: after a failed test it may already be cancelled.
		using CancellationTokenSource cancellation = new(OperationTimeout);
		// A dedicated anchor: no .clio-pages baseline of a test takes part, and none is left behind for one.
		string anchor = Directory.CreateTempSubdirectory("clio-e2e-seed-page-").FullName;
		try {
			PageRead first = await ReadAsync(session, environmentName, anchor, cancellation.Token);
			if (!first.Response.Success) {
				string diagnostics = await DescribeAsync(session, environmentName, cancellation.Token);
				first.Response.Success.Should().BeTrue(
					because: "get-page must read the shared page '{0}' {1}. Error: {2}. {3}",
					SchemaName, phase, first.Response.Error, diagnostics);
			}
			if (IsBlank(first.Body, out string? reason)) {
				return;
			}
			TestContext.Progress.WriteLine(
				$"[seed-page] '{SchemaName}' is not blank {phase} ({reason}); resetting it. Read: {first.Describe()}");

			PageSyncPageResult? reset = await WriteBlankAsync(session, environmentName, anchor, cancellation.Token);
			if (reset is not { Success: true }) {
				string diagnostics = await DescribeAsync(session, environmentName, cancellation.Token);
				(reset?.Success ?? false).Should().BeTrue(
					because: "sync-pages must reset the shared page '{0}' to its blank body {1}; it was not blank "
						+ "because {2}. Error of the reset: {3}. Warnings: {4}. {5}",
					SchemaName, phase, reason, reset?.Error ?? "sync-pages returned no page result",
					string.Join(" | ", reset?.Validation?.Warnings ?? []), diagnostics);
			}

			List<string> reads = [];
			PageRead observed = await BoundedPollGate.PollUntilAsync(
				async pollToken => {
					PageRead read = await ReadAsync(session, environmentName, anchor, pollToken);
					reads.Add(read.Describe());
					return read;
				},
				read => read.Response.Success && IsBlank(read.Body, out _),
				ResetObservationAttempts,
				ResetObservationPollInterval,
				cancellation.Token);
			if (reads.Count > 1) {
				// Recorded even when the poll recovers: a reset that was not readable at once is the very
				// observation the investigation of the stale read-back needs.
				TestContext.Progress.WriteLine(
					$"[seed-page] the reset of '{SchemaName}' {phase} was readable only on read {reads.Count}: "
					+ string.Join(" || ", reads));
			}
			string? stillNotBlank = null;
			bool blank = observed.Response.Success && IsBlank(observed.Body, out stillNotBlank);
			if (!blank) {
				string diagnostics = await DescribeAsync(session, environmentName, cancellation.Token);
				blank.Should().BeTrue(
					because: "sync-pages reported the reset of the shared page '{0}' {1} as saved (digest checksum "
						+ "{2}, conflict check {3}), yet {4} get-page read(s) over ~{5}s did not return the blank "
						+ "body: {6}. Reads: {7}. {8}",
					SchemaName, phase, reset?.Digest?.Checksum ?? "none", reset?.ConflictCheck ?? "none", reads.Count,
					(reads.Count - 1) * ResetObservationPollInterval.TotalSeconds,
					observed.Response.Success ? stillNotBlank : observed.Response.Error,
					string.Join(" || ", reads), diagnostics);
			}
		} finally {
			TryDeleteDirectory(anchor);
		}
	}

	/// <summary>
	/// Describes what the stand holds for the page: what <c>get-page</c> resolves and every stored
	/// <c>SysSchema</c> row of that name. Never throws; a part that could not be read says why.
	/// </summary>
	/// <param name="session">The MCP server session of the fixture.</param>
	/// <param name="environmentName">The sandbox environment.</param>
	/// <param name="cancellationToken">Cancels the reads.</param>
	/// <returns>A multi-line description for a failure message or the build log.</returns>
	internal static async Task<string> DescribeAsync(
		McpServerSession session, string environmentName, CancellationToken cancellationToken) {
		StringBuilder description = new();
		description.AppendLine().AppendLine($"--- state of '{SchemaName}' on '{environmentName}' ---");
		string anchor = Directory.CreateTempSubdirectory("clio-e2e-seed-page-diag-").FullName;
		try {
			PageRead read = await ReadAsync(session, environmentName, anchor, cancellationToken);
			description.AppendLine($"get-page: {read.Describe()}");
			description.AppendLine($"get-page body: {Shorten(read.Body ?? string.Empty)}");
			description.AppendLine($"get-page schema chain (first is the page): {await ReadSchemaChainAsync(read, cancellationToken)}");
		} catch (Exception readFailure) {
			description.AppendLine($"get-page could not be read: {readFailure.Message}");
		} finally {
			TryDeleteDirectory(anchor);
		}
		description.AppendLine(await DescribeStoredLayersAsync(session, environmentName, cancellationToken));
		return description.ToString();
	}

	/// <summary>
	/// Lists every <c>SysSchema</c> row named <see cref="SchemaName"/> through <c>execute-esq</c>, which reads
	/// the database and not the schema designer. More than one row in one package means the name lookups of
	/// <c>get-page</c> and <c>update-page</c> (one row, no order) can resolve different schemas.
	/// </summary>
	/// <param name="session">The MCP server session of the fixture.</param>
	/// <param name="environmentName">The sandbox environment.</param>
	/// <param name="cancellationToken">Cancels the read.</param>
	/// <returns>One line per stored row, or the reason the rows could not be read.</returns>
	internal static async Task<string> DescribeStoredLayersAsync(
		McpServerSession session, string environmentName, CancellationToken cancellationToken) {
		try {
			object query = SelectQueryHelper.BuildSelectQuery("SysSchema",
				[
					new SelectQueryHelper.SelectQueryColumnDefinition("UId", "UId"),
					new SelectQueryHelper.SelectQueryColumnDefinition("SysPackage.Name", "PackageName"),
					new SelectQueryHelper.SelectQueryColumnDefinition("SysPackage.UId", "PackageUId"),
					new SelectQueryHelper.SelectQueryColumnDefinition("ExtendParent", "ExtendParent"),
					new SelectQueryHelper.SelectQueryColumnDefinition("Checksum", "Checksum"),
					new SelectQueryHelper.SelectQueryColumnDefinition("CreatedOn", "CreatedOn", OrderDirection: 1,
						OrderPosition: 0),
					new SelectQueryHelper.SelectQueryColumnDefinition("ModifiedOn", "ModifiedOn")
				],
				[
					new SelectQueryHelper.SelectQueryFilterDefinition("Name", SchemaName,
						SelectQueryHelper.TextDataValueType),
					new SelectQueryHelper.SelectQueryFilterDefinition("ManagerName", ClientUnitSchemaManagerName,
						SelectQueryHelper.TextDataValueType)
				], rowCount: 50);
			CallToolResult result = await session.CallToolAsync(
				ExecuteEsqTool.ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["query"] = JsonSerializer.SerializeToElement(query)
					}
				},
				cancellationToken);
			ExecuteEsqResponse response = EntitySchemaStructuredResultParser.Extract<ExecuteEsqResponse>(result);
			if (!response.Success || response.Rows is not { ValueKind: JsonValueKind.Array } rows) {
				return $"SysSchema rows could not be read: {response.Error ?? "no rows array in the answer"}";
			}
			return DescribeStoredLayers(rows);
		} catch (Exception readFailure) {
			return $"SysSchema rows could not be read: {readFailure.Message}";
		}
	}

	/// <summary>
	/// Formats the <c>SysSchema</c> rows of the page and names the packages that hold more than one.
	/// </summary>
	/// <param name="rows">The rows array of the SelectQuery answer.</param>
	/// <returns>One line per row and a verdict line.</returns>
	internal static string DescribeStoredLayers(JsonElement rows) {
		StringBuilder description = new();
		List<string> packages = [];
		foreach (JsonElement row in rows.EnumerateArray()) {
			string package = Text(row, "PackageName");
			packages.Add(package);
			description.AppendLine(
				$"SysSchema row: uId={Text(row, "UId")} package={package} ({Text(row, "PackageUId")}) "
				+ $"extendParent={Text(row, "ExtendParent")} checksum={Text(row, "Checksum")} "
				+ $"createdOn={Text(row, "CreatedOn")} modifiedOn={Text(row, "ModifiedOn")}");
		}
		string[] duplicated = packages
			.GroupBy(package => package, StringComparer.OrdinalIgnoreCase)
			.Where(group => group.Count() > 1)
			.Select(group => $"{group.Key} x{group.Count()}")
			.ToArray();
		description.Append(duplicated.Length > 0
			? $"SysSchema rows: {packages.Count}. MORE THAN ONE ROW IN ONE PACKAGE ({string.Join(", ", duplicated)}): "
				+ "a read and a write can resolve different schemas of this page."
			: $"SysSchema rows: {packages.Count}, at most one per package.");
		return description.ToString();
	}

	private static async Task<PageRead> ReadAsync(
		McpServerSession session, string environmentName, string anchor, CancellationToken cancellationToken) {
		CallToolResult result = await session.CallToolAsync(
			PageGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = SchemaName,
					["environment-name"] = environmentName,
					["output-directory"] = anchor
				}
			},
			cancellationToken);
		PageGetResponse response = EntitySchemaStructuredResultParser.Extract<PageGetResponse>(result);
		string? body = response.Success && !string.IsNullOrWhiteSpace(response.Files?.BodyFile)
			? await File.ReadAllTextAsync(response.Files.BodyFile, cancellationToken)
			: null;
		return new PageRead(response, body);
	}

	private static async Task<PageSyncPageResult?> WriteBlankAsync(
		McpServerSession session, string environmentName, string anchor, CancellationToken cancellationToken) {
		CallToolResult result = await session.CallToolAsync(
			PageSyncTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["pages"] = new[] {
						new Dictionary<string, object?> {
							["schema-name"] = SchemaName,
							["body"] = BuildBlankBody(),
							// The reset overwrites whatever a failed test left, by design.
							["force"] = true
						}
					},
					["validate"] = true,
					["skip-sampling"] = true,
					["output-directory"] = anchor
				}
			},
			cancellationToken);
		PageSyncResponse response = EntitySchemaStructuredResultParser.Extract<PageSyncResponse>(result);
		return response.Pages?.FirstOrDefault();
	}

	private static async Task<string> ReadSchemaChainAsync(PageRead read, CancellationToken cancellationToken) {
		string? bundleFile = read.Response.Files?.BundleFile;
		if (!read.Response.Success || string.IsNullOrWhiteSpace(bundleFile) || !File.Exists(bundleFile)) {
			return "not available";
		}
		using JsonDocument bundle = JsonDocument.Parse(await File.ReadAllTextAsync(bundleFile, cancellationToken));
		foreach (JsonProperty property in bundle.RootElement.EnumerateObject()) {
			if (string.Equals(property.Name, "schemas", StringComparison.OrdinalIgnoreCase)) {
				return property.Value.GetRawText();
			}
		}
		return "bundle.json carries no schemas";
	}

	private static string Text(JsonElement row, string name) =>
		row.ValueKind == JsonValueKind.Object && row.TryGetProperty(name, out JsonElement value)
			? value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText()
			: "?";

	private static string RemoveWhitespace(string text) =>
		string.Concat(text.Where(character => !char.IsWhiteSpace(character)));

	private static string Shorten(string text) =>
		text.Length <= DiagnosticSnippetLength ? text : text[..DiagnosticSnippetLength] + "...";

	private static void TryDeleteDirectory(string path) {
		try {
			if (Directory.Exists(path)) {
				Directory.Delete(path, recursive: true);
			}
		} catch (Exception deleteFailure) when (deleteFailure is IOException or UnauthorizedAccessException) {
			// A leftover temp directory is harmless and must not fail the test.
		}
	}

	/// <summary>One <c>get-page</c> read of the page: the response and the body it wrote to disk.</summary>
	/// <param name="Response">The <c>get-page</c> response.</param>
	/// <param name="Body">The content of <c>body.js</c>; <c>null</c> when the read failed.</param>
	private sealed record PageRead(PageGetResponse Response, string? Body) {

		/// <summary>The facts that tell which stored schema the read resolved.</summary>
		public string Describe() => Response.Success
			? $"editableSchema={Response.Editable?.EditableSchemaUId ?? "none"} "
				+ $"exists={Response.Editable?.EditableSchemaExists.ToString() ?? "?"} "
				+ $"checksum={Response.Editable?.Checksum ?? "none"} modifiedOn={Response.Editable?.ModifiedOn ?? "none"} "
				+ $"pagePackage={Response.Page?.PackageName} designPackage={Response.Page?.DesignPackageName} "
				+ $"({Response.Page?.DesignPackageUId}) willCreateReplacing={Response.Page?.WillCreateReplacingInDesignPackage} "
				+ $"bodyLength={Body?.Length ?? 0} blank={IsBlank(Body, out _)}"
			: $"failed: {Response.Error}";
	}

	/// <summary>
	/// A test's use of the shared page. The fixture's <c>[TearDown]</c> ends it, which resets the page when
	/// the test left it not blank.
	/// </summary>
	internal sealed class Lease {
		private readonly McpServerSession _session;
		private readonly string _environmentName;

		internal Lease(McpServerSession session, string environmentName) {
			_session = session;
			_environmentName = environmentName;
		}

		/// <summary>
		/// Resets the page when the test left it not blank and asserts that the reset is readable.
		/// </summary>
		internal Task EndAsync() => EnsureBlankAsync(_session, _environmentName, "after the test");
	}
}
