using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using Clio.Common.Kubernetes;

namespace Clio.Common.Database
{
	/// <summary>
	/// Checks database connectivity via TCP connection.
	/// </summary>
	public class DatabaseConnectivityChecker : IDatabaseConnectivityChecker
	{
		public async Task<bool> CheckConnectivityAsync(DiscoveredDatabase database, int timeoutSeconds = 10)
		{
			try
			{
				using var client = new TcpClient();
				var connectTask = client.ConnectAsync(database.Host, database.Port);
				var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));

				var completedTask = await Task.WhenAny(connectTask, timeoutTask);

				if (completedTask == timeoutTask)
				{
					return false; // Timeout
				}

				if (connectTask.IsFaulted)
				{
					return false;
				}

				return client.Connected;
			}
			catch
			{
				return false;
			}
		}
	}
}
