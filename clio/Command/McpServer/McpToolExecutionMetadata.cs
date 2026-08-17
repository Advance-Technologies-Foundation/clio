using System;
using System.Collections.Generic;

namespace Clio.Command.McpServer;

/// <summary>
/// Where a tool call executes: in the MCP host process, or in a short-lived child worker the host
/// supervises and can kill.
/// </summary>
/// <remarks>
/// <see cref="Unspecified"/> is the zero value on purpose: a tool whose
/// <see cref="McpToolExecutionAttribute"/> omits <see cref="McpToolExecutionAttribute.Location"/> reads
/// back as <see cref="Unspecified"/> rather than as a plausible-looking default, so the Stage 1 coverage
/// test can tell "not classified yet" from "deliberately classified".
/// </remarks>
public enum McpToolExecutionLocation {
	/// <summary>No value declared. Never a valid classification — the coverage test fails on it.</summary>
	Unspecified = 0,

	/// <summary>Runs in the MCP host process; the call is never relayed to a worker.</summary>
	InProcess,

	/// <summary>Runs in a supervised child worker; the call is relayed.</summary>
	Worker
}

/// <summary>
/// Whether the worker that serves a call survives the response (so a later status poll can reach it).
/// </summary>
public enum McpToolExecutionLifetime {
	/// <summary>No value declared. Never a valid classification — the coverage test fails on it.</summary>
	Unspecified = 0,

	/// <summary>
	/// The field does not apply because the tool never routes to a worker
	/// (<see cref="McpToolExecutionLocation.InProcess"/>).
	/// </summary>
	NotApplicable,

	/// <summary>The worker is created for this call and reaped when it answers.</summary>
	PerCall,

	/// <summary>The worker outlives the response so a status poller of the same family can reach it.</summary>
	Sticky
}

/// <summary>
/// Groups a long-running starter with the status poller that must reach the SAME sticky worker, and
/// names the shared reservation that applies to the family.
/// </summary>
public enum McpToolOperationFamily {
	/// <summary>No value declared. Never a valid classification — the coverage test fails on it.</summary>
	Unspecified = 0,

	/// <summary>The tool belongs to no operation family: no status poll and no shared reservation.</summary>
	None,

	/// <summary>Configuration build (<c>compile-creatio</c> / <c>compile-status</c> / <c>install-process-builder</c>).</summary>
	ConfigurationBuild,

	/// <summary>Application restart (<c>restart-*</c> starters and <c>restart-status</c>).</summary>
	Restart,

	/// <summary>Application-section creation (<c>create-app-section</c>).</summary>
	AppSectionCreate,

	/// <summary>Environment deploy / uninstall, bounded by its authoritative terminal stage.</summary>
	Deploy
}

/// <summary>
/// How the parent bounds a call: by killing the worker at a budget, by waiting for an authoritative
/// terminal stage, or not at all.
/// </summary>
public enum McpToolBudgetPolicy {
	/// <summary>No value declared. Never a valid classification — the coverage test fails on it.</summary>
	Unspecified = 0,

	/// <summary>No parent budget applies because the tool never routes to a worker.</summary>
	None,

	/// <summary>Ordinary worker call: the parent kills the worker at the default budget.</summary>
	ParentKillDefault,

	/// <summary>Sticky worker call: the parent kills the worker at the extended budget.</summary>
	ParentKillExtended,

	/// <summary>
	/// Bounded by the authoritative terminal stage plus a stage-event silence timeout, never by a generic
	/// kill — a killed deploy can leave a half-installed environment.
	/// </summary>
	TerminalStage
}

/// <summary>
/// Which client-initiated request kinds the tool needs from the MCP client mid-call, i.e. whether the
/// relay must be full-duplex for it.
/// </summary>
/// <remarks>
/// Members keep power-of-two values so <c>Sampling | Progress</c> composes (that combination is the
/// inventory's "both", named <see cref="Both"/> here) and a third request kind can be added later without
/// a combinatorial explosion of literals.
/// </remarks>
/// <remarks>
/// Deliberately NOT marked <c>[Flags]</c>. The zero member must stay <see cref="Unspecified"/> — it is what
/// lets the coverage test tell "the author left this field at its default" apart from <see cref="None"/>
/// ("declared: this tool needs nothing"), and a nullable enum is not a legal attribute-argument type in C#,
/// so there is no other way to encode the absent case. A <c>[Flags]</c> enum whose zero member is not named
/// <c>None</c> trips SonarCloud S2346, and between losing the omission check and losing an attribute that
/// only affects <c>ToString</c> decomposition, the attribute is the cheaper thing to give up.
/// </remarks>
public enum McpToolClientRequests {
	/// <summary>No value declared. Never a valid classification — the coverage test fails on it.</summary>
	Unspecified = 0,

	/// <summary>Declared: the tool issues no client requests; a half-duplex relay is sufficient.</summary>
	None = 1,

	/// <summary>The tool calls <c>server.SampleAsync</c>; a relay that drops it degrades the answer silently.</summary>
	Sampling = 2,

	/// <summary>The tool emits <c>notifications/progress</c> or stage events.</summary>
	Progress = 4,

	/// <summary>Both <see cref="Sampling"/> and <see cref="Progress"/>.</summary>
	Both = Sampling | Progress
}

/// <summary>
/// The concrete on-disk artifact two processes could corrupt once the tool runs outside the host, and
/// therefore which interprocess file gate has to exist before the tool is relayed.
/// </summary>
public enum McpToolSharedFileResource {
	/// <summary>No value declared. Never a valid classification — the coverage test fails on it.</summary>
	Unspecified = 0,

	/// <summary>The tool touches no shared file resource.</summary>
	None,

	/// <summary>The <c>.clio-pages/{schema}/meta.json</c> baseline store.</summary>
	ClioPages,

	/// <summary>The browser-session cache under the clio home directory.</summary>
	BrowserSessionCache,

	/// <summary>The configuration-build reservation shared by compile and process-builder installs.</summary>
	ConfigurationBuild
}

/// <summary>
/// Declares HOW and WHERE an MCP tool executes, alongside the tool's own <c>[McpServerTool]</c>
/// annotation. Read reflectively; never inferred from the safety hints.
/// </summary>
/// <remarks>
/// <para>
/// Routing cannot reuse an existing property: <c>IMcpToolInvokerRegistry</c> exposes only
/// read-only / destructive / retry-safety, and <c>McpCoreToolProfile</c> describes <c>tools/list</c>
/// residency, not execution. <c>get-page</c> is resident AND must run in a worker, while most
/// long-running tools are non-resident and are reached through <c>clio-run</c>. See
/// <c>spec/adr/adr-mcp-worker-execution-boundary.md</c> (rule 7).
/// </para>
/// <para>
/// Every field is an ENUM, so a misspelled value is a compile error rather than a routing decision made
/// from a typo, and each enum's zero value is <c>Unspecified</c>, so an omitted property is detectable by
/// the Stage 1 coverage test instead of silently defaulting to something plausible.
/// </para>
/// <para>
/// Stage 1 declares and asserts this metadata; nothing reads it to route yet.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [McpServerTool(Name = ToolName, ReadOnly = true)]
/// [McpToolExecution(
///     Location = McpToolExecutionLocation.Worker,
///     Lifetime = McpToolExecutionLifetime.PerCall,
///     OperationFamily = McpToolOperationFamily.None,
///     BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
///     RequiresClientRequests = McpToolClientRequests.None,
///     SharedFileResource = McpToolSharedFileResource.None)]
/// public MyResponse Run(MyOptions options) => ...;
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class McpToolExecutionAttribute : Attribute {

	/// <summary>Whether the call is relayed to a worker at all.</summary>
	public McpToolExecutionLocation Location { get; set; }

	/// <summary>Whether the worker survives the response.</summary>
	public McpToolExecutionLifetime Lifetime { get; set; }

	/// <summary>Which sticky worker a status poll must reach; which shared reservation applies.</summary>
	public McpToolOperationFamily OperationFamily { get; set; }

	/// <summary>How the parent bounds the call.</summary>
	public McpToolBudgetPolicy BudgetPolicy { get; set; }

	/// <summary>Whether the relay must be full-duplex for this tool.</summary>
	public McpToolClientRequests RequiresClientRequests { get; set; }

	/// <summary>Which interprocess file gate the tool needs.</summary>
	public McpToolSharedFileResource SharedFileResource { get; set; }

	/// <summary>
	/// When this method declares a DEPRECATED tool name that delegates to another tool method (for example
	/// <c>StopAllCreatio</c> delegating to <c>stop-all-creatio</c>), the canonical tool name it delegates to;
	/// otherwise <c>null</c>.
	/// </summary>
	/// <remarks>
	/// A deprecated name registered as its own <c>[McpServerTool]</c> method is invisible to
	/// <see cref="IMcpToolCompatibilityCatalog"/> — the only link today is prose in the description. This
	/// property is that missing machine-readable link: an alias and its canonical execute the SAME code, so
	/// their execution metadata must be identical, and the coverage test can only check that for a link it
	/// can read. It is deliberately NOT one of the six routing fields.
	/// </remarks>
	public string AliasOf { get; set; }
}

/// <summary>
/// The six declared execution-metadata fields of one MCP tool, plus the optional alias link, read off a
/// tool method's <see cref="McpToolExecutionAttribute"/>.
/// </summary>
/// <param name="Location">Whether the call is relayed to a worker at all.</param>
/// <param name="Lifetime">Whether the worker survives the response.</param>
/// <param name="OperationFamily">Which sticky worker a status poll must reach; which shared reservation applies.</param>
/// <param name="BudgetPolicy">How the parent bounds the call.</param>
/// <param name="RequiresClientRequests">Whether the relay must be full-duplex for this tool.</param>
/// <param name="SharedFileResource">Which interprocess file gate the tool needs.</param>
/// <param name="AliasOf">The canonical tool name this deprecated declaration delegates to, or <c>null</c>.</param>
public sealed record McpToolExecutionMetadata(
	McpToolExecutionLocation Location,
	McpToolExecutionLifetime Lifetime,
	McpToolOperationFamily OperationFamily,
	McpToolBudgetPolicy BudgetPolicy,
	McpToolClientRequests RequiresClientRequests,
	McpToolSharedFileResource SharedFileResource,
	string AliasOf = null) {

	/// <summary>
	/// The names of the fields left <c>Unspecified</c>, in declaration order. Empty when the tool is fully
	/// classified. Drives the coverage test's failure message, so a partially annotated tool names its own
	/// gaps instead of failing with a bare boolean.
	/// </summary>
	public IReadOnlyList<string> UnspecifiedFieldNames {
		get {
			List<string> missing = [];
			if (Location == McpToolExecutionLocation.Unspecified) {
				missing.Add(nameof(Location));
			}
			if (Lifetime == McpToolExecutionLifetime.Unspecified) {
				missing.Add(nameof(Lifetime));
			}
			if (OperationFamily == McpToolOperationFamily.Unspecified) {
				missing.Add(nameof(OperationFamily));
			}
			if (BudgetPolicy == McpToolBudgetPolicy.Unspecified) {
				missing.Add(nameof(BudgetPolicy));
			}
			if (RequiresClientRequests == McpToolClientRequests.Unspecified) {
				missing.Add(nameof(RequiresClientRequests));
			}
			if (SharedFileResource == McpToolSharedFileResource.Unspecified) {
				missing.Add(nameof(SharedFileResource));
			}
			return missing;
		}
	}

	/// <summary>
	/// <c>true</c> when all six routing fields carry a declared value. A partially annotated tool is NOT
	/// classified — the coverage test treats it exactly as harshly as a tool with no attribute at all.
	/// </summary>
	public bool IsFullyClassified => UnspecifiedFieldNames.Count == 0;

	/// <summary>
	/// The two cross-field invariants from the execution-metadata inventory (§3), as failure messages.
	/// Empty when the row is internally consistent. A row can satisfy every field rule separately and still
	/// be internally impossible — these catch that class in the build rather than in review.
	/// </summary>
	public IReadOnlyList<string> CrossFieldViolations {
		get {
			List<string> violations = [];
			if (OperationFamily == McpToolOperationFamily.Deploy) {
				if (Location != McpToolExecutionLocation.Worker) {
					violations.Add(
						$"OperationFamily = Deploy requires Location = Worker, but Location = {Location}.");
				}
				if (BudgetPolicy != McpToolBudgetPolicy.TerminalStage) {
					violations.Add(
						"OperationFamily = Deploy requires BudgetPolicy = TerminalStage (a generic kill can leave " +
						$"a half-installed environment), but BudgetPolicy = {BudgetPolicy}.");
				}
			}
			if (Location == McpToolExecutionLocation.InProcess) {
				if (OperationFamily != McpToolOperationFamily.None) {
					violations.Add(
						"Location = InProcess requires OperationFamily = None (there is no sticky worker for a " +
						$"poll to reach), but OperationFamily = {OperationFamily}.");
				}
				if (Lifetime != McpToolExecutionLifetime.NotApplicable) {
					violations.Add(
						"Location = InProcess requires Lifetime = NotApplicable (no worker exists to outlive the " +
						$"response), but Lifetime = {Lifetime}.");
				}
				if (BudgetPolicy != McpToolBudgetPolicy.None) {
					violations.Add(
						"Location = InProcess requires BudgetPolicy = None (there is no parent budget to expire), " +
						$"but BudgetPolicy = {BudgetPolicy}.");
				}
			}
			return violations;
		}
	}
}
