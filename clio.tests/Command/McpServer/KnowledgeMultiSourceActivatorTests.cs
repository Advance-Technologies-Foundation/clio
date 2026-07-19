using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
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
public sealed class KnowledgeMultiSourceActivatorTests {
	[Test]
	[Description("Configuration refresh deactivates disabled and removed sources while independently activating newly configured sources and pins.")]
	public void EnsureActivated_ShouldReconcileEachSource_WhenConfigurationChanges() {
		// Arrange
		IKnowledgeBundleRuntime runtime = Substitute.For<IKnowledgeBundleRuntime>();
		IKnowledgeSourceInstallationStore store = Substitute.For<IKnowledgeSourceInstallationStore>();
		IKnowledgeRuntimeConfigurationProvider configurationProvider =
			Substitute.For<IKnowledgeRuntimeConfigurationProvider>();
		KnowledgeConfiguration currentConfiguration = Configuration(
			("alpha", Source("com.example.alpha", priority: 100)),
			("beta", Source("com.example.beta", priority: 50)));
		configurationProvider.GetCurrent().Returns(_ => currentConfiguration);
		Dictionary<string, KnowledgeSourceCurrentState> states = new(StringComparer.OrdinalIgnoreCase) {
			["alpha"] = State("alpha", "com.example.alpha", 1),
			["beta"] = State("beta", "com.example.beta", 2),
			["gamma"] = State("gamma", "com.example.gamma", 3)
		};
		store.ReadCurrent(Arg.Any<string>(), out Arg.Any<string?>()).Returns(call => {
			call[1] = null;
			return states[call.ArgAt<string>(0)];
		});
		store.TryReadCandidate(
			Arg.Any<string>(),
			Arg.Any<KnowledgeSourceGenerationPointer>(),
			out Arg.Any<InstalledKnowledgeSourceCandidate?>(),
			out Arg.Any<string?>()).Returns(call => {
			KnowledgeSourceGenerationPointer pointer = call.ArgAt<KnowledgeSourceGenerationPointer>(1);
			call[2] = new InstalledKnowledgeSourceCandidate(
				pointer,
				Path.Combine(Path.GetTempPath(), "knowledge", pointer.LibraryId),
				[1, 2, 3]);
			call[3] = null;
			return true;
		});
		runtime.ActivateLibrary(
			Arg.Any<string>(),
			Arg.Any<int>(),
			Arg.Any<KnowledgeSourceParticipation>(),
			Arg.Any<Stream>(),
			Arg.Any<string?>(),
			Arg.Any<string?>(),
			Arg.Any<string?>()).Returns(call => {
			string alias = call.ArgAt<string>(0);
			ulong sequence = states[alias].Active.Sequence;
			return Activated(sequence);
		});
		List<IReadOnlyDictionary<string, string>> observedPins = [];
		runtime.When(instance => instance.SetTopicPins(Arg.Any<IReadOnlyDictionary<string, string>>()))
			.Do(call => observedPins.Add(new Dictionary<string, string>(
				call.ArgAt<IReadOnlyDictionary<string, string>>(0), StringComparer.Ordinal)));
		ServiceCollection services = new();
		services.AddSingleton(runtime);
		services.AddSingleton(new KnowledgeBundleActivationOptions(FailureRetryMilliseconds: 0));
		services.AddSingleton(store);
		services.AddSingleton(configurationProvider);
		services.AddSingleton(Substitute.For<IKnowledgeTrustFingerprintService>());
		services.AddSingleton<IKnowledgeBundleActivator, KnowledgeMultiSourceActivator>();
		using ServiceProvider container = services.BuildServiceProvider();
		IKnowledgeBundleActivator activator = container.GetRequiredService<IKnowledgeBundleActivator>();

		// Act
		activator.EnsureActivated();
		currentConfiguration = Configuration(
			("alpha", Source("com.example.alpha", priority: 100, enabled: false)),
			("gamma", Source("com.example.gamma", priority: 75)));
		currentConfiguration.TopicPins["creatio.esq.filters"] = "com.example.gamma";
		activator.EnsureActivated();

		// Assert
		CountCalls(runtime, nameof(IKnowledgeBundleRuntime.ActivateLibrary), "alpha").Should().Be(1,
			because: "alpha should activate only while enabled");
		CountCalls(runtime, nameof(IKnowledgeBundleRuntime.ActivateLibrary), "beta").Should().Be(1,
			because: "beta's first activation must remain independent from alpha");
		CountCalls(runtime, nameof(IKnowledgeBundleRuntime.ActivateLibrary), "gamma").Should().Be(1,
			because: "a newly configured source must activate during the next refresh");
		CountCalls(runtime, nameof(IKnowledgeBundleRuntime.DeactivateLibrary), "alpha").Should().Be(1,
			because: "a disabled source must stop serving while its cached generation is retained");
		CountCalls(runtime, nameof(IKnowledgeBundleRuntime.DeactivateLibrary), "beta").Should().Be(1,
			because: "a removed source must leave the runtime snapshot on refresh");
		observedPins.Should().HaveCount(2,
			because: "topic pins must be refreshed on every reconciliation, even when generations are unchanged");
		observedPins[^1]["creatio.esq.filters"].Should().Be("com.example.gamma",
			because: "the most recent settings file must control logical-topic routing");
		activator.LastDiagnostic.Should().BeNull(
			because: "independent source activation and deactivation completed without fallback errors");
	}

	[Test]
	[Description("A configuration refresh failure clears all active libraries instead of serving a stale mixed snapshot.")]
	public void EnsureActivated_ShouldDeactivateRuntime_WhenConfigurationRefreshFails() {
		// Arrange
		IKnowledgeBundleRuntime runtime = Substitute.For<IKnowledgeBundleRuntime>();
		IKnowledgeSourceInstallationStore store = Substitute.For<IKnowledgeSourceInstallationStore>();
		IKnowledgeRuntimeConfigurationProvider configurationProvider =
			Substitute.For<IKnowledgeRuntimeConfigurationProvider>();
		configurationProvider.GetCurrent().Returns(_ => throw new InvalidDataException("synthetic invalid settings"));
		ServiceCollection services = new();
		services.AddSingleton(runtime);
		services.AddSingleton(new KnowledgeBundleActivationOptions(FailureRetryMilliseconds: 0));
		services.AddSingleton(store);
		services.AddSingleton(configurationProvider);
		services.AddSingleton(Substitute.For<IKnowledgeTrustFingerprintService>());
		services.AddSingleton<IKnowledgeBundleActivator, KnowledgeMultiSourceActivator>();
		using ServiceProvider container = services.BuildServiceProvider();
		IKnowledgeBundleActivator activator = container.GetRequiredService<IKnowledgeBundleActivator>();

		// Act
		activator.EnsureActivated();

		// Assert
		CountCalls(runtime, nameof(IKnowledgeBundleRuntime.Deactivate)).Should().Be(1,
			because: "invalid live configuration must fail closed rather than retain a stale source set");
		activator.LastDiagnostic.Should().Contain("synthetic invalid settings",
			because: "operators need a useful explanation for a rejected settings refresh");
	}

	[Test]
	[Description("One corrupt source marker deactivates only that library while another configured source still activates.")]
	public void EnsureActivated_ShouldIsolateMarkerFailure_WhenAnotherSourceIsHealthy() {
		// Arrange
		IKnowledgeBundleRuntime runtime = Substitute.For<IKnowledgeBundleRuntime>();
		IKnowledgeSourceInstallationStore store = Substitute.For<IKnowledgeSourceInstallationStore>();
		IKnowledgeRuntimeConfigurationProvider configurationProvider =
			Substitute.For<IKnowledgeRuntimeConfigurationProvider>();
		configurationProvider.GetCurrent().Returns(Configuration(
			("alpha", Source("com.example.alpha", priority: 100)),
			("beta", Source("com.example.beta", priority: 50))));
		KnowledgeSourceCurrentState betaState = State("beta", "com.example.beta", 2);
		store.ReadCurrent(Arg.Any<string>(), out Arg.Any<string?>()).Returns(call => {
			string alias = call.ArgAt<string>(0);
			call[1] = alias == "alpha" ? "synthetic alpha marker failure" : null;
			return alias == "beta" ? betaState : null;
		});
		store.TryReadCandidate(
			"beta",
			betaState.Active,
			out Arg.Any<InstalledKnowledgeSourceCandidate?>(),
			out Arg.Any<string?>()).Returns(call => {
			call[2] = new InstalledKnowledgeSourceCandidate(
				betaState.Active,
				Path.Combine(Path.GetTempPath(), "knowledge", "beta"),
				[1, 2, 3]);
			call[3] = null;
			return true;
		});
		runtime.ActivateLibrary(
			"beta", 50, KnowledgeSourceParticipation.Authoritative, Arg.Any<Stream>(), "1.0.0",
			"com.example.beta", Arg.Any<string?>()).Returns(Activated(2));
		ServiceCollection services = new();
		services.AddSingleton(runtime);
		services.AddSingleton(new KnowledgeBundleActivationOptions(FailureRetryMilliseconds: 0));
		services.AddSingleton(store);
		services.AddSingleton(configurationProvider);
		services.AddSingleton(Substitute.For<IKnowledgeTrustFingerprintService>());
		services.AddSingleton<IKnowledgeBundleActivator, KnowledgeMultiSourceActivator>();
		using ServiceProvider container = services.BuildServiceProvider();
		IKnowledgeBundleActivator activator = container.GetRequiredService<IKnowledgeBundleActivator>();

		// Act
		activator.EnsureActivated();

		// Assert
		CountCalls(runtime, nameof(IKnowledgeBundleRuntime.DeactivateLibrary), "alpha").Should().Be(1,
			because: "the corrupt source must fail closed without changing another library");
		CountCalls(runtime, nameof(IKnowledgeBundleRuntime.ActivateLibrary), "beta").Should().Be(1,
			because: "a healthy source must remain independently activatable");
		CountCalls(runtime, nameof(IKnowledgeBundleRuntime.Deactivate)).Should().Be(0,
			because: "one source-level marker failure must not clear the entire multi-source runtime");
		activator.LastDiagnostic.Should().Contain("synthetic alpha marker failure",
			because: "the isolated source failure must remain visible to operators");
	}

	[Test]
	[Description("A live signing-trust change forces revalidation and withdraws a generation that the replacement key does not trust.")]
	public void EnsureActivated_ShouldRevalidateAndDeactivate_WhenSourceTrustChanges() {
		// Arrange
		IKnowledgeBundleRuntime runtime = Substitute.For<IKnowledgeBundleRuntime>();
		IKnowledgeSourceInstallationStore store = Substitute.For<IKnowledgeSourceInstallationStore>();
		IKnowledgeRuntimeConfigurationProvider configurationProvider =
			Substitute.For<IKnowledgeRuntimeConfigurationProvider>();
		KnowledgeSourceConfiguration source = Source("com.example.alpha", priority: 100);
		KnowledgeConfiguration configuration = Configuration(("alpha", source));
		configurationProvider.GetCurrent().Returns(_ => configuration);
		KnowledgeSourceCurrentState state = State("alpha", "com.example.alpha", 1);
		store.ReadCurrent("alpha", out Arg.Any<string?>()).Returns(call => {
			call[1] = null;
			return state;
		});
		store.TryReadCandidate(
			"alpha",
			state.Active,
			out Arg.Any<InstalledKnowledgeSourceCandidate?>(),
			out Arg.Any<string?>()).Returns(call => {
			call[2] = new InstalledKnowledgeSourceCandidate(
				state.Active,
				Path.Combine(Path.GetTempPath(), "knowledge", "alpha"),
				[1, 2, 3]);
			call[3] = null;
			return true;
		});
		runtime.ActivateLibrary(
			"alpha", 100, KnowledgeSourceParticipation.Authoritative, Arg.Any<Stream>(), "1.0.0",
			"com.example.alpha", Arg.Any<string?>()).Returns(
			Activated(1),
			new KnowledgeBundleActivationResult(
				KnowledgeBundleActivationStatus.Rejected,
				KnowledgeBundleRejectionCode.InvalidSignature,
				1,
				null,
				"replacement key rejected the installed bundle"));
		ServiceCollection services = new();
		services.AddSingleton(runtime);
		services.AddSingleton(new KnowledgeBundleActivationOptions(FailureRetryMilliseconds: 60_000));
		services.AddSingleton(store);
		services.AddSingleton(configurationProvider);
		services.AddSingleton(Substitute.For<IKnowledgeTrustFingerprintService>());
		services.AddSingleton<IKnowledgeBundleActivator, KnowledgeMultiSourceActivator>();
		using ServiceProvider container = services.BuildServiceProvider();
		IKnowledgeBundleActivator activator = container.GetRequiredService<IKnowledgeBundleActivator>();

		// Act
		activator.EnsureActivated();
		KnowledgeSourceConfiguration replacementTrust = Source("com.example.alpha", priority: 100);
		replacementTrust.TrustedKeyId = "replacement-signing-key";
		configuration = Configuration(("alpha", replacementTrust));
		activator.EnsureActivated();
		activator.EnsureActivated();

		// Assert
		CountCalls(runtime, nameof(IKnowledgeBundleRuntime.ActivateLibrary), "alpha").Should().Be(2,
			because: "changing trust must revalidate once while the bounded failure cooldown prevents repeated disk and signature work");
		CountCalls(runtime, nameof(IKnowledgeBundleRuntime.DeactivateLibrary), "alpha").Should().Be(1,
			because: "a generation rejected by replacement trust must stop serving immediately");
		activator.LastDiagnostic.Should().Contain("replacement key rejected",
			because: "operators need the validation reason after a live trust change");
	}

	[Test]
	[Description("Replacing public-key bytes at the same configured path changes activation identity and forces immediate revalidation.")]
	public void EnsureActivated_ShouldRevalidate_WhenTrustFingerprintChangesAtSamePath() {
		// Arrange
		IKnowledgeBundleRuntime runtime = Substitute.For<IKnowledgeBundleRuntime>();
		IKnowledgeSourceInstallationStore store = Substitute.For<IKnowledgeSourceInstallationStore>();
		IKnowledgeRuntimeConfigurationProvider configurationProvider =
			Substitute.For<IKnowledgeRuntimeConfigurationProvider>();
		IKnowledgeTrustFingerprintService fingerprints = Substitute.For<IKnowledgeTrustFingerprintService>();
		KnowledgeSourceConfiguration source = Source("com.example.alpha", priority: 100);
		configurationProvider.GetCurrent().Returns(Configuration(("alpha", source)));
		KnowledgeSourceCurrentState state = State("alpha", "com.example.alpha", 1);
		store.ReadCurrent("alpha", out Arg.Any<string?>()).Returns(call => {
			call[1] = null;
			return state;
		});
		store.TryReadCandidate(
			"alpha", state.Active, out Arg.Any<InstalledKnowledgeSourceCandidate?>(), out Arg.Any<string?>())
			.Returns(call => {
				call[2] = new InstalledKnowledgeSourceCandidate(
					state.Active, Path.Combine(Path.GetTempPath(), "knowledge", "alpha"), [1, 2, 3]);
				call[3] = null;
				return true;
			});
		fingerprints.TryGetFingerprint(source.TrustedPublicKeyPath, out Arg.Any<string>()).Returns(
			call => { call[1] = "FINGERPRINT-A"; return true; },
			call => { call[1] = "FINGERPRINT-B"; return true; });
		runtime.ActivateLibrary(
			"alpha", 100, KnowledgeSourceParticipation.Authoritative, Arg.Any<Stream>(), "1.0.0",
			"com.example.alpha", Arg.Any<string?>()).Returns(
			Activated(1),
			new KnowledgeBundleActivationResult(
				KnowledgeBundleActivationStatus.Rejected,
				KnowledgeBundleRejectionCode.InvalidSignature,
				1,
				null,
				"replacement key rejected the installed bundle"));
		ServiceCollection services = new();
		services.AddSingleton(runtime);
		services.AddSingleton(new KnowledgeBundleActivationOptions(FailureRetryMilliseconds: 60_000));
		services.AddSingleton(store);
		services.AddSingleton(configurationProvider);
		services.AddSingleton(fingerprints);
		services.AddSingleton<IKnowledgeBundleActivator, KnowledgeMultiSourceActivator>();
		using ServiceProvider container = services.BuildServiceProvider();
		IKnowledgeBundleActivator activator = container.GetRequiredService<IKnowledgeBundleActivator>();

		// Act
		activator.EnsureActivated();
		activator.EnsureActivated();

		// Assert
		CountCalls(runtime, nameof(IKnowledgeBundleRuntime.ActivateLibrary), "alpha").Should().Be(2,
			because: "effective key replacement must invalidate the observed activation even when the path is unchanged");
		CountCalls(runtime, nameof(IKnowledgeBundleRuntime.DeactivateLibrary), "alpha").Should().Be(1,
			because: "a generation rejected under replacement trust must be withdrawn immediately");
	}

	[Test]
	[Description("Runtime configuration provider rereads the bounded settings file so source enablement changes are visible without restart.")]
	public void GetCurrent_ShouldReadLatestKnowledgeObject_WhenSettingsFileChanges() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		string settingsPath = TestFileSystem.GetRootedPath("clio", "appsettings.json");
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		settingsRepository.AppSettingsFilePath.Returns(settingsPath);
		fileSystem.AddFile(settingsPath, new MockFileData(SettingsJson(enabled: true)));
		ServiceCollection services = new();
		services.AddSingleton(settingsRepository);
		services.AddSingleton<System.IO.Abstractions.IFileSystem>(fileSystem);
		services.AddSingleton<IKnowledgeRuntimeConfigurationProvider, KnowledgeRuntimeConfigurationProvider>();
		using ServiceProvider container = services.BuildServiceProvider();
		IKnowledgeRuntimeConfigurationProvider provider =
			container.GetRequiredService<IKnowledgeRuntimeConfigurationProvider>();

		// Act
		KnowledgeConfiguration before = provider.GetCurrent();
		fileSystem.File.WriteAllText(settingsPath, SettingsJson(enabled: false));
		KnowledgeConfiguration after = provider.GetCurrent();

		// Assert
		before.Sources["partner"].Enabled.Should().BeTrue(
			because: "the initial file state must be reflected in the runtime configuration");
		after.Sources["partner"].Enabled.Should().BeFalse(
			because: "subsequent reads must observe source kill-switch changes without restarting clio");
		settingsRepository.ReceivedCalls().Count(call =>
			call.GetMethodInfo().Name == nameof(ISettingsRepository.GetKnowledgeConfiguration)).Should().Be(0,
			because: "an existing live file is authoritative over the repository's startup snapshot");
	}

	private static KnowledgeConfiguration Configuration(
		params (string Alias, KnowledgeSourceConfiguration Source)[] sources) => new() {
		Sources = sources.ToDictionary(
			pair => pair.Alias,
			pair => pair.Source,
			StringComparer.OrdinalIgnoreCase),
		TopicPins = new Dictionary<string, string>(StringComparer.Ordinal)
	};

	private static KnowledgeSourceConfiguration Source(string libraryId, int priority, bool enabled = true) => new() {
		LibraryId = libraryId,
		Type = KnowledgeSourceType.NuGet,
		Location = "https://feed.invalid/v3/index.json",
		TrustedKeyId = "test-signing-key",
		TrustedPublicKeyPath = TestFileSystem.GetRootedPath("keys", "test-public.pem"),
		PackageId = "Example.Knowledge",
		Enabled = enabled,
		Priority = priority,
		Participation = KnowledgeSourceParticipation.Authoritative
	};

	private static KnowledgeSourceCurrentState State(string alias, string libraryId, ulong sequence) {
		KnowledgeSourceGenerationPointer pointer = new(
			libraryId,
			"1.0.0",
			sequence,
			$"generations/{sequence}-digest",
			$"digest-{sequence}",
			$"revision-{sequence}",
			DateTimeOffset.UtcNow);
		return new KnowledgeSourceCurrentState(1, alias, pointer, null);
	}

	private static KnowledgeBundleActivationResult Activated(ulong sequence) => new(
		KnowledgeBundleActivationStatus.Activated,
		KnowledgeBundleRejectionCode.None,
		sequence,
		sequence,
		null);

	private static int CountCalls(object substitute, string method, string? firstArgument = null) =>
		substitute.ReceivedCalls().Count(call =>
			call.GetMethodInfo().Name == method
			&& (firstArgument is null || call.GetArguments().FirstOrDefault() as string == firstArgument));

	private static string SettingsJson(bool enabled) {
		string publicKeyPath = TestFileSystem.GetRootedPath("keys", "partner-public.pem").Replace('\\', '/');
		return $$"""
		{
		  "knowledge": {
		    "sources": {
		      "partner": {
		        "library-id": "com.example.partner",
		        "type": "nuget",
		        "location": "https://feed.invalid/v3/index.json",
		        "trusted-key-id": "partner-signing-2026",
		        "trusted-public-key-path": "{{publicKeyPath}}",
		        "package-id": "Example.Knowledge",
		        "enabled": {{enabled.ToString().ToLowerInvariant()}},
		        "priority": 10,
		        "participation": "supplement"
		      }
		    },
		    "topic-pins": {}
		  }
		}
		""";
	}
}
