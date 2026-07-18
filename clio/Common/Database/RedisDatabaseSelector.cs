using System;
using System.Threading;
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

	/// <summary>
	/// Number of connect attempts made against the target Redis before the selection is reported as
	/// failed. The per-attempt wait stays bounded by <see cref="ConnectTimeout"/> (the ENG-90640
	/// fail-fast contract), so this bounded retry absorbs a single transient connect blip — e.g. a
	/// Rancher Desktop port-forward hiccup during <c>deploy-creatio</c> — without turning a genuinely
	/// unreachable Redis into a multi-minute hang.
	/// </summary>
	internal const int MaxConnectAttempts = 3;

	/// <summary>
	/// Base backoff applied between transient-failure retries. The delay grows exponentially per
	/// attempt (<see cref="GetBackoffDelay"/>) so a reachable Redis that momentarily blipped gets a
	/// brief pause to recover before the next attempt.
	/// </summary>
	internal static readonly TimeSpan RetryBackoff = TimeSpan.FromMilliseconds(500);

	private readonly Func<ConfigurationOptions, IConnectionMultiplexer> _connectionFactory;

	private readonly Action<TimeSpan> _delay;

	/// <summary>
	/// Initializes a new instance of the <see cref="RedisDatabaseSelector"/> class using the real
	/// StackExchange.Redis connect factory and <see cref="Thread.Sleep(TimeSpan)"/> backoff.
	/// </summary>
	public RedisDatabaseSelector()
		: this(configurationOptions => ConnectionMultiplexer.Connect(configurationOptions), Thread.Sleep)
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="RedisDatabaseSelector"/> class with injectable
	/// connect and backoff seams. Exposed internally so the bounded retry/backoff loop can be unit
	/// tested without a live Redis or real thread sleeps.
	/// </summary>
	/// <param name="connectionFactory">Factory that opens a Redis connection for the supplied options.</param>
	/// <param name="delay">Backoff delay action invoked between retry attempts.</param>
	internal RedisDatabaseSelector(Func<ConfigurationOptions, IConnectionMultiplexer> connectionFactory, Action<TimeSpan> delay)
	{
		_connectionFactory = connectionFactory;
		_delay = delay;
	}

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
		ConfigurationOptions configurationOptions = BuildConfigurationOptions(hostname, port, username, password);

		// BOUNDED RETRY contract: a single transient connect blip (e.g. a Rancher Desktop port-forward
		// hiccup) must not abort the deployment when the reachable Redis responds fine immediately
		// before and after. Each attempt keeps the ENG-90640 fail-fast per-attempt timeout, and only
		// transient connect/timeout failures are retried — a definitive error (bad credentials, all
		// databases in use) is surfaced without wasting further attempts.
		Exception lastError = null;
		int attemptsMade = 0;
		for (int attempt = 1; attempt <= MaxConnectAttempts; attempt++)
		{
			attemptsMade = attempt;
			try
			{
				return SelectEmptyDatabase(configurationOptions, hostname, port);
			}
			catch (Exception ex) when (IsTransientConnectFailure(ex))
			{
				lastError = ex;
				if (attempt < MaxConnectAttempts)
				{
					_delay(GetBackoffDelay(attempt));
				}
			}
			catch (Exception ex)
			{
				lastError = ex;
				break;
			}
		}

		string errorMessage = $"[Redis Connection Error] Could not connect to Redis at {hostname}:{port} " +
							  $"after {attemptsMade} attempt(s). " +
							  $"Error: {lastError.Message}. " +
							  "Make sure Redis is running and accessible. " +
							  "You can also manually specify a database number using the --redis-db option";

		return new RedisDatabaseSelectionResult
		{
			Success = false,
			DatabaseNumber = -1,
			ErrorMessage = errorMessage
		};
	}

	/// <summary>
	/// Opens a single Redis connection and scans databases <c>1..DatabaseCount-1</c> for the first
	/// empty one. A connect/probe failure throws (so the caller can retry); an exhausted-but-reachable
	/// server returns a definitive non-retryable failure result.
	/// </summary>
	/// <param name="configurationOptions">Fail-fast configuration for the target endpoint.</param>
	/// <param name="hostname">Redis hostname.</param>
	/// <param name="port">Redis port.</param>
	/// <returns>Selection result with the chosen db number, or an "all in use" failure.</returns>
	private RedisDatabaseSelectionResult SelectEmptyDatabase(ConfigurationOptions configurationOptions, string hostname, int port)
	{
		using IConnectionMultiplexer redis = _connectionFactory(configurationOptions);
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

	/// <summary>
	/// Classifies whether an exception represents a transient connect/timeout failure worth retrying.
	/// A genuinely unreachable Redis still fails fast because each retry keeps the bounded
	/// <see cref="ConnectTimeout"/> and the attempt count is capped by <see cref="MaxConnectAttempts"/>.
	/// Authentication and protocol failures are treated as definitive misconfigurations rather than
	/// transient blips — StackExchange.Redis surfaces a wrong password/ACL user as a
	/// <see cref="RedisConnectionException"/> with <see cref="ConnectionFailureType.AuthenticationFailure"/>,
	/// and retrying it would only waste the whole connect budget and misreport the cause as connectivity.
	/// </summary>
	/// <param name="ex">Exception thrown while connecting to or probing Redis.</param>
	/// <returns><c>true</c> when the failure is a transient connect/timeout condition.</returns>
	private static bool IsTransientConnectFailure(Exception ex)
	{
		return ex switch
		{
			RedisTimeoutException => true,
			RedisConnectionException connectionException =>
				connectionException.FailureType is not (ConnectionFailureType.AuthenticationFailure
					or ConnectionFailureType.ProtocolFailure),
			var _ => false
		};
	}

	/// <summary>
	/// Computes the exponential backoff for the given retry attempt (attempt 1 waits
	/// <see cref="RetryBackoff"/>, attempt 2 waits twice that, and so on).
	/// </summary>
	/// <param name="attempt">1-based attempt number that just failed.</param>
	/// <returns>Backoff delay before the next attempt.</returns>
	private static TimeSpan GetBackoffDelay(int attempt)
	{
		return TimeSpan.FromMilliseconds(RetryBackoff.TotalMilliseconds * Math.Pow(2, attempt - 1));
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
