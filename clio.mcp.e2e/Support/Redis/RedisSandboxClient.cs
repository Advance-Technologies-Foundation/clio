using StackExchange.Redis;

namespace Clio.Mcp.E2E.Support.Redis;

internal sealed class RedisSandboxClient : IAsyncDisposable {
	private readonly ConnectionMultiplexer _connection;

	private RedisSandboxClient(ConnectionMultiplexer connection, IDatabase database) {
		_connection = connection;
		Database = database;
	}

	public IDatabase Database { get; }

	public static async Task<RedisSandboxClient> ConnectAsync(string connectionString) {
		ConfigurationOptions options = BuildConfigurationOptions(connectionString);
		ConnectionMultiplexer connection = await ConnectionMultiplexer.ConnectAsync(options);
		return new RedisSandboxClient(connection, connection.GetDatabase(options.DefaultDatabase ?? -1));
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

	private static ConfigurationOptions BuildConfigurationOptions(string connectionString) {
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

		ConfigurationOptions options = new() {
			AbortOnConnectFail = false
		};
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
