using System;
using System.Threading.Tasks;
using Clio.Common.db;
using StackExchange.Redis;

namespace Clio.Common.Assertions;

/// <summary>
/// Validates Redis authentication enforcement for configured endpoints.
/// </summary>
public interface IRedisAuthenticationValidator
{
	/// <summary>
	/// Validates that Redis endpoint enforces authentication for anonymous clients.
	/// </summary>
	/// <param name="redisServer">Resolved Redis endpoint.</param>
	/// <returns>
	/// Validation result with <see cref="RedisAuthValidationResult.IsAuthenticationEnforced"/> set to <c>true</c>
	/// when anonymous access is blocked.
	/// </returns>
	Task<RedisAuthValidationResult> ValidateAuthenticationIsEnforcedAsync(ResolvedLocalRedisServer redisServer);
}

/// <summary>
/// Default Redis authentication validator.
/// </summary>
public class RedisAuthenticationValidator : IRedisAuthenticationValidator
{
	/// <inheritdoc />
	public async Task<RedisAuthValidationResult> ValidateAuthenticationIsEnforcedAsync(ResolvedLocalRedisServer redisServer)
	{
		if (redisServer == null)
		{
			return new RedisAuthValidationResult
			{
				IsAuthenticationEnforced = false,
				ErrorMessage = "Redis server is not resolved"
			};
		}

		try
		{
			ConfigurationOptions anonymousOptions = new()
			{
				AbortOnConnectFail = false,
				ConnectTimeout = 5000,
				SyncTimeout = 5000
			};
			anonymousOptions.EndPoints.Add(redisServer.Host, redisServer.Port);

			using ConnectionMultiplexer anonymousConnection = await ConnectionMultiplexer.ConnectAsync(anonymousOptions);
			IDatabase database = anonymousConnection.GetDatabase(0);
			await database.PingAsync();

			return new RedisAuthValidationResult
			{
				IsAuthenticationEnforced = false,
				ErrorMessage =
					$"Redis server '{redisServer.Name}' at {redisServer.Host}:{redisServer.Port} allows anonymous access while credentials are configured"
			};
		}
		catch (RedisServerException ex) when (IsAuthRelated(ex.Message))
		{
			return RedisAuthValidationResult.Enforced();
		}
		catch (RedisConnectionException ex) when (ex.FailureType == ConnectionFailureType.AuthenticationFailure)
		{
			return RedisAuthValidationResult.Enforced();
		}
		catch (Exception ex)
		{
			// Any failure to use anonymous access is treated as authentication being enforced.
			return new RedisAuthValidationResult
			{
				IsAuthenticationEnforced = true,
				ErrorMessage = $"Anonymous Redis probe failed: {ex.Message}"
			};
		}
	}

	private static bool IsAuthRelated(string message)
	{
		if (string.IsNullOrWhiteSpace(message))
		{
			return false;
		}

		return message.Contains("NOAUTH", StringComparison.OrdinalIgnoreCase) ||
			   message.Contains("WRONGPASS", StringComparison.OrdinalIgnoreCase) ||
			   message.Contains("AUTH", StringComparison.OrdinalIgnoreCase) ||
			   message.Contains("NOPERM", StringComparison.OrdinalIgnoreCase);
	}
}

/// <summary>
/// Redis authentication validation result.
/// </summary>
public class RedisAuthValidationResult
{
	/// <summary>
	/// Gets or sets a value indicating whether Redis blocks anonymous access.
	/// </summary>
	public bool IsAuthenticationEnforced { get; set; }

	/// <summary>
	/// Gets or sets validation details or failure message.
	/// </summary>
	public string ErrorMessage { get; set; }

	/// <summary>
	/// Creates successful validation result.
	/// </summary>
	public static RedisAuthValidationResult Enforced()
	{
		return new RedisAuthValidationResult
		{
			IsAuthenticationEnforced = true
		};
	}
}
