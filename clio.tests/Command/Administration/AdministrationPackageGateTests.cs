using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Command.Administration;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.Administration;

/// <summary>Proves both public dispatch paths enforce the administration bridge package floor.</summary>
[TestFixture, Property("Module", "McpServer"), NonParallelizable]
public sealed class AdministrationPackageGateTests : BaseClioModuleTests {

	private IApplicationPackageListProvider _packages;
	private IAdministrationService _administration;
	private IToolCommandResolver _resolver;
	private ICreatioVersionProvider _versions;

	protected override void AdditionalRegistrations(IServiceCollection services) {
		_packages = Substitute.For<IApplicationPackageListProvider>();
		_administration = Substitute.For<IAdministrationService>();
		_resolver = Substitute.For<IToolCommandResolver>();
		_versions = Substitute.For<ICreatioVersionProvider>();
		services.AddSingleton(_packages);
		services.AddSingleton(_administration);
		services.AddSingleton(_resolver);
		services.AddSingleton(_versions);
		services.AddSingleton(Substitute.For<IBundledPackageConvergence>());
	}

	public override void TearDown() {
		_packages.ClearReceivedCalls();
		_administration.ClearReceivedCalls();
		_resolver.ClearReceivedCalls();
		_versions.ClearReceivedCalls();
		base.TearDown();
	}

	[TestCase(false, "cliogate", "2.0.0.48")]
	[TestCase(true, "cliogate_netcore", "2.0.0.48")]
	[TestCase(false, "cliogate", "2.0.0.49")]
	[TestCase(true, "cliogate_netcore", "2.0.0.49")]
	[Description("CLI dispatch refuses both bridge actions when the installed package predates their endpoints.")]
	public void Cli_RejectsOlderBridge(bool licenses, string packageName, string version) {
		// Arrange
		Installed(packageName, version);
		object options = licenses
			? new ManageLicenseOptions { Action = "role-redistribute" }
			: new ManageRoleOptions { Action = "remove-functional" };
		// Act
		bool blocked = Clio.Program.TryGetPackageRequirementError(options,
			Container.GetRequiredService<IRequiredPackageChecker>(), out string message);
		// Assert
		blocked.Should().BeTrue(because: "the older package does not contain the administration endpoints");
		message.Should().Contain("2.0.0.50", because: "the refusal must name the required version");
		message.Should().Contain("install-gate", because: "the refusal must give the operator the installation action");
	}

	[TestCase(false, "2.0.0.48")]
	[TestCase(true, "2.0.0.48")]
	[TestCase(false, "2.0.0.49")]
	[TestCase(true, "2.0.0.49")]
	[Description("MCP resolves the same action-specific version floor against its selected environment before any administration mutation.")]
	public void Mcp_RejectsOlderBridgeBeforeMutation(bool licenses, string version) {
		// Arrange
		Installed("cliogate_netcore", version);
		_resolver.Resolve<IRequiredPackageChecker>(Arg.Any<EnvironmentOptions>())
			.Returns(Container.GetRequiredService<IRequiredPackageChecker>());
		// Act
		CommandExecutionResult result = licenses
			? Container.GetRequiredService<ManageLicenseTool>().Manage(new ManageLicenseArgs {
				EnvironmentName = "exclusive-lab", Action = "role-redistribute" })
			: Container.GetRequiredService<ManageRoleTool>().Manage(new ManageRoleArgs {
				EnvironmentName = "exclusive-lab", Action = "remove-functional" });
		// Assert
		result.ExitCode.Should().Be(1, because: "an unmet package requirement is a fixable precondition");
		result.Output.Select(message => message.Value?.ToString()).Should().Contain(message => message.Contains("2.0.0.50"),
			because: "the agent must receive the required package version");
		_administration.ReceivedCalls().Should().BeEmpty(because: "a refused tool must not execute any administration operation");
	}

	[TestCase(false, "cliogate")]
	[TestCase(true, "cliogate_netcore")]
	[Description("The required bridge release permits dispatch for both package identities.")]
	public void CurrentBridge_PermitsDispatch(bool licenses, string packageName) {
		// Arrange
		Installed(packageName, "2.0.0.50");
		object options = licenses ? new ManageLicenseOptions { Action = "role-redistribute" } : new ManageRoleOptions { Action = "remove-functional" };
		// Act
		bool blocked = Clio.Program.TryGetPackageRequirementError(options,
			Container.GetRequiredService<IRequiredPackageChecker>(), out string message);
		// Assert
		blocked.Should().BeFalse(because: "the required package contains the endpoints and receipt contract");
		message.Should().BeNull(because: "a compatible version must have no dispatch error");
	}

	[TestCase(false)]
	[TestCase(true)]
	[Description("Native inspection actions do not query package inventory when no bridge is needed.")]
	public void NativeInspection_DoesNotRequireGate(bool licenses) {
		// Arrange
		object options = licenses ? new ManageLicenseOptions { Action = "user-list" } : new ManageRoleOptions { Action = "list" };
		// Act
		bool blocked = Clio.Program.TryGetPackageRequirementError(options,
			Container.GetRequiredService<IRequiredPackageChecker>(), out _);
		// Assert
		blocked.Should().BeFalse(because: "native reads have no dependency on the new bridge");
		_packages.ReceivedCalls().Should().BeEmpty(because: "inactive conditional requirements must not fetch packages");
	}

	[TestCase("create", "8.1.5.0")]
	[TestCase("password", "8.1.5.0")]
	[TestCase("create", "0.0.0.0")]
	[TestCase("password", "0.0.0.0")]
	[TestCase("create", "10.1.584.0")]
	[TestCase("password", "10.1.584.0")]
	[Description("Both dispatch paths reject password writes when safe native logging has no verified version evidence.")]
	public void PasswordWrites_RejectUnverifiedVersions(string action, string version) {
		// Arrange
		_versions.Resolve().Returns(CreatioVersionResolution.Resolved(Version.Parse(version)));
		ICreatioVersionChecker checker = Container.GetRequiredService<ICreatioVersionChecker>();
		_resolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>()).Returns(checker);
		// Act
		bool cliBlocked = Clio.Program.TryGetCreatioVersionRequirementError(new ManageUserOptions { Action = action }, checker, out string message);
		CommandExecutionResult mcp = Container.GetRequiredService<ManageUserTool>().Manage(new ManageUserArgs {
			EnvironmentName = "exclusive-lab", Action = action, PasswordEnvironmentVariable = "UNREAD_SECRET_REFERENCE"
		});
		// Assert
		cliBlocked.Should().BeTrue(because: "known older and unversioned servers must not receive password payloads");
		message.Should().Contain("10.1.585.0", because: "the refusal must identify the verified native version boundary");
		mcp.ExitCode.Should().Be(78, because: "MCP must enforce the same version prerequisite before executing the command");
		_administration.ReceivedCalls().Should().BeEmpty(because: "neither path may reach a password-writing service");
	}

	[Test]
	[Description("Missing operation priority fails before the administration service can reorder grants.")]
	public void OperationPosition_RequiresExplicitValue() {
		// Arrange
		ManageAccessOptions options = new() { Action = "operation-position", Confirm = true, Id = Guid.NewGuid() };
		// Act
		int result = Container.GetRequiredService<ManageAccessCommand>().Execute(options);
		// Assert
		result.Should().Be(1, because: "an omitted position must not silently become the highest priority");
		_administration.ReceivedCalls().Should().BeEmpty(because: "the mutation needs explicit ordering intent");
	}

	[TestCase("cliogate", "2.0.0.50")]
	[TestCase("cliogate_netcore", "2.0.0.50")]
	[TestCase("cliogate", "2.0.0.51")]
	[TestCase("cliogate_netcore", "2.0.0.51")]
	[Description("CLI and MCP reject priority changes before any write when the cache bridge is missing.")]
	public void Priority_RequiresCacheBridge(string packageName, string version) {
		// Arrange
		Installed(packageName, version);
		_versions.Resolve().Returns(CreatioVersionResolution.Resolved(Version.Parse("10.1.585.0")));
		_resolver.Resolve<ICreatioVersionChecker>(Arg.Any<EnvironmentOptions>()).Returns(Container.GetRequiredService<ICreatioVersionChecker>());
		_resolver.Resolve<IRequiredPackageChecker>(Arg.Any<EnvironmentOptions>()).Returns(Container.GetRequiredService<IRequiredPackageChecker>());
		// Act
		bool blocked = Clio.Program.TryGetPackageRequirementError(new ManageAccessOptions { Action = "operation-position" },
			Container.GetRequiredService<IRequiredPackageChecker>(), out string message);
		CommandExecutionResult mcp = Container.GetRequiredService<ManageAccessTool>().Manage(new ManageAccessArgs {
			EnvironmentName = "exclusive-lab", Action = "operation-position", Position = 0
		});
		// Assert
		blocked.Should().BeTrue(because: "priority writes require the cache invalidation endpoint");
		message.Should().Contain("2.0.0.52", because: "the operator needs the cache bridge release");
		mcp.ExitCode.Should().Be(1, because: "MCP must enforce the same package prerequisite");
		_administration.ReceivedCalls().Should().BeEmpty(because: "refusal must happen before priority changes persist");
	}

	[TestCase("cliogate")]
	[TestCase("cliogate_netcore")]
	[Description("The cache bridge release satisfies the action-specific priority package gate.")]
	public void Priority_CurrentBridgePermitsDispatch(string packageName) {
		// Arrange
		Installed(packageName, "2.0.0.52");
		// Act
		bool blocked = Clio.Program.TryGetPackageRequirementError(new ManageAccessOptions { Action = "operation-position" },
			Container.GetRequiredService<IRequiredPackageChecker>(), out string message);
		// Assert
		blocked.Should().BeFalse(because: "this release contains native rights-cache invalidation");
		message.Should().BeNull(because: "the compatible package satisfies the prerequisite");
	}

	private void Installed(string name, string version) => _packages.GetPackages().Returns(new[] {
		new PackageInfo(new PackageDescriptor { Name = name, PackageVersion = version }, "", new List<string>())
	});
}
