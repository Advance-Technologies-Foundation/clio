using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading.Tasks;
using Clio.Common.Kubernetes;

namespace Clio.Common.Assertions;

/// <summary>
/// Aggregates full infrastructure assertion results for MCP consumers.
/// </summary>
public interface IAssertInfrastructureAggregator
{
	/// <summary>
	/// Executes the full infrastructure assertion sweep across Kubernetes, local infrastructure, and filesystem.
	/// </summary>
	/// <returns>Aggregate structured result containing section-level assertion results and normalized database candidates.</returns>
	Task<AssertInfrastructureResult> ExecuteAsync();
}

/// <summary>
/// Executes the full infrastructure assertion sweep used by the assert MCP tool.
/// </summary>
public sealed class AssertInfrastructureAggregator : IAssertInfrastructureAggregator
{
	private const string FullSweepDatabaseEngines = "postgres,mssql";
	private const string RequiredInfrastructureNamespace = "clio-infrastructure";
	private const string DefaultFilesystemPathKey = "iis-clio-root-path";

	private readonly IK8ContextValidator _contextValidator;
	private readonly IK8DatabaseAssertion _k8DatabaseAssertion;
	private readonly IK8RedisAssertion _k8RedisAssertion;
	private readonly IFsPathAssertion _fsPathAssertion;
	private readonly IFsPermissionAssertion _fsPermissionAssertion;
	private readonly ILocalDatabaseAssertion _localDatabaseAssertion;
	private readonly ILocalRedisAssertion _localRedisAssertion;
	private readonly IKubernetesClient _kubernetesClient;

	/// <summary>
	/// Initializes a new instance of the <see cref="AssertInfrastructureAggregator"/> class.
	/// </summary>
	public AssertInfrastructureAggregator(
		IK8ContextValidator contextValidator,
		IK8DatabaseAssertion k8DatabaseAssertion,
		IK8RedisAssertion k8RedisAssertion,
		IFsPathAssertion fsPathAssertion,
		IFsPermissionAssertion fsPermissionAssertion,
		ILocalDatabaseAssertion localDatabaseAssertion,
		ILocalRedisAssertion localRedisAssertion,
		IKubernetesClient kubernetesClient)
	{
		_contextValidator = contextValidator ?? throw new ArgumentNullException(nameof(contextValidator));
		_k8DatabaseAssertion = k8DatabaseAssertion ?? throw new ArgumentNullException(nameof(k8DatabaseAssertion));
		_k8RedisAssertion = k8RedisAssertion ?? throw new ArgumentNullException(nameof(k8RedisAssertion));
		_fsPathAssertion = fsPathAssertion ?? throw new ArgumentNullException(nameof(fsPathAssertion));
		_fsPermissionAssertion = fsPermissionAssertion ?? throw new ArgumentNullException(nameof(fsPermissionAssertion));
		_localDatabaseAssertion = localDatabaseAssertion ?? throw new ArgumentNullException(nameof(localDatabaseAssertion));
		_localRedisAssertion = localRedisAssertion ?? throw new ArgumentNullException(nameof(localRedisAssertion));
		_kubernetesClient = kubernetesClient ?? throw new ArgumentNullException(nameof(kubernetesClient));
	}

	/// <inheritdoc />
	public async Task<AssertInfrastructureResult> ExecuteAsync()
	{
		AssertionResult k8Result = await ExecuteSectionAsync(AssertionScope.K8, ExecuteK8Async);
		AssertionResult localResult = await ExecuteSectionAsync(AssertionScope.Local, ExecuteLocalAsync);
		AssertionResult filesystemResult = await ExecuteSectionAsync(AssertionScope.Fs, ExecuteFilesystemAsync);

		IReadOnlyList<AssertInfrastructureDatabaseCandidate> databaseCandidates =
			BuildDatabaseCandidates(k8Result, localResult);
		string status = ComputeStatus(k8Result, localResult, filesystemResult, databaseCandidates);
		int exitCode = status == "pass" ? 0 : 1;
		string summary = BuildSummary(status, k8Result, localResult, filesystemResult, databaseCandidates.Count);

		return new AssertInfrastructureResult(
			status,
			exitCode,
			summary,
			new AssertInfrastructureSections(k8Result, localResult, filesystemResult),
			databaseCandidates);
	}

	private async Task<AssertionResult> ExecuteSectionAsync(
		AssertionScope scope,
		Func<Task<AssertionResult>> executeAsync)
	{
		try
		{
			return await executeAsync();
		}
		catch (Exception ex)
		{
			AssertionResult failure = AssertionResult.Failure(scope, GetDefaultFailurePhase(scope),
				$"Unexpected error during {scope.ToString().ToLowerInvariant()} assertion: {ex.Message}");
			failure.Details["exception"] = ex.GetType().Name;
			return failure;
		}
	}

	private async Task<AssertionResult> ExecuteK8Async()
	{
		AssertionResult contextResult = await _contextValidator.ValidateContextAsync();
		if (contextResult.Status == "fail")
		{
			contextResult.Scope = AssertionScope.K8;
			return contextResult;
		}

		bool namespaceExists = await _kubernetesClient.NamespaceExistsAsync(RequiredInfrastructureNamespace);
		if (!namespaceExists)
		{
			AssertionResult missingNamespace = AssertionResult.Failure(
				AssertionScope.K8,
				AssertionPhase.K8Context,
				$"Required namespace '{RequiredInfrastructureNamespace}' does not exist");
			missingNamespace.Context = contextResult.Context;
			missingNamespace.Details["namespace"] = RequiredInfrastructureNamespace;
			return missingNamespace;
		}

		AssertionResult databaseResult = await _k8DatabaseAssertion.ExecuteAsync(
			FullSweepDatabaseEngines,
			2,
			checkConnect: true,
			checkCapability: "version",
			namespaceParam: RequiredInfrastructureNamespace);
		if (databaseResult.Status == "fail")
		{
			databaseResult.Context = contextResult.Context;
			return databaseResult;
		}

		AssertionResult redisResult = await _k8RedisAssertion.ExecuteAsync(
			checkConnect: true,
			checkPing: true,
			namespaceParam: RequiredInfrastructureNamespace);
		if (redisResult.Status == "fail")
		{
			redisResult.Context = contextResult.Context;
			return redisResult;
		}

		AssertionResult success = AssertionResult.Success();
		success.Scope = AssertionScope.K8;
		success.Context = contextResult.Context;
		MergeResolved(success, databaseResult, "databases");
		MergeResolved(success, redisResult, "redis");
		return success;
	}

	private async Task<AssertionResult> ExecuteLocalAsync()
	{
		AssertionResult databaseResult = await _localDatabaseAssertion.ExecuteAsync(
			FullSweepDatabaseEngines,
			minDatabases: 1,
			checkConnect: true,
			checkCapability: "version",
			dbServerName: null);
		if (databaseResult.Status == "fail")
		{
			return databaseResult;
		}

		AssertionResult redisResult = await _localRedisAssertion.ExecuteAsync(
			checkConnect: true,
			checkPing: true,
			redisServerName: null);
		if (redisResult.Status == "fail")
		{
			return redisResult;
		}

		AssertionResult success = AssertionResult.Success();
		success.Scope = AssertionScope.Local;
		MergeResolved(success, databaseResult, "databases");
		MergeResolved(success, redisResult, "redis");
		return success;
	}

	private Task<AssertionResult> ExecuteFilesystemAsync()
	{
		AssertionResult pathResult = _fsPathAssertion.Execute(DefaultFilesystemPathKey);
		if (pathResult.Status == "fail")
		{
			return Task.FromResult(pathResult);
		}

		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			return Task.FromResult(pathResult);
		}

		if (!TryResolveIisIusrsIdentity(out string identity))
		{
			AssertionResult failure = AssertionResult.Failure(
				AssertionScope.Fs,
				AssertionPhase.FsUser,
				"Cannot resolve IIS_IUSRS Windows identity for full filesystem assertions");
			failure.Details["candidates"] = new[] { @"BUILTIN\IIS_IUSRS", "IIS_IUSRS" };
			return Task.FromResult(failure);
		}

		return Task.FromResult(_fsPermissionAssertion.Execute(DefaultFilesystemPathKey, identity, "full-control"));
	}

	private static void MergeResolved(AssertionResult target, AssertionResult source, string key)
	{
		if (source.Resolved.ContainsKey(key))
		{
			target.Resolved[key] = source.Resolved[key];
		}
	}

	private static AssertionPhase GetDefaultFailurePhase(AssertionScope scope)
	{
		return scope switch
		{
			AssertionScope.K8 => AssertionPhase.K8Context,
			AssertionScope.Local => AssertionPhase.DbDiscovery,
			AssertionScope.Fs => AssertionPhase.FsPath,
			_ => AssertionPhase.DbDiscovery
		};
	}

	private static IReadOnlyList<AssertInfrastructureDatabaseCandidate> BuildDatabaseCandidates(
		AssertionResult k8Result,
		AssertionResult localResult)
	{
		List<AssertInfrastructureDatabaseCandidate> candidates = [];
		candidates.AddRange(ExtractDatabaseCandidates("k8", k8Result, isConnectable: true));
		candidates.AddRange(ExtractDatabaseCandidates("local", localResult, isConnectable: true));
		return candidates;
	}

	private static IEnumerable<AssertInfrastructureDatabaseCandidate> ExtractDatabaseCandidates(
		string source,
		AssertionResult result,
		bool isConnectable)
	{
		if (result.Status != "pass" ||
			!result.Resolved.TryGetValue("databases", out object databases) ||
			databases is not IEnumerable<object> databaseEntries)
		{
			yield break;
		}

		foreach (object databaseEntry in databaseEntries)
		{
			if (!TryReadDatabaseCandidate(databaseEntry, source, isConnectable, out AssertInfrastructureDatabaseCandidate candidate))
			{
				continue;
			}

			yield return candidate;
		}
	}

	private static bool TryReadDatabaseCandidate(
		object databaseEntry,
		string source,
		bool isConnectable,
		out AssertInfrastructureDatabaseCandidate candidate)
	{
		if (databaseEntry is not IDictionary<string, object> values)
		{
			candidate = null;
			return false;
		}

		string engine = TryReadString(values, "engine");
		string name = TryReadString(values, "name");
		string host = TryReadString(values, "host");
		int? port = TryReadInt(values, "port");
		if (string.IsNullOrWhiteSpace(engine) ||
			string.IsNullOrWhiteSpace(name) ||
			string.IsNullOrWhiteSpace(host) ||
			!port.HasValue)
		{
			candidate = null;
			return false;
		}

		candidate = new AssertInfrastructureDatabaseCandidate(
			source,
			engine,
			name,
			host,
			port.Value,
			TryReadString(values, "version"),
			isConnectable);
		return true;
	}

	private static string TryReadString(IDictionary<string, object> values, string key)
	{
		return values.TryGetValue(key, out object value) ? value?.ToString() : null;
	}

	private static int? TryReadInt(IDictionary<string, object> values, string key)
	{
		if (!values.TryGetValue(key, out object value) || value is null)
		{
			return null;
		}

		if (value is int intValue)
		{
			return intValue;
		}

		return int.TryParse(value.ToString(), out int parsedValue) ? parsedValue : null;
	}

	private static string ComputeStatus(
		AssertionResult k8Result,
		AssertionResult localResult,
		AssertionResult filesystemResult,
		IReadOnlyList<AssertInfrastructureDatabaseCandidate> databaseCandidates)
	{
		bool[] sectionPasses = [k8Result.Status == "pass", localResult.Status == "pass", filesystemResult.Status == "pass"];
		int passedSections = sectionPasses.Count(sectionPassed => sectionPassed);
		if (passedSections == sectionPasses.Length)
		{
			return "pass";
		}

		if (passedSections == 0 || databaseCandidates.Count == 0)
		{
			return "fail";
		}

		return "partial";
	}

	private static string BuildSummary(
		string status,
		AssertionResult k8Result,
		AssertionResult localResult,
		AssertionResult filesystemResult,
		int databaseCandidateCount)
	{
		return $"Infrastructure assertion {status}: " +
			$"k8={k8Result.Status}, local={localResult.Status}, filesystem={filesystemResult.Status}, " +
			$"databaseCandidates={databaseCandidateCount}.";
	}

	private static bool TryResolveIisIusrsIdentity(out string identity)
	{
		string[] candidates = { @"BUILTIN\IIS_IUSRS", "IIS_IUSRS" };
		foreach (string candidate in candidates)
		{
			try
			{
				IdentityReference account = new NTAccount(candidate);
				account.Translate(typeof(SecurityIdentifier));
				identity = candidate;
				return true;
			}
			catch (IdentityNotMappedException)
			{
			}
			catch (SystemException)
			{
			}
		}

		identity = null;
		return false;
	}
}
