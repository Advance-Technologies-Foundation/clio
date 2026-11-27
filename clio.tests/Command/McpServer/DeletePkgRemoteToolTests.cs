namespace Clio.Tests.Command.McpServer;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.PackageCommand;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

/// <summary>
/// Covers the delete-pkg-remote MCP surface: the read-only pre-flight refusals, the single delete write,
/// and the read-back the response promises.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class DeletePkgRemoteToolTests : BaseClioModuleTests {

	private const string EnvironmentName = "dev";
	private const string PackageName = "UsrScratchPkg";
	private const string PackageUId = "6f9619ff-8b86-d011-b42d-00c05fc76f10";
	private const string SelectUrl = "http://localhost/0/DataService/json/SyncReply/SelectQuery";
	private const string DeleteUrlMarker = "/ServiceModel/AppInstallerService.svc/DeletePackage";

	private IApplicationClient _applicationClient = null!;
	private IApplicationPackageListProvider _packageListProvider = null!;

	// Per-rootSchema answers for the pre-flight Selects; the read-back Select after the delete answers
	// from _sysPackageAfterDelete instead, so the same row the pre-flight read is not what it verifies.
	private readonly Dictionary<string, string> _selectAnswers = new(StringComparer.Ordinal);
	private string _deleteAnswer = """{"success":true}""";
	private string _sysPackageAfterDelete = Answer();
	private bool _deleteSent;

	public override void TearDown() {
		_applicationClient.ClearReceivedCalls();
		_packageListProvider.ClearReceivedCalls();
		base.TearDown();
	}

	public override void Setup() {
		base.Setup();
		// NUnit 4 reuses one fixture instance per fixture, so per-test state must be reset here.
		_selectAnswers.Clear();
		_deleteSent = false;
		_deleteAnswer = """{"success":true}""";
		_sysPackageAfterDelete = Answer();
		// The environment's own maintainer, the value the foreign rule compares a package's maintainer against.
		_selectAnswers["SysSettingsValue"] = Answer(new { TextValue = "Customer" });
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_applicationClient = Substitute.For<IApplicationClient>();
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns(callInfo => Route(callInfo.ArgAt<string>(0), callInfo.ArgAt<string>(1)));

		_packageListProvider = Substitute.For<IApplicationPackageListProvider>();
		_packageListProvider.GetPackages().Returns([]);

		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select).Returns(SelectUrl);

		containerBuilder.AddTransient(_ => _applicationClient);
		containerBuilder.AddTransient(_ => _packageListProvider);
		containerBuilder.AddTransient(_ => serviceUrlBuilder);
	}

	private string Route(string url, string body) {
		if (url.Contains(DeleteUrlMarker, StringComparison.Ordinal)) {
			_deleteSent = true;
			return _deleteAnswer;
		}
		using JsonDocument document = JsonDocument.Parse(body);
		string rootSchema = document.RootElement.GetProperty("rootSchemaName").GetString() ?? string.Empty;
		if (rootSchema == "SysPackage" && _deleteSent) {
			return _sysPackageAfterDelete;
		}
		return _selectAnswers.TryGetValue(rootSchema, out string answer) ? answer : Answer(rows: []);
	}

	private static string Answer(params object[] rows) =>
		"""{"success":true,"rows":""" + JsonSerializer.Serialize(rows) + "}";

	private static object PackageRow(int? installType = 0, string? maintainer = "Customer") =>
		new { Name = PackageName, UId = PackageUId, Maintainer = maintainer, InstallType = installType };

	private static string DependencyAnswer(params string[] dependentNames) =>
		Answer(dependentNames.Select(name => new Dictionary<string, string> { ["SysPackage.Name"] = name }).ToArray());

	private void ReadyPreFlight() {
		_selectAnswers["SysPackage"] = Answer(PackageRow());
		_selectAnswers["SysPackageDependency"] = DependencyAnswer();
		_selectAnswers["SysSchema"] = Answer(new { Name = "UsrOrder" });
	}

	private DeletePkgRemoteTool BuildTool() {
		DeletePackageCommand command = Container.GetRequiredService<DeletePackageCommand>();
		PackageDeletionPreflight preflight = Container.GetRequiredService<IPackageDeletionPreflight>()
			as PackageDeletionPreflight ?? throw new InvalidOperationException("The container must resolve the pre-flight.");
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<DeletePackageCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);
		commandResolver.Resolve<IPackageDeletionPreflight>(Arg.Any<EnvironmentOptions>()).Returns(preflight);
		return new DeletePkgRemoteTool(command, Container.GetRequiredService<ILogger>(), commandResolver);
	}

	[Test]
	[Description("Advertises the stable MCP tool name for the package deletion surface.")]
	public void DeletePkgRemoteTool_Should_Advertise_Stable_Tool_Name() {
		// Assert
		DeletePkgRemoteTool.DeletePkgRemoteToolName.Should().Be("delete-pkg-remote",
			because: "the tool name is the CLI verb the tool wraps, and prompts and callers reuse it");
	}

	[Test]
	[Description("Marks the tool destructive and not read-only so MCP clients enforce the destructive-call path for it.")]
	public void DeletePkgRemoteTool_Should_Be_Marked_Destructive_And_Not_ReadOnly() {
		// Arrange
		System.Reflection.MethodInfo method = typeof(DeletePkgRemoteTool).GetMethod(nameof(DeletePkgRemoteTool.DeletePkgRemote))!;
		McpServerToolAttribute attribute = method
			.GetCustomAttributes(typeof(McpServerToolAttribute), inherit: false)
			.Cast<McpServerToolAttribute>()
			.Single();

		// Act
		bool destructive = attribute.Destructive;
		bool readOnly = attribute.ReadOnly;

		// Assert
		destructive.Should().BeTrue(because: "the tool deletes a package the environment keeps");
		readOnly.Should().BeFalse(because: "the tool sends a write to the environment");
	}

	[Test]
	[Description("Refuses an empty environment-name before any call reaches the environment.")]
	public void DeletePkgRemote_Should_Refuse_When_The_Environment_Name_Is_Missing() {
		// Arrange
		DeletePkgRemoteTool tool = BuildTool();

		// Act
		DeletePkgRemoteResponse response = tool.DeletePkgRemote(new DeletePkgRemoteArgs(
			EnvironmentName: string.Empty, PackageName: PackageName));

		// Assert
		response.Success.Should().BeFalse(because: "no environment was named, so nothing may be deleted");
		response.Error.Should().Contain("environment-name", because: "the caller must see which argument to fix");
		_applicationClient.DidNotReceive()
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("Refuses an empty package-name before any call reaches the environment.")]
	public void DeletePkgRemote_Should_Refuse_When_The_Package_Name_Is_Missing() {
		// Arrange
		DeletePkgRemoteTool tool = BuildTool();

		// Act
		DeletePkgRemoteResponse response = tool.DeletePkgRemote(new DeletePkgRemoteArgs(
			EnvironmentName: EnvironmentName, PackageName: " "));

		// Assert
		response.Success.Should().BeFalse(because: "no package was named, so nothing may be deleted");
		response.Error.Should().Contain("package-name", because: "the caller must see which argument to fix");
		response.PackageName.Should().BeNull(
			because: "no package was named, so the response must not echo the environment name as the package");
		_applicationClient.DidNotReceive()
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("Reports not-found without calling the delete service when no package with the name is installed.")]
	public void DeletePkgRemote_Should_Report_NotFound_Without_Calling_The_Delete() {
		// Arrange
		_selectAnswers["SysPackage"] = Answer();
		DeletePkgRemoteTool tool = BuildTool();

		// Act
		DeletePkgRemoteResponse response = tool.DeletePkgRemote(new DeletePkgRemoteArgs(
			EnvironmentName: EnvironmentName, PackageName: PackageName));

		// Assert
		response.Success.Should().BeFalse(because: "there was nothing to delete");
		response.Status.Should().Be(DeletePkgRemoteTool.StatusNotFound,
			because: "the pre-flight found no SysPackage row for the name");
		response.Deleted.Should().BeFalse(because: "the call deleted nothing");
		DidNotCallDelete();
	}

	[Test]
	[Description("Reports locked without calling the delete service when the package is a product, market-place or repository package.")]
	public void DeletePkgRemote_Should_Report_Locked_Without_Calling_The_Delete() {
		// Arrange
		_selectAnswers["SysPackage"] = Answer(PackageRow(maintainer: "Partner"));
		DeletePkgRemoteTool tool = BuildTool();

		// Act
		DeletePkgRemoteResponse response = tool.DeletePkgRemote(new DeletePkgRemoteArgs(
			EnvironmentName: EnvironmentName, PackageName: PackageName));

		// Assert
		response.Success.Should().BeFalse(because: "a locked package is not one the environment deletes");
		response.Status.Should().Be(DeletePkgRemoteTool.StatusLocked,
			because: "the lock is a refusal the pre-flight surfaces instead of sending");
		response.PackageUId.Should().Be(PackageUId, because: "the caller sees the identifier it must not delete");
		DidNotCallDelete();
	}

	[Test]
	[Description("Reports blocked-by-dependents and names the dependents without calling the delete service.")]
	public void DeletePkgRemote_Should_Report_BlockedByDependents_Without_Calling_The_Delete() {
		// Arrange
		_selectAnswers["SysPackage"] = Answer(PackageRow());
		_selectAnswers["SysPackageDependency"] = DependencyAnswer("UsrChildA", "UsrChildB");
		DeletePkgRemoteTool tool = BuildTool();

		// Act
		DeletePkgRemoteResponse response = tool.DeletePkgRemote(new DeletePkgRemoteArgs(
			EnvironmentName: EnvironmentName, PackageName: PackageName));

		// Assert
		response.Success.Should().BeFalse(because: "deleting it would leave its dependents without a dependency");
		response.Status.Should().Be(DeletePkgRemoteTool.StatusBlockedByDependents,
			because: "SysPackageDependency has rows pointing at the package");
		response.Dependents.Should().Equal(["UsrChildA", "UsrChildB"],
			because: "the caller needs the exact names to unblock before retrying");
		DidNotCallDelete();
	}

	[Test]
	[Description("Reports unavailable without calling the delete service when the pre-flight cannot read the environment.")]
	public void DeletePkgRemote_Should_Report_Unavailable_Without_Calling_The_Delete() {
		// Arrange
		_selectAnswers["SysPackage"] = """{"success":false,"errorInfo":{"message":"service down"}}""";
		DeletePkgRemoteTool tool = BuildTool();

		// Act
		DeletePkgRemoteResponse response = tool.DeletePkgRemote(new DeletePkgRemoteArgs(
			EnvironmentName: EnvironmentName, PackageName: PackageName));

		// Assert
		response.Success.Should().BeFalse(because: "a deletion whose state was never read is not a safe deletion");
		response.Status.Should().Be(DeletePkgRemoteTool.StatusUnavailable,
			because: "the pre-flight could not establish enough to approve the delete");
		response.Deleted.Should().BeNull(because: "the environment could not be read, so the outcome is not claimed either way");
		response.Error.Should().NotBeNull(because: "the caller must learn the environment could not be read");
		DidNotCallDelete();
	}

	[Test]
	[Description("Deletes the package and reports deleted=true only after the read-back confirms it is gone.")]
	public void DeletePkgRemote_Should_Delete_And_Report_Deleted_When_The_ReadBack_Confirms_It_Is_Gone() {
		// Arrange
		ReadyPreFlight();
		_packageListProvider.GetPackages().Returns([]);
		_sysPackageAfterDelete = Answer();
		DeletePkgRemoteTool tool = BuildTool();

		// Act
		DeletePkgRemoteResponse response = tool.DeletePkgRemote(new DeletePkgRemoteArgs(
			EnvironmentName: EnvironmentName, PackageName: PackageName));

		// Assert
		response.Success.Should().BeTrue(because: "the delete completed and the read-back confirmed the absence");
		response.Status.Should().Be(DeletePkgRemoteTool.StatusDeleted,
			because: "both the call and the read-back agree the package is gone");
		response.Deleted.Should().BeTrue(because: "the read-back found no SysPackage row");
		response.PackageUId.Should().Be(PackageUId, because: "the report carries the identifier it deleted");
		response.Inventory.Should().NotBeNull(because: "the inventory is reported before the rows leave with the package");
		response.Inventory!.SchemaCount.Should().Be(1, because: "the package held one schema when the delete was considered");
		response.Error.Should().BeNull(because: "nothing failed");
	}

	[Test]
	[Description("Reports delete-failed when the site refuses the delete and the package is still listed.")]
	public void DeletePkgRemote_Should_Report_DeleteFailed_When_The_Site_Refuses_The_Delete() {
		// Arrange
		ReadyPreFlight();
		_deleteAnswer = """{"success":false,"errorInfo":{"message":"package is in use"}}""";
		_packageListProvider.GetPackages().Returns([
			new PackageInfo(new PackageDescriptor { Name = PackageName }, string.Empty, new List<string>())
		]);
		_sysPackageAfterDelete = Answer(PackageRow());
		DeletePkgRemoteTool tool = BuildTool();

		// Act
		DeletePkgRemoteResponse response = tool.DeletePkgRemote(new DeletePkgRemoteArgs(
			EnvironmentName: EnvironmentName, PackageName: PackageName));

		// Assert
		response.Success.Should().BeFalse(because: "the site refused the delete");
		response.Status.Should().Be(DeletePkgRemoteTool.StatusDeleteFailed,
			because: "the delete call did not remove the package");
		response.Deleted.Should().BeFalse(because: "the read-back still lists the package");
		response.Error.Should().NotBeNull(because: "the caller must see that the delete was refused");
	}

	[Test]
	[Description("Reports delete-unverified when the delete call completed but the read-back could not run.")]
	public void DeletePkgRemote_Should_Report_DeleteUnverified_When_The_ReadBack_Fails() {
		// Arrange
		ReadyPreFlight();
		_packageListProvider.GetPackages().Returns([]);
		_sysPackageAfterDelete = """{"success":false,"errorInfo":{"message":"connection dropped"}}""";
		DeletePkgRemoteTool tool = BuildTool();

		// Act
		DeletePkgRemoteResponse response = tool.DeletePkgRemote(new DeletePkgRemoteArgs(
			EnvironmentName: EnvironmentName, PackageName: PackageName));

		// Assert
		response.Success.Should().BeFalse(
			because: "the tool promises to verify the absence, and it could not, so it does not report success");
		response.Status.Should().Be(DeletePkgRemoteTool.StatusDeleteUnverified,
			because: "the call completed but the read-back could not confirm the package is gone");
		response.Deleted.Should().BeNull(because: "the read-back failed, so the outcome is not claimed either way");
		response.Error.Should().Contain("list-packages",
			because: "the caller is told how to check before retrying the destructive call");
	}

	private void DidNotCallDelete() {
		_applicationClient.DidNotReceive()
			.ExecutePostRequest(Arg.Is<string>(url => url.Contains(DeleteUrlMarker, StringComparison.Ordinal)),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}
}
