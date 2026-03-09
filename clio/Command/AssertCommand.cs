using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Clio.Common;
using Clio.Common.Assertions;
using Clio.Common.Kubernetes;

namespace Clio.Command
{
	/// <summary>
	/// Command for validating infrastructure and filesystem resources.
	/// Provides deterministic assertion mechanism for AI agents and humans.
	/// </summary>
	public class AssertCommand : Command<AssertOptions>
	{
		private readonly ILogger _logger;
		private readonly IKubernetesClient _k8sClient;
		private readonly IK8ContextValidator _contextValidator;
		private readonly IK8DatabaseAssertion _databaseAssertion;
		private readonly IK8RedisAssertion _redisAssertion;
		private readonly IFsPathAssertion _fsPathAssertion;
		private readonly IFsPermissionAssertion _fsPermissionAssertion;
		private readonly ILocalDatabaseAssertion _localDatabaseAssertion;
		private readonly ILocalRedisAssertion _localRedisAssertion;

		public AssertCommand(
			ILogger logger, 
			IKubernetesClient k8sClient, 
			IK8ContextValidator contextValidator,
			IK8DatabaseAssertion databaseAssertion,
			IK8RedisAssertion redisAssertion,
			IFsPathAssertion fsPathAssertion,
			IFsPermissionAssertion fsPermissionAssertion,
			ILocalDatabaseAssertion localDatabaseAssertion,
			ILocalRedisAssertion localRedisAssertion)
		{
			_logger = logger;
			_k8sClient = k8sClient;
			_contextValidator = contextValidator;
			_databaseAssertion = databaseAssertion;
			_redisAssertion = redisAssertion;
			_fsPathAssertion = fsPathAssertion;
			_fsPermissionAssertion = fsPermissionAssertion;
			_localDatabaseAssertion = localDatabaseAssertion;
			_localRedisAssertion = localRedisAssertion;
		}

		public override int Execute(AssertOptions options)
		{
			try
			{
				var scope = ParseScope(options.Scope);
				ValidateAllOptionUsage(scope, options);

				AssertionResult result = scope switch
				{
					AssertionScope.K8 => ExecuteK8Assertions(options),
					AssertionScope.Local => ExecuteLocalAssertions(options),
					AssertionScope.Fs => ExecuteFsAssertions(options),
					_ => throw new InvalidOperationException($"Unknown scope: {options.Scope}")
				};

				OutputResult(result);

				return result.Status == "pass" ? 0 : 1;
			}
			catch (ArgumentException ex)
			{
				_logger.WriteError($"Invalid invocation: {ex.Message}");
				return 2;
			}
			catch (Exception ex)
			{
				_logger.WriteError($"Unexpected error: {ex.Message}");
				return 2;
			}
		}

		private AssertionScope ParseScope(string scope)
		{
			return scope?.ToLowerInvariant() switch
			{
				"k8" => AssertionScope.K8,
				"local" => AssertionScope.Local,
				"fs" => AssertionScope.Fs,
				_ => throw new ArgumentException($"Invalid scope: {scope}. Must be 'k8', 'local', or 'fs'.")
			};
		}

		private AssertionResult ExecuteLocalAssertions(AssertOptions options)
		{
			ValidateLocalScopeOptions(options);

			bool hasDbRequest = options.All || HasDatabaseRequest(options);
			bool hasRedisRequest = options.All || options.Redis || !string.IsNullOrWhiteSpace(options.RedisServerName);
			string databaseEngines = options.All ? "postgres,mssql" : options.DatabaseEngines;
			int databaseMinimum = options.All ? 1 : (options.DatabaseMinimum <= 0 ? 1 : options.DatabaseMinimum);
			bool databaseConnect = options.All || options.DatabaseConnect;
			string databaseCheck = options.All ? "version" : options.DatabaseCheck;
			string dbServerName = options.All ? null : options.DbServerName;
			bool redisConnect = options.All || options.RedisConnect;
			bool redisPing = options.All || options.RedisPing;
			string redisServerName = options.All ? null : options.RedisServerName;

			if (!hasDbRequest && !hasRedisRequest)
			{
				throw new ArgumentException("At least one local assertion must be requested: --db ... or --redis");
			}

			var successResult = AssertionResult.Success();
			successResult.Scope = AssertionScope.Local;

			if (hasDbRequest)
			{
				var dbResult = _localDatabaseAssertion.ExecuteAsync(
					databaseEngines,
					databaseMinimum,
					databaseConnect,
					databaseCheck,
					dbServerName
				).GetAwaiter().GetResult();

				if (dbResult.Status == "fail")
				{
					return dbResult;
				}

				if (dbResult.Resolved.ContainsKey("databases"))
				{
					successResult.Resolved["databases"] = dbResult.Resolved["databases"];
				}
			}

			if (hasRedisRequest)
			{
				var redisResult = _localRedisAssertion.ExecuteAsync(
					redisConnect,
					redisPing,
					redisServerName
				).GetAwaiter().GetResult();

				if (redisResult.Status == "fail")
				{
					return redisResult;
				}

				if (redisResult.Resolved.ContainsKey("redis"))
				{
					successResult.Resolved["redis"] = redisResult.Resolved["redis"];
				}
			}

			return successResult;
		}

		private static bool HasDatabaseRequest(AssertOptions options)
		{
			return !string.IsNullOrWhiteSpace(options.DatabaseEngines) ||
				   options.DatabaseConnect ||
				   !string.IsNullOrWhiteSpace(options.DatabaseCheck) ||
				   options.DatabaseMinimum > 1;
		}

		private static void ValidateLocalScopeOptions(AssertOptions options)
		{
			string[] unsupportedFlags = {
				!string.IsNullOrWhiteSpace(options.Context) ? "--context" : null,
				!string.IsNullOrWhiteSpace(options.ContextRegex) ? "--context-regex" : null,
				!string.IsNullOrWhiteSpace(options.Cluster) ? "--cluster" : null,
				!string.IsNullOrWhiteSpace(options.Namespace) ? "--namespace" : null,
				!string.IsNullOrWhiteSpace(options.Path) ? "--path" : null,
				!string.IsNullOrWhiteSpace(options.User) ? "--user" : null,
				!string.IsNullOrWhiteSpace(options.Permission) ? "--perm" : null
			};

			string unsupported = string.Join(", ", unsupportedFlags.Where(f => !string.IsNullOrWhiteSpace(f)));
			if (!string.IsNullOrWhiteSpace(unsupported))
			{
				throw new ArgumentException($"Unsupported option(s) for local scope: {unsupported}");
			}

			if ((options.RedisConnect || options.RedisPing || !string.IsNullOrWhiteSpace(options.RedisServerName)) && !options.Redis)
			{
				throw new ArgumentException("--redis parameter is required when --redis-connect, --redis-ping or --redis-server-name is specified");
			}

			if (HasDatabaseRequest(options))
			{
				if (string.IsNullOrWhiteSpace(options.DatabaseEngines))
				{
					throw new ArgumentException("--db parameter is required when database checks are specified in local scope");
				}
			}
		}

		private AssertionResult ExecuteK8Assertions(AssertOptions options)
		{
			string databaseEngines = options.All ? "postgres,mssql" : options.DatabaseEngines;
			int databaseMinimum = options.All ? 2 : options.DatabaseMinimum;
			bool databaseConnect = options.All || options.DatabaseConnect;
			string databaseCheck = options.All ? "version" : options.DatabaseCheck;
			bool redisRequested = options.All || options.Redis;
			bool redisConnect = options.All || options.RedisConnect;
			bool redisPing = options.All || options.RedisPing;

			// Phase 0: Validate Kubernetes context (mandatory)
			var contextResult = _contextValidator.ValidateContextAsync(
				options.Context,
				options.ContextRegex,
				options.Cluster,
				options.Namespace
			).GetAwaiter().GetResult();

			if (contextResult.Status == "fail")
			{
				return contextResult;
			}

			// Validate namespace exists
			const string requiredNamespace = "clio-infrastructure";
			bool namespaceExists = _k8sClient.NamespaceExistsAsync(requiredNamespace).GetAwaiter().GetResult();
			if (!namespaceExists)
			{
				var result = AssertionResult.Failure(
					AssertionScope.K8,
					AssertionPhase.K8Context,
					$"Required namespace '{requiredNamespace}' does not exist"
				);
				result.Details["namespace"] = requiredNamespace;
				return result;
			}

			// Initialize success result with context
			var successResult = AssertionResult.Success();
			successResult.Scope = AssertionScope.K8;
			successResult.Context = contextResult.Context;

			// Execute database assertions if requested
			if (!string.IsNullOrEmpty(databaseEngines))
			{
				var dbResult = _databaseAssertion.ExecuteAsync(
					databaseEngines,
					databaseMinimum,
					databaseConnect,
					databaseCheck,
					requiredNamespace
				).GetAwaiter().GetResult();

				if (dbResult.Status == "fail")
				{
					dbResult.Context = contextResult.Context;
					return dbResult;
				}

				// Merge resolved databases
				if (dbResult.Resolved.ContainsKey("databases"))
				{
					successResult.Resolved["databases"] = dbResult.Resolved["databases"];
				}
			}

			// Execute Redis assertions if requested
			if (redisRequested)
			{
				var redisResult = _redisAssertion.ExecuteAsync(
					redisConnect,
					redisPing,
					requiredNamespace
				).GetAwaiter().GetResult();

				if (redisResult.Status == "fail")
				{
					redisResult.Context = contextResult.Context;
					return redisResult;
				}

				// Merge resolved Redis
				if (redisResult.Resolved.ContainsKey("redis"))
				{
					successResult.Resolved["redis"] = redisResult.Resolved["redis"];
				}
			}

			return successResult;
		}

		private AssertionResult ExecuteFsAssertions(AssertOptions options)
		{
			if (options.All)
			{
				return ExecuteFsAllAssertions();
			}

			// Validate required path parameter
			if (string.IsNullOrWhiteSpace(options.Path))
			{
				return AssertionResult.Failure(
					AssertionScope.Fs,
					AssertionPhase.FsPath,
					"--path parameter is required for filesystem assertions"
				);
			}

			// Phase 1: Validate path exists
			var pathResult = _fsPathAssertion.Execute(options.Path);
			if (pathResult.Status == "fail")
			{
				return pathResult;
			}

			// Phase 2: Validate permissions if requested
			if (!string.IsNullOrWhiteSpace(options.User) || !string.IsNullOrWhiteSpace(options.Permission))
			{
				// Both user and permission must be specified together
				if (string.IsNullOrWhiteSpace(options.User))
				{
					return AssertionResult.Failure(
						AssertionScope.Fs,
						AssertionPhase.FsUser,
						"--user parameter is required when --perm is specified"
					);
				}

				if (string.IsNullOrWhiteSpace(options.Permission))
				{
					return AssertionResult.Failure(
						AssertionScope.Fs,
						AssertionPhase.FsPerm,
						"--perm parameter is required when --user is specified"
					);
				}

				// Execute permission assertion
				var permResult = _fsPermissionAssertion.Execute(options.Path, options.User, options.Permission);
				return permResult;
			}

			// Success: path exists and no permission check requested
			return pathResult;
		}

		private AssertionResult ExecuteFsAllAssertions()
		{
			const string defaultPathKey = "iis-clio-root-path";
			var pathResult = _fsPathAssertion.Execute(defaultPathKey);
			if (pathResult.Status == "fail")
			{
				return pathResult;
			}

			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				return pathResult;
			}

			if (!TryResolveIisIusrsIdentity(out string iisIdentity))
			{
				var failure = AssertionResult.Failure(
					AssertionScope.Fs,
					AssertionPhase.FsUser,
					"Cannot resolve IIS_IUSRS Windows identity for --all filesystem assertions"
				);
				failure.Details["candidates"] = new[] { @"BUILTIN\IIS_IUSRS", "IIS_IUSRS" };
				return failure;
			}

			return _fsPermissionAssertion.Execute(defaultPathKey, iisIdentity, "full-control");
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
					// Try next candidate.
				}
				catch (SystemException)
				{
					// Try next candidate.
				}
			}

			identity = null;
			return false;
		}

		private static void ValidateAllOptionUsage(AssertionScope scope, AssertOptions options)
		{
			if (!options.All)
			{
				return;
			}

			string[] mixedOptions = scope switch
			{
				AssertionScope.K8 => new[]
				{
					!string.IsNullOrWhiteSpace(options.Context) ? "--context" : null,
					!string.IsNullOrWhiteSpace(options.ContextRegex) ? "--context-regex" : null,
					!string.IsNullOrWhiteSpace(options.Cluster) ? "--cluster" : null,
					!string.IsNullOrWhiteSpace(options.Namespace) ? "--namespace" : null,
					!string.IsNullOrWhiteSpace(options.DatabaseEngines) ? "--db" : null,
					options.DatabaseMinimum > 1 ? "--db-min" : null,
					options.DatabaseConnect ? "--db-connect" : null,
					!string.IsNullOrWhiteSpace(options.DatabaseCheck) ? "--db-check" : null,
					options.Redis ? "--redis" : null,
					options.RedisConnect ? "--redis-connect" : null,
					options.RedisPing ? "--redis-ping" : null,
					!string.IsNullOrWhiteSpace(options.RedisServerName) ? "--redis-server-name" : null
				},
				AssertionScope.Local => new[]
				{
					!string.IsNullOrWhiteSpace(options.DatabaseEngines) ? "--db" : null,
					options.DatabaseMinimum > 1 ? "--db-min" : null,
					options.DatabaseConnect ? "--db-connect" : null,
					!string.IsNullOrWhiteSpace(options.DatabaseCheck) ? "--db-check" : null,
					!string.IsNullOrWhiteSpace(options.DbServerName) ? "--db-server-name" : null,
					options.Redis ? "--redis" : null,
					options.RedisConnect ? "--redis-connect" : null,
					options.RedisPing ? "--redis-ping" : null,
					!string.IsNullOrWhiteSpace(options.RedisServerName) ? "--redis-server-name" : null
				},
				AssertionScope.Fs => new[]
				{
					!string.IsNullOrWhiteSpace(options.Path) ? "--path" : null,
					!string.IsNullOrWhiteSpace(options.User) ? "--user" : null,
					!string.IsNullOrWhiteSpace(options.Permission) ? "--perm" : null,
					!string.IsNullOrWhiteSpace(options.RedisServerName) ? "--redis-server-name" : null
				},
				_ => Array.Empty<string>()
			};

			string unsupported = string.Join(", ", mixedOptions.Where(f => !string.IsNullOrWhiteSpace(f)));
			if (!string.IsNullOrWhiteSpace(unsupported))
			{
				throw new ArgumentException($"--all cannot be combined with explicit scope options: {unsupported}");
			}
		}

		private void OutputResult(AssertionResult result) {
			if (result.Status != "pass") {
				_logger.WriteError("Assertion failed:");
			}
			else {
				_logger.WriteInfo("Assertion passed");
			}
			string json = result.ToJson();
			_logger.WriteLine(json);
		}
	}
}
