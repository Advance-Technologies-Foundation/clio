using System;
using StackExchange.Redis;

namespace Clio.Common.Database;

/// <summary>
/// Represents Redis database selection result.
/// </summary>
public class RedisDatabaseSelectionResult
{
	/// <summary>
	/// Gets or sets a value indicating whether selection completed successfully.
	/// </summary>
	public bool Success { get; set; }

	/// <summary>
	/// Gets or sets selected database index.
	/// </summary>
	public int DatabaseNumber { get; set; }

	/// <summary>
	/// Gets or sets user-facing error message when selection fails.
	/// </summary>
	public string ErrorMessage { get; set; }
}

/// <summary>
/// Defines Redis database selection operations.
/// </summary>
public interface IRedisDatabaseSelector
{
	/// <summary>
	/// Finds an empty Redis database for local Redis instance.
	/// </summary>
	/// <returns>Selection result with chosen db number or failure reason.</returns>
	RedisDatabaseSelectionResult FindEmptyLocalDatabase();

	/// <summary>
	/// Finds an empty Redis database for local Redis instance using optional credentials.
	/// </summary>
	/// <param name="username">Redis ACL username.</param>
	/// <param name="password">Redis password.</param>
	/// <returns>Selection result with chosen db number or failure reason.</returns>
	RedisDatabaseSelectionResult FindEmptyLocalDatabase(string username, string password);

	/// <summary>
	/// Finds an empty Redis database for specific host and port.
	/// </summary>
	/// <param name="hostname">Redis hostname.</param>
	/// <param name="port">Redis port.</param>
	/// <returns>Selection result with chosen db number or failure reason.</returns>
	RedisDatabaseSelectionResult FindEmptyDatabase(string hostname, int port);

	/// <summary>
	/// Finds an empty Redis database for specific host and port using optional credentials.
	/// </summary>
	/// <param name="hostname">Redis hostname.</param>
	/// <param name="port">Redis port.</param>
	/// <param name="username">Redis ACL username.</param>
	/// <param name="password">Redis password.</param>
	/// <returns>Selection result with chosen db number or failure reason.</returns>
	RedisDatabaseSelectionResult FindEmptyDatabase(string hostname, int port, string username, string password);
}

/// <summary>
/// Provides Redis database selection based on server database occupancy.
/// </summary>
public class RedisDatabaseSelector : IRedisDatabaseSelector
{
	/// <summary>
	/// Hard upper bound applied to the production Redis connect/command path. With
	/// <see cref="ConfigurationOptions.AbortOnConnectFail"/> set to <c>true</c> an unreachable Redis
	/// surfaces a descriptive connection error within this window instead of hanging the caller
	/// indefinitely. Previously the connect used an unbounded retry plus a 500s sync timeout, which
	/// froze install/assert flows — and the MCP e2e suite — for minutes against an unreachable Redis.
	/// </summary>
	internal static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(12);

	private static readonly int ConnectTimeoutMilliseconds = (int)ConnectTimeout.TotalMilliseconds;

	/// <inheritdoc />
	public RedisDatabaseSelectionResult FindEmptyLocalDatabase()
	{
		return FindEmptyDatabase("localhost", 6379, null, null);
	}

	/// <inheritdoc />
	public RedisDatabaseSelectionResult FindEmptyLocalDatabase(string username, string password)
	{
		return FindEmptyDatabase("localhost", 6379, username, password);
	}

	/// <inheritdoc />
	public RedisDatabaseSelectionResult FindEmptyDatabase(string hostname, int port)
	{
		return FindEmptyDatabase(hostname, port, null, null);
	}

	/// <inheritdoc />
	public RedisDatabaseSelectionResult FindEmptyDatabase(string hostname, int port, string username, string password)
	{
		try
		{
			ConfigurationOptions configurationOptions = BuildConfigurationOptions(hostname, port, username, password);

			ConnectionMultiplexer redis = ConnectionMultiplexer.Connect(configurationOptions);
			IServer server = redis.GetServer(hostname, port);
			int count = server.DatabaseCount;
			for (int i = 1; i < count; i++)
			{
				long records = server.DatabaseSize(i);
				if (records == 0)
				{
					return new RedisDatabaseSelectionResult
					{
						Success = true,
						DatabaseNumber = i
					};
				}
			}

			string errorMessage = $"[Redis Configuration Error] Could not find an empty Redis database. " +
								  $"All {count - 1} available databases (1-{count - 1}) at {hostname}:{port} are in use. " +
								  "Please either: " +
								  "1) Clear some Redis databases, " +
								  "2) Increase the number of Redis databases, " +
								  "3) Manually specify a database number using the --redis-db option";

			return new RedisDatabaseSelectionResult
			{
				Success = false,
				DatabaseNumber = -1,
				ErrorMessage = errorMessage
			};
		}
		catch (Exception ex)
		{
			string errorMessage = $"[Redis Connection Error] Could not connect to Redis at {hostname}:{port}. " +
								  $"Error: {ex.Message}. " +
								  "Make sure Redis is running and accessible. " +
								  "You can also manually specify a database number using the --redis-db option";

			return new RedisDatabaseSelectionResult
			{
				Success = false,
				DatabaseNumber = -1,
				ErrorMessage = errorMessage
			};
		}
	}

	/// <summary>
	/// Builds the StackExchange.Redis <see cref="ConfigurationOptions"/> for the production connect
	/// path with a bounded, fail-fast wiring so an unreachable Redis throws a descriptive
	/// <see cref="RedisConnectionException"/> within <see cref="ConnectTimeout"/> (caught and turned
	/// into a structured result) instead of hanging. Exposed internally for unit testing of the
	/// timeout wiring.
	/// </summary>
	/// <param name="hostname">Redis hostname.</param>
	/// <param name="port">Redis port.</param>
	/// <param name="username">Optional Redis ACL username.</param>
	/// <param name="password">Optional Redis password.</param>
	/// <returns>Bounded, fail-fast configuration options for the target endpoint.</returns>
	internal static ConfigurationOptions BuildConfigurationOptions(string hostname, int port, string username, string password)
	{
		// FAIL-FAST contract: an unreachable Redis must surface as a connection error within a few
		// seconds, never as a multi-minute hang. Enabling AbortOnConnectFail makes Connect throw on a
		// failed connect rather than returning a multiplexer that keeps retrying forever in the
		// background, and the bounded connect, sync and async timeouts plus a single connect retry cap
		// the wait. When Redis IS reachable these bounds are generous enough not to change behavior.
		ConfigurationOptions configurationOptions = new()
		{
			AbortOnConnectFail = true,
			ConnectTimeout = ConnectTimeoutMilliseconds,
			SyncTimeout = ConnectTimeoutMilliseconds,
			AsyncTimeout = ConnectTimeoutMilliseconds,
			ConnectRetry = 1
		};
		configurationOptions.EndPoints.Add(hostname, port);
		if (!string.IsNullOrWhiteSpace(username))
		{
			configurationOptions.User = username;
		}
		if (!string.IsNullOrWhiteSpace(password))
		{
			configurationOptions.Password = password;
		}
		return configurationOptions;
	}
}
