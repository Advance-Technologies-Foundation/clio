using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace ClioRing.Ipc;

/// <summary>
/// A fully-resolved deploy-creatio plan produced by the Deploy Creatio wizard. Pure data; the request
/// JSON is built deterministically by <see cref="DeployRequestBuilder"/>. When <see cref="Local"/> is
/// false (the Rancher / default Kubernetes path) the db/redis server names are OMITTED from the
/// request; when true they are required and included.
/// </summary>
public sealed record DeployPlan {
	/// <summary>Creatio instance name (required, non-empty).</summary>
	public string SiteName { get; init; } = string.Empty;

	/// <summary>Absolute path to the Creatio build archive (.zip) — a build's full-path (required).</summary>
	public string ZipFile { get; init; } = string.Empty;

	/// <summary>Local IIS port (required; validated to the clio 40000–42000 range).</summary>
	public int SitePort { get; init; }

	/// <summary>True = Local infra (db+redis provided); false = Rancher/default Kubernetes (db/redis omitted).</summary>
	public bool Local { get; init; }

	/// <summary>Prefer HTTPS for a local IIS deployment; clio falls back to HTTP when no usable certificate exists.</summary>
	public bool UseHttps { get; init; }

	/// <summary>Local database server configuration name (required only when <see cref="Local"/>).</summary>
	public string? DbServerName { get; init; }

	/// <summary>Local Redis server configuration name (required only when <see cref="Local"/>).</summary>
	public string? RedisServerName { get; init; }
}

/// <summary>
/// Builds the <c>clio-run</c> request that dispatches <c>deploy-creatio</c>, and validates a plan.
/// Kept separate from any UI so the exact request can be unit/harness-verified without firing the
/// (destructive) deploy. JSON is written with <see cref="Utf8JsonWriter"/> so it is safe under the
/// AOT host's disabled reflection serializer.
/// </summary>
public static class DeployRequestBuilder {
	/// <summary>clio port range for local IIS deployment (see find-empty-iis-port).</summary>
	public const int MinPort = 40000;

	/// <summary>Upper bound of the clio local IIS port range.</summary>
	public const int MaxPort = 42000;

	/// <summary>
	/// Validates the plan, returning a list of human-readable errors (empty = valid). Enforces the
	/// deploy-creatio required fields and the Local-infra db/redis requirement.
	/// </summary>
	public static IReadOnlyList<string> Validate(DeployPlan plan) {
		ArgumentNullException.ThrowIfNull(plan);
		var errors = new List<string>();
		if (string.IsNullOrWhiteSpace(plan.SiteName)) {
			errors.Add("Site name is required.");
		}
		if (string.IsNullOrWhiteSpace(plan.ZipFile)) {
			errors.Add("A build (zipFile) must be selected.");
		}
		if (plan.SitePort < MinPort || plan.SitePort > MaxPort) {
			errors.Add($"Port must be a number between {MinPort} and {MaxPort}.");
		}
		if (plan.Local) {
			if (string.IsNullOrWhiteSpace(plan.DbServerName)) {
				errors.Add("Local infrastructure requires a database server.");
			}
			if (string.IsNullOrWhiteSpace(plan.RedisServerName)) {
				errors.Add("Local infrastructure requires a Redis server.");
			}
		}
		return errors;
	}

	/// <summary>
	/// Builds the inner deploy-creatio arguments object (siteName, zipFile, sitePort, and — only for
	/// Local — dbServerName, redisServerName, and useHttps). Rancher omits all local-only fields.
	/// </summary>
	public static string BuildDeployArgsJson(DeployPlan plan) {
		ArgumentNullException.ThrowIfNull(plan);
		var buffer = new MemoryStream();
		using (var writer = new Utf8JsonWriter(buffer)) {
			writer.WriteStartObject();
			writer.WriteString("siteName", plan.SiteName);
			writer.WriteString("zipFile", plan.ZipFile);
			writer.WriteNumber("sitePort", plan.SitePort);
			if (plan.Local) {
				writer.WriteString("dbServerName", plan.DbServerName ?? string.Empty);
				writer.WriteString("redisServerName", plan.RedisServerName ?? string.Empty);
				writer.WriteBoolean("useHttps", plan.UseHttps);
			}
			writer.WriteEndObject();
		}
		return Encoding.UTF8.GetString(buffer.ToArray());
	}

	/// <summary>
	/// Builds the full <c>clio-run</c> request that the ring sends to dispatch deploy-creatio:
	/// <c>{"command":"deploy-creatio","args":{…}}</c>.
	/// </summary>
	public static string BuildClioRunJson(DeployPlan plan) {
		ArgumentNullException.ThrowIfNull(plan);
		var buffer = new MemoryStream();
		using (var writer = new Utf8JsonWriter(buffer)) {
			writer.WriteStartObject();
			writer.WriteString("command", "deploy-creatio");
			writer.WritePropertyName("args");
			writer.WriteStartObject();
			writer.WriteString("siteName", plan.SiteName);
			writer.WriteString("zipFile", plan.ZipFile);
			writer.WriteNumber("sitePort", plan.SitePort);
			if (plan.Local) {
				writer.WriteString("dbServerName", plan.DbServerName ?? string.Empty);
				writer.WriteString("redisServerName", plan.RedisServerName ?? string.Empty);
				writer.WriteBoolean("useHttps", plan.UseHttps);
			}
			writer.WriteEndObject();
			writer.WriteEndObject();
		}
		return Encoding.UTF8.GetString(buffer.ToArray());
	}

	/// <summary>
	/// A human-readable, SECRET-FREE review summary of the plan (deploy-creatio carries no credentials).
	/// </summary>
	public static string DescribePlan(DeployPlan plan) {
		ArgumentNullException.ThrowIfNull(plan);
		var sb = new StringBuilder();
		sb.AppendLine($"site name   : {plan.SiteName}");
		sb.AppendLine($"build (zip) : {plan.ZipFile}");
		sb.AppendLine($"port        : {plan.SitePort}");
		sb.AppendLine($"infra       : {(plan.Local ? "Local" : "Rancher (default Kubernetes)")}");
		if (plan.Local) {
			sb.AppendLine($"db server   : {plan.DbServerName}");
			sb.AppendLine($"redis server: {plan.RedisServerName}");
			sb.AppendLine($"protocol    : {(plan.UseHttps ? "HTTPS preferred (HTTP fallback)" : "HTTP")}");
		}
		else {
			sb.AppendLine("db/redis    : omitted (default Kubernetes path)");
		}
		return sb.ToString().TrimEnd();
	}
}
