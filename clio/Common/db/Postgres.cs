using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Npgsql;

namespace Clio.Common.db;


public interface IPostgres
{
	void Init(string host, int port, string username, string password);
	bool CreateDbFromTemplate(string templateName, string dbName);
	bool CreateDb(string dbName);
	bool SetDatabaseAsTemplate(string dbName);
	bool CheckTemplateExists(string templateName);
	bool CheckDbExists(string dbName);
	bool DropDb(string dbName);
	bool SetDatabaseComment(string dbName, string comment);
	string GetDatabaseComment(string dbName);
	string FindTemplateBySourceFile(string sourceFileName);

	/// <summary>
	/// Returns all clio-managed PostgreSQL templates.
	/// </summary>
	/// <returns>Templates whose shared database comment contains valid clio metadata.</returns>
	IReadOnlyList<PostgresManagedTemplate> GetManagedTemplates();

	/// <summary>Returns one exact clio-managed PostgreSQL template when it remains eligible.</summary>
	/// <param name="databaseName">Exact database name selected by the caller.</param>
	/// <returns>The canonical template metadata, or <c>null</c> when the database is not eligible.</returns>
	PostgresManagedTemplate GetManagedTemplate(string databaseName);

	/// <summary>Counts active sessions connected to the named database.</summary>
	/// <param name="databaseName">Canonical database name returned by <see cref="GetManagedTemplates"/>.</param>
	/// <returns>The number of active PostgreSQL sessions.</returns>
	long CountActiveSessions(string databaseName);

	/// <summary>Sets or clears the PostgreSQL template flag using <c>ALTER DATABASE</c>.</summary>
	/// <param name="databaseName">Canonical database name returned by <see cref="GetManagedTemplates"/>.</param>
	/// <param name="isTemplate">The template-flag value to apply.</param>
	void SetTemplateFlag(string databaseName, bool isTemplate);

	/// <summary>Determines whether the named database still exists.</summary>
	/// <param name="databaseName">Canonical database name.</param>
	/// <returns><c>true</c> when the database exists; otherwise <c>false</c>.</returns>
	bool DatabaseExists(string databaseName);

	/// <summary>Drops one database without terminating connections or forcing the operation.</summary>
	/// <param name="databaseName">Canonical database name returned by deletion-time revalidation.</param>
	void DropDatabaseWithoutForce(string databaseName);
}

/// <summary>Metadata identifying a PostgreSQL template created and managed by clio.</summary>
/// <param name="DatabaseName">Generated PostgreSQL database name.</param>
/// <param name="SourceFile">Creatio distribution or backup source identifier.</param>
/// <param name="CreatedDate">UTC timestamp recorded when the template was created.</param>
/// <param name="MetadataVersion">Version of the clio database-comment metadata format.</param>
public sealed record PostgresManagedTemplate(
	string DatabaseName,
	string SourceFile,
	DateTimeOffset CreatedDate,
	string MetadataVersion);

public class Postgres : IPostgres
{

	private string _connectionString;
	private readonly ILogger _logger;

	public Postgres() {
		_logger = ConsoleLogger.Instance;
	}

	public Postgres(ILogger logger) {
		_logger = logger ?? ConsoleLogger.Instance;
	}
	
	public Postgres(int port, string username, string password, ILogger logger = null) {
		_connectionString = BuildConnectionString(BindingsModule.k8sDns, port, username, password);
		_logger = logger ?? ConsoleLogger.Instance;
	}
	
	public void Init(string host, int port, string username, string password)
	{
		_connectionString = BuildConnectionString(host, port, username, password);
	}

	internal static string BuildConnectionString(string host, int port, string username, string password) =>
		new NpgsqlConnectionStringBuilder {
			Host = host,
			Port = port,
			Username = username,
			Password = password,
			Database = "postgres"
		}.ConnectionString;
	
	public virtual bool CreateDbFromTemplate (string templateName, string dbName) {
		//_logger.WriteInfo($"Creating database '{dbName}' from template '{templateName}'");
		bool dbExists = CheckDbExists(dbName);
		_logger.WriteInfo($"Database '{dbName}' exists: {dbExists}");
		if (dbExists) {
			_logger.WriteWarning($"Dropping existing database '{dbName}'");
			DropDb(dbName);
			_logger.WriteWarning($"Dropped existing database '{dbName}'");
		}
		try {
			_logger.WriteInfo($"Creating database '{dbName}' from template '{templateName}'");
			using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(_connectionString);
			using NpgsqlConnection cnn = dataSource.OpenConnection();
			
			string killSqlConnections = @$"
			SELECT pg_terminate_backend(pg_stat_activity.pid)
			FROM pg_stat_activity
			WHERE pg_stat_activity.datname = '{templateName}'
			";
			using NpgsqlCommand killConnectionCmd = dataSource.CreateCommand(killSqlConnections);
			killConnectionCmd.ExecuteNonQuery();
			
			using NpgsqlCommand cmd = dataSource.CreateCommand($"CREATE DATABASE \"{dbName}\" TEMPLATE=\"{templateName}\" ENCODING UTF8 CONNECTION LIMIT -1");
			cmd.CommandTimeout = 600; // 10 minutes
			cmd.ExecuteNonQuery();
			cnn.Close();
			return true;
		} catch (Exception e)  when (e is PostgresException pe){
			_logger.WriteError($"[{pe.Severity}] - {pe.MessageText}");
			return false;
		}
		catch(Exception e) when (e is NpgsqlException ne) {
			_logger.WriteError(ne.Message + ": " + ne.InnerException?.Message);
			return false;
		}
		catch(Exception e) {
			_logger.WriteError(e.Message);
			return false;
		}
	}
	
	public virtual bool CreateDb (string dbName) {
		
		try {
			using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(_connectionString);
			using NpgsqlConnection cnn = dataSource.OpenConnection();
			using NpgsqlCommand cmd = dataSource.CreateCommand($"CREATE DATABASE \"{dbName}\" ENCODING UTF8 CONNECTION LIMIT -1");
			cmd.ExecuteNonQuery();
			cnn.Close();
			return true;
		} catch (Exception e)  when (e is PostgresException pe){
			_logger.WriteError($"[{pe.Severity}] - {pe.MessageText}");
			return false; // 3 minutes should be enough time to restore from template
		}
		catch(Exception e) when (e is NpgsqlException ne) {
			_logger.WriteError(ne.Message + ": " + ne.InnerException?.Message);
			return false;
		}
		catch(Exception e) {
			_logger.WriteError(e.Message);
			return false;
		}
	}
	
	public virtual bool SetDatabaseAsTemplate( string dbName) {
		try {
			
			using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(_connectionString);
			using NpgsqlConnection cnn = dataSource.OpenConnection();
			using NpgsqlCommand cmd = dataSource.CreateCommand($"UPDATE pg_database SET datistemplate='true' WHERE datname='{dbName}'");
			cmd.ExecuteNonQuery();
			cnn.Close();
			return true;
		} catch (Exception e)  when (e is PostgresException pe){
			_logger.WriteError($"[{pe.Severity}] - {pe.MessageText}");
			return false;
		}
		catch(Exception e) when (e is NpgsqlException ne) {
			_logger.WriteError(ne.Message + ": " + ne.InnerException?.Message);
			return false;
		}
		catch(Exception e) {
			_logger.WriteError(e.Message);
			return false;
		}
	}
	
	public virtual bool CheckTemplateExists (string templateName) {
		try {
			string sqlText = @$"
				SELECT COUNT(datname) 
				FROM pg_catalog.pg_database d 
				WHERE datistemplate = true AND datName = '{templateName}';
			";
			
			using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(_connectionString);
			using NpgsqlConnection cnn = dataSource.OpenConnection();
			using NpgsqlCommand cmd = dataSource.CreateCommand(sqlText);
			object result = cmd.ExecuteScalar();
			cnn.Close();
			return result is long and 1;
		} catch (Exception e)  when (e is PostgresException pe){
			_logger.WriteError($"[{pe.Severity}] - {pe.MessageText}");
			return false;
		}
		catch(Exception e) when (e is NpgsqlException ne) {
			_logger.WriteError(ne.Message + ": " + ne.InnerException?.Message);
			return false;
		}
		catch(Exception e) {
			_logger.WriteError(e.Message);
			return false;
		}
	}
	
	public virtual bool CheckDbExists (string templateName) {
		try {
			string sqlText = @$"
				SELECT COUNT(datname) 
				FROM pg_catalog.pg_database d 
				WHERE datName = '{templateName}';
			";
			
			using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(_connectionString);
			using NpgsqlConnection cnn = dataSource.OpenConnection();
			using NpgsqlCommand cmd = dataSource.CreateCommand(sqlText);
			object result = cmd.ExecuteScalar();
			cnn.Close();
			return result is long and 1;
		} catch (Exception e)  when (e is PostgresException pe){
			_logger.WriteError($"[{pe.Severity}] - {pe.MessageText}");
			return false;
		}
		catch(Exception e) when (e is NpgsqlException ne) {
			_logger.WriteError(ne.Message + ": " + ne.InnerException?.Message);
			return false;
		}
		catch(Exception e) {
			_logger.WriteError(e.Message);
			return false;
		}
	}
	
	public virtual bool DropDb(string dbName){
		try {
			using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(_connectionString);
			using NpgsqlConnection cnn = dataSource.OpenConnection();

			// PostgreSQL refuses to drop databases marked as templates until the flag is cleared.
			using NpgsqlCommand clearTemplateFlagCmd = dataSource.CreateCommand(
				$"UPDATE pg_database SET datistemplate='false' WHERE datname='{dbName}'");
			clearTemplateFlagCmd.ExecuteNonQuery();
			
			string killSqlConnections = @$"
			SELECT pg_terminate_backend(pg_stat_activity.pid)
			FROM pg_stat_activity
			WHERE pg_stat_activity.datname = '{dbName}'
			";
			using NpgsqlCommand killConnectionCmd = dataSource.CreateCommand(killSqlConnections);
			killConnectionCmd.ExecuteNonQuery();
			using NpgsqlCommand cmd = dataSource.CreateCommand($"DROP DATABASE IF EXISTS \"{dbName}\";");
			cmd.ExecuteNonQuery();
			cnn.Close();
			return true;
		} catch (Exception e)  when (e is PostgresException pe){
			_logger.WriteError($"[{pe.Severity}] - {pe.MessageText}");
			return false;
		}
		catch(Exception e) when (e is NpgsqlException ne) {
			_logger.WriteError(ne.Message + ": " + ne.InnerException?.Message);
			return false;
		}
		catch(Exception e) {
			_logger.WriteError(e.Message);
			return false;
		}
	}
	
	public virtual bool SetDatabaseComment(string dbName, string comment){
		try {
			using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(_connectionString);
			using NpgsqlConnection cnn = dataSource.OpenConnection();
			string escapedComment = comment.Replace("'", "''");
			using NpgsqlCommand cmd = dataSource.CreateCommand($"COMMENT ON DATABASE \"{dbName}\" IS '{escapedComment}'");
			cmd.ExecuteNonQuery();
			cnn.Close();
			return true;
		} catch (Exception e)  when (e is PostgresException pe){
			_logger.WriteError($"[{pe.Severity}] - {pe.MessageText}");
			return false;
		}
		catch(Exception e) when (e is NpgsqlException ne) {
			_logger.WriteError(ne.Message + ": " + ne.InnerException?.Message);
			return false;
		}
		catch(Exception e) {
			_logger.WriteError(e.Message);
			return false;
		}
	}
	
	public virtual string GetDatabaseComment(string dbName){
		try {
			string sqlText = @$"
				SELECT obj_description(oid, 'pg_database') 
				FROM pg_database 
				WHERE datname = '{dbName}'
			";
			
			using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(_connectionString);
			using NpgsqlConnection cnn = dataSource.OpenConnection();
			using NpgsqlCommand cmd = dataSource.CreateCommand(sqlText);
			object result = cmd.ExecuteScalar();
			cnn.Close();
			return result?.ToString();
		} catch (Exception e)  when (e is PostgresException pe){
			_logger.WriteError($"[{pe.Severity}] - {pe.MessageText}");
			return null;
		}
		catch(Exception e) when (e is NpgsqlException ne) {
			_logger.WriteError(ne.Message + ": " + ne.InnerException?.Message);
			return null;
		}
		catch(Exception e) {
			_logger.WriteError(e.Message);
			return null;
		}
	}
	
	public virtual string FindTemplateBySourceFile(string sourceFileName){
		try {
			string sqlText = @$"
				SELECT datname 
				FROM pg_database 
				WHERE datistemplate = true 
				  AND shobj_description(oid, 'pg_database') LIKE '%sourceFile:{sourceFileName}%'
				LIMIT 1
			";
			
			using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(_connectionString);
			using NpgsqlConnection cnn = dataSource.OpenConnection();
			using NpgsqlCommand cmd = dataSource.CreateCommand(sqlText);
			object result = cmd.ExecuteScalar();
			cnn.Close();
			
			if (result != null && result != DBNull.Value) {
				return result.ToString();
			}
			
			// Backward compatibility: try old naming pattern
			string oldStyleTemplateName = $"template_{sourceFileName}";
			if (CheckTemplateExists(oldStyleTemplateName)) {
				return oldStyleTemplateName;
			}
			
			return null;
		} catch (Exception e)  when (e is PostgresException pe){
			_logger.WriteError($"[{pe.Severity}] - {pe.MessageText}");
			return null;
		}
		catch(Exception e) when (e is NpgsqlException ne) {
			_logger.WriteError(ne.Message + ": " + ne.InnerException?.Message);
			return null;
		}
		catch(Exception e) {
			_logger.WriteError(e.Message);
			return null;
		}
	}

	/// <inheritdoc />
	public virtual IReadOnlyList<PostgresManagedTemplate> GetManagedTemplates() => QueryManagedTemplates();

	/// <inheritdoc />
	public virtual PostgresManagedTemplate GetManagedTemplate(string databaseName) {
		ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
		return QueryManagedTemplates(databaseName).SingleOrDefault();
	}

	private IReadOnlyList<PostgresManagedTemplate> QueryManagedTemplates(string databaseName = null) {
		string databaseFilter = string.IsNullOrWhiteSpace(databaseName)
			? string.Empty
			: "AND d.datname = @databaseName";
		string sqlText = $"""
			SELECT d.datname, shobj_description(d.oid, 'pg_database')
			FROM pg_catalog.pg_database d
			WHERE d.datistemplate = true
			  AND d.datname NOT IN ('template0', 'template1')
			  {databaseFilter}
			ORDER BY d.datname;
			""";

		using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(_connectionString);
		using NpgsqlCommand command = dataSource.CreateCommand(sqlText);
		if (!string.IsNullOrWhiteSpace(databaseName)) {
			command.Parameters.AddWithValue("databaseName", databaseName);
		}

		using NpgsqlDataReader reader = command.ExecuteReader();
		List<PostgresManagedTemplate> templates = [];
		while (reader.Read()) {
			string canonicalName = reader.GetString(0);
			string metadata = reader.IsDBNull(1) ? null : reader.GetString(1);
			if (TryParseManagedTemplateMetadata(metadata, out string sourceFile,
				out DateTimeOffset createdDate, out string metadataVersion)) {
				templates.Add(new PostgresManagedTemplate(canonicalName, sourceFile, createdDate, metadataVersion));
			}
		}
		return templates;
	}

	/// <inheritdoc />
	public virtual long CountActiveSessions(string databaseName) {
		const string sqlText = """
			SELECT COUNT(*)
			FROM pg_catalog.pg_stat_activity
			WHERE datname = @databaseName;
			""";
		using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(_connectionString);
		using NpgsqlCommand command = dataSource.CreateCommand(sqlText);
		command.Parameters.AddWithValue("databaseName", databaseName);
		return (long)command.ExecuteScalar();
	}

	/// <inheritdoc />
	public virtual void SetTemplateFlag(string databaseName, bool isTemplate) {
		string quotedName = QuoteIdentifier(databaseName);
		using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(_connectionString);
		using NpgsqlCommand command = dataSource.CreateCommand(
			$"ALTER DATABASE {quotedName} IS_TEMPLATE {isTemplate.ToString().ToLowerInvariant()};");
		command.ExecuteNonQuery();
	}

	/// <inheritdoc />
	public virtual bool DatabaseExists(string databaseName) {
		const string sqlText = """
			SELECT EXISTS(
				SELECT 1
				FROM pg_catalog.pg_database
				WHERE datname = @databaseName);
			""";
		using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(_connectionString);
		using NpgsqlCommand command = dataSource.CreateCommand(sqlText);
		command.Parameters.AddWithValue("databaseName", databaseName);
		return (bool)command.ExecuteScalar();
	}

	/// <inheritdoc />
	public virtual void DropDatabaseWithoutForce(string databaseName) {
		string quotedName = QuoteIdentifier(databaseName);
		using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(_connectionString);
		using NpgsqlCommand command = dataSource.CreateCommand($"DROP DATABASE {quotedName};");
		command.CommandTimeout = 30;
		command.ExecuteNonQuery();
	}

	internal static string QuoteIdentifier(string identifier) {
		ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
		return $"\"{identifier.Replace("\"", "\"\"")}\"";
	}

	internal static bool TryParseManagedTemplateMetadata(string metadata, out string sourceFile,
		out DateTimeOffset createdDate, out string metadataVersion) {
		sourceFile = null;
		createdDate = default;
		metadataVersion = null;
		if (string.IsNullOrWhiteSpace(metadata)) {
			return false;
		}

		Dictionary<string, string> values = new(StringComparer.Ordinal);
		foreach (string part in metadata.Split('|', StringSplitOptions.RemoveEmptyEntries)) {
			int separatorIndex = part.IndexOf(':');
			if (separatorIndex <= 0 || separatorIndex == part.Length - 1) {
				return false;
			}
			string key = part[..separatorIndex];
			string value = part[(separatorIndex + 1)..];
			if (!values.TryAdd(key, value)) {
				return false;
			}
		}

		return values.TryGetValue("sourceFile", out sourceFile)
			&& !string.IsNullOrWhiteSpace(sourceFile)
			&& values.TryGetValue("createdDate", out string createdDateText)
			&& DateTimeOffset.TryParse(createdDateText, CultureInfo.InvariantCulture,
				DateTimeStyles.RoundtripKind, out createdDate)
			&& values.TryGetValue("version", out metadataVersion)
			&& !string.IsNullOrWhiteSpace(metadataVersion);
	}
}
