namespace Clio.Common.db;

public interface IDbClientFactory
{

	IMssql CreateMssql(string host, int port, string username, string password, bool isWindowsAuth = false);

	IMssql CreateMssql(int port, string username, string password);

	Postgres CreatePostgres(string host, int port, string username, string password, bool isWindowsAuth = false);

	Postgres CreatePostgres(int port, string username, string password);

	Postgres CreatePostgresSilent(string host, int port, string username, string password, bool isWindowsAuth = false);
	
	Postgres CreatePostgresSilent(int port, string username, string password, bool isWindowsAuth = false);

}

public class DbClientFactory : IDbClientFactory
{

	public IMssql CreateMssql(string host, int port, string username, string password, bool isWindowsAuth = false) {
		ConsoleLogger.Instance.WriteLine($"Connecting to MSSQL server {host}:{port} using {(isWindowsAuth ? "Windows Authentication" : $"SQL Authentication with user '{username}'")}");
		return new Mssql(host, port, username, password, isWindowsAuth);
	}
	
	public IMssql CreateMssql(int port, string username, string password) {
		return new Mssql(port, username, password);
	}

	public Postgres CreatePostgres(string host, int port, string username, string password, bool isWindowsAuth = false) {
		Postgres postgres = new Postgres();
		postgres.Init(host, port, username, password, isWindowsAuth);
		return postgres;
	}
	
	public Postgres CreatePostgres(int port, string username, string password) {
		return new Postgres(port, username, password);
	}

	public Postgres CreatePostgresSilent(string host, int port, string username, string password, bool isWindowsAuth = false) {
		Postgres postgres = new Postgres(NullLogger.Instance);
		postgres.Init(host, port, username, password, isWindowsAuth);
		return postgres;
	}
	
	/// <summary>
	/// Creates a Postgres instance with NullLogger for silent operations (connection testing)
	/// </summary>
	public Postgres CreatePostgresSilent(int port, string username, string password, bool isWindowsAuth = false) {
		return new Postgres(port, username, password, NullLogger.Instance);
	}

}