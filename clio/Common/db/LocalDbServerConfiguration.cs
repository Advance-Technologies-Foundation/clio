using System.Collections.Generic;

namespace Clio.Common.db;

public class LocalDbServerConfiguration
{
	public string DbType { get; set; }
	public string Hostname { get; set; }
	public int Port { get; set; }
	public string Username { get; set; }
	public string Password { get; set; }
	public bool UseWindowsAuth { get; set; }
	public string Description { get; set; }
	public string PgToolsPath { get; set; }

	/// <summary>
	/// Controls whether this local DB server configuration is active for clio operations.
	/// Missing value in appsettings is treated as enabled for backward compatibility.
	/// </summary>
	public bool Enabled { get; set; } = true;
}

public class DbServersConfiguration
{
	public Dictionary<string, LocalDbServerConfiguration> Db { get; set; } = new();
}
