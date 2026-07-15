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
			"pg.local", 5544, "creatio", "app", "secret", SslMode: "prefer"),
			because: "dbHub must receive the exact PostgreSQL connection fields");
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

	[Test]
	[Description("Skips SQL Server TLS validation that dbHub cannot represent without weakening it.")]
	public void Create_ShouldSkipUnsupportedSqlCertificateValidation() {
		// Arrange
		WriteConfig("db",
			"Data Source=sql.local;Initial Catalog=creatio;User ID=sa;Password=secret;Encrypt=true;TrustServerCertificate=false");

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

	private void WriteConfig(string name, string connectionString) {
		string encoded = System.Security.SecurityElement.Escape(connectionString);
		File.WriteAllText(Path.Combine(_directory, "ConnectionStrings.config"),
			$"<connectionStrings><add name=\"{name}\" connectionString=\"{encoded}\" /></connectionStrings>");
	}
}
