using System;
using System.Collections.Generic;
using Clio.Command.IdentityServiceDeployment;
using Clio.Common;
using Clio.Common.IIS;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.IdentityServiceDeployment;

[Property("Module", "Command")]
public sealed class UninstallIdentityCommandTests : BaseCommandTests<UninstallIdentityOptions> {
	private readonly IIdentityServiceLifecycle _lifecycle = Substitute.For<IIdentityServiceLifecycle>();
	private readonly IIdentityReferenceCleanup _references = Substitute.For<IIdentityReferenceCleanup>();
	private readonly IDeploymentTargetReservation _reservation = Substitute.For<IDeploymentTargetReservation>();
	private UninstallIdentityCommand _sut;

	protected override void AdditionalRegistrations(IServiceCollection services) {
		services.AddSingleton(_lifecycle).AddSingleton(_references).AddSingleton(_reservation);
	}

	public override void Setup() { base.Setup(); _sut = Container.GetRequiredService<UninstallIdentityCommand>(); }
	public override void TearDown() {
		_lifecycle.ClearReceivedCalls(); _references.ClearReceivedCalls(); _reservation.ClearReceivedCalls(); base.TearDown();
	}

	[TestCase(false, false), TestCase(true, false), TestCase(false, true)]
	[Description("CRM references clear before identity removal; explicit skip or persisted completion avoids HTTP cleanup.")]
	public void Execute_ShouldRespectCleanupOrder_WhenRemovingIdentity(bool skip, bool completed) {
		// Arrange
		List<string> calls = [];
		IdentityServiceAttachment attachment = new() { EnvironmentPath = "identity", CrmReferencesCleared = completed };
		IdentityRemovalPlan plan = new("crm", "crm-path", attachment);
		_lifecycle.PrepareRemoval("crm").Returns(plan);
		_lifecycle.MarkReferencesCleared(plan).Returns(_ => { calls.Add("checkpoint"); return plan; });
		_references.When(service => service.Clear(attachment)).Do(_ => calls.Add("references"));
		_lifecycle.When(service => service.Remove(plan)).Do(_ => calls.Add("remove"));
		// Act
		int result = _sut.Execute(new UninstallIdentityOptions { Environment = "crm", SkipCrmCleanup = skip });
		// Assert
		result.Should().Be(0, because: "a validated recorded identity can be removed");
		calls.Should().Equal(completed ? ["remove"] : skip ? ["checkpoint", "remove"] : ["references", "checkpoint", "remove"],
			because: "the checkpoint precedes deletion and prevents authentication through a previously removed identity");
	}

	[Test]
	[Description("A failed CRM cleanup prevents identity deletion and leaves the checkpoint unset.")]
	public void Execute_ShouldPreserveIdentity_WhenCrmCleanupFails() {
		// Arrange
		IdentityServiceAttachment attachment = new() { EnvironmentPath = "failed-identity" };
		IdentityRemovalPlan plan = new("failed", "crm-path", attachment);
		_lifecycle.PrepareRemoval("failed").Returns(plan);
		_references.When(service => service.Clear(attachment)).Do(_ => throw new InvalidOperationException("CRM unavailable"));
		// Act
		int result = _sut.Execute(new UninstallIdentityOptions { Environment = "failed" });
		// Assert
		result.Should().Be(1, because: "failure must be visible instead of reporting removal success");
		_lifecycle.ReceivedCalls().Should().NotContain(call => call.GetMethodInfo().Name == nameof(IIdentityServiceLifecycle.Remove),
			because: "identity must remain available when CRM references could not be cleared");
	}
}

[TestFixture, Category("Unit"), Property("Module", "Command")]
public sealed class IdentityReferenceCleanupTests {
	[TestCase(true), TestCase(false)]
	[Description("Only matching CRM references are cleared, with the identity URL last.")]
	public void Clear_ShouldPreserveUnrelatedSettings_WhenIdentityUrlDiffers(bool matches) {
		// Arrange
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		manager.GetAllUsersDefaultByCode("OAuth20IdentityServerUrl").Returns(matches ? "http://localhost:40301" : "https://other.example");
		List<string> updated = [];
		manager.UpdateSysSetting(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>()).Returns(call => {
			updated.Add(call.ArgAt<string>(0)); return true;
		});
		ServiceCollection services = new();
		services.AddSingleton(manager).AddTransient<IIdentityReferenceCleanup, IdentityReferenceCleanup>();
		using ServiceProvider container = services.BuildServiceProvider();
		// Act
		container.GetRequiredService<IIdentityReferenceCleanup>().Clear(new IdentityServiceAttachment { Uri = "http://localhost:40301" });
		// Assert
		updated.Should().Equal(matches ? ["OAuth20IdentityServerClientSecret", "OAuth20IdentityServerClientId", "OAuth20IdentityServerUrl"] : Array.Empty<string>(),
			because: "only the matching integration is cleared and URL remains usable until the final write");
	}
}
