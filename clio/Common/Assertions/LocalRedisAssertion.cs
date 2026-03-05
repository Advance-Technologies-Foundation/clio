using System;
using System.Threading.Tasks;
using Clio.Common.Database;
using Clio.Common.db;
using StackExchange.Redis;

namespace Clio.Common.Assertions;

/// <summary>
/// Defines local Redis assertion operations for assert local scope.
/// </summary>
public interface ILocalRedisAssertion
{
	/// <summary>
	/// Executes local Redis assertions.
	/// </summary>
	/// <param name="checkConnect">Whether TCP connectivity should be asserted.</param>
	/// <param name="checkPing">Whether Redis PING should be asserted.</param>
	/// <param name="redisServerName">Configured Redis server name from appsettings.json.</param>
	/// <returns>Structured assertion result.</returns>
	Task<AssertionResult> ExecuteAsync(bool checkConnect, bool checkPing, string redisServerName);
}

/// <summary>
/// Executes Redis assertions against local Redis endpoint.
/// </summary>
public class LocalRedisAssertion : ILocalRedisAssertion
{
	private readonly IRedisAuthenticationValidator _redisAuthenticationValidator;
	private readonly ILocalRedisServerResolver _localRedisServerResolver;
	private readonly IRedisDatabaseSelector _redisDatabaseSelector;

	/// <summary>
	/// Initializes a new instance of the <see cref="LocalRedisAssertion"/> class.
	/// </summary>
	/// <param name="localRedisServerResolver">Resolver for local Redis endpoint.</param>
	/// <param name="redisDatabaseSelector">Redis database selector service.</param>
	/// <param name="redisAuthenticationValidator">Redis authentication validator.</param>
	public LocalRedisAssertion(
		ILocalRedisServerResolver localRedisServerResolver,
		IRedisDatabaseSelector redisDatabaseSelector,
		IRedisAuthenticationValidator redisAuthenticationValidator)
	{
		_localRedisServerResolver = localRedisServerResolver ?? throw new ArgumentNullException(nameof(localRedisServerResolver));
		_redisDatabaseSelector = redisDatabaseSelector ?? throw new ArgumentNullException(nameof(redisDatabaseSelector));
		_redisAuthenticationValidator =
			redisAuthenticationValidator ?? throw new ArgumentNullException(nameof(redisAuthenticationValidator));
	}

	/// <inheritdoc />
	public async Task<AssertionResult> ExecuteAsync(bool checkConnect, bool checkPing, string redisServerName)
	{
		if (!_localRedisServerResolver.TryResolve(redisServerName, out ResolvedLocalRedisServer redisServer, out string resolveError))
		{
			return AssertionResult.Failure(
				AssertionScope.Local,
				AssertionPhase.RedisDiscovery,
				resolveError ?? "Failed to resolve local Redis server configuration");
		}

		RedisDatabaseSelectionResult discovery = _redisDatabaseSelector.FindEmptyDatabase(
			redisServer.Host,
			redisServer.Port,
			redisServer.Username,
			redisServer.Password);
		if (!discovery.Success)
		{
			AssertionResult discoveryFailure = AssertionResult.Failure(
				AssertionScope.Local,
				AssertionPhase.RedisDiscovery,
				discovery.ErrorMessage ?? $"Redis not available at {redisServer.Host}:{redisServer.Port}");
			discoveryFailure.Details["name"] = redisServer.Name;
			discoveryFailure.Details["host"] = redisServer.Host;
			discoveryFailure.Details["port"] = redisServer.Port;
			return discoveryFailure;
		}

		if (checkConnect)
		{
			bool isConnectable = await CheckConnectivityAsync(redisServer);
			if (!isConnectable)
			{
				AssertionResult connectFailure = AssertionResult.Failure(
					AssertionScope.Local,
					AssertionPhase.RedisConnect,
					$"Cannot connect to Redis at {redisServer.Host}:{redisServer.Port}");
				connectFailure.Details["name"] = redisServer.Name;
				connectFailure.Details["host"] = redisServer.Host;
				connectFailure.Details["port"] = redisServer.Port;
				return connectFailure;
			}
		}

		if (checkPing)
		{
			bool pingSuccess = await CheckPingAsync(redisServer, discovery.DatabaseNumber);
			if (!pingSuccess)
			{
				AssertionResult pingFailure = AssertionResult.Failure(
					AssertionScope.Local,
					AssertionPhase.RedisPing,
					$"Redis PING command failed at {redisServer.Host}:{redisServer.Port}");
				pingFailure.Details["name"] = redisServer.Name;
				pingFailure.Details["host"] = redisServer.Host;
				pingFailure.Details["port"] = redisServer.Port;
				return pingFailure;
			}
		}

		if (HasConfiguredCredentials(redisServer))
		{
			RedisAuthValidationResult authValidationResult =
				await _redisAuthenticationValidator.ValidateAuthenticationIsEnforcedAsync(redisServer);
			if (!authValidationResult.IsAuthenticationEnforced)
			{
				AssertionResult authFailure = AssertionResult.Failure(
					AssertionScope.Local,
					AssertionPhase.RedisConnect,
					authValidationResult.ErrorMessage ??
					$"Redis authentication is not enforced at {redisServer.Host}:{redisServer.Port}");
				authFailure.Details["name"] = redisServer.Name;
				authFailure.Details["host"] = redisServer.Host;
				authFailure.Details["port"] = redisServer.Port;
				authFailure.Details["requiresAuthentication"] = true;
				return authFailure;
			}
		}

		AssertionResult successResult = AssertionResult.Success();
		successResult.Scope = AssertionScope.Local;
		successResult.Resolved["redis"] = new RedisAssertionResolvedDto
		{
			Name = redisServer.Name,
			Host = redisServer.Host,
			Port = redisServer.Port,
			FirstAvailableDb = discovery.DatabaseNumber
		};
		return successResult;
	}

	private static bool HasConfiguredCredentials(ResolvedLocalRedisServer redisServer)
	{
		return !string.IsNullOrWhiteSpace(redisServer?.Username) || !string.IsNullOrWhiteSpace(redisServer?.Password);
	}

	private static async Task<bool> CheckConnectivityAsync(ResolvedLocalRedisServer redisServer)
	{
		try
		{
			await ConnectAsync(redisServer);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static async Task<bool> CheckPingAsync(ResolvedLocalRedisServer redisServer, int databaseNumber)
	{
		try
		{
			using ConnectionMultiplexer connection = await ConnectAsync(redisServer);
			IDatabase database = connection.GetDatabase(databaseNumber);
			TimeSpan pingTime = await database.PingAsync();
			return pingTime >= TimeSpan.Zero;
		}
		catch
		{
			return false;
		}
	}

	private static Task<ConnectionMultiplexer> ConnectAsync(ResolvedLocalRedisServer redisServer)
	{
		ConfigurationOptions configurationOptions = new()
		{
			AbortOnConnectFail = false,
			ConnectTimeout = 10000,
			SyncTimeout = 10000
		};
		configurationOptions.EndPoints.Add(redisServer.Host, redisServer.Port);
		if (!string.IsNullOrWhiteSpace(redisServer.Username))
		{
			configurationOptions.User = redisServer.Username;
		}
		if (!string.IsNullOrWhiteSpace(redisServer.Password))
		{
			configurationOptions.Password = redisServer.Password;
		}
		return ConnectionMultiplexer.ConnectAsync(configurationOptions);
	}
}
