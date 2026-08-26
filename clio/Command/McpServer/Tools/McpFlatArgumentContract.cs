using System;

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
