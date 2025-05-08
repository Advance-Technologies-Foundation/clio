namespace Clio.Common.db;

public interface IDbClientFactory
{

    #region Methods: Public

    IMssql CreateMssql(string host, int port, string username, string password);

    IMssql CreateMssql(int port, string username, string password);

    Postgres CreatePostgres(int port, string username, string password);

    #endregion

}

public class DbClientFactory : IDbClientFactory
{

    #region Methods: Public

    public IMssql CreateMssql(string host, int port, string username, string password)
    {
        return new Mssql(host, port, username, password);
    }

    public IMssql CreateMssql(int port, string username, string password)
    {
        return new Mssql(port, username, password);
    }

    //TODO: Add interface
    public Postgres CreatePostgres(int port, string username, string password)
    {
        return new Postgres(port, username, password);
    }

    #endregion

}
