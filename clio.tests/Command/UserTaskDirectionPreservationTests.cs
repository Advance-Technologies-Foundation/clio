using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Clio.Command;
using Clio.Common;
using Clio.Package;
using Clio.Workspaces;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>Guards directions when the native designer DTO omits them.</summary>
[TestFixture]
[Property("Module", "Command")]
public sealed class UserTaskDirectionPreservationTests : BaseCommandTests<ModifyUserTaskParametersOptions> {
	private IApplicationClient _client;
	private IUserTaskMetadataDirectionApplier _directions;
	private IFileDesignModePackages _packages;
	private ModifyUserTaskParametersCommand _command;
	private string _metadataPath;

	/// <inheritdoc />
	protected override void AdditionalRegistrations(IServiceCollection services) {
		base.AdditionalRegistrations(services);
		_client = Substitute.For<IApplicationClient>();
		_packages = Substitute.For<IFileDesignModePackages>();
		_packages.LoadPackagesToDb().Returns(FileDesignModeLoadResult.Completed);
		var paths = Substitute.For<IWorkspacePathBuilder>();
		string root = Path.GetFullPath("direction-test-workspace");
		string settingsPath = Path.Combine(root, ".clio", "workspaceSettings.json");
		string packagePath = Path.Combine(root, "packages", "TestPackage");
		_metadataPath = Path.Combine(packagePath, "Schemas", "UsrTest", "metadata.json");
		FileSystem.Directory.CreateDirectory(root);
		paths.RootPath.Returns(root);
		paths.IsWorkspace.Returns(true);
		paths.WorkspaceSettingsPath.Returns(settingsPath);
		paths.BuildPackagePath("TestPackage").Returns(packagePath);
		var converter = Substitute.For<IJsonConverter>();
		converter.DeserializeObjectFromFile<WorkspaceSettings>(settingsPath)
			.Returns(new WorkspaceSettings { Packages = ["TestPackage"] });
		var routes = Substitute.For<IServiceUrlBuilder>();
		routes.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>()).Returns(call => call.Arg<ServiceUrlBuilder.KnownRoute>().ToString());
		services.AddSingleton(_client);
		services.AddSingleton(_packages);
		services.AddSingleton(paths);
		services.AddSingleton(converter);
		services.AddSingleton(routes);
	}

	/// <inheritdoc />
	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<ModifyUserTaskParametersCommand>();
		_directions = Container.GetRequiredService<IUserTaskMetadataDirectionApplier>();
	}

	/// <inheritdoc />
	public override void TearDown() {
		_client.ClearReceivedCalls();
		_packages.ClearReceivedCalls();
		base.TearDown();
	}

	/// <summary>Checks preservation, removal and explicit override in the same modification.</summary>
	[TestCase(0, null, 0)]
	[TestCase(1, null, 1)]
	[TestCase(2, null, 2)]
	[TestCase(0, "Keep=Out", 1)]
	[Description("Preserves existing native directions across a DTO save, drops removed parameters and honors explicit overrides.")]
	public void Modify_PreservesDirections(int originalDirection, string update, int expectedDirection) {
		// Arrange
		Guid schemaUid = Guid.NewGuid();
		FileSystem.AddFile(_metadataPath, new System.IO.Abstractions.TestingHelpers.MockFileData(
			JsonSerializer.Serialize(new { MetaData = new { Schema = new { FJ1 = new[] {
				new { A2 = "Keep", L12 = originalDirection }, new { A2 = "Remove", L12 = 1 }
			} } } })));
		_client.ExecutePostRequest("GetWorkspaceItems", Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(JsonSerializer.Serialize(new { items = new[] { new { name = "UsrTest", uId = schemaUid, packageName = "TestPackage", type = 8 } } }));
		_client.ExecutePostRequest("GetUserTaskSchema", Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(JsonSerializer.Serialize(new { success = true, schema = new { name = "UsrTest", uId = schemaUid, parameters = new[] { new { name = "Keep" }, new { name = "Remove" } } } }));
		_client.ExecutePostRequest("SaveUserTaskSchema", Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(_ => {
				FileSystem.File.WriteAllText(_metadataPath, """{"MetaData":{"Schema":{"FJ1":[{"A2":"Keep"},{"A2":"Added"}]}}}""");
				return JsonSerializer.Serialize(new { success = true, schemaUid });
			});
		ModifyUserTaskParametersOptions options = new() {
			UserTaskName = "UsrTest", RemoveParameters = ["Remove"],
			AddParameters = ["code=Added;title=Added;type=Unlimited text;direction=Out"],
			SetDirections = update is null ? [] : [update]
		};

		// Act
		int result = _command.Execute(options);
		IReadOnlyDictionary<string, int> actual = _directions.ReadDirections("TestPackage", "UsrTest");

		// Assert
		result.Should().Be(0, because: "a valid parameter change must complete");
		actual.Should().BeEquivalentTo(new Dictionary<string, int> { ["Keep"] = expectedDirection, ["Added"] = 1 },
			because: "saving an unrelated parameter must preserve directions and explicit edits must take precedence");
	}
}
