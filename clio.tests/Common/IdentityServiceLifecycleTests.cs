using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using Clio.Common;
using Clio.Common.IIS;
using Clio.Requests;
using Clio.UserEnvironment;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture, Category("Unit"), Property("Module", "Common")]
public sealed class IdentityServiceLifecycleTests {
	private ISettingsRepository _settings;
	private IIisScanner _iis;
	private MockFileSystem _files;
	private EnvironmentSettings _environment;
	private Dictionary<string, EnvironmentSettings> _environments;
	private List<UnregisteredSite> _targets;
	private ServiceProvider _container;
	private IIdentityServiceLifecycle _sut;
	private IdentityServiceAttachment _attachment;

	[SetUp]
	public void SetUp() {
		string root = Path.Combine(DirectoryPathIdentity.Normalize(Path.GetTempPath()), "identity-tests", Guid.NewGuid().ToString("N"));
		_attachment = new IdentityServiceAttachment {
			EnvironmentPath = Path.Combine(root, "identity"), IisTarget = "custom-identity",
			ApplicationPool = "custom-pool", Uri = "http://localhost:40101"
		};
		_environment = new EnvironmentSettings { EnvironmentPath = Path.Combine(root, "crm"), IdentityService = _attachment };
		_environments = new() { ["crm"] = _environment };
		_settings = Substitute.For<ISettingsRepository>();
		_settings.FindCurrentEnvironment("crm").Returns(_ => _environment);
		_settings.GetAllEnvironments().Returns(_environments);
		_settings.UpdateIdentityAttachment("crm", _environment.EnvironmentPath, Arg.Any<IdentityServiceAttachment>(),
			Arg.Any<IdentityServiceAttachment>(), Arg.Any<bool>()).Returns(call => {
				if (_environment.IdentityService != call.ArgAt<IdentityServiceAttachment>(2)) {
					return false;
				}
				_environment.IdentityService = call.ArgAt<IdentityServiceAttachment>(3);
				return true;
			});
		_files = new MockFileSystem();
		_files.AddDirectory(_attachment.EnvironmentPath);
		_files.AddDirectory(_environment.EnvironmentPath);
		_targets = [new UnregisteredSite(new SiteBinding(_attachment.IisTarget, "Started", "",
			_attachment.EnvironmentPath, _attachment.ApplicationPool), [new Uri(_attachment.Uri)], SiteType.NotCreatioSite)];
		_iis = Substitute.For<IIisScanner>();
		_iis.TryFindAllVirtualDirectories(out Arg.Any<IReadOnlyList<IisVirtualDirectory>>()).Returns(call => {
			call[0] = Array.Empty<IisVirtualDirectory>(); return true;
		});
		_iis.TryFindAllIisTargets(out Arg.Any<IReadOnlyList<UnregisteredSite>>()).Returns(call => {
			call[0] = _targets.ToArray(); return true;
		});
		_iis.IsIisTargetExclusive(_attachment.IisTarget).Returns(true);
		_iis.TryStopIisTarget(_attachment.IisTarget, _attachment.EnvironmentPath, _attachment.ApplicationPool).Returns(true);
		_iis.TryDeleteIisTarget(_attachment.IisTarget, _attachment.EnvironmentPath, _attachment.ApplicationPool)
			.Returns(_ => { _targets.Clear(); return true; });
		ServiceCollection services = new();
		services.AddSingleton(_settings).AddSingleton(_iis)
			.AddSingleton<System.IO.Abstractions.IFileSystem>(_files).AddSingleton(Substitute.For<ILogger>())
			.AddTransient<IIdentityServiceLifecycle, IdentityServiceLifecycle>();
		_container = services.BuildServiceProvider();
		_sut = _container.GetRequiredService<IIdentityServiceLifecycle>();
	}

	[TearDown]
	public void TearDown() { _settings.ClearReceivedCalls(); _iis.ClearReceivedCalls(); _container.Dispose(); }

	[Test]
	[Description("Empty attachments do not interrogate IIS or mutate files.")]
	public void PrepareRemoval_ShouldReturnNothing_WhenAttachmentIsEmpty() {
		// Arrange
		_environment.IdentityService = new IdentityServiceAttachment();
		// Act
		IdentityRemovalPlan result = _sut.PrepareRemoval("crm");
		// Assert
		result.Should().BeNull(because: "empty component values describe no installed identity");
		_iis.ReceivedCalls().Should().BeEmpty(because: "no attachment means there is no IIS target to discover");
	}

	[TestCase(false), TestCase(true)]
	[Description("Non-root virtual directories prevent deletion of shared files or unrelated IIS mappings.")]
	public void PrepareRemoval_ShouldReject_WhenVirtualDirectorySharesTarget(bool insideIdentitySite) {
		// Arrange
		_iis.TryFindAllVirtualDirectories(out Arg.Any<IReadOnlyList<IisVirtualDirectory>>()).Returns(call => {
			call[0] = new[] { new IisVirtualDirectory(insideIdentitySite ? _attachment.IisTarget + "/assets" : "other/assets",
				insideIdentitySite ? _environment.EnvironmentPath : Path.Combine(_attachment.EnvironmentPath, "assets")) };
			return true;
		});
		// Act
		Action act = () => _sut.PrepareRemoval("crm");
		// Assert
		act.Should().Throw<InvalidOperationException>(because: "virtual directories are live IIS consumers even without their own application");
		_files.Directory.Exists(_attachment.EnvironmentPath).Should().BeTrue(because: "shared identity files must be preserved");
	}

	[TestCase(false), TestCase(true)]
	[Platform("Win")]
	[Description("Unresolvable foreign IIS mappings cannot prove exclusive ownership and must block deletion.")]
	public void PrepareRemoval_ShouldPreserveArtifacts_WhenForeignPathCannotBeResolved(bool virtualDirectory) {
		// Arrange
		string invalidPath = Path.Combine(Path.GetTempPath(), "NUL");
		if (virtualDirectory) {
			_iis.TryFindAllVirtualDirectories(out Arg.Any<IReadOnlyList<IisVirtualDirectory>>()).Returns(call => {
				call[0] = new[] { new IisVirtualDirectory("other/assets", invalidPath) };
				return true;
			});
		}
		else {
			_targets.Add(_targets[0] with { siteBinding = _targets[0].siteBinding with { name = "other", path = invalidPath } });
		}
		// Act
		Action act = () => _sut.PrepareRemoval("crm");
		// Assert
		act.Should().Throw<InvalidOperationException>(because: "unresolved physical identity does not prove a foreign mapping is disjoint")
			.WithMessage("*Restore access or correct/remove*", because: "the refusal must describe how to resolve stale configuration");
		_iis.ReceivedCalls().Should().NotContain(call => call.GetMethodInfo().Name == nameof(IIisScanner.TryStopIisTarget)
			|| call.GetMethodInfo().Name == nameof(IIisScanner.TryDeleteIisTarget), because: "prevalidation must finish before destructive IIS calls");
		_files.Directory.Exists(_attachment.EnvironmentPath).Should().BeTrue(because: "uncertain ownership must preserve files");
		_environment.IdentityService.Should().Be(_attachment, because: "repairing the foreign mapping must leave cleanup retryable");
	}

	[TestCase(false), TestCase(true)]
	[Description("Identity removal preserves CRM files, preserves shared pools, and empties the attachment.")]
	public void Remove_ShouldDeleteOnlyIdentity_WhenTargetIsVerified(bool sharedPool) {
		// Arrange
		_iis.DeleteAppPoolIfUnused(_attachment.ApplicationPool).Returns(sharedPool
			? IisAppPoolMutationResult.PreservedShared : IisAppPoolMutationResult.Completed);
		IdentityRemovalPlan plan = _sut.PrepareRemoval("crm");
		// Act
		_sut.Remove(plan);
		// Assert
		_files.Directory.Exists(_attachment.EnvironmentPath).Should().BeFalse(because: "verified identity files belong to the selected removal");
		_files.Directory.Exists(_environment.EnvironmentPath).Should().BeTrue(because: "standalone cleanup never removes CRM files");
		_environment.IdentityService.Should().Be(new IdentityServiceAttachment(), because: "the attachment clears only after artifact removal succeeds");
	}

	[Test]
	[Description("A failed pool deletion preserves files and attachment; retry converges a missing IIS target.")]
	public void Remove_ShouldRetainScopeAndRetry_WhenPoolDeletionFails() {
		// Arrange
		IdentityRemovalPlan plan = _sut.PrepareRemoval("crm");
		_iis.DeleteAppPoolIfUnused(_attachment.ApplicationPool).Returns(IisAppPoolMutationResult.Failed);
		// Act
		Action first = () => _sut.Remove(plan);
		// Assert
		first.Should().Throw<InvalidOperationException>(because: "a failed pool cleanup cannot be reported as completed");
		_environment.IdentityService.Should().Be(_attachment, because: "retry needs the original deletion authority");
		_files.Directory.Exists(_attachment.EnvironmentPath).Should().BeTrue(because: "files must survive a failed IIS stage");
		// Act
		_iis.DeleteAppPoolIfUnused(_attachment.ApplicationPool).Returns(IisAppPoolMutationResult.Completed);
		_sut.Remove(_sut.PrepareRemoval("crm"));
		// Assert
		_environment.IdentityService.Should().Be(new IdentityServiceAttachment(), because: "retry must converge despite the missing IIS target");
	}

	[TestCase("incomplete"), TestCase("overlap"), TestCase("shared"), TestCase("replaced"), TestCase("foreign-iis")]
	[Description("Ambiguous identity metadata and topology abort before any IIS deletion.")]
	public void PrepareRemoval_ShouldRefuseUnsafeScope_WhenOwnershipConflicts(string conflict) {
		// Arrange
		switch (conflict) {
			case "incomplete": _environment.IdentityService = _attachment with { IisTarget = "" }; break;
			case "overlap": _environment.EnvironmentPath = _attachment.EnvironmentPath; break;
			case "shared": _environments["other"] = new EnvironmentSettings { IdentityService = _attachment }; break;
			case "replaced": _targets[0] = _targets[0] with { siteBinding = _targets[0].siteBinding with { path = _environment.EnvironmentPath } }; break;
			case "foreign-iis": _targets.Add(_targets[0] with { siteBinding = _targets[0].siteBinding with { name = "other" } }); break;
		}
		// Act
		Action act = () => _sut.PrepareRemoval("crm");
		// Assert
		act.Should().Throw<InvalidOperationException>(because: "ambiguous identity targets must never authorize destructive work");
		_files.Directory.Exists(_attachment.EnvironmentPath).Should().BeTrue(because: "prevalidation must preserve all identity files");
	}

	[Test]
	[Description("A complete recorded scope permits cleanup when its files and IIS site are already absent.")]
	public void Remove_ShouldClearAttachment_WhenArtifactsAreMissing() {
		// Arrange
		_targets.Clear();
		_files.Directory.Delete(_attachment.EnvironmentPath);
		IdentityRemovalPlan plan = _sut.PrepareRemoval("crm");
		// Act
		_sut.Remove(plan);
		// Assert
		_environment.IdentityService.Should().Be(new IdentityServiceAttachment(), because: "missing artifacts are already at the desired final state");
	}

	[Test]
	[Description("Deployment saves ownership before extraction even when no OAuth credentials exist.")]
	public void RecordDeployment_ShouldPersistAttachment_WhenTargetIsValidated() {
		// Arrange
		_environment.IdentityService = new IdentityServiceAttachment();
		_targets.Clear();
		// Act
		_sut.RecordDeployment("crm", _attachment, overwrite: true);
		// Assert
		_environment.IdentityService.Should().Be(_attachment, because: "partial and no-app deployments need durable cleanup authority");
		_environment.ClientId.Should().BeNull(because: "recording a local attachment does not create OAuth credentials");
	}
}
