using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Clio.Common.DbHub;
using Clio.Tests.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common.DbHub;

[TestFixture]
[Property("Module", "Common")]
public sealed class DbHubTomlStoreTests : BaseClioModuleTests {
	private string _directory;
	private string _configPath;
	private IDbHubTomlStore _sut;

	public override void Setup() {
		base.Setup();
		_directory = Path.Combine(Path.GetTempPath(), $"clio-dbhub-toml-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_directory);
		_configPath = Path.Combine(_directory, "dbhub.toml");
		_sut = Container.GetRequiredService<IDbHubTomlStore>();
	}

	public override void TearDown() {
		if (Directory.Exists(_directory)) {
			Directory.Delete(_directory, recursive: true);
		}
		base.TearDown();
	}

	[Test]
	[Description("Adds a managed source without altering user-authored TOML content.")]
	public void Upsert_ShouldPreserveUserContent() {
		// Arrange
		const string userContent = "# user comment\ncustom-setting = \"preserve-me\"\n\n[[sources]]\nid = \"manual\"\ntype = \"postgres\"\ndsn = \"postgres://manual\"\n\n[tools]\nreadonly = true\n";
		File.WriteAllText(_configPath, userContent);

		// Act
		DbHubSyncResult result = _sut.Upsert(_configPath, Source("dev", "dev"));

		// Assert
		result.Changed.Should().BeTrue(because: "the desired managed source did not exist");
		File.ReadAllText(_configPath).Should().StartWith(userContent,
			because: "clio must surgically append without rewriting user-authored bytes");
	}

	[Test]
	[Description("Repeating an identical upsert is a byte-for-byte no-op.")]
	public void Upsert_ShouldBeIdempotent() {
		// Arrange
		_sut.Upsert(_configPath, Source("dev", "dev"));
		byte[] before = File.ReadAllBytes(_configPath);

		// Act
		DbHubSyncResult result = _sut.Upsert(_configPath, Source("dev", "dev"));

		// Assert
		result.Changed.Should().BeFalse(because: "the managed block already matches the desired source");
		File.ReadAllBytes(_configPath).Should().Equal(before,
			because: "an idempotent reconciliation must not churn the TOML file");
	}

	[Test]
	[Description("Writes source TLS settings using dbHub's documented TOML fields.")]
	public void Upsert_ShouldWriteTlsSettings() {
		// Arrange
		DbHubSourceDefinition source = Source("secure", "secure") with {
			SslMode = "verify-full",
			SslRootCertificate = "C:\\certs\\ca.pem"
		};

		// Act
		DbHubSyncResult result = _sut.Upsert(_configPath, source);

		// Assert
		result.Changed.Should().BeTrue(because: "the secure source was not yet configured");
		string content = File.ReadAllText(_configPath);
		content.Should().Contain("sslmode = \"verify-full\"",
			because: "dbHub must retain the requested TLS verification mode");
		content.Should().Contain("sslrootcert = \"C:\\\\certs\\\\ca.pem\"",
			because: "TOML rendering must preserve the trust-root path");
	}

	[Test]
	[Description("Refuses to take ownership of an existing user-authored source id.")]
	public void Upsert_ShouldRefuseManualSourceIdConflict() {
		// Arrange
		const string content = "[[sources]]\nid = \"dev\"\ntype = \"postgres\"\ndsn = \"postgres://manual\"\n";
		File.WriteAllText(_configPath, content);

		// Act
		DbHubSyncResult result = _sut.Upsert(_configPath, Source("dev", "dev"));

		// Assert
		result.Skipped.Should().BeTrue(because: "manual source ownership must win");
		result.Warning.ErrorCode.Should().Be("DBHUB_SOURCE_OWNERSHIP_CONFLICT",
			because: "the conflict needs a stable diagnostic code");
		File.ReadAllText(_configPath).Should().Be(content,
			because: "a conflicting user source must remain untouched");
	}

	[Test]
	[Description("Detects ownership conflicts even when a user source id uses a TOML escape sequence.")]
	public void Upsert_ShouldRefuseEscapedManualSourceIdConflict() {
		// Arrange
		const string content = "[[sources]]\nid = \"\\u0064ev\"\ntype = \"postgres\"\ndsn = \"postgres://manual\"\n";
		File.WriteAllText(_configPath, content);

		// Act
		DbHubSyncResult result = _sut.Upsert(_configPath, Source("dev", "dev"));

		// Assert
		result.Warning.ErrorCode.Should().Be("DBHUB_SOURCE_OWNERSHIP_CONFLICT",
			because: "semantic TOML ids must be compared after escape decoding");
		File.ReadAllText(_configPath).Should().Be(content,
			because: "escaped user-owned ids must remain untouched");
	}

	[Test]
	[Description("Rejects an invalid existing TOML document and preserves it exactly.")]
	public void Upsert_ShouldPreserveInvalidToml() {
		// Arrange
		const string invalid = "[[sources]\nid = \"broken\"";
		File.WriteAllText(_configPath, invalid);

		// Act
		DbHubSyncResult result = _sut.Upsert(_configPath, Source("dev", "dev"));

		// Assert
		result.Skipped.Should().BeTrue(because: "clio must not mutate a document it cannot validate");
		File.ReadAllText(_configPath).Should().Be(invalid,
			because: "failed validation must leave the original file intact");
	}

	[Test]
	[Description("Inventories and removes only the exact clio-owned environment block.")]
	public void Remove_ShouldRemoveOnlyOwnedEnvironment() {
		// Arrange
		_sut.Upsert(_configPath, Source("first", "first"));
		_sut.Upsert(_configPath, Source("second", "second"));

		// Act
		DbHubOwnedSourcesResult inventory = _sut.GetOwnedSources(_configPath);
		DbHubSyncResult result = _sut.Remove(_configPath, "first");

		// Assert
		inventory.EnvironmentNames.Should().BeEquivalentTo(["first", "second"],
			because: "stale reconciliation needs every clio ownership identity");
		result.Changed.Should().BeTrue(because: "the first owned block existed");
		string content = File.ReadAllText(_configPath);
		content.Should().NotContain("source=first", because: "the selected ownership block was removed");
		content.Should().Contain("source=second", because: "other managed sources must remain");
	}

	[Test]
	[Description("Concurrent source updates are serialized without losing any managed block.")]
	public async Task Upsert_ShouldSerializeConcurrentUpdates() {
		// Arrange
		DbHubSourceDefinition[] sources = Enumerable.Range(0, 12)
			.Select(index => Source($"environment-{index}", $"environment_{index}"))
			.ToArray();

		// Act
		DbHubSyncResult[] results = await Task.WhenAll(sources.Select(source =>
			Task.Run(() => _sut.Upsert(_configPath, source))));
		DbHubOwnedSourcesResult inventory = _sut.GetOwnedSources(_configPath);

		// Assert
		results.Should().OnlyContain(result => result.Changed,
			because: "every distinct concurrent source must be committed exactly once");
		inventory.EnvironmentNames.Should().BeEquivalentTo(sources.Select(source => source.EnvironmentName),
			because: "the adjacent lock must prevent lost updates");
	}

	[Test]
	[Description("Configuration failures never include source credentials in their warning text.")]
	public void Upsert_ShouldNotExposeSecrets_WhenValidationFails() {
		// Arrange
		File.WriteAllText(_configPath, "[[broken]");
		DbHubSourceDefinition source = Source("dev", "dev") with { Password = "super-secret-value" };

		// Act
		DbHubSyncResult result = _sut.Upsert(_configPath, source);

		// Assert
		$"{result.Warning.Message} {result.Warning.Detail}".Should().NotContain("super-secret-value",
			because: "passwords must never enter diagnostics when TOML processing fails");
	}

	private static DbHubSourceDefinition Source(string environment, string sourceId) => new(environment, sourceId,
		"postgres", "localhost", 5432, "creatio", "app", "secret");
}
