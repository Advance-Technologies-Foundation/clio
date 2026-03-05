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
			ConfigurationOptions configurationOptions = new()
			{
				SyncTimeout = 500000,
				AbortOnConnectFail = false
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
}
