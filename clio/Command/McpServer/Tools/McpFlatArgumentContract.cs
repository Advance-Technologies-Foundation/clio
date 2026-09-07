using System;
using System.Collections.Generic;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// ENG-95885. Declares that this tool has a natural NO-ARGUMENTS operation, so an empty
/// <c>tools/call</c> payload (<c>{}</c>) is a legitimate call rather than a caller mistake. The
/// flat-argument normalizer in <see cref="McpToolErrorFilter"/> then synthesizes the empty wrapper
/// object the SDK needs to bind the tool's composite <c>args</c> record.
/// </summary>
/// <remarks>
/// <para>
/// The declaration is EXPLICIT and FAIL-CLOSED: a tool that does not carry this attribute keeps
/// today's missing-parameter error for <c>{}</c>. Capability is deliberately NOT inferred from the
/// generated schema's required-property set, because that set is a weak proxy for runtime semantics —
/// <c>DataForgeMaintenanceArgs.EnvironmentName</c>, for example, is schema-optional yet
/// <c>EnsureRequired</c>-checked before the operation runs, so an inferred rule would turn a clear
/// missing-argument error into a deeper, less actionable failure.
/// </para>
/// <para>
/// Apply this ONLY when calling the tool with no arguments at all has a documented, useful meaning
/// (e.g. <c>list-apps</c> against the active environment, <c>get-request-info</c> returning the
/// catalog). Never apply it to reach a default that the caller probably did not intend.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
internal sealed class McpAcceptsEmptyArgumentsAttribute : Attribute {
}

/// <summary>
/// ENG-95885. Declares that this tool validates and recovers UNKNOWN top-level argument keys itself —
/// it binds a <c>[JsonExtensionData]</c> overflow bag and inspects it (alias renames, flat-shape
/// recovery, or an explicit unknown-argument error) before doing any work. The flat-argument
/// normalizer then FORWARDS an unknown-only flat payload to the tool instead of refusing it, so the
/// tool's own richer diagnosis wins.
/// </summary>
/// <remarks>
/// <para>
/// The declaration is EXPLICIT and FAIL-CLOSED, and that is the whole point. Most resident args
/// records carry no overflow bag (verified on <c>ApplicationTool</c>, <c>GetPkgListTool</c>,
/// <c>EntitySchemaTool</c>, <c>PageGetTool</c>, <c>PageValidateTool</c>, <c>ShowWebAppListTool</c>,
/// <c>DataForgeTool</c>). For those, wrapping an unknown-only payload would let the serializer drop
/// the unknown key, materialize the record with defaults, and let the tool answer a validation
/// mistake with a plausible list/default SUCCESS — strictly worse for an agent than a hard failure.
/// So the normalizer refuses unknown-only by default and only forwards where a tool has taken
/// responsibility for the keys.
/// </para>
/// <para>
/// "This tool validates its overflow bag" is a convention that reflection cannot see, which is why it
/// is declared here rather than inferred from the presence of a <c>[JsonExtensionData]</c> property.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
internal sealed class McpRecoversUnknownArgumentsAttribute : Attribute {
}

/// <summary>
/// ENG-95885. What the flat-argument classifier DECIDED about one <c>tools/call</c> payload, so the
/// decision can be observed from outside the filter.
/// </summary>
/// <remarks>
/// The normalizer rewrites <c>Arguments</c> IN PLACE, so without this every downstream observer -
/// the host log, a support bundle, the Applicant measurement run that gates ENG-95885's closure - sees
/// only the POST-rewrite wrapped shape and cannot tell a flat first attempt from a correctly wrapped
/// one. The fix would otherwise make its own success unmeasurable.
/// </remarks>
internal enum McpArgumentShapeOutcome {

	/// <summary>The payload was left exactly as it arrived: already wrapped, or outside the trigger gate.</summary>
	Untouched = 0,

	/// <summary>Top-level keys were moved into the wrapper. This is the accommodation ENG-95885 exists to provide.</summary>
	WrappedFlat = 1,

	/// <summary>An empty <c>{}</c> payload was given the synthesized empty wrapper (declared capability).</summary>
	SynthesizedEmpty = 2,

	/// <summary>Refused: at least one top-level key is not a wire property of the args record.</summary>
	RefusedUnknown = 3,

	/// <summary>Refused: a wrapper object AND extra top-level keys arrived together.</summary>
	RefusedAmbiguous = 4
}

/// <summary>
/// ENG-95885. One classifier decision plus the ORIGINAL top-level key NAMES it applied to, captured
/// before any rewrite.
/// </summary>
/// <remarks>
/// Carries names only, never VALUES. An argument value can hold a password, a token or a connection
/// string (<c>reg-web-app</c>, the credential-passthrough tools), and <c>clio/AGENTS.md</c> forbids
/// logging secret-bearing configuration. Key names are already echoed to the caller by
/// the unknown-argument and ambiguous-shape errors, so naming them adds no exposure that the response
/// does not already carry.
/// </remarks>
/// <param name="Outcome">What the classifier decided.</param>
/// <param name="TopLevelKeys">The payload's original top-level key names, pre-rewrite. Empty when nothing interesting happened.</param>
internal readonly record struct McpArgumentShapeReport(
	McpArgumentShapeOutcome Outcome,
	IReadOnlyList<string> TopLevelKeys) {

	/// <summary>The no-op report: nothing was rewritten and nothing was refused.</summary>
	internal static McpArgumentShapeReport Untouched { get; } =
		new(McpArgumentShapeOutcome.Untouched, []);
}
