using System;
using System.IO;
using System.Linq;
using Clio.Command.McpServer.Knowledge;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class KnowledgeBundleDiscoveryTests {
	private IKnowledgeBundleRuntime _runtime = null!;
	private IKnowledgeInstallationStore _store = null!;
	private EnvironmentKnowledgeBundleActivator _activator = null!;

	[SetUp]
	public void SetUp() {
		_runtime = Substitute.For<IKnowledgeBundleRuntime>();
		_store = Substitute.For<IKnowledgeInstallationStore>();
		_activator = new EnvironmentKnowledgeBundleActivator(
			_runtime,
			_store,
			new KnowledgeBundleActivationOptions(FailureRetryMilliseconds: 0));
	}

	[TearDown]
	public void TearDown() {
		_runtime.ClearReceivedCalls();
		_store.ClearReceivedCalls();
	}

	[Test]
	[Description("Activates a disk candidate when an external install command publishes the first activation marker.")]
	public void EnsureActivated_ShouldActivate_WhenCacheMarkerExists() {
		// Arrange
		KnowledgeCurrentState installed = State("1.0.0", 10, "aa");
		_store.ReadCurrent(out Arg.Any<string?>()).Returns(installed);
		ConfigureCandidate(installed.Active, KnowledgeBundleActivationStatus.Activated);

		// Act
		_activator.EnsureActivated();

		// Assert
		_runtime.Received(1).Activate(Arg.Any<Stream>(), "1.0.0");
	}

	[Test]
	[Description("Reuses the active runtime while the persisted activation marker identity remains unchanged.")]
	public void EnsureActivated_ShouldNotReactivate_WhenMarkerIsUnchanged() {
		// Arrange
		KnowledgeCurrentState current = State("1.0.0", 10, "aa");
		_store.ReadCurrent(out Arg.Any<string?>()).Returns(current);
		ConfigureCandidate(current.Active, KnowledgeBundleActivationStatus.Activated);

		// Act
		_activator.EnsureActivated();
		_activator.EnsureActivated();

		// Assert
		_runtime.Received(1).Activate(Arg.Any<Stream>(), "1.0.0");
		_store.Received(2).ReadCurrent(out Arg.Any<string?>());
	}

	[Test]
	[Description("Uses only the disk cache and clears runtime content once when no activation marker exists.")]
	public void EnsureActivated_ShouldRemainDiskOnly_WhenCacheIsMissing() {
		// Arrange
		_store.ReadCurrent(out Arg.Any<string?>()).Returns((KnowledgeCurrentState?)null);

		// Act
		_activator.EnsureActivated();
		_activator.EnsureActivated();

		// Assert
		_runtime.Received(1).Deactivate();
		_runtime.DidNotReceive().Activate(Arg.Any<Stream>(), Arg.Any<string?>());
		_store.Received(2).ReadCurrent(out Arg.Any<string?>());
	}

	[Test]
	[Description("Stops serving in-memory guidance on the next lookup after the persistent cache marker is deleted.")]
	public void EnsureActivated_ShouldDeactivate_WhenMarkerIsDeleted() {
		// Arrange
		KnowledgeCurrentState current = State("1.0.0", 10, "aa");
		_store.ReadCurrent(out Arg.Any<string?>()).Returns(current, (KnowledgeCurrentState?)null);
		ConfigureCandidate(current.Active, KnowledgeBundleActivationStatus.Activated);

		// Act
		_activator.EnsureActivated();
		_activator.EnsureActivated();

		// Assert
		_runtime.Received(1).Deactivate();
		_runtime.Received(1).Activate(Arg.Any<Stream>(), "1.0.0");
	}

	[Test]
	[Description("Activates a newer disk bundle on the next guidance lookup without restarting the MCP process.")]
	public void EnsureActivated_ShouldHotReload_WhenMarkerChanges() {
		// Arrange
		KnowledgeCurrentState initial = State("1.0.0", 10, "aa");
		KnowledgeCurrentState updated = State("1.1.0", 20, "bb", initial.Active);
		_store.ReadCurrent(out Arg.Any<string?>()).Returns(initial, updated);
		ConfigureCandidate(initial.Active, KnowledgeBundleActivationStatus.Activated);
		ConfigureCandidate(updated.Active, KnowledgeBundleActivationStatus.Activated);

		// Act
		_activator.EnsureActivated();
		_activator.EnsureActivated();

		// Assert
		_runtime.Received(1).Activate(Arg.Any<Stream>(), "1.0.0");
		_runtime.Received(1).Activate(Arg.Any<Stream>(), "1.1.0");
	}

	[Test]
	[Description("Falls back to the previous installed version when a cold process cannot activate the current marker target.")]
	public void EnsureActivated_ShouldActivatePrevious_WhenColdCurrentActivationFails() {
		// Arrange
		KnowledgeVersionPointer previous = Pointer("1.0.0", 10, "aa");
		KnowledgeCurrentState current = State("1.1.0", 20, "bb", previous);
		_store.ReadCurrent(out Arg.Any<string?>()).Returns(current);
		ConfigureCandidate(current.Active, KnowledgeBundleActivationStatus.Rejected);
		ConfigureCandidate(previous, KnowledgeBundleActivationStatus.Activated);
		_runtime.ActiveSequence.Returns((ulong?)null);

		// Act
		_activator.EnsureActivated();

		// Assert
		_runtime.Received(1).Validate(Arg.Any<Stream>(), "1.1.0");
		_runtime.DidNotReceive().Activate(Arg.Any<Stream>(), "1.1.0");
		_runtime.Received(1).Activate(Arg.Any<Stream>(), "1.0.0");
	}

	[Test]
	[Description("Keeps already active guidance when a hot-reload candidate fails instead of replacing it with older content.")]
	public void EnsureActivated_ShouldKeepActiveRuntime_WhenHotReloadFails() {
		// Arrange
		KnowledgeCurrentState initial = State("1.0.0", 10, "aa");
		KnowledgeCurrentState updated = State("1.1.0", 20, "bb", initial.Active);
		_store.ReadCurrent(out Arg.Any<string?>()).Returns(initial, updated);
		ConfigureCandidate(initial.Active, KnowledgeBundleActivationStatus.Activated);
		ConfigureCandidate(updated.Active, KnowledgeBundleActivationStatus.Rejected);
		ulong? activeSequence = null;
		_runtime.ActiveSequence.Returns(_ => activeSequence);
		_runtime.Activate(Arg.Any<Stream>(), "1.0.0").Returns(_ => {
			activeSequence = 10;
			return Activation(KnowledgeBundleActivationStatus.Activated, 10);
		});

		// Act
		_activator.EnsureActivated();
		_activator.EnsureActivated();

		// Assert
		_runtime.Received(1).Validate(Arg.Any<Stream>(), "1.1.0");
		_runtime.DidNotReceive().Activate(Arg.Any<Stream>(), "1.1.0");
		_runtime.Received(1).Activate(Arg.Any<Stream>(), "1.0.0");
	}

	[Test]
	[Description("Rejects a marker whose sequence differs from the verified bundle before mutating the active runtime.")]
	public void EnsureActivated_ShouldRetainActiveRuntime_WhenMarkerSequenceIsTampered() {
		// Arrange
		KnowledgeCurrentState initial = State("1.0.0", 10, "aa");
		KnowledgeCurrentState tampered = State("1.1.0", 21, "bb", initial.Active);
		_store.ReadCurrent(out Arg.Any<string?>()).Returns(initial, tampered);
		_runtime.ActiveSequence.Returns((ulong?)10);
		ConfigureCandidate(initial.Active, KnowledgeBundleActivationStatus.Activated);
		InstalledKnowledgeCandidate candidate = new(tampered.Active, "bundle.zip", [4, 5, 6]);
		_store.TryReadCandidate(tampered.Active, out Arg.Any<InstalledKnowledgeCandidate?>(), out Arg.Any<string?>())
			.Returns(callInfo => {
				callInfo[1] = candidate;
				callInfo[2] = null;
				return true;
			});
		_runtime.Validate(Arg.Any<Stream>(), "1.1.0").Returns(new KnowledgeBundleValidationResult(
			KnowledgeBundleActivationStatus.Activated,
			KnowledgeBundleRejectionCode.None,
			20,
			null));

		// Act
		_activator.EnsureActivated();
		_activator.EnsureActivated();

		// Assert
		_runtime.DidNotReceive().Activate(Arg.Any<Stream>(), "1.1.0");
		_runtime.Received(1).Activate(Arg.Any<Stream>(), "1.0.0");
		_activator.LastDiagnostic.Should().Contain("marker sequence",
			because: "operators need a safe explanation for refusing a tampered activation marker");
	}

	[Test]
	[Description("Retries an unchanged marker after a transient candidate read failure and records a safe diagnostic.")]
	public void EnsureActivated_ShouldRetrySameMarker_WhenFirstReadFails() {
		// Arrange
		KnowledgeCurrentState current = State("1.0.0", 10, "aa");
		InstalledKnowledgeCandidate candidate = new(current.Active, "bundle.zip", [1, 2, 3]);
		_store.ReadCurrent(out Arg.Any<string?>()).Returns(current);
		_store.TryReadCandidate(current.Active, out Arg.Any<InstalledKnowledgeCandidate?>(), out Arg.Any<string?>())
			.Returns(
				callInfo => {
					callInfo[1] = null;
					callInfo[2] = "temporary sharing failure";
					return false;
				},
				callInfo => {
					callInfo[1] = candidate;
					callInfo[2] = null;
					return true;
				});
		_runtime.Validate(Arg.Any<Stream>(), "1.0.0").Returns(new KnowledgeBundleValidationResult(
			KnowledgeBundleActivationStatus.Activated,
			KnowledgeBundleRejectionCode.None,
			10,
			null));
		_runtime.Activate(Arg.Any<Stream>(), "1.0.0").Returns(Activation(
			KnowledgeBundleActivationStatus.Activated,
			10));

		// Act
		_activator.EnsureActivated();
		string? firstDiagnostic = _activator.LastDiagnostic;
		_activator.EnsureActivated();

		// Assert
		firstDiagnostic.Should().Be("temporary sharing failure",
			because: "operators need a safe reason for a failed hot reload");
		_activator.LastDiagnostic.Should().BeNull(
			because: "a successful retry should clear the stale diagnostic");
		_runtime.Received(1).Activate(Arg.Any<Stream>(), "1.0.0");
	}

	[Test]
	[Description("Applies a bounded cooldown to permanent failures while continuing to poll the small activation marker.")]
	public void EnsureActivated_ShouldNotRevalidateFailedBundle_DuringCooldown() {
		// Arrange
		KnowledgeCurrentState current = State("1.0.0", 10, "aa");
		_store.ReadCurrent(out Arg.Any<string?>()).Returns(current);
		ConfigureCandidate(current.Active, KnowledgeBundleActivationStatus.Rejected);
		EnvironmentKnowledgeBundleActivator activator = new(
			_runtime,
			_store,
			new KnowledgeBundleActivationOptions(FailureRetryMilliseconds: 10_000));

		// Act
		activator.EnsureActivated();
		activator.EnsureActivated();

		// Assert
		_store.Received(2).ReadCurrent(out Arg.Any<string?>());
		_runtime.Received(1).Validate(Arg.Any<Stream>(), "1.0.0");
	}

	private void ConfigureCandidate(
		KnowledgeVersionPointer pointer,
		KnowledgeBundleActivationStatus status) {
		InstalledKnowledgeCandidate candidate = new(pointer, $"C:\\knowledge\\{pointer.PackageVersion}\\bundle.zip", [1, 2, 3]);
		_store.TryReadCandidate(pointer, out Arg.Any<InstalledKnowledgeCandidate?>(), out Arg.Any<string?>())
			.Returns(callInfo => {
				callInfo[1] = candidate;
				callInfo[2] = null;
				return true;
			});
		_runtime.Validate(Arg.Any<Stream>(), pointer.PackageVersion).Returns(new KnowledgeBundleValidationResult(
			status,
			status == KnowledgeBundleActivationStatus.Activated
				? KnowledgeBundleRejectionCode.None
				: KnowledgeBundleRejectionCode.InvalidContent,
			pointer.Sequence,
			status == KnowledgeBundleActivationStatus.Activated ? null : "synthetic rejection"));
		if (status == KnowledgeBundleActivationStatus.Activated) {
			_runtime.Activate(Arg.Any<Stream>(), pointer.PackageVersion)
				.Returns(Activation(status, pointer.Sequence));
		}
	}

	private static KnowledgeBundleActivationResult Activation(
		KnowledgeBundleActivationStatus status,
		ulong sequence) => new(
		status,
		status == KnowledgeBundleActivationStatus.Activated
			? KnowledgeBundleRejectionCode.None
			: KnowledgeBundleRejectionCode.InvalidContent,
		sequence,
		status == KnowledgeBundleActivationStatus.Activated ? sequence : null,
		status == KnowledgeBundleActivationStatus.Activated ? null : "synthetic rejection");

	private static KnowledgeCurrentState State(
		string version,
		ulong sequence,
		string digestSeed,
		KnowledgeVersionPointer? previous = null) => new(1, Pointer(version, sequence, digestSeed), previous);

	private static KnowledgeVersionPointer Pointer(string version, ulong sequence, string digestSeed) => new(
		version,
		sequence,
		$"versions/{version}",
		string.Concat(Enumerable.Repeat(digestSeed, 32)),
		DateTimeOffset.UtcNow);
}
