using System;
using System.Data;
using System.Threading.Tasks;
using Clio.Common.Kubernetes;
using Npgsql;
using Microsoft.Data.SqlClient;

namespace Clio.Common.Database
{
	/// <summary>
	/// Checks database capabilities like version.
	/// </summary>
	public class DatabaseCapabilityChecker : IDatabaseCapabilityChecker
	{
		public async Task<CapabilityCheckResult> CheckVersionAsync(DiscoveredDatabase database, string connectionString)
		{
			try
			{
				return database.Engine switch
				{
					DatabaseEngine.Postgres => await CheckPostgresVersionAsync(connectionString),
					DatabaseEngine.Mssql => await CheckMssqlVersionAsync(connectionString),
					_ => new CapabilityCheckResult
					{
						Success = false,
						Error = $"Unknown database engine: {database.Engine}"
					}
				};
			}
			catch (Exception ex)
			{
				return new CapabilityCheckResult
				{
					Success = false,
					Error = ex.Message
				};
			}
		}

		private async Task<CapabilityCheckResult> CheckPostgresVersionAsync(string connectionString)
		{
			try
			{
				using var connection = new NpgsqlConnection(connectionString);
				await connection.OpenAsync();

				using var command = new NpgsqlCommand("SELECT version()", connection);
				var version = await command.ExecuteScalarAsync();

				return new CapabilityCheckResult
				{
					Success = true,
					Version = version?.ToString()
				};
			}
			catch (Exception ex)
			{
				return new CapabilityCheckResult
				{
					Success = false,
					Error = ex.Message
				};
			}
		}

		private async Task<CapabilityCheckResult> CheckMssqlVersionAsync(string connectionString)
		{
			try
			{
				await using SqlConnection connection = new SqlConnection(connectionString);
				await connection.OpenAsync();
				const string cmdText = """
									   SELECT CONCAT(
									          CAST(SERVERPROPERTY('Edition') AS VARCHAR(MAX))
									          ,' - ',
									          CAST(SERVERPROPERTY('ProductVersion') AS VARCHAR(MAX))
									   ) AS Version
									   """;
				
				await using SqlCommand command = new (cmdText, connection);
				object version = await command.ExecuteScalarAsync();

				return new CapabilityCheckResult {
					Success = true,
					Version = version?.ToString()
				};
			}
			catch (Exception ex) {
				return new CapabilityCheckResult {
					Success = false,
					Error = ex.Message
				};
			}
		}
	}
}
