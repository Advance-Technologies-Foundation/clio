using System;
using System.IO;
using Clio.Common.DbHub;
using Clio.Tests.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common.DbHub;

[TestFixture]
[Property("Module", "Common")]
public sealed class DbHubConnectionSourceFactoryTests : BaseClioModuleTests {
	private string _directory;
	private IDbHubConnectionSourceFactory _sut;

	public override void Setup() {
		base.Setup();
		_directory = Path.Combine(Path.GetTempPath(), $"clio-dbhub-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_directory);
		_sut = Container.GetRequiredService<IDbHubConnectionSourceFactory>();
	}

	public override void TearDown() {
		if (Directory.Exists(_directory)) {
			Directory.Delete(_directory, recursive: true);
		}
		base.TearDown();
	}

	[Test]
	[Description("Reads a PostgreSQL source from the local Creatio connection configuration.")]
	public void Create_ShouldReadPostgresSource() {
		// Arrange
		WriteConfig("dbPostgreSql", "Host=pg.local;Port=5544;Database=creatio;Username=app;Password=secret");

		// Act
		DbHubSourceDiscoveryResult result = _sut.Create("My Local", new EnvironmentSettings {
			EnvironmentPath = _directory
		});

		// Assert
		result.Success.Should().BeTrue(because: "a valid local PostgreSQL configuration is eligible");
		result.Source.Should().BeEquivalentTo(new DbHubSourceDefinition("My Local", "my_local", "postgres",
			"pg.local", 5544, "creatio", "app", "secret", SslMode: "require"),
			because: "dbHub must receive the PostgreSQL fields and a TLS mode accepted by dbHub 0.23");
	}

	[Test]
	[Description("Maps Npgsql Allow to dbHub disable because dbHub 0.23 has no opportunistic Allow token.")]
	public void Create_ShouldMapPostgresAllowToDisable_WhenDbHubCannotRepresentOpportunisticTls() {
		// Arrange
		WriteConfig("dbPostgreSql",
			"Host=pg.local;Database=creatio;Username=app;Password=secret;SSL Mode=Allow");

		// Act
		DbHubSourceDiscoveryResult result = _sut.Create("Allow TLS", new EnvironmentSettings {
			EnvironmentPath = _directory
		});

		// Assert
		result.Source.SslMode.Should().Be("disable",
			because: "dbHub 0.23 accepts only disable, require, verify-ca, and verify-full");
	}

	[Test]
	[Description("Preserves PostgreSQL certificate verification settings in the dbHub source.")]
	public void Create_ShouldPreservePostgresTlsSettings() {
		// Arrange
		WriteConfig("dbPostgreSql",
			"Host=pg.local;Database=creatio;Username=app;Password=secret;SSL Mode=VerifyFull;Root Certificate=C:\\certs\\ca.pem");

		// Act
		DbHubSourceDiscoveryResult result = _sut.Create("TLS", new EnvironmentSettings {
			EnvironmentPath = _directory
		});

		// Assert
		result.Source.SslMode.Should().Be("verify-full",
			because: "dbHub must retain PostgreSQL peer-verification semantics");
		result.Source.SslRootCertificate.Should().Be("C:\\certs\\ca.pem",
			because: "the configured trust root is part of the authoritative connection settings");
	}

	[Test]
	[Description("Reads a SQL Server source including its named instance and explicit port.")]
	public void Create_ShouldReadSqlServerSource() {
		// Arrange
		WriteConfig("db", "Data Source=tcp:sql.local\\SQLEXPRESS,1444;Initial Catalog=creatio;User ID=sa;Password=secret;TrustServerCertificate=true");

		// Act
		DbHubSourceDiscoveryResult result = _sut.Create("SQL Dev", new EnvironmentSettings {
			EnvironmentPath = _directory
		});

		// Assert
		result.Success.Should().BeTrue(because: "SQL authentication is supported by dbHub");
		result.Source.Should().BeEquivalentTo(new DbHubSourceDefinition("SQL Dev", "sql_dev", "sqlserver",
			"sql.local", 1444, "creatio", "sa", "secret", "SQLEXPRESS", "require"),
			because: "instance and port semantics must survive conversion");
	}

	[TestCase("Encrypt=true;TrustServerCertificate=false")]
	[TestCase("Encrypt=strict;TrustServerCertificate=false")]
	[Description("Skips SQL Server TLS validation that dbHub cannot represent without weakening it.")]
	public void Create_ShouldSkipUnsupportedSqlCertificateValidation(string tlsSettings) {
		// Arrange
		WriteConfig("db",
			$"Data Source=sql.local;Initial Catalog=creatio;User ID=sa;Password=secret;{tlsSettings}");

		// Act
		DbHubSourceDiscoveryResult result = _sut.Create("Secure SQL", new EnvironmentSettings {
			EnvironmentPath = _directory
		});

		// Assert
		result.Success.Should().BeFalse(because: "dbHub 0.23.0 would disable SQL Server certificate validation");
		result.Warning.ErrorCode.Should().Be("DBHUB_SQL_TLS_UNSUPPORTED",
			because: "callers need a stable safe skip classification");
		result.Warning.Detail.Should().NotContain("sql.local",
			because: "safe diagnostics must not expose database connection details");
	}

	[Test]
	[Description("Skips SQL Server integrated authentication without exposing connection details.")]
	public void Create_ShouldSkipIntegratedSqlAuthentication() {
		// Arrange
		WriteConfig("db", "Data Source=sql.local;Initial Catalog=creatio;Integrated Security=true;TrustServerCertificate=true");

		// Act
		DbHubSourceDiscoveryResult result = _sut.Create("Integrated", new EnvironmentSettings {
			EnvironmentPath = _directory
		});

		// Assert
		result.Success.Should().BeFalse(because: "dbHub cannot use Windows integrated authentication");
		result.Warning.ErrorCode.Should().Be("DBHUB_SQL_AUTH_UNSUPPORTED",
			because: "callers need a stable safe skip classification");
		result.Warning.Detail.Should().NotContain("sql.local", because: "warnings must not expose connection data");
	}

	[TestCase("sql.local,0")]
	[TestCase("sql.local,65536")]
	[TestCase("sql.local,not-a-port")]
	[Description("Skips malformed SQL Server ports instead of emitting an invalid dbHub source.")]
	public void Create_ShouldSkipInvalidSqlServerPort(string dataSource) {
		// Arrange
		WriteConfig("db",
			$"Data Source={dataSource};Initial Catalog=creatio;User ID=sa;Password=secret;Encrypt=false");

		// Act
		DbHubSourceDiscoveryResult result = _sut.Create("Invalid SQL", new EnvironmentSettings {
			EnvironmentPath = _directory
		});

		// Assert
		result.Success.Should().BeFalse(because: "dbHub cannot connect through an invalid TCP port");
		result.Warning.ErrorCode.Should().Be("DBHUB_CONNECTION_CONFIG_UNAVAILABLE",
			because: "malformed connection configuration uses the stable safe skip classification");
	}

	[Test]
	[Description("Skips SQL Server identity-provider authentication that cannot be transferred safely.")]
	public void Create_ShouldSkipActiveDirectorySqlAuthentication() {
		// Arrange
		WriteConfig("db",
			"Data Source=sql.local;Initial Catalog=creatio;Authentication=Active Directory Default;Encrypt=true");

		// Act
		DbHubSourceDiscoveryResult result = _sut.Create("Azure SQL", new EnvironmentSettings {
			EnvironmentPath = _directory
		});

		// Assert
		result.Success.Should().BeFalse(because: "dbHub cannot reproduce the configured identity-provider context");
		result.Warning.ErrorCode.Should().Be("DBHUB_SQL_AUTH_UNSUPPORTED",
			because: "unsupported SQL authentication modes share one stable safe classification");
	}

	[Test]
	[Description("Rejects DTD-bearing connection configuration without resolving external entities.")]
	public void Create_ShouldRejectExternalEntityConfiguration() {
		// Arrange
		File.WriteAllText(Path.Combine(_directory, "ConnectionStrings.config"),
			"<!DOCTYPE connectionStrings [<!ENTITY xxe SYSTEM \"file:///sensitive-file\">]><connectionStrings><add name=\"dbPostgreSql\" connectionString=\"&xxe;\" /></connectionStrings>");

		// Act
		DbHubSourceDiscoveryResult result = _sut.Create("Unsafe XML", new EnvironmentSettings {
			EnvironmentPath = _directory
		});

		// Assert
		result.Success.Should().BeFalse(because: "external entity expansion is prohibited for local config discovery");
		result.Warning.Detail.Should().NotContain("sensitive-file",
			because: "the rejected external path must not enter user-facing diagnostics");
	}

	[TestCase(" My--DEV ", "my_dev")]
	[TestCase("alpha.beta/gamma", "alpha_beta_gamma")]
	[Description("Normalizes environment names to deterministic dbHub-safe source identifiers.")]
	public void NormalizeSourceId_ShouldReturnSafeId(string input, string expected) {
		// Act
		string result = DbHubConnectionSourceFactory.NormalizeSourceId(input);

		// Assert
		result.Should().Be(expected, because: "source ids must use the portable lowercase dbHub subset");
	}

	[Test]
	[Description("Source diagnostics redact database credentials generated by positional records.")]
	public void SourceToString_ShouldRedactCredentials() {
		// Arrange
		DbHubSourceDefinition source = new("dev", "dev", "postgres", "localhost", 5432, "creatio",
			"secret-user", "super-secret-value");

		// Act
		string diagnostic = source.ToString();

		// Assert
		diagnostic.Should().NotContainAny(["secret-user", "super-secret-value"],
			because: "exceptions and debuggers may stringify source values outside explicit logging code");
		diagnostic.Should().Contain("[redacted]",
			because: "diagnostics should make intentional credential suppression visible");
	}

	private void WriteConfig(string name, string connectionString) {
		string encoded = System.Security.SecurityElement.Escape(connectionString);
		File.WriteAllText(Path.Combine(_directory, "ConnectionStrings.config"),
			$"<connectionStrings><add name=\"{name}\" connectionString=\"{encoded}\" /></connectionStrings>");
	}
}
