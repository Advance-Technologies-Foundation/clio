using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Common.db;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class DbTemplatePruneServiceTests {
	private ISettingsRepository _settingsRepository;
	private IDbClientFactory _dbClientFactory;
	private Postgres _postgres;
	private DbTemplatePruneService _sut;

	[SetUp]
	public void Setup() {
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_dbClientFactory = Substitute.For<IDbClientFactory>();
		_postgres = Substitute.For<Postgres>();
		_settingsRepository.Reload().Returns(new SettingsReloadResult(true, null, null));
		_settingsRepository.GetLocalDbServer("local-pg").Returns(new LocalDbServerConfiguration {
			DbType = "postgres",
			Hostname = "localhost",
			Port = 5432,
			Username = "postgres",
			Password = "secret"
		});
		_dbClientFactory.CreatePostgres("localhost", 5432, "postgres", "secret").Returns(_postgres);
		_sut = new DbTemplatePruneService(_settingsRepository, _dbClientFactory);
	}

	[Test]
	[Category("Unit")]
	[Description("Deletes an explicitly requested template only after fresh eligibility and session checks.")]
	public void Prune_EligibleInactiveTemplate_DeletesIt() {
		// Arrange
		PostgresManagedTemplate template = Template("canonical-template");
		_postgres.GetManagedTemplate("requested-template").Returns(template);
		_postgres.CountActiveSessions("canonical-template").Returns(0);

		// Act
		DbTemplatePruneResult result = _sut.Prune("local-pg", ["requested-template"]);

		// Assert
		result.Success.Should().BeTrue(because: "the only requested template was safely deleted");
		result.Status.Should().Be(DbTemplatePruneService.CompleteSuccessStatus,
			because: "every requested item completed successfully");
		Received.InOrder(() => {
			_postgres.GetManagedTemplate("requested-template");
			_postgres.CountActiveSessions("canonical-template");
			_postgres.SetTemplateFlag("canonical-template", false);
			_postgres.DropDatabaseWithoutForce("canonical-template");
		});
	}

	[Test]
	[Category("Unit")]
	[Description("Skips a selected template when active sessions exist and performs no mutation.")]
	public void Prune_TemplateWithActiveSessions_SkipsWithoutMutation() {
		// Arrange
		_postgres.GetManagedTemplate("busy-template").Returns(Template("busy-template"));
		_postgres.CountActiveSessions("busy-template").Returns(2);

		// Act
		DbTemplatePruneResult result = _sut.Prune("local-pg", ["busy-template"]);

		// Assert
		result.Results[0].Outcome.Should().Be(DbTemplatePruneService.SkippedOutcome,
			because: "the command must never force-disconnect active users");
		_postgres.DidNotReceive().SetTemplateFlag(Arg.Any<string>(), Arg.Any<bool>());
		_postgres.DidNotReceive().DropDatabaseWithoutForce(Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("Skips a requested database that is no longer an eligible managed template.")]
	public void Prune_RevalidationReturnsNoTemplate_SkipsWithoutMutation() {
		// Arrange
		_postgres.GetManagedTemplate("missing-template").Returns((PostgresManagedTemplate)null);

		// Act
		DbTemplatePruneResult result = _sut.Prune("local-pg", ["missing-template"]);

		// Assert
		result.Results[0].Outcome.Should().Be(DbTemplatePruneService.SkippedOutcome,
			because: "deletion-time eligibility loss must prevent mutation");
		_postgres.DidNotReceive().SetTemplateFlag(Arg.Any<string>(), Arg.Any<bool>());
	}

	[Test]
	[Category("Unit")]
	[Description("Restores the template flag when dropping a database fails after the flag was cleared.")]
	public void Prune_DropFails_RestoresTemplateFlag() {
		// Arrange
		_postgres.GetManagedTemplate("template-a").Returns(Template("template-a"));
		_postgres.CountActiveSessions("template-a").Returns(0);
		_postgres.When(postgres => postgres.DropDatabaseWithoutForce("template-a"))
			.Do(_ => throw new InvalidOperationException("drop failed"));
		_postgres.DatabaseExists("template-a").Returns(true);

		// Act
		DbTemplatePruneResult result = _sut.Prune("local-pg", ["template-a"]);

		// Assert
		result.Success.Should().BeFalse(because: "a failed drop cannot be reported as success");
		_postgres.Received(1).SetTemplateFlag("template-a", true);
	}

	[Test]
	[Category("Unit")]
	[Description("Restores discoverability when clearing the template flag reports an ambiguous database failure.")]
	public void Prune_ClearFlagReportsFailure_RestoresTemplateFlag() {
		// Arrange
		_postgres.GetManagedTemplate("template-a").Returns(Template("template-a"));
		_postgres.CountActiveSessions("template-a").Returns(0);
		_postgres.When(postgres => postgres.SetTemplateFlag("template-a", false))
			.Do(_ => throw new InvalidOperationException("connection lost"));
		_postgres.DatabaseExists("template-a").Returns(true);

		// Act
		DbTemplatePruneResult result = _sut.Prune("local-pg", ["template-a"]);

		// Assert
		result.Success.Should().BeFalse(because: "an ambiguous flag mutation cannot be reported as deletion success");
		_postgres.Received(1).SetTemplateFlag("template-a", true);
		_postgres.DidNotReceive().DropDatabaseWithoutForce(Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("Restores the template flag when a drop times out.")]
	public void Prune_DropTimesOut_RestoresTemplateFlag() {
		// Arrange
		_postgres.GetManagedTemplate("template-a").Returns(Template("template-a"));
		_postgres.CountActiveSessions("template-a").Returns(0);
		_postgres.When(postgres => postgres.DropDatabaseWithoutForce("template-a"))
			.Do(_ => throw new TimeoutException("lock wait"));
		_postgres.DatabaseExists("template-a").Returns(true);

		// Act
		DbTemplatePruneResult result = _sut.Prune("local-pg", ["template-a"]);

		// Assert
		result.Success.Should().BeFalse(because: "a timed-out drop is a failed deletion");
		_postgres.Received(1).SetTemplateFlag("template-a", true);
	}

	[Test]
	[Category("Unit")]
	[Description("Reports an uncertain recovery when database existence cannot be verified after a failed drop.")]
	public void Prune_RecoveryCheckFails_ReturnsRecoveryFailure() {
		// Arrange
		_postgres.GetManagedTemplate("template-a").Returns(Template("template-a"));
		_postgres.When(postgres => postgres.DropDatabaseWithoutForce("template-a"))
			.Do(_ => throw new InvalidOperationException("drop failed"));
		_postgres.When(postgres => postgres.DatabaseExists("template-a"))
			.Do(_ => throw new InvalidOperationException("check failed"));

		// Act
		DbTemplatePruneResult result = _sut.Prune("local-pg", ["template-a"]);

		// Assert
		result.Results[0].Message.Should().Contain("could not verify or restore",
			because: "an uncertain recovery needs an explicit operator diagnostic");
	}

	[Test]
	[Category("Unit")]
	[Description("Reports failure rather than success when a failed drop is followed by a missing database.")]
	public void Prune_DropFailsAndDatabaseIsMissing_ReturnsFailure() {
		// Arrange
		_postgres.GetManagedTemplate("template-a").Returns(Template("template-a"));
		_postgres.When(postgres => postgres.DropDatabaseWithoutForce("template-a"))
			.Do(_ => throw new InvalidOperationException("ambiguous drop"));
		_postgres.DatabaseExists("template-a").Returns(false);

		// Act
		DbTemplatePruneResult result = _sut.Prune("local-pg", ["template-a"]);

		// Assert
		result.Success.Should().BeFalse(because: "an abnormal drop completion cannot be reported as success");
		result.Results[0].Message.Should().Contain("no longer exists",
			because: "the diagnostic should describe the observed catalog state");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects incomplete PostgreSQL server settings as a structured configuration failure.")]
	public void Inventory_IncompleteServerConfiguration_ReturnsConfigurationFailure() {
		// Arrange
		_settingsRepository.GetLocalDbServer("broken-pg").Returns(new LocalDbServerConfiguration {
			DbType = "postgres",
			Hostname = "",
			Port = 5432,
			Username = "postgres"
		});

		// Act
		DbTemplateInventoryResult result = _sut.Inventory("broken-pg");

		// Assert
		result.ErrorCategory.Should().Be("configuration",
			because: "malformed configured servers must not escape as MCP transport exceptions");
		_dbClientFactory.DidNotReceiveWithAnyArgs().CreatePostgres(default, default, default, default);
	}

	[Test]
	[Category("Unit")]
	[Description("Distinguishes an empty successful inventory from a database access failure.")]
	public void Inventory_EmptyResult_ReturnsSuccess() {
		// Arrange
		_postgres.GetManagedTemplates().Returns(Array.Empty<PostgresManagedTemplate>());

		// Act
		DbTemplateInventoryResult result = _sut.Inventory("local-pg");

		// Assert
		result.Success.Should().BeTrue(because: "a reachable server may legitimately have no managed templates");
		result.Templates.Should().BeEmpty(because: "the database query returned no eligible templates");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a structured connection failure when the inventory query cannot execute.")]
	public void Inventory_QueryFails_ReturnsConnectionFailure() {
		// Arrange
		_postgres.GetManagedTemplates().Returns(_ => throw new InvalidOperationException("query failed"));

		// Act
		DbTemplateInventoryResult result = _sut.Inventory("local-pg");

		// Assert
		result.Success.Should().BeFalse(because: "a failed query is not an empty successful inventory");
		result.ErrorCategory.Should().Be("connection",
			because: "database access failures use the structured inventory error contract");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects unknown and non-PostgreSQL explicit server names before creating a database client.")]
	public void Inventory_InvalidExplicitServer_ReturnsConfigurationFailure() {
		// Arrange
		_settingsRepository.GetLocalDbServer("local-sql").Returns(new LocalDbServerConfiguration {
			DbType = "mssql",
			Hostname = "localhost",
			Port = 1433,
			Username = "sa"
		});

		// Act
		DbTemplateInventoryResult unknown = _sut.Inventory("unknown");
		DbTemplateInventoryResult nonPostgres = _sut.Inventory("local-sql");

		// Assert
		unknown.ErrorCategory.Should().Be("configuration",
			because: "an unknown explicit server must not fall back to another configuration");
		nonPostgres.ErrorCategory.Should().Be("configuration",
			because: "a non-PostgreSQL server is outside this command's scope");
		_dbClientFactory.DidNotReceiveWithAnyArgs().CreatePostgres(default, default, default, default);
	}

	[Test]
	[Category("Unit")]
	[Description("Continues after a skipped item and reports a partial batch outcome.")]
	public void Prune_DeletedAndSkippedItems_ReturnsEveryOutcomeAsPartialFailure() {
		// Arrange
		int completedItems = 0;
		_postgres.GetManagedTemplate("template-a").Returns(Template("template-a"));
		_postgres.GetManagedTemplate("template-b").Returns(Template("template-b"));
		_postgres.CountActiveSessions("template-a").Returns(0);
		_postgres.CountActiveSessions("template-b").Returns(1);

		// Act
		DbTemplatePruneResult result = _sut.Prune("local-pg", ["template-a", "template-b"],
			() => completedItems++);

		// Assert
		result.Status.Should().Be(DbTemplatePruneService.PartialFailureStatus,
			because: "one deletion and one skip is an incomplete batch");
		result.Results.Should().HaveCount(2, because: "every distinct requested database needs an outcome");
		result.Results[0].Outcome.Should().Be(DbTemplatePruneService.DeletedOutcome,
			because: "the inactive eligible template was deleted");
		result.Results[1].Outcome.Should().Be(DbTemplatePruneService.SkippedOutcome,
			because: "the busy template was safely skipped");
		completedItems.Should().Be(2,
			because: "every deleted or skipped outcome should advance progress");
	}

	[Test]
	[Category("Unit")]
	[Description("Continues with later templates after a handled database failure.")]
	public void Prune_ShouldContinueBestEffort_WhenEarlierDatabaseOperationFails() {
		// Arrange
		int completedItems = 0;
		_postgres.GetManagedTemplate("template-a").Returns(_ =>
			throw new InvalidOperationException("database inspection failed"));
		_postgres.GetManagedTemplate("template-b").Returns(Template("template-b"));
		_postgres.CountActiveSessions("template-b").Returns(0);

		// Act
		DbTemplatePruneResult result = _sut.Prune("local-pg", ["template-a", "template-b"],
			() => completedItems++);

		// Assert
		result.Status.Should().Be(DbTemplatePruneService.PartialFailureStatus,
			because: "one handled failure and one deletion is a partial batch failure");
		result.Results.Select(item => item.Outcome).Should().Equal(
			[DbTemplatePruneService.FailedOutcome, DbTemplatePruneService.DeletedOutcome],
			because: "the second template must still be deleted after the first database failure");
		completedItems.Should().Be(2,
			because: "failed and deleted outcomes both count as processed progress");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects an empty deletion request before connecting to PostgreSQL.")]
	public void Prune_EmptySelection_ReturnsValidationFailure() {
		// Arrange

		// Act
		DbTemplatePruneResult result = _sut.Prune("local-pg", Array.Empty<string>());

		// Assert
		result.ErrorCategory.Should().Be("validation",
			because: "automation must never interpret an omitted selection as delete all");
		_dbClientFactory.DidNotReceiveWithAnyArgs().CreatePostgres(default, default, default, default);
	}

	private static PostgresManagedTemplate Template(string name) =>
		new(name, "Studio.zip", DateTimeOffset.Parse("2026-08-22T10:20:30+00:00"), "1.0");
}
