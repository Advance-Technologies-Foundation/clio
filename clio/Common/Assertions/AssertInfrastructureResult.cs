using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Clio.Common.Assertions;

/// <summary>
/// Structured MCP result for a full infrastructure assertion sweep.
/// </summary>
public sealed record AssertInfrastructureResult(
	[property: JsonPropertyName("status")]
	[property: Description("Overall infrastructure assertion status: pass, partial, or fail")]
	string Status,

	[property: JsonPropertyName("exit-code")]
	[property: Description("Overall infrastructure assertion exit code")]
	int ExitCode,

	[property: JsonPropertyName("summary")]
	[property: Description("Human-readable summary of the infrastructure assertion sweep")]
	string Summary,

	[property: JsonPropertyName("sections")]
	[property: Description("Per-scope assertion results for the full infrastructure sweep")]
	AssertInfrastructureSections Sections,

	[property: JsonPropertyName("database-candidates")]
	[property: Description("Normalized database candidates discovered across successful K8 and local assertion sections")]
	IReadOnlyList<AssertInfrastructureDatabaseCandidate> DatabaseCandidates
);

/// <summary>
/// Per-scope assertion results for the full infrastructure MCP tool.
/// </summary>
public sealed record AssertInfrastructureSections(
	[property: JsonPropertyName("k8")]
	[property: Description("Kubernetes assertion result")]
	AssertionResult K8,

	[property: JsonPropertyName("local")]
	[property: Description("Local infrastructure assertion result")]
	AssertionResult Local,

	[property: JsonPropertyName("filesystem")]
	[property: Description("Filesystem assertion result")]
	AssertionResult Filesystem
);

/// <summary>
/// Normalized database candidate returned from a successful infrastructure assertion section.
/// </summary>
public sealed record AssertInfrastructureDatabaseCandidate(
	[property: JsonPropertyName("source")]
	[property: Description("Infrastructure source where the database candidate was discovered")]
	string Source,

	[property: JsonPropertyName("engine")]
	[property: Description("Database engine")]
	string Engine,

	[property: JsonPropertyName("name")]
	[property: Description("Resolved database name")]
	string Name,

	[property: JsonPropertyName("host")]
	[property: Description("Resolved database host")]
	string Host,

	[property: JsonPropertyName("port")]
	[property: Description("Resolved database port")]
	int Port,

	[property: JsonPropertyName("version")]
	[property: Description("Resolved database version when available")]
	string Version,

	[property: JsonPropertyName("is-connectable")]
	[property: Description("Whether connectivity was validated successfully for this candidate")]
	bool? IsConnectable
);
