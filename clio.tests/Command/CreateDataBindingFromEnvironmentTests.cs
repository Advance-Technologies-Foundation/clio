using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Command;
using Clio.Common;
using Clio.Workspaces;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// <c>create-data-binding --from-environment</c>: writes one stored DB binding to disk as the platform package export
/// does. The stored values below are shaped like the ones read from a Creatio 10.2.959 stand (ENG-106290).
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
internal sealed class CreateDataBindingFromEnvironmentTests : BaseClioModuleTests {
	private const string PackageName = "TestPkg";
	private const string BindingName = "SysDetail_TestPkg";
	private const string SelectUrl = "http://localhost/0/DataService/json/SyncReply/SelectQuery";
	private const string RegistrationId = "0292fcc5-22f0-44c0-9677-f602c7f09d7e";
	private const string BindingUId = "dba513e4-9693-4900-9008-a9d76f938c6e";
	private const string StoredData =
		"{\r\n\t\"PackageData\": [\r\n\t\t{\r\n\t\t\t\"Row\": [\r\n\t\t\t\t{\r\n\t\t\t\t\t\"SchemaColumnUId\": \"ae0e45ca-c495-4fe7-a39d-3ab7278e1617\",\r\n\t\t\t\t\t\"Value\": \"68b50750-f294-4211-aee8-c5288b29ecb5\"\r\n\t\t\t\t},\r\n\t\t\t\t{\r\n\t\t\t\t\t\"SchemaColumnUId\": \"8585929c-62ae-4673-ae8d-bb37f87e5144\",\r\n\t\t\t\t\t\"Value\": \"null\",\r\n\t\t\t\t\t\"DisplayValue\": \"null\"\r\n\t\t\t\t}\r\n\t\t\t]\r\n\t\t}\r\n\t]\r\n}\r\n";
	private const string StoredEnglish =
		"{\r\n  \"PackageData\": [\r\n    {\r\n      \"Row\": [\r\n        {\r\n          \"SchemaColumnUId\": \"ae0e45ca-c495-4fe7-a39d-3ab7278e1617\",\r\n          \"ColumnName\": \"Id\",\r\n          \"Value\": \"68b50750-f294-4211-aee8-c5288b29ecb5\"\r\n        },\r\n        {\r\n          \"SchemaColumnUId\": \"49ad8ee2-65a6-4cdd-b8f6-85c2f10a54dc\",\r\n          \"ColumnName\": \"Caption\",\r\n          \"Value\": \"Test detail\"\r\n        }\r\n      ]\r\n    }\r\n  ]\r\n}";
	private const string StoredUkrainian =
		"{\r\n  \"PackageData\": [\r\n    {\r\n      \"Row\": [\r\n        {\r\n          \"SchemaColumnUId\": \"ae0e45ca-c495-4fe7-a39d-3ab7278e1617\",\r\n          \"ColumnName\": \"Id\",\r\n          \"Value\": \"68b50750-f294-4211-aee8-c5288b29ecb5\"\r\n        },\r\n        {\r\n          \"SchemaColumnUId\": \"49ad8ee2-65a6-4cdd-b8f6-85c2f10a54dc\",\r\n          \"ColumnName\": \"Caption\",\r\n          \"Value\": \"Тестова деталь\"\r\n        }\r\n      ]\r\n    }\r\n  ]\r\n}";

	private static readonly string WorkspaceRoot =
		Path.Combine(Path.GetTempPath(), $"clio-create-data-binding-from-env-{Guid.NewGuid():N}");
	private static readonly Guid PackageUId = Guid.Parse("1d07fd0e-2ca4-4d20-93b4-eb5a795ea03f");

	private CreateDataBindingCommand _command = null!;
	private IApplicationClient _applicationClient = null!;
	private ILogger _logger = null!;
	private string _registrationRowsJson = string.Empty;
	private string _localizationRowsJson = string.Empty;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<CreateDataBindingCommand>();
		_registrationRowsJson = JsonSerializer.Serialize(new[] {
			new Dictionary<string, object> {
				["Id"] = RegistrationId,
				["UId"] = BindingUId,
				["Name"] = BindingName,
				["InstallType"] = 1,
				["SchemaUId"] = "363c2008-c733-407d-9b01-9e6a7cd9a77a",
				["SchemaName"] = "SysDetail",
				["Data"] = StoredData
			}
		});
		_localizationRowsJson = JsonSerializer.Serialize(new[] {
			new Dictionary<string, object> { ["CultureName"] = "en-US", ["Data"] = StoredEnglish },
			new Dictionary<string, object> { ["CultureName"] = "uk-UA", ["Data"] = StoredUkrainian },
			new Dictionary<string, object> { ["CultureName"] = "de-DE", ["Data"] = "{\"PackageData\":[]}" }
		});
	}

	public override void TearDown() {
		base.TearDown();
		_applicationClient.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
	}

	protected override MockFileSystem CreateFs() {
		return new MockFileSystem(new Dictionary<string, MockFileData> {
			[WorkspacePath(".clio", "workspaceSettings.json")] = new("{}"),
			[WorkspacePath("packages", PackageName, "descriptor.json")] = new("{}"),
			[WorkspacePath("packages", PackageName, "Data", "SysModule_TestPkg", "data.json")] = new("{\"keep\":true}")
		}, WorkspaceRoot);
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_applicationClient = Substitute.For<IApplicationClient>();
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(callInfo => BuildSelectResponse(callInfo.ArgAt<string>(1)));
		_applicationClient.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"@odata.context":"x","ModifiedOn":"2026-11-10T12:50:57Z"}""");
		_logger = Substitute.For<ILogger>();
		IWorkspacePathBuilder workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		workspacePathBuilder.RootPath.Returns(WorkspaceRoot);
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select).Returns(SelectUrl);
		serviceUrlBuilder.Build(Arg.Any<string>()).Returns(callInfo => $"http://localhost/0/{callInfo.ArgAt<string>(0)}");

		containerBuilder.AddTransient(_ => _applicationClient);
		containerBuilder.AddTransient(_ => _logger);
		containerBuilder.AddTransient(_ => workspacePathBuilder);
		containerBuilder.AddTransient(_ => serviceUrlBuilder);
	}

	[Test]
	[Description("Writes the stored registration as descriptor.json: the DB UId, name, ModifiedOn in UTC, install type, schema and columns with their IsForceUpdate and IsKey flags.")]
	public void Execute_ShouldWriteDescriptorFromStoredRegistration_WhenFromEnvironment() {
		// Act
		int result = _command.Execute(FromEnvironmentOptions());

		// Assert
		result.Should().Be(0, because: "the binding exists on the environment");
		JsonNode descriptor = JsonNode.Parse(ReadBindingFile("descriptor.json"))!["Descriptor"]!;
		descriptor["UId"]!.GetValue<string>().Should().Be(BindingUId,
			because: "the folder must describe the DB binding, not a new one");
		descriptor["Name"]!.GetValue<string>().Should().Be(BindingName);
		descriptor["ModifiedOnUtc"]!.GetValue<string>().Should().Be("/Date(1794315057000)/",
			because: "the export writes the registration's ModifiedOn in UTC milliseconds");
		descriptor["InstallType"]!.GetValue<int>().Should().Be(1, because: "the stored install type wins");
		descriptor["Schema"]!["Name"]!.GetValue<string>().Should().Be("SysDetail");
		descriptor["Schema"]!["UId"]!.GetValue<string>().Should().Be("363c2008-c733-407d-9b01-9e6a7cd9a77a");
		JsonArray columns = descriptor["Columns"]!.AsArray();
		columns.Select(column => column!["ColumnName"]!.GetValue<string>()).Should().Equal(
			["Caption", "DetailSchemaUId", "Id"], because: "the stored columns are written, ordered by name");
		columns.Single(column => column!["ColumnName"]!.GetValue<string>() == "Id")!["IsKey"]!.GetValue<bool>()
			.Should().BeTrue();
		columns.Single(column => column!["ColumnName"]!.GetValue<string>() == "DetailSchemaUId")!["IsForceUpdate"]!
			.GetValue<bool>().Should().BeTrue(because: "the stored IsForceUpdate flag must survive");
	}

	[Test]
	[Description("Writes data.json and each culture's Localization file verbatim from the stored snapshots, skips a culture whose snapshot has no rows, and writes an empty filter.json.")]
	public void Execute_ShouldWriteStoredSnapshotsVerbatim_WhenFromEnvironment() {
		// Act
		int result = _command.Execute(FromEnvironmentOptions());

		// Assert
		result.Should().Be(0, because: "the binding exists on the environment");
		ReadBindingFile("data.json").Should().Be(StoredData,
			because: "the stored snapshot keeps the export's shapes, such as an empty lookup as \"null\"");
		ReadBindingFile("Localization", "data.en-US.json").Should().Be(StoredEnglish);
		ReadBindingFile("Localization", "data.uk-UA.json").Should().Be(StoredUkrainian);
		FileSystem.File.Exists(BindingPath("Localization", "data.de-DE.json")).Should().BeFalse(
			because: "a culture without stored rows gets no file");
		ReadBindingFile("filter.json").Should().BeEmpty();
	}

	[Test]
	[Description("Replaces an existing binding folder of the same schema, drops a stale Localization folder when the binding has no culture snapshot, and leaves every other binding folder untouched.")]
	public void Execute_ShouldReplaceOnlyTheBindingFolder_WhenFolderAlreadyExists() {
		// Arrange
		_localizationRowsJson = "[]";
		FileSystem.AddFile(BindingPath("descriptor.json"),
			new MockFileData("""{"Descriptor":{"UId":"11111111-1111-1111-1111-111111111111","Schema":{"Name":"SysDetail"},"Columns":[]}}"""));
		FileSystem.AddFile(BindingPath("data.json"), new MockFileData("""{"PackageData":[]}"""));
		FileSystem.AddFile(BindingPath("Localization", "data.en-US.json"), new MockFileData("stale"));

		// Act
		int result = _command.Execute(FromEnvironmentOptions());

		// Assert
		result.Should().Be(0, because: "an existing folder of the same schema is replaced");
		JsonNode.Parse(ReadBindingFile("descriptor.json"))!["Descriptor"]!["UId"]!.GetValue<string>()
			.Should().Be(BindingUId, because: "the descriptor is rewritten with the DB UId");
		ReadBindingFile("data.json").Should().Be(StoredData);
		FileSystem.Directory.Exists(BindingPath("Localization")).Should().BeFalse(
			because: "the export has no Localization folder for a binding without culture snapshots");
		FileSystem.File.ReadAllText(WorkspacePath("packages", PackageName, "Data", "SysModule_TestPkg", "data.json"))
			.Should().Be("{\"keep\":true}", because: "only the requested binding folder is written");
	}

	[Test]
	[Description("Fails naming the binding and package, and writes nothing, when the package has no binding by that name.")]
	public void Execute_ShouldFailAndWriteNothing_WhenBindingIsNotFound() {
		// Arrange
		_registrationRowsJson = "[]";

		// Act
		int result = _command.Execute(FromEnvironmentOptions());

		// Assert
		result.Should().Be(1, because: "there is no binding to write");
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains($"'{BindingName}'") && message.Contains($"'{PackageName}'") && message.Contains("not found")));
		FileSystem.Directory.Exists(BindingPath()).Should().BeFalse(because: "a failed read writes nothing");
	}

	[Test]
	[Description("Refuses --values or --localizations together with --from-environment before any request is sent.")]
	public void Execute_ShouldFailBeforeAnyRequest_WhenValuesAreCombinedWithFromEnvironment() {
		// Arrange
		CreateDataBindingOptions options = FromEnvironmentOptions();
		options.ValuesJson = """{"Caption":"x"}""";

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "the stored rows and explicit values cannot both be written");
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("--values") && message.Contains("--from-environment")));
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default!, default!, default, default, default);
		FileSystem.Directory.Exists(BindingPath()).Should().BeFalse();
	}

	[Test]
	[Description("Refuses --from-environment without an environment, since the binding can only be read from one.")]
	public void Execute_ShouldFail_WhenFromEnvironmentHasNoEnvironment() {
		// Arrange
		CreateDataBindingOptions options = FromEnvironmentOptions();
		options.Environment = null;

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "there is no environment to read the binding from");
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("--environment")));
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default!, default!, default, default, default);
	}

	[Test]
	[Description("Refuses a --schema that differs from the stored binding's schema and writes nothing.")]
	public void Execute_ShouldFailAndWriteNothing_WhenSchemaDiffersFromStoredBinding() {
		// Arrange
		CreateDataBindingOptions options = FromEnvironmentOptions();
		options.SchemaName = "SysModule";

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "the requested schema does not match the binding");
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("'SysDetail'") && message.Contains("'SysModule'")));
		FileSystem.Directory.Exists(BindingPath()).Should().BeFalse();
	}

	[Test]
	[Description("Refuses to overwrite a folder on disk that holds a binding of another schema.")]
	public void Execute_ShouldFailAndKeepFolder_WhenFolderOnDiskHasAnotherSchema() {
		// Arrange
		const string otherDescriptor = """{"Descriptor":{"Schema":{"Name":"SysModule"},"Columns":[]}}""";
		FileSystem.AddFile(BindingPath("descriptor.json"), new MockFileData(otherDescriptor));

		// Act
		int result = _command.Execute(FromEnvironmentOptions());

		// Assert
		result.Should().Be(1, because: "the folder on disk belongs to a binding of another schema");
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("already exists for schema 'SysModule'")));
		ReadBindingFile("descriptor.json").Should().Be(otherDescriptor);
	}

	[Test]
	[Description("Refuses a stored data snapshot that is not package data JSON instead of writing it to disk.")]
	public void Execute_ShouldFailAndWriteNothing_WhenStoredDataIsNotPackageData() {
		// Arrange
		JsonArray rows = JsonNode.Parse(_registrationRowsJson)!.AsArray();
		rows[0]!["Data"] = "<html>not json</html>";
		_registrationRowsJson = rows.ToJsonString();

		// Act
		int result = _command.Execute(FromEnvironmentOptions());

		// Assert
		result.Should().Be(1, because: "a corrupt snapshot must not reach the package folder");
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("data.json") && message.Contains("not package data JSON")));
		FileSystem.Directory.Exists(BindingPath()).Should().BeFalse();
	}

	private static CreateDataBindingOptions FromEnvironmentOptions() => new() {
		Environment = "dev",
		PackageName = PackageName,
		BindingName = BindingName,
		FromEnvironment = true
	};

	private string BuildSelectResponse(string body) {
		string? rootSchemaName = JsonNode.Parse(body)?["rootSchemaName"]?.GetValue<string>();
		string rows = rootSchemaName switch {
			"SysPackageSchemaData" => _registrationRowsJson,
			"SysPackageSchemaDataColumn" => JsonSerializer.Serialize(new[] {
				ColumnRow("ae0e45ca-c495-4fe7-a39d-3ab7278e1617", "Id", isKey: true, isForceUpdate: false,
					"23018567-a13c-4320-8687-fd6f9e3699bd"),
				ColumnRow("8585929c-62ae-4673-ae8d-bb37f87e5144", "DetailSchemaUId", isKey: false, isForceUpdate: true,
					"b295071f-7ea9-4e62-8d1a-919bf3732ff2"),
				ColumnRow("49ad8ee2-65a6-4cdd-b8f6-85c2f10a54dc", "Caption", isKey: false, isForceUpdate: false,
					"ddb3a1ee-07e8-4d62-b7a9-d0e618b00fbd")
			}),
			"SysPackageDataLcz" => _localizationRowsJson,
			"SysPackage" => JsonSerializer.Serialize(new[] {
				new Dictionary<string, object> { ["Name"] = PackageName, ["UId"] = PackageUId.ToString(), ["InstallType"] = 0 }
			}),
			_ => "[]"
		};
		return $$"""{"rows":{{rows}},"success":true}""";
	}

	private static Dictionary<string, object> ColumnRow(
		string columnUId, string columnName, bool isKey, bool isForceUpdate, string dataTypeValueUId) => new() {
		["ColumnUId"] = columnUId,
		["ColumnName"] = columnName,
		["IsForceUpdate"] = isForceUpdate,
		["IsKey"] = isKey,
		["DataTypeValueUId"] = dataTypeValueUId
	};

	private string ReadBindingFile(params string[] segments) => FileSystem.File.ReadAllText(BindingPath(segments));

	private static string BindingPath(params string[] segments) =>
		WorkspacePath(["packages", PackageName, "Data", BindingName, .. segments]);

	private static string WorkspacePath(params string[] segments) => Path.Combine([WorkspaceRoot, .. segments]);
}
