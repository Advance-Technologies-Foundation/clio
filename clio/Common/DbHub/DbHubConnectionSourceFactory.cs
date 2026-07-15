using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace Clio.Common.DbHub;

/// <summary>Discovers a dbHub source from a local Creatio environment.</summary>
public interface IDbHubConnectionSourceFactory {
	/// <summary>Reads the authoritative connection configuration and creates a source.</summary>
	/// <param name="environmentName">Clio environment key.</param>
	/// <param name="environment">Registered environment settings.</param>
	/// <returns>A source or a safe skip warning.</returns>
	DbHubSourceDiscoveryResult Create(string environmentName, EnvironmentSettings environment);
}

/// <inheritdoc />
public sealed class DbHubConnectionSourceFactory : IDbHubConnectionSourceFactory {
	private const string MissingConfigCode = "DBHUB_CONNECTION_CONFIG_UNAVAILABLE";
	private const string UnsupportedAuthenticationCode = "DBHUB_SQL_AUTH_UNSUPPORTED";
	private const string UnsupportedTlsCode = "DBHUB_SQL_TLS_UNSUPPORTED";

	/// <inheritdoc />
	public DbHubSourceDiscoveryResult Create(string environmentName, EnvironmentSettings environment) {
		if (string.IsNullOrWhiteSpace(environment?.EnvironmentPath)) {
			return Skip(environmentName, "The environment is not a local clio deployment.", MissingConfigCode);
		}

		string path = Path.Combine(environment.EnvironmentPath, "ConnectionStrings.config");
		if (!File.Exists(path)) {
			return Skip(environmentName, "ConnectionStrings.config is unavailable.", MissingConfigCode);
		}

		try {
			(string name, string connectionString) = ReadConnectionString(path);
			return string.Equals(name, "dbPostgreSql", StringComparison.OrdinalIgnoreCase)
				? CreatePostgres(environmentName, connectionString)
				: CreateSqlServer(environmentName, connectionString);
		}
		catch (XmlException) {
			return Skip(environmentName, "ConnectionStrings.config is not valid XML.", MissingConfigCode);
		}
		catch (ArgumentException) {
			return Skip(environmentName, "The database connection configuration is not valid.", MissingConfigCode);
		}
		catch (IOException) {
			return Skip(environmentName, "ConnectionStrings.config could not be read.", MissingConfigCode);
		}
		catch (UnauthorizedAccessException) {
			return Skip(environmentName, "ConnectionStrings.config could not be read.", MissingConfigCode);
		}
	}

	/// <summary>Normalizes a clio environment key into a dbHub-safe source id.</summary>
	public static string NormalizeSourceId(string environmentName) {
		string value = environmentName?.Trim().ToLowerInvariant() ?? string.Empty;
		StringBuilder result = new(value.Length);
		bool previousUnderscore = false;
		foreach (char character in value) {
			bool isAsciiLetter = character is >= 'a' and <= 'z';
			bool isDigit = character is >= '0' and <= '9';
			if (isAsciiLetter || isDigit) {
				result.Append(character);
				previousUnderscore = false;
			} else if (!previousUnderscore && result.Length > 0) {
				result.Append('_');
				previousUnderscore = true;
			}
		}
		string normalized = result.ToString().Trim('_');
		if (!string.IsNullOrEmpty(normalized)) {
			return normalized;
		}
		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(environmentName ?? string.Empty));
		return $"env_{Convert.ToHexString(hash.AsSpan(0, 4)).ToLowerInvariant()}";
	}

	private static (string Name, string ConnectionString) ReadConnectionString(string path) {
		XmlReaderSettings settings = new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
		using XmlReader reader = XmlReader.Create(path, settings);
		XDocument document = XDocument.Load(reader, LoadOptions.None);
		XElement postgres = document.Root?.Elements("add").FirstOrDefault(item =>
			string.Equals(item.Attribute("name")?.Value, "dbPostgreSql", StringComparison.OrdinalIgnoreCase));
		XElement database = postgres ?? document.Root?.Elements("add").FirstOrDefault(item =>
			string.Equals(item.Attribute("name")?.Value, "db", StringComparison.OrdinalIgnoreCase));
		string connectionString = database?.Attribute("connectionString")?.Value;
		if (string.IsNullOrWhiteSpace(connectionString)) {
			throw new ArgumentException("Missing database connection string.");
		}
		return (database.Attribute("name")?.Value, connectionString);
	}

	private static DbHubSourceDiscoveryResult CreatePostgres(string environmentName, string connectionString) {
		NpgsqlConnectionStringBuilder builder = new(connectionString);
		DbHubSourceDefinition source = new(environmentName, NormalizeSourceId(environmentName), "postgres",
			builder.Host, builder.Port, builder.Database, builder.Username, builder.Password,
			SslMode: ToDbHubSslMode(builder.SslMode), SslRootCertificate: builder.RootCertificate);
		return new DbHubSourceDiscoveryResult(source);
	}

	private static DbHubSourceDiscoveryResult CreateSqlServer(string environmentName, string connectionString) {
		SqlConnectionStringBuilder builder = new(connectionString);
		if (builder.IntegratedSecurity || builder.Authentication != SqlAuthenticationMethod.NotSpecified
			|| string.IsNullOrWhiteSpace(builder.UserID)) {
			return Skip(environmentName,
				"The configured SQL Server authentication is not supported by clio's dbHub integration; use SQL authentication.",
				UnsupportedAuthenticationCode);
		}
		if (builder.Encrypt != SqlConnectionEncryptOption.Optional
			&& (builder.Encrypt != SqlConnectionEncryptOption.Mandatory || !builder.TrustServerCertificate)) {
			return Skip(environmentName,
				"SQL Server certificate validation cannot be represented safely by dbHub 0.23.0.",
				UnsupportedTlsCode);
		}

		(string host, int port, string instance) = ParseSqlDataSource(builder.DataSource);
		string sslMode = builder.Encrypt == SqlConnectionEncryptOption.Optional ? "disable" : "require";
		DbHubSourceDefinition source = new(environmentName, NormalizeSourceId(environmentName), "sqlserver",
			host, port, builder.InitialCatalog, builder.UserID, builder.Password, instance, sslMode);
		return new DbHubSourceDiscoveryResult(source);
	}

	private static string ToDbHubSslMode(SslMode sslMode) => sslMode switch {
		SslMode.Disable => "disable",
		SslMode.Allow => "disable",
		SslMode.Prefer => "require",
		SslMode.Require => "require",
		SslMode.VerifyCA => "verify-ca",
		SslMode.VerifyFull => "verify-full",
		_ => throw new ArgumentOutOfRangeException(nameof(sslMode), sslMode, null)
	};

	private static (string Host, int Port, string Instance) ParseSqlDataSource(string dataSource) {
		string value = (dataSource ?? string.Empty).Trim();
		if (value.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase)) {
			value = value[4..];
		}
		int port = 1433;
		int comma = value.LastIndexOf(',');
		if (comma > 0 && int.TryParse(value[(comma + 1)..], NumberStyles.None, CultureInfo.InvariantCulture,
			out int parsedPort)) {
			port = parsedPort;
			value = value[..comma];
		}
		string instance = null;
		int slash = value.IndexOf('\\');
		if (slash >= 0) {
			instance = value[(slash + 1)..];
			value = value[..slash];
		}
		return (value, port, instance);
	}

	private static DbHubSourceDiscoveryResult Skip(string environmentName, string detail, string errorCode) =>
		new(null, new DbHubWarning($"dbHub source '{environmentName}' was skipped.", detail, errorCode));
}
