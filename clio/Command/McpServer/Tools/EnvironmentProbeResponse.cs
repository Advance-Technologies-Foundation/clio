using System.Text.Json.Serialization;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Canonical response envelope for environment PROBE tools — read-only, per-call
/// environment-scoped tools that resolve values which can never come from a static
/// CDN catalog (process signatures, printable templates, lookup values, …).
/// OOTB button-action requests initiative (ENG-93187).
/// </summary>
/// <remarks>
/// <para>
/// The probe contract, extracted from the <c>get-process-signature</c> prototype:
/// </para>
/// <list type="bullet">
/// <item><see cref="Success"/> — whether the probe produced a usable result.</item>
/// <item><see cref="ResolutionFailed"/> — the hard-vs-transient lever. <c>true</c> means the
/// probe DEFINITIVELY could not resolve the requested resource (not found, or ambiguous
/// with candidates listed in <see cref="Error"/>) — a consumer gate may block a write on it.
/// <c>false</c> with <c>Success=false</c> means a transient/transport failure — a gate must
/// degrade to a warning, never block a write on a backend hiccup. Omitted (<c>null</c>) when
/// the probe has no resolution phase (e.g. a pure list probe).</item>
/// <item><see cref="Error"/> — human-readable failure detail; on an ambiguous resolution it
/// carries the candidate list so the agent can ask the user instead of guessing.</item>
/// </list>
/// <para>
/// Agent-facing rule carried by every probe's tool description: values for
/// environment-dependent request parameters are filled ONLY from a probe's results —
/// an agent must never invent them. The request registry marks such parameters with a
/// <c>valueSource</c> annotation naming the probe tool.
/// </para>
/// <para>
/// <c>get-process-signature</c> predates this envelope and keeps its shipped wire shape
/// (<c>processResolutionFailed</c>); it is the semantic prototype, not a subclass. New
/// probes derive from this type.
/// </para>
/// </remarks>
public abstract class EnvironmentProbeResponse {
	/// <summary>
	/// Gets whether the probe produced a usable result.
	/// </summary>
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	/// <summary>
	/// Gets the hard-failure marker: <c>true</c> when the probe definitively could not
	/// resolve the requested resource (as opposed to a transient transport failure).
	/// Omitted when the probe has no resolution phase.
	/// </summary>
	[JsonPropertyName("resolutionFailed")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? ResolutionFailed { get; init; }

	/// <summary>
	/// Gets the human-readable failure detail; carries candidate lists on ambiguous
	/// resolutions so the agent can ask the user instead of guessing.
	/// </summary>
	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Error { get; init; }
}
