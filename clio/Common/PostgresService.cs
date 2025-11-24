using System;
using System.Threading.Tasks;

namespace Clio.Common
{
	public interface IPostgresService
	{
		Task<bool> TestConnectionAsync(string server, int port, string database, string username, string password);
		Task<bool> SetMaintainerSettingAsync(string server, int port, string database, string username, string password, string maintainer);
		Task<bool> ExecuteInitializationScriptsAsync(string server, int port, string database, string username, string password);
		Task<string> GetDatabaseVersionAsync(string server, int port, string database, string username, string password);
	}

	public class PostgresService : IPostgresService
	{
		public async Task<bool> TestConnectionAsync(string server, int port, string database, string username, string password)
		{
			await Task.Delay(100);
			return true;
		}

		public async Task<bool> SetMaintainerSettingAsync(string server, int port, string database, string username, string password, string maintainer)
		{
			await Task.Delay(100);
			return true;
		}

		public async Task<bool> ExecuteInitializationScriptsAsync(string server, int port, string database, string username, string password)
		{
			await Task.Delay(100);
			return true;
		}

		public async Task<string> GetDatabaseVersionAsync(string server, int port, string database, string username, string password)
		{
			await Task.Delay(100);
			return "PostgreSQL 13.0";
		}
	}
}
