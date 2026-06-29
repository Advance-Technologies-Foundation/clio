using NUnit.Framework;
using StackExchange.Redis;

namespace Clio.Mcp.E2E.Support.Redis;

internal sealed class RedisSandboxClient : IAsyncDisposable {
	/// <summary>
	/// Hard upper bound on how long the harness will wait while connecting to (or issuing a
	/// command against) the sandbox Redis. The sandbox Redis is not reachable from CI agents,
	/// so an unbounded connect would freeze the whole e2e suite until the build force-stop.
	/// This bound guarantees the connect FAILS FAST with a descriptive error instead of hanging.
	/// </summary>
	internal static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(12);

	private static readonly int ConnectTimeoutMilliseconds = (int)ConnectTimeout.TotalMilliseconds;

	private readonly ConnectionMultiplexer _connection;

	private RedisSandboxClient(ConnectionMultiplexer connection, IDatabase database) {
		_connection = connection;
		Database = database;
	}

	public IDatabase Database { get; }

	public static async Task<RedisSandboxClient> ConnectAsync(string connectionString) {
		ConfigurationOptions options = BuildConfigurationOptions(connectionString);

		// Belt-and-suspenders: even though the StackExchange.Redis options already bound the
		// connect (AbortOnConnectFail=true + ConnectTimeout), wrap the await in a hard token so a
		// stuck connect can never block the suite past ConnectTimeout regardless of client behavior.
		using CancellationTokenSource timeoutSource = new(ConnectTimeout);
		try {
			ConnectionMultiplexer connection = await ConnectAsync(options, timeoutSource.Token);
			return new RedisSandboxClient(connection, connection.GetDatabase(options.DefaultDatabase ?? -1));
		}
		catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested) {
			string message = $"Redis sandbox connect did not complete within {ConnectTimeout.TotalSeconds:0}s " +
				$"for endpoints '{string.Join(",", options.EndPoints)}'. The sandbox Redis is most likely unreachable " +
				"from this agent. Failing fast instead of hanging the e2e suite.";
			TestContext.Out.WriteLine(message);
			throw new TimeoutException(message);
		}
		catch (RedisConnectionException exception) {
			string message = $"Redis sandbox connect failed within {ConnectTimeout.TotalSeconds:0}s " +
				$"for endpoints '{string.Join(",", options.EndPoints)}': {exception.Message}. " +
				"The sandbox Redis is most likely unreachable from this agent. " +
				"Failing fast instead of hanging the e2e suite.";
			TestContext.Out.WriteLine(message);
			throw;
		}
	}

	public async Task SeedKeyAsync(string key, string value) {
		await Database.StringSetAsync(key, value);
	}

	public async Task<bool> KeyExistsAsync(string key) {
		return await Database.KeyExistsAsync(key);
	}

	public async Task DeleteKeyIfExistsAsync(string key) {
		await Database.KeyDeleteAsync(key);
	}

	public async Task WaitUntilKeyDeletedAsync(string key, TimeSpan timeout, CancellationToken cancellationToken) {
		DateTime deadlineUtc = DateTime.UtcNow.Add(timeout);
		while (DateTime.UtcNow < deadlineUtc) {
			cancellationToken.ThrowIfCancellationRequested();
			if (!await KeyExistsAsync(key)) {
				return;
			}

			await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
		}

		throw new TimeoutException($"Redis key '{key}' still exists after waiting {timeout}.");
	}

	public async ValueTask DisposeAsync() {
		await _connection.DisposeAsync();
	}

	private static async Task<ConnectionMultiplexer> ConnectAsync(ConfigurationOptions options, CancellationToken cancellationToken) {
		// Offload to a pool thread: ConnectionMultiplexer.ConnectAsync runs a SYNCHRONOUS prologue
		// (endpoint/DNS resolution, socket setup) on the calling thread before it yields a Task. When
		// the sandbox Redis host does not resolve/respond from a CI agent that prologue blocks for a
		// long time, and because it runs before the first await neither the WhenAny/Task.Delay bound
		// below nor a caller's WaitAsync(token) can interrupt it (there is no awaitable yet). Task.Run
		// moves that synchronous work off the test thread so the timeout bound actually applies; a
		// still-blocked prologue leaks on the pool thread but no longer freezes the suite.
		Task<ConnectionMultiplexer> connectTask = Task.Run(() => ConnectionMultiplexer.ConnectAsync(options));
		Task completed = await Task.WhenAny(connectTask, Task.Delay(Timeout.Infinite, cancellationToken));
		if (completed != connectTask) {
			cancellationToken.ThrowIfCancellationRequested();
		}

		return await connectTask;
	}

	/// <summary>
	/// Builds the StackExchange.Redis <see cref="ConfigurationOptions"/> used by the harness, with a
	/// bounded connect (<see cref="ConfigurationOptions.AbortOnConnectFail"/> = <c>true</c> plus a small
	/// <see cref="ConfigurationOptions.ConnectTimeout"/>/<see cref="ConfigurationOptions.SyncTimeout"/>)
	/// so an unreachable sandbox Redis throws within <see cref="ConnectTimeout"/> instead of hanging.
	/// Exposed for unit testing of the timeout wiring.
	/// </summary>
	internal static ConfigurationOptions BuildConfigurationOptions(string connectionString) {
		ConfigurationOptions options = ParseConfigurationOptions(connectionString);
		return ApplyConnectTimeoutBounds(options);
	}

	private static ConfigurationOptions ParseConfigurationOptions(string connectionString) {
		if (!connectionString.Contains("host=", StringComparison.OrdinalIgnoreCase)) {
			return ConfigurationOptions.Parse(connectionString);
		}

		Dictionary<string, string> parts = connectionString
			.Split(';', StringSplitOptions.RemoveEmptyEntries)
			.Select(part => part.Split('=', 2))
			.Where(part => part.Length == 2)
			.ToDictionary(part => part[0].Trim(), part => part[1].Trim(), StringComparer.OrdinalIgnoreCase);

		string host = GetRequiredPart(parts, "host");
		int port = GetIntPart(parts, "port", 6379);

		ConfigurationOptions options = new();
		options.EndPoints.Add(host, port);

		if (parts.TryGetValue("db", out string? db) && int.TryParse(db, out int database)) {
			options.DefaultDatabase = database;
		}

		if (parts.TryGetValue("password", out string? password) && !string.IsNullOrWhiteSpace(password)) {
			options.Password = password;
		}

		if (parts.TryGetValue("user", out string? user) && !string.IsNullOrWhiteSpace(user)) {
			options.User = user;
		}

		if (parts.TryGetValue("ssl", out string? ssl) && bool.TryParse(ssl, out bool useSsl)) {
			options.Ssl = useSsl;
		}

		return options;
	}

	private static ConfigurationOptions ApplyConnectTimeoutBounds(ConfigurationOptions options) {
		// FAIL-FAST contract: an unreachable Redis must surface as a connect error within a few
		// seconds, never as a suite hang. AbortOnConnectFail=true makes ConnectAsync throw on a
		// failed connect (instead of returning a multiplexer that retries forever in the background);
		// the bounded ConnectTimeout/SyncTimeout and minimal ConnectRetry cap the wait. When Redis IS
		// reachable these bounds are generous enough not to change behavior.
		options.AbortOnConnectFail = true;
		options.ConnectTimeout = ConnectTimeoutMilliseconds;
		options.SyncTimeout = ConnectTimeoutMilliseconds;
		options.AsyncTimeout = ConnectTimeoutMilliseconds;
		options.ConnectRetry = 1;
		return options;
	}

	private static string GetRequiredPart(IReadOnlyDictionary<string, string> parts, string key) {
		if (!parts.TryGetValue(key, out string? value) || string.IsNullOrWhiteSpace(value)) {
			throw new InvalidOperationException($"Redis connection string from ConnectionStrings.config is missing '{key}'.");
		}

		return value;
	}

	private static int GetIntPart(IReadOnlyDictionary<string, string> parts, string key, int defaultValue) {
		if (!parts.TryGetValue(key, out string? value) || !int.TryParse(value, out int parsed)) {
			return defaultValue;
		}

		return parsed;
	}
}
