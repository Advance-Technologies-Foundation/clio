namespace Clio.Common.db;

public interface IDbClientFactory
{

	IMssql CreateMssql(string host, int port, string username, string password);

	IMssql CreateMssql(int port, string username, string password);

	Postgres CreatePostgres(int port, string username, string password);
	
	Postgres CreatePostgresSilent(int port, string username, string password);

}

public class DbClientFactory : IDbClientFactory
{

	public IMssql CreateMssql(string host, int port, string username, string password) {
		return new Mssql(host, port, username, password);
	}
	
	public IMssql CreateMssql(int port, string username, string password) {
		return new Mssql(port, username, password);
	}
	
	//TODO: Add interface
	public Postgres CreatePostgres(int port, string username, string password) {
		return new Postgres(port, username, password);
	}
	
	/// <summary>
	/// Creates a Postgres instance with NullLogger for silent operations (connection testing)
	/// </summary>
	public Postgres CreatePostgresSilent(int port, string username, string password) {
		return new Postgres(port, username, password, NullLogger.Instance);
	}

}