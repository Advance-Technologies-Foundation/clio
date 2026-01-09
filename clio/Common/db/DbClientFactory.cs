namespace Clio.Common.db;

public interface IDbClientFactory
{

	IMssql CreateMssql(string host, int port, string username, string password);

	IMssql CreateMssql(int port, string username, string password);

	Postgres CreatePostgres(string host, int port, string username, string password);

	Postgres CreatePostgres(int port, string username, string password);

	Postgres CreatePostgresSilent(string host, int port, string username, string password);
	
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

	public Postgres CreatePostgres(string host, int port, string username, string password) {
		Postgres postgres = new Postgres();
		postgres.Init(host, port, username, password);
		return postgres;
	}
	
	public Postgres CreatePostgres(int port, string username, string password) {
		return new Postgres(port, username, password);
	}

	public Postgres CreatePostgresSilent(string host, int port, string username, string password) {
		Postgres postgres = new Postgres(NullLogger.Instance);
		postgres.Init(host, port, username, password);
		return postgres;
	}
	
	/// <summary>
	/// Creates a Postgres instance with NullLogger for silent operations (connection testing)
	/// </summary>
	public Postgres CreatePostgresSilent(int port, string username, string password) {
		return new Postgres(port, username, password, NullLogger.Instance);
	}

}