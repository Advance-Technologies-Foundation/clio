using System;
using System.Linq;
using Clio.Command.Administration;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.Administration;

/// <summary>Restricts password reads to the explicitly provisioned administration namespace.</summary>
[TestFixture, NonParallelizable]
public sealed class AdministrationPasswordReferenceTests : BaseCommandTests<ManageUserOptions> {
	private IAdministrationService _administration;
	private ILogger _logger;
	private ManageUserCommand _command;

	protected override void AdditionalRegistrations(IServiceCollection services) {
		base.AdditionalRegistrations(services);
		_administration = Substitute.For<IAdministrationService>();
		_logger = Substitute.For<ILogger>();
		services.AddSingleton(_administration);
		services.AddSingleton(_logger);
	}

	[SetUp]
	public void ResolveCommand() => _command = Container.GetRequiredService<ManageUserCommand>();

	public override void TearDown() {
		_administration.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	[TestCase(null)]
	[TestCase("CLIO_ADMIN_PASSWORD_")]
	[TestCase("CLIO_ADMIN_PASSWORD_lowercase")]
	[TestCase("CLIO_ADMIN_PASSWORD_BAD=NAME")]
	[TestCase("CLIO_ADMIN_PASSWORD_BAD NAME")]
	[TestCase("CLIO_ADMIN_PASSWORD_BAD\0NAME")]
	[Description("Malformed password references receive the same safe error before service execution.")]
	public void Execute_RejectsMalformedReferences(string reference) {
		// Arrange
		ManageUserOptions options = PasswordOptions(reference);
		// Act
		int result = _command.Execute(options);
		// Assert
		AssertRefused(result);
	}

	[TestCase(false, true)]
	[TestCase(true, false)]
	[Description("An unrelated populated host variable and a missing allowed variable are indistinguishable and never sent.")]
	public void Execute_RejectsUnavailableOrUnrelatedReference(bool allowedPrefix, bool populated) {
		// Arrange
		string reference = (allowedPrefix ? "CLIO_ADMIN_PASSWORD_" : "UNRELATED_HOST_SECRET_") + Guid.NewGuid().ToString("N").ToUpperInvariant();
		Environment.SetEnvironmentVariable(reference, populated ? "unrelated-secret" : null);
		try {
			// Act
			int result = _command.Execute(PasswordOptions(reference));
			// Assert
			AssertRefused(result);
		} finally {
			Environment.SetEnvironmentVariable(reference, null);
		}
	}

	[Test]
	[Description("An explicitly provisioned password is passed unchanged and never echoed.")]
	public void Execute_AcceptsDedicatedReference() {
		// Arrange
		string reference = "CLIO_ADMIN_PASSWORD_" + Guid.NewGuid().ToString("N").ToUpperInvariant();
		const string password = " temporary-test-password ";
		Environment.SetEnvironmentVariable(reference, password);
		ManageUserOptions options = PasswordOptions(reference);
		try {
			// Act
			int result = _command.Execute(options);
			// Assert
			result.Should().Be(0, because: "the operator explicitly provided this dedicated password reference");
			object[] arguments = _administration.ReceivedCalls().Should().ContainSingle(because: "one password mutation is expected").Which.GetArguments();
			arguments[1].Should().Be(password, because: "password whitespace is significant and must be preserved");
			Output().Should().NotContain(password, because: "the secret must not appear in command logs");
		} finally {
			Environment.SetEnvironmentVariable(reference, null);
		}
	}

	private void AssertRefused(int result) {
		result.Should().Be(1, because: "unapproved or absent password references must fail closed");
		_administration.ReceivedCalls().Should().BeEmpty(because: "no account mutation may receive an unrelated host secret");
		Output().Should().Be("Supply a populated password reference named CLIO_ADMIN_PASSWORD_<SUFFIX> using uppercase letters, digits or underscores.",
			because: "the error must not reveal whether an arbitrary host variable exists");
	}

	private string Output() => string.Join(" ", _logger.ReceivedCalls().SelectMany(call => call.GetArguments()).OfType<string>());
	private static ManageUserOptions PasswordOptions(string reference) => new() {
		Action = "password", Id = Guid.NewGuid(), PasswordEnvironmentVariable = reference, Confirm = true
	};
}
