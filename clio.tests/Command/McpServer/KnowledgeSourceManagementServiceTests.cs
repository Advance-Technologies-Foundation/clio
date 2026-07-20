using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Clio.Command;
using Clio.Command.McpServer.Knowledge;
using Clio.Tests.Infrastructure;
using Clio.UserEnvironment;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class KnowledgeSourceManagementServiceTests {
	private ISettingsRepository _settings = null!;
	private IKnowledgeSourceInstallationStore _store = null!;
	private IKnowledgeBundleRuntime _runtime = null!;
	private IKnowledgeArtifactTransport _transport = null!;
	private IKnowledgeRepositoryTransport _gitTransport = null!;
	private IKnowledgeGitRepositoryReader _gitReader = null!;
	private MockFileSystem _fileSystem = null!;
	private ServiceProvider _container = null!;
	private IKnowledgeSourceManagementService _service = null!;
	private string _keyDirectory = null!;
	private string _publicKeyPath = null!;

	[SetUp]
	public void SetUp() {
		_settings = Substitute.For<ISettingsRepository>();
		_store = Substitute.For<IKnowledgeSourceInstallationStore>();
		_runtime = Substitute.For<IKnowledgeBundleRuntime>();
		_transport = Substitute.For<IKnowledgeArtifactTransport>();
		_transport.Type.Returns(KnowledgeSourceType.NuGet);
		_gitTransport = Substitute.For<IKnowledgeRepositoryTransport>();
		_gitTransport.Type.Returns(KnowledgeSourceType.Git);
		_gitReader = Substitute.For<IKnowledgeGitRepositoryReader>();
		_settings.TryAddKnowledgeSource(
			Arg.Any<string>(),
			Arg.Any<KnowledgeSourceConfiguration>()).Returns(true);
		_store.ExecuteWithSourceMutationLock(
			Arg.Any<string>(),
			Arg.Any<Func<KnowledgeSourceOperationResult>>()).Returns(call =>
				call.ArgAt<Func<KnowledgeSourceOperationResult>>(1)());
		_store.TryExecuteWithSourceMutationLock(Arg.Any<string>(), Arg.Any<Action>()).Returns(call => {
			call.ArgAt<Action>(1)();
			return true;
		});
		_runtime.ActivateGitRepository(
			Arg.Any<string>(),
			Arg.Any<int>(),
			Arg.Any<KnowledgeSourceParticipation>(),
			Arg.Any<KnowledgeGitRepositorySnapshot>()).Returns(new KnowledgeBundleActivationResult(
				KnowledgeBundleActivationStatus.Activated,
				KnowledgeBundleRejectionCode.None,
				1,
				1,
				null));
		_fileSystem = TestFileSystem.MockFileSystem();
		_keyDirectory = Path.Combine(Path.GetTempPath(), $"clio-source-trust-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_keyDirectory);
		_publicKeyPath = Path.Combine(_keyDirectory, "publisher-public.pem");
		using (ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256)) {
			File.WriteAllText(_publicKeyPath, key.ExportSubjectPublicKeyInfoPem());
		}
		ServiceCollection services = new();
		services.AddSingleton(_settings);
		services.AddSingleton(_store);
		services.AddSingleton(_runtime);
		services.AddSingleton<IKnowledgeArtifactTransport>(_transport);
		services.AddSingleton(_gitTransport);
		services.AddSingleton(_gitReader);
		services.AddSingleton<System.IO.Abstractions.IFileSystem>(_fileSystem);
		services.AddSingleton<IKnowledgeSourceManagementService, KnowledgeSourceManagementService>();
		_container = services.BuildServiceProvider();
		_service = _container.GetRequiredService<IKnowledgeSourceManagementService>();
	}

	[TearDown]
	public void TearDown() {
		_container.Dispose();
		if (Directory.Exists(_keyDirectory)) {
			Directory.Delete(_keyDirectory, recursive: true);
		}
	}

	[Test]
	[Description("Installing all sources selects only enabled sources and publishes the validated result under its own alias.")]
	public void Install_ShouldSelectOnlyEnabledSources_WhenAliasIsOmitted() {
		// Arrange
		_settings.GetKnowledgeConfiguration().Returns(Configuration(
			("alpha", Source("com.example.alpha", enabled: true)),
			("beta", Source("com.example.beta", enabled: false))));
		ConfigureCurrent(_ => null);
		byte[] bundle = [1, 2, 3];
		_transport.Retrieve(Arg.Any<KnowledgeTransportRequest>()).Returns(new KnowledgeTransportResult(
			KnowledgeTransportStatus.Downloaded,
			"1.2.0",
			bundle,
			null));
		_runtime.Validate(Arg.Any<Stream>(), Arg.Any<string?>(), "com.example.alpha").Returns(new KnowledgeBundleValidationResult(
			KnowledgeBundleActivationStatus.Activated,
			KnowledgeBundleRejectionCode.None,
			12,
			null,
			"com.example.alpha",
			"1.2.0",
			"digest"));
		_store.Publish(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ulong>(), Arg.Any<string>(),
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<bool>(),
			Arg.Any<KnowledgeSourceGenerationPointer?>()).Returns(new KnowledgeInstallationResult(
				KnowledgeInstallationStatus.Installed, "installed"));

		// Act
		KnowledgeSourceBatchResult result = _service.Install(sourceAlias: null);

		// Assert
		result.Success.Should().BeTrue(because: "the enabled source produced a valid install candidate");
		result.Sources.Should().ContainSingle(
			because: "bulk install must skip disabled sources rather than touching their retained caches");
		result.Sources[0].SourceAlias.Should().Be("alpha",
			because: "only the enabled source should participate in the bulk operation");
		_transport.ReceivedCalls().Count(call =>
			call.GetMethodInfo().Name == nameof(IKnowledgeArtifactTransport.Retrieve)
			&& call.GetArguments()[0] is KnowledgeTransportRequest request
			&& request.SourceAlias == "alpha").Should().Be(1,
			because: "transport retrieval must be scoped to the selected source alias");
		_store.ReceivedCalls().Count(call =>
			call.GetMethodInfo().Name == nameof(IKnowledgeSourceInstallationStore.Publish)
			&& call.GetArguments()[0] as string == "alpha"
			&& call.GetArguments()[8] is false).Should().Be(1,
			because: "the first lifecycle operation must publish as an install for alpha only");
	}

	[Test]
	[Description("Updating an installed source passes its active revision and pointer through transport and compare-and-swap publication.")]
	public void Update_ShouldUseActiveRevisionAndExpectedPointer_WhenSourceIsInstalled() {
		// Arrange
		_settings.GetKnowledgeConfiguration().Returns(Configuration(
			("alpha", Source("com.example.alpha", enabled: true))));
		KnowledgeSourceCurrentState state = State("alpha", "com.example.alpha", 10, "revision-old");
		ConfigureCurrent(alias => alias == "alpha" ? state : null);
		KnowledgeTransportRequest? observedRequest = null;
		_transport.Retrieve(Arg.Any<KnowledgeTransportRequest>()).Returns(call => {
			observedRequest = call.Arg<KnowledgeTransportRequest>();
			return new KnowledgeTransportResult(
				KnowledgeTransportStatus.Downloaded, "revision-new", [4, 5, 6], null);
		});
		_runtime.Validate(Arg.Any<Stream>(), Arg.Any<string?>(), "com.example.alpha").Returns(new KnowledgeBundleValidationResult(
			KnowledgeBundleActivationStatus.Activated,
			KnowledgeBundleRejectionCode.None,
			11,
			null,
			"com.example.alpha",
			"1.1.0",
			"digest-new"));
		_store.Publish(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ulong>(), Arg.Any<string>(),
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<bool>(),
			Arg.Any<KnowledgeSourceGenerationPointer?>()).Returns(new KnowledgeInstallationResult(
				KnowledgeInstallationStatus.Updated, "updated"));

		// Act
		KnowledgeSourceBatchResult result = _service.Update("alpha");

		// Assert
		result.Success.Should().BeTrue(because: "a strictly newer validated source candidate was published");
		observedRequest.Should().NotBeNull(because: "update must invoke the configured source transport");
		observedRequest!.ActiveRevision.Should().Be("revision-old",
			because: "the transport needs the installed revision to return no-candidate when already current");
		_store.ReceivedCalls().Count(call =>
			call.GetMethodInfo().Name == nameof(IKnowledgeSourceInstallationStore.Publish)
			&& call.GetArguments()[0] as string == "alpha"
			&& call.GetArguments()[8] is true
			&& Equals(call.GetArguments()[9], state.Active)).Should().Be(1,
			because: "updates must compare-and-swap against the exact pointer observed before download");
	}

	[Test]
	[Description("Disabling a source updates its kill switch, deactivates it immediately, and leaves installation storage untouched.")]
	public void Disable_ShouldDeactivateOnlySelectedLibrary_WithoutDeletingCache() {
		// Arrange
		const string alias = "alpha";

		// Act
		KnowledgeSourceCommandResult result = _service.Disable(alias);

		// Assert
		result.Success.Should().BeTrue(because: "the settings repository accepted the source kill-switch update");
		_settings.ReceivedCalls().Count(call =>
			call.GetMethodInfo().Name == nameof(ISettingsRepository.SetKnowledgeSourceEnabled)
			&& call.GetArguments()[0] as string == alias
			&& call.GetArguments()[1] is false).Should().Be(1,
			because: "disable must persist only the selected source's enabled flag");
		_runtime.ReceivedCalls().Count(call =>
			call.GetMethodInfo().Name == nameof(IKnowledgeBundleRuntime.DeactivateLibrary)
			&& call.GetArguments()[0] as string == alias).Should().Be(1,
			because: "disabled knowledge must stop serving immediately without waiting for a later lookup");
		_store.ReceivedCalls().Should().BeEmpty(
			because: "disabling a source retains every installed generation for later re-enable");
	}

	[Test]
	[Description("Adding a source persists its publisher-specific signing key identity and absolute public-key path.")]
	public void Add_ShouldPersistPerSourceSigningTrust_WhenConfigurationIsValid() {
		// Arrange
		string publicKeyPath = _publicKeyPath;
		_settings.GetKnowledgeConfiguration().Returns(Configuration());
		KnowledgeSourceAddRequest request = new(
			"partner",
			"com.example.partner",
			"nuget",
			"https://packages.example.test/v3/index.json",
			"partner-signing-2026",
			publicKeyPath,
			"Example.Partner.Knowledge",
			null,
			null,
			null,
			true,
			50,
			"supplement");

		// Act
		KnowledgeSourceCommandResult result = _service.Add(request);

		// Assert
		result.Success.Should().BeTrue(
			because: "the source supplies an explicit signing identity and fully qualified public-key path");
		_settings.Received(1).TryAddKnowledgeSource(
			"partner",
			Arg.Is<KnowledgeSourceConfiguration>(source =>
				source.TrustedKeyId == "partner-signing-2026"
				&& source.TrustedPublicKeyPath == Path.GetFullPath(publicKeyPath)));
	}

	[Test]
	[Description("Rejects a missing trusted public-key file before adding a publisher trust root to settings.")]
	public void Add_ShouldRejectSource_WhenTrustedPublicKeyDoesNotExist() {
		// Arrange
		string missingPath = Path.Combine(_keyDirectory, "missing-public.pem");
		_settings.GetKnowledgeConfiguration().Returns(Configuration());
		KnowledgeSourceAddRequest request = new(
			"partner", "com.example.partner", "nuget",
			"https://packages.example.test/v3/index.json", "partner-signing-2026", missingPath,
			"Example.Partner.Knowledge", null, null, null, true, 50, "supplement");

		// Act
		KnowledgeSourceCommandResult result = _service.Add(request);

		// Assert
		result.Success.Should().BeFalse(
			because: "a configured signing trust root must be readable and valid at publication time");
		result.Message.Should().Contain("existing bounded local regular file",
			because: "the operator needs an actionable safe-path requirement without leaking key material");
		_settings.DidNotReceiveWithAnyArgs().TryAddKnowledgeSource(default!, default!);
	}

	[Test]
	[Description("Rejects a legacy v0 bundle before publishing a configured multi-source installation.")]
	public void Install_ShouldSkipPublish_WhenConfiguredSourceRejectsLegacyContract() {
		// Arrange
		_settings.GetKnowledgeConfiguration().Returns(Configuration(
			("alpha", Source("com.example.alpha", enabled: true))));
		ConfigureCurrent(_ => null);
		_transport.Retrieve(Arg.Any<KnowledgeTransportRequest>()).Returns(new KnowledgeTransportResult(
			KnowledgeTransportStatus.Downloaded, "revision-v0", [1, 2, 3], null));
		_runtime.Validate(Arg.Any<Stream>(), Arg.Any<string?>(), "com.example.alpha").Returns(
			new KnowledgeBundleValidationResult(
				KnowledgeBundleActivationStatus.Rejected,
				KnowledgeBundleRejectionCode.UnsupportedContract,
				1,
				"Configured sources require the multi-source contract."));
		_runtime.ClearReceivedCalls();

		// Act
		KnowledgeSourceBatchResult result = _service.Install("alpha");

		// Assert
		result.Success.Should().BeFalse(
			because: "legacy bundles cannot enter configured multi-source storage");
		_store.DidNotReceiveWithAnyArgs().Publish(
			default!, default!, default!, default, default!, default!, default!, default!, default, default);
		object[][] validationArguments = _runtime.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == nameof(IKnowledgeBundleRuntime.Validate))
			.Select(call => call.GetArguments())
			.ToArray();
		validationArguments.Should().NotBeEmpty(
			because: "downloaded candidates must cross the runtime validation boundary before publication");
		validationArguments.Should().OnlyContain(arguments =>
			arguments[2] as string == "com.example.alpha",
			because: "every fallback candidate must be validated against the configured library identity");
	}

	[Test]
	[Description("Rejected transport diagnostics are redacted and never publish candidate content.")]
	public void Install_ShouldRedactDiagnosticAndSkipPublish_WhenTransportRejectsCandidate() {
		// Arrange
		const string secret = "transport-secret-abc123";
		_settings.GetKnowledgeConfiguration().Returns(Configuration(
			("alpha", Source("com.example.alpha", enabled: true))));
		ConfigureCurrent(_ => null);
		_transport.Retrieve(Arg.Any<KnowledgeTransportRequest>()).Returns(new KnowledgeTransportResult(
			KnowledgeTransportStatus.Rejected,
			"1.2.0",
			null,
			null,
			Diagnostic: $"request failed: Authorization: Bearer {secret}"));

		// Act
		KnowledgeSourceBatchResult result = _service.Install("alpha");

		// Assert
		result.Success.Should().BeFalse(because: "a rejected transport result is not an installable candidate");
		result.Sources.Should().ContainSingle(
			because: "the source-specific failure must remain visible without affecting other libraries");
		result.Sources[0].Message.Should().NotContain(secret,
			because: "transport credentials must never escape through command diagnostics");
		result.Sources[0].Message.Should().Contain("[redacted]",
			because: "the operator should see that sensitive diagnostic material was intentionally removed");
		_store.ReceivedCalls().Count(call =>
			call.GetMethodInfo().Name == nameof(IKnowledgeSourceInstallationStore.Publish)).Should().Be(0,
			because: "rejected transport output must never reach immutable storage");
	}

	[Test]
	[Description("Fails a first installation when the configured transport returns no candidate.")]
	public void Install_ShouldFail_WhenColdSourceReturnsNoCandidate() {
		// Arrange
		_settings.GetKnowledgeConfiguration().Returns(Configuration(
			("alpha", Source("com.example.alpha", enabled: true))));
		ConfigureCurrent(_ => null);
		_transport.Retrieve(Arg.Any<KnowledgeTransportRequest>()).Returns(new KnowledgeTransportResult(
			KnowledgeTransportStatus.NoCandidate,
			null,
			null,
			null));

		// Act
		KnowledgeSourceBatchResult result = _service.Install("alpha");

		// Assert
		result.Success.Should().BeFalse(
			because: "an empty or unreachable source cannot satisfy a first installation");
		result.Sources.Should().ContainSingle()
			.Which.Status.Should().Be("failed",
				because: "cold no-candidate must remain distinguishable from an installed source being up to date");
		_store.DidNotReceiveWithAnyArgs().Publish(
			default!, default!, default!, default, default!, default!, default!, default!, default, default);
	}

	[Test]
	[Description("Updating an installed source fails when its transport cannot determine whether a newer revision exists.")]
	public void Update_ShouldFail_WhenTransportRetrievalFails() {
		// Arrange
		_settings.GetKnowledgeConfiguration().Returns(Configuration(
			("alpha", Source("com.example.alpha", enabled: true))));
		ConfigureCurrent(_ => State("alpha", "com.example.alpha", 10, "1.0.0"));
		_transport.Retrieve(Arg.Any<KnowledgeTransportRequest>()).Returns(new KnowledgeTransportResult(
			KnowledgeTransportStatus.Failed,
			null,
			null,
			null,
			Diagnostic: "NuGet feed timed out"));

		// Act
		KnowledgeSourceBatchResult result = _service.Update("alpha");

		// Assert
		result.Success.Should().BeFalse(
			because: "a failed feed check cannot establish that the installed generation is current");
		result.Sources.Should().ContainSingle()
			.Which.Status.Should().Be("failed",
				because: "retrieval failure must remain distinct from a genuine no-candidate result");
		result.Sources[0].Message.Should().Contain("timed out",
			because: "the operator needs the bounded transport diagnostic to retry or repair the source");
		_store.DidNotReceiveWithAnyArgs().Publish(
			default!, default!, default!, default, default!, default!, default!, default!, default, default);
	}

	[Test]
	[Description("Info reports unknown update availability when the configured transport check fails.")]
	public void GetInfo_ShouldReportUnknown_WhenUpdateCheckFails() {
		// Arrange
		_settings.GetKnowledgeConfiguration().Returns(Configuration(
			("alpha", Source("com.example.alpha", enabled: true))));
		KnowledgeSourceCurrentState state = State("alpha", "com.example.alpha", 10, "1.0.0");
		ConfigureCurrent(_ => state);
		_store.TryReadCandidate("alpha", state.Active, out Arg.Any<InstalledKnowledgeSourceCandidate?>(),
			out Arg.Any<string?>()).Returns(call => {
				call[2] = null;
				call[3] = null;
				return false;
			});
		KnowledgeTransportRequest? observedRequest = null;
		_transport.Retrieve(Arg.Any<KnowledgeTransportRequest>()).Returns(call => {
			observedRequest = call.Arg<KnowledgeTransportRequest>();
			return new KnowledgeTransportResult(
				KnowledgeTransportStatus.Failed,
				null,
				null,
				null,
				Diagnostic: "NuGet package download timed out");
		});

		// Act
		KnowledgeSourceInfoResult result = _service.GetInfo("alpha", checkUpdates: true);

		// Assert
		result.Success.Should().BeTrue(
			because: "the configured and installed source can still be described when a remote check fails");
		result.Sources.Should().ContainSingle();
		result.Sources[0].UpdateAvailability.Should().Be("unknown",
			because: "a failed remote check cannot prove that the installed generation is current");
		result.Sources[0].Diagnostic.Should().Contain("timed out",
			because: "info must explain why update availability is unknown");
		observedRequest.Should().NotBeNull(
			because: "the explicit update check must invoke the configured transport");
		observedRequest!.TransportDeadlineMilliseconds.Should().BeInRange(1, 30_000,
			because: "an info update probe must never leave transport duration unbounded");
	}

	[Test]
	[Description("Installation rejects an invalid highest candidate and accepts the next lower valid candidate within the bounded fallback loop.")]
	public void Install_ShouldPublishLowerCandidate_WhenHigherCandidateIsRejected() {
		// Arrange
		_settings.GetKnowledgeConfiguration().Returns(Configuration(
			("alpha", Source("com.example.alpha", enabled: true))));
		ConfigureCurrent(_ => null);
		KnowledgeTransportRequest? fallbackRequest = null;
		int retrievalCount = 0;
		_transport.Retrieve(Arg.Any<KnowledgeTransportRequest>()).Returns(call => {
			if (retrievalCount++ > 0) {
				fallbackRequest = call.Arg<KnowledgeTransportRequest>();
				return new KnowledgeTransportResult(KnowledgeTransportStatus.Downloaded, "1.9.0", [1], null);
			}
			return new KnowledgeTransportResult(KnowledgeTransportStatus.Downloaded, "2.0.0", [2], null);
		});
		_runtime.Validate(Arg.Any<Stream>(), Arg.Any<string?>(), "com.example.alpha").Returns(
			new KnowledgeBundleValidationResult(
				KnowledgeBundleActivationStatus.Rejected,
				KnowledgeBundleRejectionCode.InvalidContent,
				2,
				"invalid highest candidate"),
			new KnowledgeBundleValidationResult(
				KnowledgeBundleActivationStatus.Activated,
				KnowledgeBundleRejectionCode.None,
				1,
				null,
				"com.example.alpha",
				"1.9.0",
				"digest-lower"));
		_store.Publish(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ulong>(), Arg.Any<string>(),
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<bool>(),
			Arg.Any<KnowledgeSourceGenerationPointer?>(), Arg.Any<bool>()).Returns(new KnowledgeInstallationResult(
				KnowledgeInstallationStatus.Installed, "installed"));

		// Act
		KnowledgeSourceBatchResult result = _service.Install("alpha");

		// Assert
		result.Success.Should().BeTrue(
			because: "one rejected candidate must not block a lower compatible candidate in the same catalog");
		fallbackRequest.Should().NotBeNull(because: "the transport must be invoked again for bounded fallback");
		fallbackRequest!.RejectedRevisions.Should().Contain("2.0.0",
			because: "fallback retrieval must never repeat a rejected revision");
		fallbackRequest.FallbackCeilingRevision.Should().Be("2.0.0",
			because: "the next lookup must remain below the rejected candidate");
		_store.ReceivedCalls().Count(call =>
			call.GetMethodInfo().Name == nameof(IKnowledgeSourceInstallationStore.Publish)
			&& call.GetArguments()[0] as string == "alpha"
			&& call.GetArguments()[2] as string == "1.9.0"
			&& (ulong)call.GetArguments()[3]! == 1
			&& call.GetArguments()[6] as string == "1.9.0").Should().Be(1,
			because: "only the lower validated fallback candidate may reach immutable publication");
	}

	[Test]
	[Description("Install repairs a corrupt current generation by retrieving the same revision and publishing equal signed sequence and digest as a new generation.")]
	public void Install_ShouldRequestExplicitRepair_WhenCurrentGenerationIsInvalid() {
		// Arrange
		_settings.GetKnowledgeConfiguration().Returns(Configuration(
			("alpha", Source("com.example.alpha", enabled: true))));
		KnowledgeSourceCurrentState state = State("alpha", "com.example.alpha", 10, "1.0.0");
		ConfigureCurrent(_ => state);
		_store.TryReadCandidate("alpha", state.Active, out Arg.Any<InstalledKnowledgeSourceCandidate?>(),
			out Arg.Any<string?>()).Returns(call => {
				call[2] = null;
				call[3] = "installed digest mismatch";
				return false;
			});
		KnowledgeTransportRequest? observedRequest = null;
		_transport.Retrieve(Arg.Any<KnowledgeTransportRequest>()).Returns(call => {
			observedRequest = call.Arg<KnowledgeTransportRequest>();
			return new KnowledgeTransportResult(KnowledgeTransportStatus.Downloaded, "1.0.0", [1, 2], null);
		});
		_runtime.Validate(Arg.Any<Stream>(), Arg.Any<string?>(), "com.example.alpha").Returns(new KnowledgeBundleValidationResult(
			KnowledgeBundleActivationStatus.Activated, KnowledgeBundleRejectionCode.None, 10, null,
			"com.example.alpha", "1.0.0", state.Active.BundleDigest));
		_store.Publish(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ulong>(), Arg.Any<string>(),
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<bool>(),
			Arg.Any<KnowledgeSourceGenerationPointer?>(), Arg.Any<bool>()).Returns(new KnowledgeInstallationResult(
				KnowledgeInstallationStatus.Updated, "repaired"));

		// Act
		KnowledgeSourceBatchResult result = _service.Install("alpha");

		// Assert
		result.Success.Should().BeTrue(because: "an exact signed replacement should repair corrupt local bytes");
		observedRequest!.ActiveRevision.Should().BeNull(
			because: "repair must retrieve the active revision rather than treating it as already current");
		observedRequest.ExactRevision.Should().Be(state.Active.ResolvedRevision,
			because: "repair must request the exact immutable revision recorded by the damaged generation");
		_store.ReceivedCalls().Count(call =>
			call.GetMethodInfo().Name == nameof(IKnowledgeSourceInstallationStore.Publish)
			&& call.GetArguments()[0] as string == "alpha"
			&& (ulong)call.GetArguments()[3]! == 10
			&& call.GetArguments()[8] is true
			&& Equals(call.GetArguments()[9], state.Active)
			&& call.GetArguments()[10] is true).Should().Be(1,
			because: "repair publication must carry the observed pointer and explicit equal-sequence repair authority");
	}

	[TestCase(true)]
	[TestCase(false)]
	[Description("Enable and disable return a structured command failure when the requested source alias is missing.")]
	public void SetEnabled_ShouldReturnStructuredFailure_WhenAliasIsMissing(bool enable) {
		// Arrange
		_settings.When(repository => repository.SetKnowledgeSourceEnabled("missing", enable))
			.Do(_ => throw new KeyNotFoundException("Knowledge source 'missing' is not configured."));

		// Act
		KnowledgeSourceCommandResult result = enable ? _service.Enable("missing") : _service.Disable("missing");

		// Assert
		result.Success.Should().BeFalse(because: "missing aliases are expected operator errors, not exceptions");
		result.SourceAlias.Should().Be("missing", because: "the structured failure must identify the requested alias");
		result.Message.Should().Contain("not configured",
			because: "the operator needs an actionable source-selection diagnostic");
		_runtime.ReceivedCalls().Count(call =>
			call.GetMethodInfo().Name == nameof(IKnowledgeBundleRuntime.DeactivateLibrary)).Should().Be(0,
			because: "a failed disable must not deactivate an unrelated or nonexistent library");
	}

	[Test]
	[Description("Source removal wins the settings compare-and-swap and deactivates trust before deleting its cache.")]
	public void Remove_ShouldDeleteCacheOnlyAfterConfigurationCasSucceeds() {
		// Arrange
		KnowledgeSourceConfiguration source = Source("com.example.alpha", enabled: true);
		List<string> order = [];
		_settings.GetKnowledgeConfiguration().Returns(Configuration(("alpha", source)));
		_settings.TryRemoveKnowledgeSource("alpha", source).Returns(_ => {
			order.Add("configuration-cas");
			return true;
		});
		_runtime.When(runtime => runtime.DeactivateLibrary("alpha")).Do(_ => order.Add("deactivate"));
		_store.Delete("alpha", confirmed: true).Returns(_ => {
			order.Add("cache-delete");
			return new KnowledgeInstallationResult(KnowledgeInstallationStatus.Deleted, "deleted");
		});

		// Act
		KnowledgeSourceCommandResult result = _service.Remove("alpha", confirmed: true);

		// Assert
		result.Success.Should().BeTrue(because: "the unchanged source can be removed atomically");
		order.Should().Equal(new[] { "configuration-cas", "deactivate", "cache-delete" },
			because: "removing a trusted source must stop it serving before best-effort cache cleanup");
	}

	[Test]
	[Description("The built-in Creatio-curated library cannot be removed and directs operators to its enabled kill switch.")]
	public void Remove_ShouldRefuseBuiltInCuratedLibrary() {
		// Arrange
		KnowledgeSourceConfiguration source = Source(CuratedKnowledgeSourceDefaults.LibraryId, enabled: true);
		_settings.GetKnowledgeConfiguration().Returns(Configuration(("creatio-curated", source)));

		// Act
		KnowledgeSourceCommandResult result = _service.Remove("creatio-curated", confirmed: true);

		// Assert
		result.Success.Should().BeFalse(
			because: "the next MCP bootstrap must be able to rely on the built-in source configuration existing");
		result.Message.Should().Contain("cannot be removed",
			because: "operators need a direct explanation of the built-in source invariant");
		result.Message.Should().Contain("disable-knowledge-source --alias creatio-curated",
			because: "the supported kill switch must include the exact valid CLI option in the refusal message");
		_settings.DidNotReceiveWithAnyArgs().TryRemoveKnowledgeSource(default!, default!);
		_runtime.DidNotReceiveWithAnyArgs().DeactivateLibrary(default!);
		_store.DidNotReceiveWithAnyArgs().Delete(default!, default);
	}

	[Test]
	[Description("Source removal stays deactivated when best-effort cache deletion fails.")]
	public void Remove_ShouldKeepSourceDeactivated_WhenCacheDeleteFails() {
		// Arrange
		KnowledgeSourceConfiguration source = Source("com.example.alpha", enabled: true);
		_settings.GetKnowledgeConfiguration().Returns(
			Configuration(("alpha", source)),
			Configuration());
		_settings.TryRemoveKnowledgeSource("alpha", source).Returns(true);
		_store.Delete("alpha", confirmed: true).Returns(new KnowledgeInstallationResult(
			KnowledgeInstallationStatus.Failed,
			"cache is locked"));

		// Act
		KnowledgeSourceCommandResult result = _service.Remove("alpha", confirmed: true);

		// Assert
		result.Success.Should().BeFalse(because: "the trusted source was removed but cache cleanup is incomplete");
		result.Message.Should().Contain("orphaned cache",
			because: "operators need an actionable cleanup diagnostic without restoring trust");
		_settings.DidNotReceiveWithAnyArgs().TryAddKnowledgeSource(default!, default!);
		_runtime.Received(1).DeactivateLibrary("alpha");
	}

	[Test]
	[Description("Source removal retains the cache and active runtime when source configuration changes before compare-and-swap.")]
	public void Remove_ShouldNotDeleteCache_WhenConfigurationCasLosesRace() {
		// Arrange
		KnowledgeSourceConfiguration source = Source("com.example.alpha", enabled: true);
		_settings.GetKnowledgeConfiguration().Returns(Configuration(("alpha", source)));
		_settings.TryRemoveKnowledgeSource("alpha", source).Returns(false);

		// Act
		KnowledgeSourceCommandResult result = _service.Remove("alpha", confirmed: true);

		// Assert
		result.Success.Should().BeFalse(because: "a concurrent source edit invalidates the removal authority");
		_store.ReceivedCalls().Count(call =>
			call.GetMethodInfo().Name == nameof(IKnowledgeSourceInstallationStore.Delete)).Should().Be(0,
			because: "a lost configuration race must retain the last known-good cache");
		_runtime.ReceivedCalls().Count(call =>
			call.GetMethodInfo().Name == nameof(IKnowledgeBundleRuntime.DeactivateLibrary)).Should().Be(0,
			because: "a lost configuration race must retain the active library");
	}

	[Test]
	[Description("A caller-supplied single-source installation deadline is propagated to the repository transport.")]
	public void Install_ShouldUseCallerDeadline_WhenOneGitSourceIsSelected() {
		// Arrange
		const int startupDeadlineMilliseconds = 5_000;
		KnowledgeSourceConfiguration source = GitSource("com.example.git");
		source.Branch = "main";
		_settings.GetKnowledgeConfiguration().Returns(Configuration(("git-source", source)));
		string repositoryPath = TestFileSystem.GetRootedPath("knowledge", "git-source", "repository");
		_store.GetGitRepositoryPath("git-source", true).Returns(repositoryPath);
		KnowledgeTransportRequest? observedRequest = null;
		_gitTransport.Synchronize(Arg.Do<KnowledgeTransportRequest>(request => observedRequest = request), repositoryPath)
			.Returns(new KnowledgeTransportResult(
				KnowledgeTransportStatus.Failed,
				null,
				null,
				repositoryPath,
				"synthetic timeout"));

		// Act
		KnowledgeSourceBatchResult result = _service.Install(
			"git-source",
			startupDeadlineMilliseconds,
			CancellationToken.None);

		// Assert
		result.Success.Should().BeFalse(
			because: "the synthetic transport failure must remain visible to the startup caller");
		observedRequest.Should().NotBeNull(
			because: "the selected Git source must be handed to its repository transport");
		observedRequest!.TransportDeadlineMilliseconds.Should().Be(startupDeadlineMilliseconds,
			because: "a bounded MCP startup must not silently expand to the ordinary thirty-second operation deadline");
	}

	[Test]
	[Description("A discovered Git default branch is persisted with targeted compare-and-swap only after successful repository validation.")]
	public void Install_ShouldPersistDiscoveredGitBranch_AfterSuccessfulValidation() {
		// Arrange
		KnowledgeSourceConfiguration source = Source("com.example.git", enabled: true);
		source.Type = KnowledgeSourceType.Git;
		source.PackageId = null;
		source.TrustedKeyId = null;
		source.TrustedPublicKeyPath = null;
		source.Location = "https://example.invalid/knowledge.git";
		_settings.GetKnowledgeConfiguration().Returns(Configuration(("git-source", source)));
		string repositoryPath = TestFileSystem.GetRootedPath("knowledge", "git-source", "repository");
		_fileSystem.AddDirectory(Path.Combine(repositoryPath, ".git"));
		_store.GetGitRepositoryPath("git-source", true).Returns(repositoryPath);
		_gitTransport.Synchronize(Arg.Any<KnowledgeTransportRequest>(), repositoryPath).Returns(new KnowledgeTransportResult(
			KnowledgeTransportStatus.Downloaded,
			"0123456789abcdef0123456789abcdef01234567",
			null,
			repositoryPath,
			null,
			ResolvedBranch: "main"));
		KnowledgeGitRepositorySnapshot snapshot = GitSnapshot("com.example.git");
		_gitReader.TryRead(repositoryPath, "com.example.git", out Arg.Any<KnowledgeGitRepositorySnapshot?>(),
			out Arg.Any<string?>()).Returns(call => {
				call[2] = snapshot;
				call[3] = null;
				return true;
			});
		_settings.TrySetKnowledgeSourceBranch("git-source", source, "main").Returns(true);

		// Act
		KnowledgeSourceBatchResult result = _service.Install("git-source");

		// Assert
		result.Success.Should().BeTrue(because: "repository validation and targeted branch persistence both succeeded");
		_settings.ReceivedCalls().Count(call =>
			call.GetMethodInfo().Name == nameof(ISettingsRepository.TrySetKnowledgeSourceBranch)
			&& call.GetArguments()[0] as string == "git-source"
			&& ReferenceEquals(call.GetArguments()[1], source)
			&& call.GetArguments()[2] as string == "main").Should().Be(1,
			because: "the targeted compare-and-swap must use the exact source snapshot used for retrieval");
	}

	[Test]
	[Description("Installing an existing Git source still synchronizes it so origin and ref provenance are revalidated.")]
	public void Install_ShouldSynchronize_WhenGitRepositoryAlreadyExists() {
		// Arrange
		KnowledgeSourceConfiguration source = GitSource("com.example.git");
		source.Branch = "main";
		_settings.GetKnowledgeConfiguration().Returns(Configuration(("git-source", source)));
		string repositoryPath = TestFileSystem.GetRootedPath("knowledge", "git-source", "repository");
		_fileSystem.AddDirectory(Path.Combine(repositoryPath, ".git"));
		_store.GetGitRepositoryPath("git-source", true).Returns(repositoryPath);
		const string revision = "1111111111111111111111111111111111111111";
		_gitTransport.GetCurrentRevision(repositoryPath).Returns(revision);
		_gitTransport.Synchronize(Arg.Any<KnowledgeTransportRequest>(), repositoryPath).Returns(
			new KnowledgeTransportResult(
				KnowledgeTransportStatus.NoCandidate,
				revision,
				null,
				repositoryPath,
				ResolvedBranch: "main",
				ResolvedCommit: revision));
		_gitReader.TryRead(repositoryPath, source.LibraryId, out Arg.Any<KnowledgeGitRepositorySnapshot?>(),
			out Arg.Any<string?>()).Returns(call => {
				call[2] = GitSnapshot(source.LibraryId);
				call[3] = null;
				return true;
			});

		// Act
		KnowledgeSourceBatchResult result = _service.Install("git-source");

		// Assert
		result.Success.Should().BeTrue(because: "the configured origin and existing checkout were valid");
		result.Sources.Single().Status.Should().Be("already-installed",
			because: "the synchronized checkout resolved to the already active commit");
		_gitTransport.Received(1).Synchronize(
			Arg.Is<KnowledgeTransportRequest>(request => request.ActiveRevision == revision),
			repositoryPath);
	}

	[Test]
	[Description("A discovered Git default branch is not persisted when repository validation fails.")]
	public void Install_ShouldNotPersistDiscoveredGitBranch_WhenValidationFails() {
		// Arrange
		KnowledgeSourceConfiguration source = Source("com.example.git", enabled: true);
		source.Type = KnowledgeSourceType.Git;
		source.PackageId = null;
		source.TrustedKeyId = null;
		source.TrustedPublicKeyPath = null;
		source.Location = "https://example.invalid/knowledge.git";
		_settings.GetKnowledgeConfiguration().Returns(Configuration(("git-source", source)));
		string repositoryPath = TestFileSystem.GetRootedPath("knowledge", "git-source", "repository");
		_store.GetGitRepositoryPath("git-source", true).Returns(repositoryPath);
		const string commit = "0123456789abcdef0123456789abcdef01234567";
		_gitTransport.Synchronize(Arg.Any<KnowledgeTransportRequest>(), repositoryPath).Returns(_ => {
			_fileSystem.AddDirectory(Path.Combine(repositoryPath, ".git"));
			return new KnowledgeTransportResult(
				KnowledgeTransportStatus.Downloaded, commit, null, repositoryPath, ResolvedBranch: "main");
		});
		_gitReader.TryRead(repositoryPath, "com.example.git", out Arg.Any<KnowledgeGitRepositorySnapshot?>(),
			out Arg.Any<string?>()).Returns(call => {
				call[2] = null;
				call[3] = "invalid repository";
				return false;
			});

		// Act
		KnowledgeSourceBatchResult result = _service.Install("git-source");

		// Assert
		result.Success.Should().BeFalse(because: "an invalid direct Git repository cannot complete installation");
		_settings.ReceivedCalls().Count(call =>
			call.GetMethodInfo().Name == nameof(ISettingsRepository.TrySetKnowledgeSourceBranch)).Should().Be(0,
			because: "branch metadata cannot be persisted for a candidate that never became active");
		_runtime.Received(1).DeactivateLibrary("git-source");
		_fileSystem.Directory.Exists(repositoryPath).Should().BeFalse(
			because: "a rejected first clone must be discarded so a later install can clone a repaired source");
	}

	[Test]
	[Description("A Git checkout is rolled back when synchronization mutates it and then rejects the result.")]
	public void Update_ShouldRollbackGitCheckout_WhenSynchronizationRejectsCandidate() {
		// Arrange
		KnowledgeSourceConfiguration source = GitSource("com.example.git");
		_settings.GetKnowledgeConfiguration().Returns(Configuration(("git-source", source)));
		string repositoryPath = TestFileSystem.GetRootedPath("knowledge", "git-source", "repository");
		_fileSystem.AddDirectory(Path.Combine(repositoryPath, ".git"));
		_store.GetGitRepositoryPath("git-source", true).Returns(repositoryPath);
		const string previousRevision = "1111111111111111111111111111111111111111";
		_gitTransport.GetCurrentRevision(repositoryPath).Returns(previousRevision);
		_gitReader.TryRead(repositoryPath, source.LibraryId, out Arg.Any<KnowledgeGitRepositorySnapshot?>(),
			out Arg.Any<string?>()).Returns(call => {
				call[2] = GitSnapshot(source.LibraryId);
				call[3] = null;
				return true;
			});
		_gitTransport.Synchronize(Arg.Any<KnowledgeTransportRequest>(), repositoryPath).Returns(
			new KnowledgeTransportResult(
				KnowledgeTransportStatus.Rejected,
				null,
				null,
				null,
				Diagnostic: "checkout contains a symbolic link"));

		// Act
		KnowledgeSourceBatchResult result = _service.Update("git-source");

		// Assert
		result.Success.Should().BeFalse(because: "a rejected synchronized checkout must not replace trusted content");
		result.Sources.Single().Status.Should().Be("rejected",
			because: "a transport policy rejection should remain distinguishable from an outage");
		_gitTransport.Received(1).Restore(repositoryPath, previousRevision);
		_runtime.Received(1).ActivateGitRepository(
			"git-source", source.Priority, source.Participation,
			Arg.Is<KnowledgeGitRepositorySnapshot>(snapshot => snapshot.Sequence == 1));
	}

	[Test]
	[Description("A synchronized Git checkout is rolled back when it regresses the previously validated sequence.")]
	public void Update_ShouldRollbackGitCheckout_WhenSequenceRegresses() {
		// Arrange
		KnowledgeSourceConfiguration source = GitSource("com.example.git");
		_settings.GetKnowledgeConfiguration().Returns(Configuration(("git-source", source)));
		string repositoryPath = TestFileSystem.GetRootedPath("knowledge", "git-source", "repository");
		_fileSystem.AddDirectory(Path.Combine(repositoryPath, ".git"));
		_store.GetGitRepositoryPath("git-source", true).Returns(repositoryPath);
		const string previousRevision = "1111111111111111111111111111111111111111";
		const string nextRevision = "2222222222222222222222222222222222222222";
		_gitTransport.GetCurrentRevision(repositoryPath).Returns(previousRevision);
		_gitTransport.Synchronize(Arg.Any<KnowledgeTransportRequest>(), repositoryPath).Returns(
			new KnowledgeTransportResult(
				KnowledgeTransportStatus.Downloaded,
				nextRevision,
				null,
				repositoryPath,
				ResolvedCommit: nextRevision));
		int reads = 0;
		_gitReader.TryRead(repositoryPath, source.LibraryId, out Arg.Any<KnowledgeGitRepositorySnapshot?>(),
			out Arg.Any<string?>()).Returns(call => {
			reads++;
			call[2] = reads == 2
				? GitSnapshot(source.LibraryId) with { Sequence = 4, ContentDigest = "older" }
				: GitSnapshot(source.LibraryId) with { Sequence = 5, ContentDigest = "current" };
			call[3] = null;
			return true;
		});

		// Act
		KnowledgeSourceBatchResult result = _service.Update("git-source");

		// Assert
		result.Success.Should().BeFalse(because: "a Git source cannot move behind its previously validated sequence");
		result.Sources.Single().Status.Should().Be("rejected",
			because: "the anti-rollback failure should be distinguishable from a transport outage");
		_gitTransport.Received(1).Restore(repositoryPath, previousRevision);
		_runtime.Received(1).ActivateGitRepository(
			"git-source", source.Priority, source.Participation,
			Arg.Is<KnowledgeGitRepositorySnapshot>(snapshot => snapshot.Sequence == 5));
	}

	[Test]
	[Description("A Git checkout and runtime are rolled back when default-branch persistence loses its settings race.")]
	public void Install_ShouldRollbackGitCheckout_WhenBranchPersistenceCasFails() {
		// Arrange
		KnowledgeSourceConfiguration source = GitSource("com.example.git");
		_settings.GetKnowledgeConfiguration().Returns(Configuration(("git-source", source)));
		string repositoryPath = TestFileSystem.GetRootedPath("knowledge", "git-source", "repository");
		_fileSystem.AddDirectory(Path.Combine(repositoryPath, ".git"));
		_store.GetGitRepositoryPath("git-source", true).Returns(repositoryPath);
		const string previousRevision = "1111111111111111111111111111111111111111";
		const string nextRevision = "2222222222222222222222222222222222222222";
		_gitTransport.GetCurrentRevision(repositoryPath).Returns(previousRevision);
		_gitTransport.Synchronize(Arg.Any<KnowledgeTransportRequest>(), repositoryPath).Returns(
			new KnowledgeTransportResult(
				KnowledgeTransportStatus.Downloaded,
				nextRevision,
				null,
				repositoryPath,
				ResolvedBranch: "main",
				ResolvedCommit: nextRevision));
		int reads = 0;
		_gitReader.TryRead(repositoryPath, source.LibraryId, out Arg.Any<KnowledgeGitRepositorySnapshot?>(),
			out Arg.Any<string?>()).Returns(call => {
			reads++;
			call[2] = reads == 2
				? GitSnapshot(source.LibraryId) with { Sequence = 2, ContentDigest = "next" }
				: GitSnapshot(source.LibraryId);
			call[3] = null;
			return true;
		});
		_settings.TrySetKnowledgeSourceBranch("git-source", source, "main").Returns(false);

		// Act
		KnowledgeSourceBatchResult result = _service.Install("git-source");

		// Assert
		result.Success.Should().BeFalse(because: "the source configuration changed before branch persistence");
		_gitTransport.Received(1).Restore(repositoryPath, previousRevision);
		_runtime.Received(1).ActivateGitRepository(
			"git-source", source.Priority, source.Participation,
			Arg.Is<KnowledgeGitRepositorySnapshot>(snapshot => snapshot.Sequence == 2));
		_runtime.Received(2).ActivateGitRepository(
			"git-source", source.Priority, source.Participation,
			Arg.Any<KnowledgeGitRepositorySnapshot>());
	}

	[Test]
	[Description("Git update information reports an available remote commit without mutating the checkout.")]
	public void GetInfo_ShouldReportAvailable_WhenGitRemoteRevisionDiffers() {
		// Arrange
		KnowledgeSourceConfiguration source = GitSource("com.example.git");
		_settings.GetKnowledgeConfiguration().Returns(Configuration(("git-source", source)));
		string repositoryPath = TestFileSystem.GetRootedPath("knowledge", "git-source", "repository");
		_fileSystem.AddDirectory(Path.Combine(repositoryPath, ".git"));
		_store.GetGitRepositoryPath("git-source", false).Returns(repositoryPath);
		const string currentRevision = "1111111111111111111111111111111111111111";
		const string remoteRevision = "2222222222222222222222222222222222222222";
		_gitTransport.GetCurrentRevision(repositoryPath).Returns(currentRevision);
		_gitReader.TryRead(repositoryPath, source.LibraryId, out Arg.Any<KnowledgeGitRepositorySnapshot?>(),
			out Arg.Any<string?>()).Returns(call => {
				call[2] = GitSnapshot(source.LibraryId);
				call[3] = null;
				return true;
			});
		_gitTransport.CheckForUpdates(Arg.Any<KnowledgeTransportRequest>(), repositoryPath).Returns(
			new KnowledgeTransportResult(
				KnowledgeTransportStatus.Downloaded,
				remoteRevision,
				null,
				null,
				ResolvedCommit: remoteRevision));

		// Act
		KnowledgeSourceInfoResult result = _service.GetInfo("git-source", checkUpdates: true);

		// Assert
		result.Success.Should().BeTrue(because: "the installed Git source and remote probe are valid");
		result.Sources.Single().UpdateAvailability.Should().Be("available",
			because: "a differing trusted remote commit represents an available update");
		_gitTransport.DidNotReceiveWithAnyArgs().Synchronize(default!, default!);
	}

	[Test]
	[Description("All-source information inspection uses bounded concurrency instead of serial transport work.")]
	public void GetInfo_ShouldInspectIndependentSourcesConcurrently_WhenAliasIsOmitted() {
		// Arrange
		(string Alias, KnowledgeSourceConfiguration Source)[] sources = Enumerable.Range(0, 12)
			.Select(index => ($"source-{index}", Source($"com.example.source{index}", enabled: true)))
			.ToArray();
		_settings.GetKnowledgeConfiguration().Returns(Configuration(sources));
		int active = 0;
		int maximum = 0;
		_store.ReadCurrent(Arg.Any<string>(), out Arg.Any<string?>()).Returns(call => {
			int current = Interlocked.Increment(ref active);
			int observed;
			do {
				observed = Volatile.Read(ref maximum);
			} while (current > observed && Interlocked.CompareExchange(ref maximum, current, observed) != observed);
			Thread.Sleep(40);
			Interlocked.Decrement(ref active);
			call[1] = null;
			return null;
		});

		// Act
		KnowledgeSourceInfoResult result = _service.GetInfo(sourceAlias: null, checkUpdates: false);

		// Assert
		result.Sources.Should().HaveCount(sources.Length,
			because: "bounded concurrency must retain one deterministic result per configured source");
		maximum.Should().BeGreaterThan(1,
			because: "independent sources should not multiply transport latency through serial execution");
		maximum.Should().BeLessThanOrEqualTo(8,
			because: "source fan-out must remain bounded to protect process and network resources");
	}

	private void ConfigureCurrent(Func<string, KnowledgeSourceCurrentState?> selector) {
		_store.ReadCurrent(Arg.Any<string>(), out Arg.Any<string?>()).Returns(call => {
			call[1] = null;
			return selector(call.ArgAt<string>(0));
		});
	}

	private static KnowledgeConfiguration Configuration(
		params (string Alias, KnowledgeSourceConfiguration Source)[] sources) => new() {
		Sources = sources.ToDictionary(
			pair => pair.Alias,
			pair => pair.Source,
			StringComparer.OrdinalIgnoreCase),
		TopicPins = new Dictionary<string, string>(StringComparer.Ordinal)
	};

	private static KnowledgeSourceConfiguration Source(string libraryId, bool enabled) => new() {
		LibraryId = libraryId,
		Type = KnowledgeSourceType.NuGet,
		Location = "https://feed.invalid/v3/index.json",
		TrustedKeyId = "test-signing-key",
		TrustedPublicKeyPath = TestFileSystem.GetRootedPath("keys", "test-public.pem"),
		PackageId = "Example.Knowledge",
		Enabled = enabled,
		Participation = KnowledgeSourceParticipation.Supplement
	};

	private static KnowledgeSourceConfiguration GitSource(string libraryId) => new() {
		LibraryId = libraryId,
		Type = KnowledgeSourceType.Git,
		Location = "https://example.invalid/knowledge.git",
		Enabled = true,
		Priority = 100,
		Participation = KnowledgeSourceParticipation.Supplement
	};

	private static KnowledgeGitRepositorySnapshot GitSnapshot(string libraryId) => new(
		libraryId,
		"1.0.0",
		1,
		"digest",
		[]);

	private static KnowledgeSourceCurrentState State(
		string alias,
		string libraryId,
		ulong sequence,
		string revision) {
		KnowledgeSourceGenerationPointer pointer = new(
			libraryId,
			"1.0.0",
			sequence,
			$"generations/{sequence}-digest",
			$"digest-{sequence}",
			revision,
			DateTimeOffset.UtcNow);
		return new KnowledgeSourceCurrentState(1, alias, pointer, null);
	}
}
