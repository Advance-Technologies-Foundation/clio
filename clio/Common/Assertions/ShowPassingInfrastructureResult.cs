using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Clio.Common.Assertions;

/// <summary>
/// Structured MCP result that exposes only passing infrastructure choices for deployment selection.
/// </summary>
public sealed record ShowPassingInfrastructureResult(
	[property: JsonPropertyName("status")]
	[property: Description("Passing-infrastructure availability status: available or unavailable")]
	string Status,

	[property: JsonPropertyName("summary")]
	[property: Description("Human-readable summary of the passing infrastructure discovery")]
	string Summary,

	[property: JsonPropertyName("kubernetes")]
	[property: Description("Passing Kubernetes deployment infrastructure choices")]
	ShowPassingInfrastructureKubernetes Kubernetes,

	[property: JsonPropertyName("local")]
	[property: Description("Passing local deployment infrastructure choices")]
	ShowPassingInfrastructureLocal Local,

	[property: JsonPropertyName("filesystem")]
	[property: Description("Passing filesystem readiness relevant for deployment")]
	ShowPassingInfrastructureFilesystem Filesystem,

	[property: JsonPropertyName("recommendedDeployment")]
	[property: Description("Recommended passing deployment choice to merge into a deploy-creatio MCP call")]
	ShowPassingInfrastructureRecommendation? RecommendedDeployment,

	[property: JsonPropertyName("recommendedByEngine")]
	[property: Description("Recommended passing deployment choices grouped by database engine")]
	ShowPassingInfrastructureRecommendationsByEngine RecommendedByEngine
);

/// <summary>
/// Passing Kubernetes deployment infrastructure choices.
/// </summary>
public sealed record ShowPassingInfrastructureKubernetes(
	[property: JsonPropertyName("isAvailable")]
	[property: Description("Whether Kubernetes is currently a passing deployment target")]
	bool IsAvailable,

	[property: JsonPropertyName("databases")]
	[property: Description("Passing Kubernetes database candidates")]
	IReadOnlyList<ShowPassingInfrastructureDatabaseCandidate> Databases,

	[property: JsonPropertyName("redis")]
	[property: Description("Passing Kubernetes Redis candidate with the discovered first available database index")]
	ShowPassingInfrastructureRedisCandidate? Redis
);

/// <summary>
/// Passing local deployment infrastructure choices.
/// </summary>
public sealed record ShowPassingInfrastructureLocal(
	[property: JsonPropertyName("databases")]
	[property: Description("Passing local database server configurations")]
	IReadOnlyList<ShowPassingInfrastructureDatabaseCandidate> Databases,

	[property: JsonPropertyName("redisServers")]
	[property: Description("Passing local Redis server configurations")]
	IReadOnlyList<ShowPassingInfrastructureRedisCandidate> RedisServers
);

/// <summary>
/// Passing filesystem readiness relevant for deployment.
/// </summary>
public sealed record ShowPassingInfrastructureFilesystem(
	[property: JsonPropertyName("isAvailable")]
	[property: Description("Whether the required deployment filesystem target is currently passing")]
	bool IsAvailable,

	[property: JsonPropertyName("path")]
	[property: Description("Resolved filesystem path when available")]
	string? Path,

	[property: JsonPropertyName("userIdentity")]
	[property: Description("Resolved Windows identity validated for deployment permissions when available")]
	string? UserIdentity,

	[property: JsonPropertyName("permission")]
	[property: Description("Validated permission level when available")]
	string? Permission
);

/// <summary>
/// Passing database candidate that can be used for deployment selection.
/// </summary>
public sealed record ShowPassingInfrastructureDatabaseCandidate(
	[property: JsonPropertyName("source")]
	[property: Description("Infrastructure source where the passing database candidate was discovered")]
	string Source,

	[property: JsonPropertyName("engine")]
	[property: Description("Database engine for the passing candidate")]
	string Engine,

	[property: JsonPropertyName("name")]
	[property: Description("Resolved candidate name")]
	string Name,

	[property: JsonPropertyName("host")]
	[property: Description("Resolved candidate host")]
	string Host,

	[property: JsonPropertyName("port")]
	[property: Description("Resolved candidate port")]
	int Port,

	[property: JsonPropertyName("version")]
	[property: Description("Resolved candidate version when available")]
	string? Version,

	[property: JsonPropertyName("dbServerName")]
	[property: Description("Local db-server-name value to pass to deploy-creatio when applicable")]
	string? DbServerName
);

/// <summary>
/// Passing Redis candidate that can be used for deployment selection.
/// </summary>
public sealed record ShowPassingInfrastructureRedisCandidate(
	[property: JsonPropertyName("source")]
	[property: Description("Infrastructure source where the passing Redis candidate was discovered")]
	string Source,

	[property: JsonPropertyName("name")]
	[property: Description("Resolved Redis workload or configuration name")]
	string Name,

	[property: JsonPropertyName("host")]
	[property: Description("Resolved Redis host")]
	string Host,

	[property: JsonPropertyName("port")]
	[property: Description("Resolved Redis port")]
	int Port,

	[property: JsonPropertyName("firstAvailableDb")]
	[property: Description("Suggested Redis database index to use for deploy-creatio")]
	int FirstAvailableDb,

	[property: JsonPropertyName("redisServerName")]
	[property: Description("Local redis-server-name value to pass to deploy-creatio when applicable")]
	string? RedisServerName
);

/// <summary>
/// Recommended passing deployment choices grouped by database engine.
/// </summary>
public sealed record ShowPassingInfrastructureRecommendationsByEngine(
	[property: JsonPropertyName("postgres")]
	[property: Description("Recommended passing deployment choice for PostgreSQL")]
	ShowPassingInfrastructureRecommendation? Postgres,

	[property: JsonPropertyName("mssql")]
	[property: Description("Recommended passing deployment choice for MSSQL")]
	ShowPassingInfrastructureRecommendation? Mssql
);

/// <summary>
/// Deploy-ready infrastructure recommendation for merging into a deploy-creatio MCP call.
/// </summary>
public sealed record ShowPassingInfrastructureRecommendation(
	[property: JsonPropertyName("deploymentMode")]
	[property: Description("Deployment mode to use: kubernetes or local")]
	string DeploymentMode,

	[property: JsonPropertyName("dbEngine")]
	[property: Description("Database engine for the recommendation")]
	string DbEngine,

	[property: JsonPropertyName("dbServerName")]
	[property: Description("db-server-name argument to pass to deploy-creatio when local mode is recommended")]
	string? DbServerName,

	[property: JsonPropertyName("redisServerName")]
	[property: Description("redis-server-name argument to pass to deploy-creatio when local mode is recommended")]
	string? RedisServerName,

	[property: JsonPropertyName("deployCreatioArguments")]
	[property: Description("Infrastructure-selection arguments that should be merged into the final deploy-creatio MCP call")]
	ShowPassingInfrastructureDeployCreatioArguments DeployCreatioArguments
);

/// <summary>
/// Partial deploy-creatio MCP arguments derived from passing infrastructure selection.
/// </summary>
public sealed record ShowPassingInfrastructureDeployCreatioArguments(
	[property: JsonPropertyName("dbServerName")]
	[property: Description("Optional local db-server-name argument for deploy-creatio")]
	string? DbServerName,

	[property: JsonPropertyName("redisServerName")]
	[property: Description("Optional local redis-server-name argument for deploy-creatio")]
	string? RedisServerName
);
