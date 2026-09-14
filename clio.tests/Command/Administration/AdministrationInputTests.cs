using Clio.Command.Administration;
using CommandLine;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.Administration;

/// <summary>Guards distinctions between the operator's connection and the managed identity.</summary>
[TestFixture, Category("Unit"), Property("Module", "Command")]
public sealed class AdministrationInputTests {
	[Test]
	[Description("CLI login authenticates the operator while user-login identifies the target account.")]
	public void UserOptions_KeepCallerAndTargetLoginSeparate() {
		// Arrange
		string[] arguments = ["--action", "list", "--login", "operator", "--user-login", "target"];
		// Act
		ParserResult<ManageUserOptions> result = Parser.Default.ParseArguments<ManageUserOptions>(arguments);
		// Assert
		ManageUserOptions options = result.Should().BeOfType<Parsed<ManageUserOptions>>(
			because: "the target option must not collide with inherited authentication options").Which.Value;
		options.Login.Should().Be("operator", because: "login belongs to the caller's environment credentials");
		options.UserLogin.Should().Be("target", because: "user-login identifies the account being managed");
	}
}
