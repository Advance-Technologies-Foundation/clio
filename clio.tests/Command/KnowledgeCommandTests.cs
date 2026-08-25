using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class InstallKnowledgeCommandTests : BaseCommandTests<InstallKnowledgeOptions> {
	private IKnowledgeSourceManagementService _service = null!;
	private ILogger _logger = null!;
	private InstallKnowledgeCommand _command = null!;

	protected override void AdditionalRegistrations(IServiceCollection services) {
		_service = Substitute.For<IKnowledgeSourceManagementService>();
		_logger = Substitute.For<ILogger>();
		services.AddSingleton(_service);
		services.AddSingleton(_logger);
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<InstallKnowledgeCommand>();
	}

	[TearDown]
	public override void TearDown() {
		_service.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("Installs every enabled source when no source alias is supplied.")]
	public void Execute_ShouldInstallAllEnabledSources_WhenSourceIsOmitted() {
		// Arrange
		_service.Install(null).Returns(KnowledgeCommandTestData.SuccessBatch("installed"));

		// Act
		int exitCode = _command.Execute(new InstallKnowledgeOptions());

		// Assert
		exitCode.Should().Be(0, because: "a successful all-enabled-source installation must return success");
		_service.Received(1).Install(null);
	}

	[Test]
	[Description("Rejects an explicitly empty source alias instead of widening the operation to all enabled sources.")]
	public void Execute_ShouldFailClosed_WhenSourceIsWhitespace() {
		// Arrange
		InstallKnowledgeOptions options = new() { Source = "   " };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1,
			because: "an explicitly empty selector must not become an unintended all-source operation");
		_service.DidNotReceive().Install(Arg.Any<string?>());
	}
}

[TestFixture]
[Property("Module", "Command")]
public sealed class UpdateKnowledgeCommandTests : BaseCommandTests<UpdateKnowledgeOptions> {
	private IKnowledgeSourceManagementService _service = null!;
	private ILogger _logger = null!;
	private UpdateKnowledgeCommand _command = null!;

	protected override void AdditionalRegistrations(IServiceCollection services) {
		_service = Substitute.For<IKnowledgeSourceManagementService>();
		_logger = Substitute.For<ILogger>();
		services.AddSingleton(_service);
		services.AddSingleton(_logger);
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<UpdateKnowledgeCommand>();
	}

	[TearDown]
	public override void TearDown() {
		_service.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("Updates only the explicitly selected source alias.")]
	public void Execute_ShouldUpdateSelectedSource_WhenAliasIsProvided() {
		// Arrange
		_service.Update("partner").Returns(new KnowledgeSourceBatchResult(
			true,
			"Knowledge operation completed for 1 source(s).",
			[new KnowledgeSourceOperationResult("partner", true, "updated", "Partner knowledge was updated.")]));

		// Act
		int exitCode = _command.Execute(new UpdateKnowledgeOptions { Source = " partner " });

		// Assert
		exitCode.Should().Be(0, because: "a successful selected-source update must return success");
		_service.Received(1).Update("partner");
		KnowledgeCommandTestData.OutputCalls(_logger).Should().Equal(
			[
				"WriteInfo(Knowledge update)",
				"WriteLine(  Result: succeeded)",
				"WriteLine(  Message: Knowledge operation completed for 1 source(s).)",
				"WriteLine()",
				"WriteInfo(Sources (1))",
				"WriteLine()",
				"WriteInfo(1. partner)",
				"WriteLine(  Status: updated)",
				"WriteLine(  Message: Partner knowledge was updated.)"
			],
			because: "a successful update must use the same ordered one-stream layout as other outcomes");
	}

	[Test]
	[Description("Emits only the unchanged structured batch result when JSON output is requested.")]
	public void Execute_ShouldEmitOnlyStructuredBatchResult_WhenJsonIsRequested() {
		// Arrange
		KnowledgeSourceBatchResult result = new(
			true,
			"Knowledge operation completed for 1 source(s).",
			[new KnowledgeSourceOperationResult("partner", true, "up-to-date", "Partner knowledge is current.")]);
		_service.Update("partner").Returns(result);

		// Act
		int exitCode = _command.Execute(new UpdateKnowledgeOptions { Source = "partner", Json = true });

		// Assert
		exitCode.Should().Be(0, because: "the structured update completed successfully");
		var calls = _logger.ReceivedCalls().ToArray();
		calls.Should().ContainSingle(because: "JSON mode must emit one payload without human-readable headings");
		calls[0].GetMethodInfo().Name.Should().Be(nameof(ILogger.WriteLine),
			because: "the structured payload is written as one line-oriented logger message");
		string json = calls[0].GetArguments().Single().Should().BeOfType<string>(
			because: "the structured payload must be serialized as JSON text").Subject;
		using JsonDocument document = JsonDocument.Parse(json);
		JsonElement root = document.RootElement;
		root.GetProperty("success").GetBoolean().Should().BeTrue(
			because: "the existing batch success field must remain present");
		root.GetProperty("message").GetString().Should().Be(result.Message,
			because: "the existing batch message field must remain unchanged");
		JsonElement source = root.GetProperty("sources").EnumerateArray().Should().ContainSingle(
			because: "the existing per-source result array must remain present").Subject;
		source.GetProperty("sourceAlias").GetString().Should().Be("partner",
			because: "the existing source alias field must remain unchanged");
		source.GetProperty("success").GetBoolean().Should().BeTrue(
			because: "the existing per-source success field must remain present");
		source.GetProperty("status").GetString().Should().Be("up-to-date",
			because: "the existing source status field must remain unchanged");
		source.GetProperty("message").GetString().Should().Be("Partner knowledge is current.",
			because: "the existing per-source message field must remain unchanged");
	}

	[Test]
	[Description("Prints the complete knowledge update as ordered vertical source blocks instead of a table.")]
	public void Execute_ShouldPrintOrderedVerticalSourceBlocks_WhenHumanOutputIsRequested() {
		// Arrange
		_service.Update(null).Returns(new KnowledgeSourceBatchResult(
			false,
			"Knowledge operation completed with one failure.",
			[
				new KnowledgeSourceOperationResult(
					"creatio-curated",
					true,
					"up-to-date",
					"Knowledge source 'creatio-curated' is up to date at 1.13.8."),
				new KnowledgeSourceOperationResult(
					"partner",
					false,
					"failed",
					"Partner verification failed.")
			]));

		// Act
		int exitCode = _command.Execute(new UpdateKnowledgeOptions());

		// Assert
		exitCode.Should().Be(1, because: "one selected source failed to update");
		KnowledgeCommandTestData.OutputCalls(_logger).Should().Equal(
			[
				"WriteInfo(Knowledge update)",
				"WriteLine(  Result: failed)",
				"WriteLine(  Message: Knowledge operation completed with one failure.)",
				"WriteLine()",
				"WriteInfo(Sources (2))",
				"WriteLine()",
				"WriteInfo(1. creatio-curated)",
				"WriteLine(  Status: up-to-date)",
				"WriteLine(  Message: Knowledge source 'creatio-curated' is up to date at 1.13.8.)",
				"WriteLine()",
				"WriteInfo(2. partner)",
				"WriteLine(  Status: failed)",
				"WriteLine(  Message: Partner verification failed.)"
			],
			because: "the multi-source report must remain ordered, numbered, and on one output stream");
	}
}

[TestFixture]
[Property("Module", "Command")]
public sealed class InfoKnowledgeCommandTests : BaseCommandTests<InfoKnowledgeOptions> {
	private IKnowledgeSourceManagementService _service = null!;
	private ILogger _logger = null!;
	private InfoKnowledgeCommand _command = null!;

	protected override void AdditionalRegistrations(IServiceCollection services) {
		_service = Substitute.For<IKnowledgeSourceManagementService>();
		_logger = Substitute.For<ILogger>();
		services.AddSingleton(_service);
		services.AddSingleton(_logger);
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<InfoKnowledgeCommand>();
	}

	[TearDown]
	public override void TearDown() {
		_service.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("Reports one source from local state without asking its transport unless update checks are requested.")]
	public void Execute_ShouldSkipUpdateCheck_WhenCheckUpdatesIsOmitted() {
		// Arrange
		_service.GetInfo("creatio", false).Returns(KnowledgeCommandTestData.Info("creatio"));

		// Act
		int exitCode = _command.Execute(new InfoKnowledgeOptions {
			Source = "creatio",
			Json = true
		});

		// Assert
		exitCode.Should().Be(0, because: "a resolved local source can be inspected without transport access");
		_service.Received(1).GetInfo("creatio", false);
		_logger.Received(1).WriteLine(Arg.Is<string>(value =>
			value.Contains("\"alias\": \"creatio\"", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Prints detailed human-readable knowledge state as vertical blocks instead of a wide table.")]
	public void Execute_ShouldPrintVerticalSourceBlocks_WhenHumanOutputIsRequested() {
		// Arrange
		KnowledgeSourceInfo primary = KnowledgeCommandTestData.Source("creatio") with {
			Diagnostic = "Primary source is healthy."
		};
		KnowledgeSourceInfo secondary = KnowledgeCommandTestData.Source("partner") with {
			Enabled = false,
			Priority = 10,
			Participation = "isolated",
			IsInstalled = false,
			IsValid = false,
			ActiveLibraryVersion = null,
			ActiveSequence = null,
			BundleDigest = null,
			ResolvedRevision = null,
			ActiveContentPath = null,
			UpdateAvailability = null,
			Diagnostic = "   "
		};
		_service.GetInfo(null, false).Returns(new KnowledgeSourceInfoResult(
			true,
			"appsettings.json",
			"knowledge",
			[primary, secondary],
			"Knowledge root is available."));

		// Act
		int exitCode = _command.Execute(new InfoKnowledgeOptions());

		// Assert
		exitCode.Should().Be(0, because: "the local knowledge state was reported successfully");
		KnowledgeCommandTestData.OutputCalls(_logger).Should().Equal(
			[
				"WriteInfo(Knowledge)",
				"WriteLine(  Settings file: appsettings.json)",
				"WriteLine(  Root path: knowledge)",
				"WriteLine(  Diagnostic: Knowledge root is available.)",
				"WriteLine()",
				"WriteInfo(Sources (2))",
				"WriteLine()",
				"WriteInfo(1. creatio)",
				"WriteLine(  Library: com.example.creatio)",
				"WriteLine(  Transport: git)",
				"WriteLine(  Enabled: yes)",
				"WriteLine(  Priority: 50)",
				"WriteLine(  Participation: supplement)",
				"WriteLine(  Installed: yes)",
				"WriteLine(  Valid: yes)",
				"WriteLine(  Library version: 1.0.0)",
				"WriteLine(  Sequence: 1)",
				"WriteLine(  Bundle digest: digest)",
				"WriteLine(  Revision: 0123456789abcdef)",
				"WriteLine(  Active path: content)",
				"WriteLine(  Update: up-to-date)",
				"WriteLine(  Diagnostic: Primary source is healthy.)",
				"WriteLine()",
				"WriteInfo(2. partner)",
				"WriteLine(  Library: com.example.partner)",
				"WriteLine(  Transport: git)",
				"WriteLine(  Enabled: no)",
				"WriteLine(  Priority: 10)",
				"WriteLine(  Participation: isolated)",
				"WriteLine(  Installed: no)",
				"WriteLine(  Valid: no)"
			],
			because: "every source field must stay ordered within its vertical block and empty optional values must be omitted");
	}
}

[TestFixture]
[Property("Module", "Command")]
public sealed class DeleteKnowledgeCommandTests : BaseCommandTests<DeleteKnowledgeOptions> {
	private IKnowledgeSourceManagementService _service = null!;
	private IInteractiveConsole _interactiveConsole = null!;
	private ILogger _logger = null!;
	private DeleteKnowledgeCommand _command = null!;

	protected override void AdditionalRegistrations(IServiceCollection services) {
		_service = Substitute.For<IKnowledgeSourceManagementService>();
		_interactiveConsole = Substitute.For<IInteractiveConsole>();
		_logger = Substitute.For<ILogger>();
		services.AddSingleton(_service);
		services.AddSingleton(_interactiveConsole);
		services.AddSingleton(_logger);
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<DeleteKnowledgeCommand>();
	}

	[TearDown]
	public override void TearDown() {
		_service.ClearReceivedCalls();
		_interactiveConsole.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("Fails closed without deleting any enabled source when interactive confirmation is refused.")]
	public void Execute_ShouldNotDelete_WhenConfirmationIsRefused() {
		// Arrange
		_interactiveConsole.Prompt(Arg.Any<string>()).Returns(false);

		// Act
		int exitCode = _command.Execute(new DeleteKnowledgeOptions());

		// Assert
		exitCode.Should().Be(1, because: "an all-source cache deletion requires explicit confirmation");
		_service.DidNotReceive().Delete(Arg.Any<string?>(), Arg.Any<bool>());
	}

	[Test]
	[Description("Uses force as explicit non-interactive confirmation and deletes only the selected source cache.")]
	public void Execute_ShouldDeleteSelectedSourceWithoutPrompt_WhenForceIsSet() {
		// Arrange
		_service.Delete("partner", true).Returns(KnowledgeCommandTestData.SuccessBatch("deleted", "partner"));

		// Act
		int exitCode = _command.Execute(new DeleteKnowledgeOptions { Source = "partner", Force = true });

		// Assert
		exitCode.Should().Be(0, because: "force authorizes deletion for the explicit source selector");
		_interactiveConsole.DidNotReceive().Prompt(Arg.Any<string>());
		_service.Received(1).Delete("partner", true);
	}
}

[TestFixture]
[Property("Module", "Command")]
public sealed class AddKnowledgeSourceCommandTests : BaseCommandTests<AddKnowledgeSourceOptions> {
	private IKnowledgeSourceManagementService _service = null!;
	private ILogger _logger = null!;
	private AddKnowledgeSourceCommand _command = null!;

	protected override void AdditionalRegistrations(IServiceCollection services) {
		_service = Substitute.For<IKnowledgeSourceManagementService>();
		_logger = Substitute.For<ILogger>();
		services.AddSingleton(_service);
		services.AddSingleton(_logger);
		services.AddTransient<AddKnowledgeSourceCommand>();
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<AddKnowledgeSourceCommand>();
	}

	[TearDown]
	public override void TearDown() {
		_service.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("Normalizes and delegates a disabled direct Git source with deterministic resolution settings.")]
	public void Execute_ShouldAddNormalizedGitSource_WhenOptionsAreValid() {
		// Arrange
		_service.Add(Arg.Any<KnowledgeSourceAddRequest>()).Returns(new KnowledgeSourceCommandResult(
			true, "added", "partner"));
		AddKnowledgeSourceOptions options = new() {
			Alias = " partner ",
			LibraryId = " com.example.partner ",
			Type = "GIT",
			Location = " https://example.test/knowledge.git ",
			Branch = " main ",
			Priority = 50,
			Participation = "Authoritative",
			Disabled = true
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "a valid source configuration was atomically accepted");
		_service.Received(1).Add(Arg.Is<KnowledgeSourceAddRequest>(request =>
			request.Alias == "partner"
			&& request.LibraryId == "com.example.partner"
			&& request.TransportType == "git"
			&& request.Location == "https://example.test/knowledge.git"
			&& request.TrustedKeyId == null
			&& request.TrustedPublicKeyPath == null
			&& request.Branch == "main"
			&& request.Priority == 50
			&& request.Participation == "authoritative"
			&& !request.Enabled));
	}

	[Test]
	[Description("Rejects a NuGet source without a package ID before configuration persistence.")]
	public void Execute_ShouldRejectNuGetSource_WhenPackageIdIsMissing() {
		// Arrange
		AddKnowledgeSourceOptions options = new() {
			Alias = "partner",
			LibraryId = "com.example.partner",
			Type = "nuget",
			Location = "https://packages.example.test/v3/index.json",
			TrustedKeyId = "partner-signing-2026",
			TrustedPublicKeyPath = Path.GetFullPath("partner-public.pem")
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a NuGet source cannot be retrieved without its package identity");
		_service.DidNotReceive().Add(Arg.Any<KnowledgeSourceAddRequest>());
	}

	[Test]
	[Description("Rejects Git-only reference options on a NuGet source instead of silently discarding them.")]
	public void Execute_ShouldRejectNuGetSource_WhenGitReferenceIsProvided() {
		// Arrange
		AddKnowledgeSourceOptions options = new() {
			Alias = "partner",
			LibraryId = "com.example.partner",
			Type = "nuget",
			Location = "https://packages.example.test/v3/index.json",
			TrustedKeyId = "partner-signing-2026",
			TrustedPublicKeyPath = Path.GetFullPath("partner-public.pem"),
			PackageId = "Example.Partner.Knowledge",
			Branch = "main"
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "transport-specific options must not be accepted and ignored");
		_service.DidNotReceive().Add(Arg.Any<KnowledgeSourceAddRequest>());
	}

	[Test]
	[Description("Rejects a relative trusted public-key path before source configuration persistence.")]
	public void Execute_ShouldRejectSource_WhenTrustedPublicKeyPathIsRelative() {
		// Arrange
		AddKnowledgeSourceOptions options = new() {
			Alias = "partner",
			LibraryId = "com.example.partner",
			Type = "nuget",
			Location = "https://packages.example.test/v3/index.json",
			TrustedKeyId = "partner-signing-2026",
			TrustedPublicKeyPath = "keys/partner-public.pem",
			PackageId = "Example.Partner.Knowledge"
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1,
			because: "a relative path could resolve to different key material in different working directories");
		_service.DidNotReceive().Add(Arg.Any<KnowledgeSourceAddRequest>());
	}
}

[TestFixture]
[Property("Module", "Command")]
public sealed class RemoveKnowledgeSourceCommandTests : BaseCommandTests<RemoveKnowledgeSourceOptions> {
	private IKnowledgeSourceManagementService _service = null!;
	private IInteractiveConsole _interactiveConsole = null!;
	private ILogger _logger = null!;
	private RemoveKnowledgeSourceCommand _command = null!;

	protected override void AdditionalRegistrations(IServiceCollection services) {
		_service = Substitute.For<IKnowledgeSourceManagementService>();
		_interactiveConsole = Substitute.For<IInteractiveConsole>();
		_logger = Substitute.For<ILogger>();
		services.AddSingleton(_service);
		services.AddSingleton(_interactiveConsole);
		services.AddSingleton(_logger);
		services.AddTransient<RemoveKnowledgeSourceCommand>();
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<RemoveKnowledgeSourceCommand>();
	}

	[TearDown]
	public override void TearDown() {
		_service.ClearReceivedCalls();
		_interactiveConsole.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("Does not remove source configuration or cache when confirmation is refused.")]
	public void Execute_ShouldNotRemoveSource_WhenConfirmationIsRefused() {
		// Arrange
		_interactiveConsole.Prompt(Arg.Any<string>()).Returns(false);

		// Act
		int exitCode = _command.Execute(new RemoveKnowledgeSourceOptions { Alias = "partner" });

		// Assert
		exitCode.Should().Be(1, because: "source removal deletes configuration and managed cache");
		_service.DidNotReceive().Remove(Arg.Any<string>(), Arg.Any<bool>());
	}

	[Test]
	[Description("Removes exactly the selected source when force supplies non-interactive confirmation.")]
	public void Execute_ShouldRemoveSelectedSource_WhenForceIsSet() {
		// Arrange
		_service.Remove("partner", true).Returns(new KnowledgeSourceCommandResult(true, "removed", "partner"));

		// Act
		int exitCode = _command.Execute(new RemoveKnowledgeSourceOptions { Alias = " partner ", Force = true });

		// Assert
		exitCode.Should().Be(0, because: "force explicitly authorizes the selected source removal");
		_service.Received(1).Remove("partner", true);
		_interactiveConsole.DidNotReceive().Prompt(Arg.Any<string>());
	}
}

[TestFixture]
[Property("Module", "Command")]
public sealed class KnowledgeSourceEnablementCommandTests : BaseCommandTests<EnableKnowledgeSourceOptions> {
	private IKnowledgeSourceManagementService _service = null!;
	private ILogger _logger = null!;
	private EnableKnowledgeSourceCommand _enableCommand = null!;
	private DisableKnowledgeSourceCommand _disableCommand = null!;

	protected override void AdditionalRegistrations(IServiceCollection services) {
		_service = Substitute.For<IKnowledgeSourceManagementService>();
		_logger = Substitute.For<ILogger>();
		services.AddSingleton(_service);
		services.AddSingleton(_logger);
		services.AddTransient<EnableKnowledgeSourceCommand>();
		services.AddTransient<DisableKnowledgeSourceCommand>();
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		_enableCommand = Container.GetRequiredService<EnableKnowledgeSourceCommand>();
		_disableCommand = Container.GetRequiredService<DisableKnowledgeSourceCommand>();
	}

	[TearDown]
	public override void TearDown() {
		_service.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("Delegates enable and disable as cache-preserving source configuration changes.")]
	public void Execute_ShouldToggleOnlySelectedSource_WhenAliasesAreProvided() {
		// Arrange
		_service.Enable("partner").Returns(new KnowledgeSourceCommandResult(true, "enabled", "partner"));
		_service.Disable("creatio").Returns(new KnowledgeSourceCommandResult(true, "disabled", "creatio"));

		// Act
		int enableExitCode = _enableCommand.Execute(new EnableKnowledgeSourceOptions { Alias = "partner" });
		int disableExitCode = _disableCommand.Execute(new DisableKnowledgeSourceOptions { Alias = "creatio" });

		// Assert
		enableExitCode.Should().Be(0, because: "the selected source was enabled successfully");
		disableExitCode.Should().Be(0, because: "the selected source was disabled successfully");
		_service.Received(1).Enable("partner");
		_service.Received(1).Disable("creatio");
	}
}

[TestFixture]
[Property("Module", "Command")]
public sealed class DisableKnowledgeSourceCommandDocumentationTests
	: BaseCommandTests<DisableKnowledgeSourceOptions> {
	protected override void AdditionalRegistrations(IServiceCollection services) {
		services.AddSingleton(Substitute.For<IKnowledgeSourceManagementService>());
	}
}

[TestFixture]
[Property("Module", "Command")]
public sealed class ListKnowledgeSourcesCommandTests : BaseCommandTests<ListKnowledgeSourcesOptions> {
	private IKnowledgeSourceManagementService _service = null!;
	private ILogger _logger = null!;
	private ListKnowledgeSourcesCommand _command = null!;

	protected override void AdditionalRegistrations(IServiceCollection services) {
		_service = Substitute.For<IKnowledgeSourceManagementService>();
		_logger = Substitute.For<ILogger>();
		services.AddSingleton(_service);
		services.AddSingleton(_logger);
		services.AddTransient<ListKnowledgeSourcesCommand>();
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<ListKnowledgeSourcesCommand>();
	}

	[TearDown]
	public override void TearDown() {
		_service.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("Emits configured sources as structured JSON without contacting their transports.")]
	public void Execute_ShouldEmitJson_WhenJsonIsRequested() {
		// Arrange
		_service.List().Returns(new KnowledgeSourceListResult(true, [KnowledgeCommandTestData.Source("partner")]));

		// Act
		int exitCode = _command.Execute(new ListKnowledgeSourcesOptions { Json = true });

		// Assert
		exitCode.Should().Be(0, because: "the source catalog was listed successfully");
		_service.Received(1).List();
		_logger.Received(1).WriteLine(Arg.Is<string>(value => value.Contains("\"alias\": \"partner\"", StringComparison.Ordinal)));
	}
}

internal static class KnowledgeCommandTestData {
	internal static string[] OutputCalls(ILogger logger) => logger.ReceivedCalls()
		.Select(call => $"{call.GetMethodInfo().Name}({string.Join(", ", call.GetArguments()
			.Select(argument => argument?.ToString() ?? "<null>"))})")
		.ToArray();

	internal static KnowledgeSourceBatchResult SuccessBatch(string message, string sourceAlias = "creatio") =>
		new(true, message, [new KnowledgeSourceOperationResult(sourceAlias, true, "succeeded", message)]);

	internal static KnowledgeSourceInfoResult Info(string sourceAlias) =>
		new(true, "appsettings.json", "knowledge", [Source(sourceAlias)]);

	internal static KnowledgeSourceInfo Source(string alias) => new(
		alias,
		$"com.example.{alias}",
		"git",
		"https://example.test/knowledge.git",
		"test-signing-key",
		Path.GetFullPath("test-public.pem"),
		true,
		50,
		"supplement",
		null,
		null,
		null,
		null,
		"main",
		null,
		null,
		true,
		true,
		"1.0.0",
		1,
		"digest",
		"0123456789abcdef",
		"content",
		"up-to-date",
		null);
}
