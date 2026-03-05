namespace Clio.Common.db;

/// <summary>
/// Represents local Redis server settings defined in appsettings.json.
/// </summary>
public class LocalRedisServerConfiguration
{
	/// <summary>
	/// Gets or sets Redis host name or IP address.
	/// </summary>
	public string Hostname { get; set; }

	/// <summary>
	/// Gets or sets Redis server port.
	/// </summary>
	public int Port { get; set; } = 6379;

	/// <summary>
	/// Gets or sets Redis ACL username.
	/// </summary>
	public string Username { get; set; }

	/// <summary>
	/// Gets or sets Redis password.
	/// </summary>
	public string Password { get; set; }

	/// <summary>
	/// Gets or sets optional user-facing description.
	/// </summary>
	public string Description { get; set; }

	/// <summary>
	/// Controls whether this Redis server configuration is active for clio operations.
	/// Missing value in appsettings is treated as enabled for backward compatibility.
	/// </summary>
	public bool Enabled { get; set; } = true;
}
