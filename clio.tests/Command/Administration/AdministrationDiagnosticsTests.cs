using System;
using System.Linq;
using Clio.Command.Administration;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.Administration;

/// <summary>Preserves authored repair instructions while sanitizing native failures.</summary>
[TestFixture]
public sealed class AdministrationDiagnosticsTests : BaseCommandTests<ManageRoleOptions> {
	private IAdministrationService _administration;
	private ILogger _logger;
	private ManageRoleCommand _command;

	protected override void AdditionalRegistrations(IServiceCollection services) {
		base.AdditionalRegistrations(services);
		_administration = Substitute.For<IAdministrationService>();
		_logger = Substitute.For<ILogger>();
		services.AddSingleton(_administration);
		services.AddSingleton(_logger);
	}

	[SetUp]
	public void ResolveCommand() => _command = Container.GetRequiredService<ManageRoleCommand>();

	public override void TearDown() {
		_administration.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	[TestCase(true)]
	[TestCase(false)]
	[Description("Only the explicit safe diagnostic category preserves its message in command output.")]
	public void Execute_PreservesSafeDiagnosisOnly(bool safe) {
		// Arrange
		const string diagnosis = "More than one manager role exists for this parent. Resolve the ambiguity explicitly.";
		Exception failure = safe ? new AdministrationStateException(diagnosis) : new InvalidOperationException("native-secret-value");
		_administration.EnsureManager(Arg.Any<Guid>()).Returns(_ => throw failure);
		// Act
		int exitCode = _command.Execute(new ManageRoleOptions { Action = "ensure-manager", ParentId = Guid.NewGuid(), Confirm = true });
		// Assert
		exitCode.Should().Be(1, because: "both diagnostic kinds represent an incomplete operation");
		string output = string.Join(" ", _logger.ReceivedCalls().SelectMany(call => call.GetArguments()).OfType<string>());
		output.Contains(diagnosis, StringComparison.Ordinal).Should().Be(safe, because: "only locally authored diagnoses may pass through");
		output.Should().NotContain("native-secret-value", because: "native exception content may contain credentials");
	}
}
