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
		const string userContent = "# user comment\ncustom-setting = \"preserve-me\"\n\n[[sources]]\nid = \"manual\"\ntype = \"postgres\"\ndsn = \"postgres://manual\"\n\n[[tools]]\nname = \"execute_sql\"\nsource = \"manual\"\nreadonly = true\n";
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
		content.Should().Contain("readonly = true",
			because: "persistent unauthenticated dbHub tools must not permit database writes");
	}

	[Test]
	[Description("Marker-looking lines inside a TOML multiline string remain user data, not clio ownership metadata.")]
	public void Upsert_ShouldPreserveMarkersInsideMultilineString() {
		// Arrange
		const string userContent = "note = \"\"\"\n# clio-managed-source begin environment=ZGV2 source=dev\nuser text\n# clio-managed-source end environment=ZGV2\n\"\"\"\n";
		File.WriteAllText(_configPath, userContent);

		// Act
		DbHubSyncResult result = _sut.Upsert(_configPath, Source("dev", "dev"));

		// Assert
		result.Changed.Should().BeTrue(because: "marker-shaped user text does not represent an owned source");
		File.ReadAllText(_configPath).Should().StartWith(userContent,
			because: "clio must preserve multiline user values byte-for-byte");
	}

	[Test]
	[Description("Repairs duplicate clio-owned blocks into one stable managed source.")]
	public void Upsert_ShouldCollapseDuplicateOwnedBlocks() {
		// Arrange
		_sut.Upsert(_configPath, Source("dev", "dev"));
		string block = File.ReadAllText(_configPath);
		File.AppendAllText(_configPath, block);

		// Act
		DbHubSyncResult result = _sut.Upsert(_configPath, Source("dev", "dev"));

		// Assert
		result.Changed.Should().BeTrue(because: "duplicate ownership blocks require deterministic repair");
		File.ReadAllText(_configPath).Split("clio-managed-source begin").Should().HaveCount(2,
			because: "exactly one managed block must remain after repair");
	}

	[Test]
	[Description("A case-only environment rename retains ownership of the same normalized source id.")]
	public void Upsert_ShouldMigrateCaseOnlyEnvironmentRename() {
		// Arrange
		_sut.Upsert(_configPath, Source("Dev", "dev"));

		// Act
		DbHubSyncResult result = _sut.Upsert(_configPath, Source("dev", "dev"));

		// Assert
		result.Changed.Should().BeTrue(because: "clio owns the existing source id and may update its environment identity");
		_sut.GetOwnedSources(_configPath).EnvironmentNames.Should().Equal(["dev"],
			because: "the stale case variant must not survive reconciliation");
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
	[Description("Removing the last managed environment leaves a harmless control source for dbHub hot reload.")]
	public void Remove_ShouldLeaveRunnableControlSource_WhenLastDatabaseSourceIsRemoved() {
		// Arrange
		_sut.Upsert(_configPath, Source("only", "only"));

		// Act
		DbHubSyncResult result = _sut.Remove(_configPath, "only");

		// Assert
		result.Changed.Should().BeTrue(because: "the owned environment source existed");
		string content = File.ReadAllText(_configPath);
		content.Should().NotContain("source=only", because: "the exact environment ownership block was removed");
		content.Should().Contain($"id = \"{DbHubTomlStore.ControlSourceId}\"",
			because: "dbHub 0.23 rejects and does not hot-reload a configuration with zero sources");
		content.Should().Contain("readonly = true",
			because: "the in-memory control source must also make its SQL policy explicit");
		_sut.GetOwnedSources(_configPath).EnvironmentNames.Should().BeEmpty(
			because: "the control source is infrastructure, not an environment ownership mapping");
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
	[Description("Installer validation preserves a manually maintained source without imposing clio tool policy.")]
	public void ValidateForInstallation_ShouldAllowManualSourceWithoutReadonlySqlTool() {
		// Arrange
		const string content = "[[sources]]\nid = \"manual\"\ntype = \"postgres\"\ndsn = \"postgres://manual\"\n";
		File.WriteAllText(_configPath, content);

		// Act
		DbHubSyncResult result = _sut.ValidateForInstallation(_configPath);

		// Assert
		result.Warning.Should().BeNull(
			because: "clio must preserve user-maintained sources and custom tool policy outside managed blocks");
		File.ReadAllText(_configPath).Should().Be(content,
			because: "installation validation must not rewrite user-owned configuration");
	}

	[Test]
	[Description("Installer validation refuses a clio-managed source whose generated SQL tool is not explicitly read-only.")]
	public void ValidateForInstallation_ShouldRefuseManagedSourceWithoutReadonlySqlTool() {
		// Arrange
		const string content = "# clio-managed-source begin environment=managed source=managed\n[[sources]]\nid = \"managed\"\ntype = \"postgres\"\ndsn = \"postgres://managed\"\n# clio-managed-source end environment=managed\n";
		File.WriteAllText(_configPath, content);

		// Act
		DbHubSyncResult result = _sut.ValidateForInstallation(_configPath);

		// Assert
		result.Warning.Should().NotBeNull(
			because: "clio-owned source blocks must retain the generated read-only SQL contract");
		File.ReadAllText(_configPath).Should().Be(content,
			because: "failed validation must not rewrite the existing configuration");
	}

	[Test]
	[Description("Control-source validation remains bounded when user-maintained sources and custom tools follow it.")]
	public void ValidateForInstallation_ShouldAllowManualToolsAfterControlSource() {
		// Arrange
		const string content = "# clio-managed-control-source: keeps dbHub configuration valid when no database environments exist\n[[sources]]\nid = \"clio_control\"\ntype = \"sqlite\"\ndsn = \"sqlite:///:memory:\"\nlazy = true\n[[tools]]\nname = \"execute_sql\"\nsource = \"clio_control\"\nreadonly = true\n\n[[sources]]\nid = \"manual\"\ntype = \"postgres\"\ndsn = \"postgres://manual\"\n[[tools]]\nname = \"custom_manual_tool\"\nsource = \"manual\"\n";
		File.WriteAllText(_configPath, content);

		// Act
		DbHubSyncResult result = _sut.ValidateForInstallation(_configPath);

		// Assert
		result.Warning.Should().BeNull(
			because: "control validation must not absorb later user-owned sources or custom tools");
		File.ReadAllText(_configPath).Should().Be(content,
			because: "manual content after the bounded control block must remain byte-for-byte unchanged");
	}

	[Test]
	[Description("Refuses a manual source whose normalized dbHub tool name collides with the managed source.")]
	public void Upsert_ShouldRefuseNormalizedManualToolCollision() {
		// Arrange
		const string content = "[[sources]]\nid = \"dev-one\"\ntype = \"postgres\"\ndsn = \"postgres://manual\"\n";
		File.WriteAllText(_configPath, content);

		// Act
		DbHubSyncResult result = _sut.Upsert(_configPath, Source("dev one", "dev_one"));

		// Assert
		result.Warning.ErrorCode.Should().Be("DBHUB_SOURCE_OWNERSHIP_CONFLICT",
			because: "dbHub maps both source ids to the same execute_sql_dev_one tool name");
		File.ReadAllText(_configPath).Should().Be(content,
			because: "clio must not shadow or rewrite the manual source");
	}

	[Test]
	[Description("Refuses an existing custom tool whose name collides with the generated dbHub MCP tool name.")]
	public void Upsert_ShouldRefuseExistingToolNameCollision() {
		// Arrange
		const string content = "[[sources]]\nid = \"manual\"\ntype = \"postgres\"\ndsn = \"postgres://manual\"\n[[tools]]\nname = \"execute_sql_dev\"\nsource = \"manual\"\nreadonly = true\n";
		File.WriteAllText(_configPath, content);

		// Act
		DbHubSyncResult result = _sut.Upsert(_configPath, Source("dev", "dev"));

		// Assert
		result.Warning.ErrorCode.Should().Be("DBHUB_SOURCE_OWNERSHIP_CONFLICT",
			because: "clio must not create a source whose generated MCP tool name is already user-owned");
		File.ReadAllText(_configPath).Should().Be(content,
			because: "a tool-name conflict must leave the adopted configuration unchanged");
	}

	[Test]
	[Description("Installer preparation and source synchronization share one lock without losing the source update.")]
	public async Task EnsureRunnable_ShouldSerializeWithConcurrentUpsert() {
		// Arrange
		File.WriteAllText(_configPath, "# user comment\n");

		// Act
		DbHubSyncResult[] results = await Task.WhenAll(
			Task.Run(() => _sut.EnsureRunnable(_configPath)),
			Task.Run(() => _sut.Upsert(_configPath, Source("dev", "dev"))));

		// Assert
		results.Should().OnlyContain(result => result.Warning == null,
			because: "both lock-owning operations should complete without a lost-update race");
		_sut.GetOwnedSources(_configPath).EnvironmentNames.Should().Contain("dev",
			because: "installer preparation must not overwrite a concurrent lifecycle source update");
	}

	[Test]
	[Description("Configuration failures never include source credentials in their warning text.")]
	public void Upsert_ShouldNotExposeSecrets_WhenValidationFails() {
		// Arrange
		File.WriteAllText(_configPath, "[[broken]");
		DbHubSourceDefinition source = Source("dev", "dev") with { Credential = "super-secret-value" };

		// Act
		DbHubSyncResult result = _sut.Upsert(_configPath, source);

		// Assert
		$"{result.Warning.Message} {result.Warning.Detail}".Should().NotContain("super-secret-value",
			because: "passwords must never enter diagnostics when TOML processing fails");
	}

	private static DbHubSourceDefinition Source(string environment, string sourceId) => new(environment, sourceId,
		"postgres", "localhost", 5432, "creatio", "app", "secret");
}
