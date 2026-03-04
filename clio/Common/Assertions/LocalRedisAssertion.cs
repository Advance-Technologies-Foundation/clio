using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Clio.Common.Database;

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
	/// <returns>Structured assertion result.</returns>
	Task<AssertionResult> ExecuteAsync(bool checkConnect, bool checkPing);
}

/// <summary>
/// Executes Redis assertions against local Redis endpoint.
/// </summary>
public class LocalRedisAssertion : ILocalRedisAssertion
{
	private const string Host = "localhost";
	private const int Port = 6379;
	private readonly IRedisDatabaseSelector _redisDatabaseSelector;

	/// <summary>
	/// Initializes a new instance of the <see cref="LocalRedisAssertion"/> class.
	/// </summary>
	/// <param name="redisDatabaseSelector">Redis database selector service.</param>
	public LocalRedisAssertion(IRedisDatabaseSelector redisDatabaseSelector)
	{
		_redisDatabaseSelector = redisDatabaseSelector ?? throw new ArgumentNullException(nameof(redisDatabaseSelector));
	}

	/// <inheritdoc />
	public async Task<AssertionResult> ExecuteAsync(bool checkConnect, bool checkPing)
	{
		RedisDatabaseSelectionResult discovery = _redisDatabaseSelector.FindEmptyLocalDatabase();
		if (!discovery.Success)
		{
			AssertionResult discoveryFailure = AssertionResult.Failure(
				AssertionScope.Local,
				AssertionPhase.RedisDiscovery,
				discovery.ErrorMessage ?? $"Redis not available at {Host}:{Port}");
			discoveryFailure.Details["host"] = Host;
			discoveryFailure.Details["port"] = Port;
			return discoveryFailure;
		}

		if (checkConnect)
		{
			bool isConnectable = await CheckConnectivityAsync(Host, Port);
			if (!isConnectable)
			{
				AssertionResult connectFailure = AssertionResult.Failure(
					AssertionScope.Local,
					AssertionPhase.RedisConnect,
					$"Cannot connect to Redis at {Host}:{Port}");
				connectFailure.Details["host"] = Host;
				connectFailure.Details["port"] = Port;
				return connectFailure;
			}
		}

		if (checkPing)
		{
			bool pingSuccess = await CheckPingAsync(Host, Port);
			if (!pingSuccess)
			{
				AssertionResult pingFailure = AssertionResult.Failure(
					AssertionScope.Local,
					AssertionPhase.RedisPing,
					$"Redis PING command failed at {Host}:{Port}");
				pingFailure.Details["host"] = Host;
				pingFailure.Details["port"] = Port;
				return pingFailure;
			}
		}

		AssertionResult successResult = AssertionResult.Success();
		successResult.Scope = AssertionScope.Local;
		successResult.Resolved["redis"] = new
		{
			name = "local-redis",
			host = Host,
			port = Port,
			db = discovery.DatabaseNumber
		};
		return successResult;
	}

	private static async Task<bool> CheckConnectivityAsync(string host, int port, int timeoutSeconds = 10)
	{
		try
		{
			using TcpClient client = new();
			Task connectTask = client.ConnectAsync(host, port);
			Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));
			Task completedTask = await Task.WhenAny(connectTask, timeoutTask);

			return completedTask != timeoutTask && !connectTask.IsFaulted && client.Connected;
		}
		catch
		{
			return false;
		}
	}

	private static async Task<bool> CheckPingAsync(string host, int port, int timeoutSeconds = 10)
	{
		try
		{
			using TcpClient client = new();
			Task connectTask = client.ConnectAsync(host, port);
			Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));
			Task completedTask = await Task.WhenAny(connectTask, timeoutTask);
			if (completedTask == timeoutTask)
			{
				return false;
			}

			using NetworkStream stream = client.GetStream();
			byte[] pingCommand = Encoding.UTF8.GetBytes("PING\r\n");
			await stream.WriteAsync(pingCommand, 0, pingCommand.Length);
			byte[] buffer = new byte[1024];
			int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
			string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);
			return response.Contains("PONG", StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
	}
}
