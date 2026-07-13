using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[NonParallelizable]
[Property("Module", "Command")]
internal class RemoteEntitySchemaCreatorTests : BaseClioModuleTests
{
	private IApplicationClient _applicationClient;
	private IApplicationPackageListProvider _packageListProvider;
	private ILogger _logger;
	private ISysSettingsManager _sysSettingsManager;
	private IRemoteEntitySchemaCreator _creator;
	private Guid _packageUId;

	public override void Setup()
	{
		base.Setup();
		_creator = Container.GetRequiredService<IRemoteEntitySchemaCreator>();
		_packageUId = Guid.Parse("11111111-1111-1111-1111-111111111111");
		_packageListProvider.GetPackages().Returns(new[] {
			new PackageInfo(new PackageDescriptor {
				Name = "UsrPkg",
				UId = _packageUId
			}, string.Empty, Enumerable.Empty<string>())
		});
		_sysSettingsManager.GetSysSettingValueByCode("SchemaNamePrefix").Returns("\"Usr\"");
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder)
	{
		base.AdditionalRegistrations(containerBuilder);
		_applicationClient = Substitute.For<IApplicationClient>();
		_packageListProvider = Substitute.For<IApplicationPackageListProvider>();
		_logger = Substitute.For<ILogger>();
		_sysSettingsManager = Substitute.For<ISysSettingsManager>();
		containerBuilder.AddTransient(_ => _applicationClient);
		containerBuilder.AddTransient(_ => _packageListProvider);
		containerBuilder.AddTransient(_ => _logger);
		containerBuilder.AddTransient(_ => _sysSettingsManager);
	}

	[Test]
	[Description("Creates a root entity schema, auto-adds the prefixed primary column from SchemaNamePrefix when needed, and persists the requested text column metadata.")]
	public void Create_CreatesSchemaWithoutParent_AndShapesSavePayload()
	{
		string saveBody = null;
		bool saveDbStructureCalled = false;
		bool runtimeVerifyCalled = false;
		SetupApplicationClient((url, body) => {
			if (url.Contains("CreateNewSchema", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"package\":{\"uId\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"UsrPkg\"},\"columns\":[],\"inheritedColumns\":[],\"indexes\":[],\"administratedByOperations\":true,\"administratedByColumns\":true,\"administratedByRecords\":true,\"useDenyRecordRights\":true,\"rightSchemaName\":\"UsrBrokenRights\"}}";
			}
			if (url.Contains("CheckUniqueSchemaName", StringComparison.Ordinal)) {
				return "{\"success\":true,\"value\":true}";
			}
			if (url.Contains("SaveSchema", StringComparison.Ordinal)) {
				saveBody = body;
				return "{\"success\":true,\"schemaUid\":\"22222222-2222-2222-2222-222222222222\"}";
			}
			if (url.Contains("SchemaDesignerRequest", StringComparison.Ordinal)) {
				saveDbStructureCalled = true;
				return "{\"success\":true}";
			}
			if (url.Contains("RuntimeEntitySchemaRequest", StringComparison.Ordinal)) {
				runtimeVerifyCalled = true;
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"name\":\"UsrVehicle\"}}";
			}
			throw new InvalidOperationException($"Unexpected url {url}");
		});

		_creator.Create(new CreateEntitySchemaOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Title = "Vehicle",
			Columns = ["Name:Text:Vehicle name"]
		});

		_applicationClient.Received().ExecutePostRequest(
			Arg.Is<string>(url => url.Contains("CreateNewSchema", StringComparison.Ordinal)),
			Arg.Any<string>(),
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
		_applicationClient.Received().ExecutePostRequest(
			Arg.Is<string>(url => url.Contains("CheckUniqueSchemaName", StringComparison.Ordinal)),
			Arg.Any<string>(),
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
		_applicationClient.Received().ExecutePostRequest(
			Arg.Is<string>(url => url.Contains("SaveSchema", StringComparison.Ordinal)),
			Arg.Any<string>(),
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
		saveDbStructureCalled.Should().BeTrue(because: "entity creation must materialize DB structure after saving schema metadata");
		runtimeVerifyCalled.Should().BeTrue(because: "entity creation must verify runtime availability after DB structure materialization");

		var json = JObject.Parse(saveBody);
		json["name"]!.Value<string>().Should().Be("UsrVehicle");
		json["caption"]![0]!["value"]!.Value<string>().Should().Be("Vehicle");
		Guid.Parse(json["package"]!["uId"]!.Value<string>()!).Should().Be(_packageUId);
		json["primaryColumn"]!["name"]!.Value<string>().Should().Be("UsrId",
			because: "the generated primary GUID column should use the configured SchemaNamePrefix");
		json["primaryDisplayColumn"]!["name"]!.Value<string>().Should().Be("Name",
			because: "the first text column should become the primary display column");
		json["columns"]!.Select(column => column["name"]!.Value<string>()).Should().Contain(["UsrId", "Name"],
			because: "the saved schema should include the generated prefixed primary column and the requested text column");
		json["columns"]!.Single(column => column["name"]!.Value<string>() == "UsrId")["type"]!.Value<int>().Should().Be(0,
			because: "the generated prefixed primary column should remain a guid column");
		json["administratedByOperations"]!.Value<bool>().Should().BeFalse(
			because: "new root entity schemas should not inherit an invalid operation-rights state from CreateNewSchema");
		json["administratedByColumns"]!.Value<bool>().Should().BeFalse(
			because: "new root entity schemas should start with column administration disabled unless explicitly configured");
		json["administratedByRecords"]!.Value<bool>().Should().BeFalse(
			because: "new root entity schemas should start with record administration disabled unless explicitly configured");
		json["useDenyRecordRights"]!.Value<bool>().Should().BeFalse(
			because: "new root entity schemas should not carry deny-record-rights metadata from the initial designer draft");
		json["rightSchemaName"]!.Value<string>().Should().BeEmpty(
			because: "new root entity schemas should clear stale right schema names before save");
	}

	[Test]
	[Description("Creates an entity schema whose image/photo column is modeled as ImageLookup, auto-referencing the SysImage schema and indexed, so the generated crt.ImageInput field works.")]
	public void Create_CreatesSchema_WithImageLookupColumn_ReferencingSysImage()
	{
		// Arrange
		string saveBody = null;
		SetupApplicationClient((url, body) => {
			if (url.Contains("CreateNewSchema", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"package\":{\"uId\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"UsrPkg\"},\"columns\":[],\"inheritedColumns\":[],\"indexes\":[]}}";
			}
			if (url.Contains("CheckUniqueSchemaName", StringComparison.Ordinal)) {
				return "{\"success\":true,\"value\":true}";
			}
			if (url.Contains("SaveSchema", StringComparison.Ordinal)) {
				saveBody = body;
				return "{\"success\":true,\"schemaUid\":\"22222222-2222-2222-2222-222222222222\"}";
			}
			if (url.Contains("SchemaDesignerRequest", StringComparison.Ordinal)) {
				return "{\"success\":true}";
			}
			if (url.Contains("RuntimeEntitySchemaRequest", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"name\":\"UsrVehicle\"}}";
			}
			throw new InvalidOperationException($"Unexpected url {url}");
		});

		// Act
		_creator.Create(new CreateEntitySchemaOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Title = "Vehicle",
			Columns = ["Name:Text:Vehicle name", "UsrPhoto:ImageLookup:Photo"]
		});

		// Assert
		JObject json = JObject.Parse(saveBody);
		JToken photoColumn = json["columns"]!.Single(column => column["name"]!.Value<string>() == "UsrPhoto");
		photoColumn["type"]!.Value<int>().Should().Be(16,
			because: "ImageLookup ('Image link') must persist as the platform data value type 16, not the binary Image type");
		photoColumn["referenceSchema"]!["name"]!.Value<string>().Should().Be("SysImage",
			because: "an ImageLookup column references the platform SysImage image-storage schema");
		Guid.Parse(photoColumn["referenceSchema"]!["uId"]!.Value<string>()!)
			.Should().Be(Guid.Parse("93986bfe-2dbd-46bc-9bf9-d03dfefbf3b8"),
				because: "the server persists ReferenceSchema.UId, so clio must supply the SysImage schema UId");
		photoColumn["indexed"]!.Value<bool>().Should().BeTrue(
			because: "ImageLookup columns are indexed, mirroring the platform entity designer");
	}

	[Test]
	[Description("Rejects an ImageLookup column that supplies a reference schema, because the reference is always the implicit SysImage schema (mirrors modify-entity-schema-column).")]
	public void Create_Throws_WhenImageLookupColumnSuppliesReferenceSchema()
	{
		// Arrange
		SetupApplicationClient((url, body) => {
			if (url.Contains("CheckUniqueSchemaName", StringComparison.Ordinal)) {
				return "{\"success\":true,\"value\":true}";
			}
			throw new InvalidOperationException($"Unexpected url {url}");
		});

		// Act
		Action act = () => _creator.Create(new CreateEntitySchemaOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Title = "Vehicle",
			Columns = ["Name:Text:Vehicle name", "UsrPhoto:ImageLookup:Photo:Contact"]
		});

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*references the SysImage schema automatically*",
				because: "the create path must reject a caller-supplied reference schema for ImageLookup, aligning with the modify path");
	}

	[Test]
	[Description("Falls back to the legacy Id primary column name when SchemaNamePrefix is empty.")]
	public void Create_CreatesSchemaWithoutParent_AndFallsBackToLegacyPrimaryColumnName_WhenSchemaNamePrefixIsEmpty()
	{
		// Arrange
		string saveBody = null;
		bool saveDbStructureCalled = false;
		bool runtimeVerifyCalled = false;
		_sysSettingsManager.GetSysSettingValueByCode("SchemaNamePrefix").Returns(string.Empty);
		SetupApplicationClient((url, body) => {
			if (url.Contains("CreateNewSchema", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"package\":{\"uId\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"UsrPkg\"},\"columns\":[],\"inheritedColumns\":[],\"indexes\":[]}}";
			}
			if (url.Contains("CheckUniqueSchemaName", StringComparison.Ordinal)) {
				return "{\"success\":true,\"value\":true}";
			}
			if (url.Contains("SaveSchema", StringComparison.Ordinal)) {
				saveBody = body;
				return "{\"success\":true,\"schemaUid\":\"22222222-2222-2222-2222-222222222222\"}";
			}
			if (url.Contains("SchemaDesignerRequest", StringComparison.Ordinal)) {
				saveDbStructureCalled = true;
				return "{\"success\":true}";
			}
			if (url.Contains("RuntimeEntitySchemaRequest", StringComparison.Ordinal)) {
				runtimeVerifyCalled = true;
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"name\":\"UsrVehicle\"}}";
			}
			throw new InvalidOperationException($"Unexpected url {url}");
		});

		// Act
		_creator.Create(new CreateEntitySchemaOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Title = "Vehicle",
			Columns = ["Name:Text:Vehicle name"]
		});

		// Assert
		JObject json = JObject.Parse(saveBody);
		json["primaryColumn"]!["name"]!.Value<string>().Should().Be("Id",
			because: "empty SchemaNamePrefix should preserve the legacy primary column name");
		json["columns"]!.Select(column => column["name"]!.Value<string>()).Should().Contain("Id",
			because: "the generated schema should keep the legacy primary column name when no prefix is configured");
		saveDbStructureCalled.Should().BeTrue(
			because: "entity creation must still materialize DB structure when the prefix is empty");
		runtimeVerifyCalled.Should().BeTrue(
			because: "entity creation must still verify runtime availability when the prefix is empty");
	}

	[Test]
	[Description("Creates an entity schema with an assigned parent schema and preserves the inherited primary column when a custom Guid column is requested.")]
	public void Create_ShouldPreserveInheritedPrimaryColumn_WhenDerivedSchemaHasCustomGuid()
	{
		// Arrange
		string? saveBody = null;
		bool saveDbStructureCalled = false;
		bool runtimeVerifyCalled = false;
		SetupApplicationClient((url, body) => {
			if (url.Contains("CreateNewSchema", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"package\":{\"uId\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"UsrPkg\"},\"columns\":[],\"inheritedColumns\":[],\"indexes\":[]}}";
			}
			if (url.Contains("CheckUniqueSchemaName", StringComparison.Ordinal)) {
				return "{\"success\":true,\"value\":true}";
			}
			if (url.Contains("GetAvailableParentSchemas", StringComparison.Ordinal)) {
				return "{\"success\":true,\"items\":[{\"uId\":\"33333333-3333-3333-3333-333333333333\",\"name\":\"Account\",\"caption\":\"Account\"}]}";
			}
			if (url.Contains("AssignParentSchema", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"package\":{\"uId\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"UsrPkg\"},\"parentSchema\":{\"uId\":\"33333333-3333-3333-3333-333333333333\",\"name\":\"Account\"},\"columns\":[],\"inheritedColumns\":[{\"uId\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",\"name\":\"Id\",\"type\":0}],\"indexes\":[],\"primaryColumn\":{\"uId\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",\"name\":\"Id\",\"type\":0}}}";
			}
			if (url.Contains("SaveSchema", StringComparison.Ordinal)) {
				saveBody = body;
				return "{\"success\":true,\"schemaUid\":\"22222222-2222-2222-2222-222222222222\"}";
			}
			if (url.Contains("SchemaDesignerRequest", StringComparison.Ordinal)) {
				saveDbStructureCalled = true;
				return "{\"success\":true}";
			}
			if (url.Contains("RuntimeEntitySchemaRequest", StringComparison.Ordinal)) {
				runtimeVerifyCalled = true;
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"name\":\"UsrAccount\"}}";
			}
			throw new InvalidOperationException($"Unexpected url {url}");
		});

		// Act
		_creator.Create(new CreateEntitySchemaOptions {
			Package = "UsrPkg",
			SchemaName = "UsrAccount",
			Title = "Account",
			ParentSchemaName = "Account",
			Columns = ["UsrName:Text:Name", "UsrExternalRecordId:Guid:External record Id"]
		});

		// Assert
		_applicationClient.Received().ExecutePostRequest(
			Arg.Is<string>(url => url.Contains("GetAvailableParentSchemas", StringComparison.Ordinal)),
			Arg.Any<string>(),
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
		_applicationClient.Received().ExecutePostRequest(
			Arg.Is<string>(url => url.Contains("AssignParentSchema", StringComparison.Ordinal)),
			Arg.Any<string>(),
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
		saveBody.Should().NotBeNullOrWhiteSpace(
			because: "derived entity creation must submit the final schema payload for saving");
		JObject json = JObject.Parse(saveBody!);
		json["primaryColumn"]!["name"]!.Value<string>().Should().Be("Id",
			because: "a derived schema must retain the primary column inherited from its assigned parent");
		json["columns"]!.Should().Contain(column => column["name"]!.Value<string>() == "UsrExternalRecordId",
			because: "the requested custom Guid must remain an ordinary own column in the saved schema");
		saveDbStructureCalled.Should().BeTrue(
			because: "derived entity creation must materialize the saved schema structure");
		runtimeVerifyCalled.Should().BeTrue(
			because: "derived entity creation must verify that the saved schema is available at runtime");
	}

	[Test]
	[Description("Stops entity creation before save when the requested schema name is already occupied in the target package context.")]
	public void Create_StopsBeforeSave_WhenSchemaNameAlreadyExists()
	{
		SetupApplicationClient((url, _) => {
			if (url.Contains("CreateNewSchema", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"package\":{\"uId\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"UsrPkg\"},\"columns\":[],\"inheritedColumns\":[],\"indexes\":[]}}";
			}
			if (url.Contains("CheckUniqueSchemaName", StringComparison.Ordinal)) {
				return "{\"success\":true,\"value\":false}";
			}
			throw new InvalidOperationException($"Unexpected url {url}");
		});

		Action action = () => _creator.Create(new CreateEntitySchemaOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Title = "Vehicle"
		});

		action.Should().Throw<InvalidOperationException>().WithMessage("*already exists*");
		_applicationClient.DidNotReceive().ExecutePostRequest(
			Arg.Is<string>(url => url.Contains("SaveSchema", StringComparison.Ordinal)),
			Arg.Any<string>(),
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
	}

	[Test]
	[Description("Stops entity creation before save when a requested lookup reference schema does not exist on the target environment.")]
	public void Create_StopsBeforeSave_WhenLookupReferenceSchemaDoesNotExist()
	{
		SetupApplicationClient((url, _) => {
			if (url.Contains("CreateNewSchema", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"package\":{\"uId\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"UsrPkg\"},\"columns\":[],\"inheritedColumns\":[],\"indexes\":[]}}";
			}
			if (url.Contains("CheckUniqueSchemaName", StringComparison.Ordinal)) {
				return "{\"success\":true,\"value\":true}";
			}
			if (url.Contains("GetAvailableReferenceSchemas", StringComparison.Ordinal)) {
				return "{\"success\":true,\"items\":[]}";
			}
			throw new InvalidOperationException($"Unexpected url {url}");
		});

		Action action = () => _creator.Create(new CreateEntitySchemaOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Title = "Vehicle",
			Columns = ["Owner:Lookup:Owner:Contact"]
		});

		action.Should().Throw<InvalidOperationException>().WithMessage("*Reference schema 'Contact'*");
		_applicationClient.DidNotReceive().ExecutePostRequest(
			Arg.Is<string>(url => url.Contains("SaveSchema", StringComparison.Ordinal)),
			Arg.Any<string>(),
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
	}

	[Test]
	[Description("Rejects non-lookup column definitions that include a reference schema segment.")]
	public void Create_StopsBeforeSave_WhenNonLookupColumnIncludesReferenceSchema()
	{
		// Arrange
		SetupApplicationClient((url, _) => {
			if (url.Contains("CreateNewSchema", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"package\":{\"uId\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"UsrPkg\"},\"columns\":[],\"inheritedColumns\":[],\"indexes\":[]}}";
			}
			if (url.Contains("CheckUniqueSchemaName", StringComparison.Ordinal)) {
				return "{\"success\":true,\"value\":true}";
			}
			throw new InvalidOperationException($"Unexpected url {url}");
		});

		Action action = () => _creator.Create(new CreateEntitySchemaOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Title = "Vehicle",
			Columns = ["Name:Text:Vehicle name:Contact"]
		});

		// Act
		Action act = action;

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*only for lookup columns*",
				because: "a fourth column segment must not be accepted for non-lookup types");
		_applicationClient.DidNotReceive().ExecutePostRequest(
			Arg.Is<string>(url => url.Contains("SaveSchema", StringComparison.Ordinal)),
			Arg.Any<string>(),
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
	}

	[Test]
	[Description("Parses structured JSON create-column specs so MCP callers can send required flags, default metadata, and frontend-style type aliases without relying on the legacy colon format.")]
	public void Create_CreatesSchema_FromStructuredJsonColumnSpec() {
		// Arrange
		string saveBody = null;
		bool saveDbStructureCalled = false;
		bool runtimeVerifyCalled = false;
		SetupApplicationClient((url, body) => {
			if (url.Contains("CreateNewSchema", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"package\":{\"uId\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"UsrPkg\"},\"columns\":[],\"inheritedColumns\":[],\"indexes\":[]}}";
			}
			if (url.Contains("CheckUniqueSchemaName", StringComparison.Ordinal)) {
				return "{\"success\":true,\"value\":true}";
			}
			if (url.Contains("SaveSchema", StringComparison.Ordinal)) {
				saveBody = body;
				return "{\"success\":true,\"schemaUid\":\"22222222-2222-2222-2222-222222222222\"}";
			}
			if (url.Contains("SchemaDesignerRequest", StringComparison.Ordinal)) {
				saveDbStructureCalled = true;
				return "{\"success\":true}";
			}
			if (url.Contains("RuntimeEntitySchemaRequest", StringComparison.Ordinal)) {
				runtimeVerifyCalled = true;
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"name\":\"UsrVehicle\"}}";
			}
			throw new InvalidOperationException($"Unexpected url {url}");
		});
		string structuredColumn = JsonSerializer.Serialize(new {
			name = "Status",
			type = "ShortText",
			title = "Status",
			required = true,
			default_value_source = "Const",
			default_value = "Draft",
			masked = true
		}).Replace("default_value_source", "default-value-source").Replace("default_value", "default-value");

		// Act
		_creator.Create(new CreateEntitySchemaOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Title = "Vehicle",
			Columns = [structuredColumn]
		});

		// Assert
		var json = JObject.Parse(saveBody);
		JToken savedColumn = json["columns"]!.Single(column => column["name"]!.Value<string>() == "Status");
		savedColumn["type"]!.Value<int>().Should().Be(27,
			because: "frontend ShortText aliases should map to the closest supported designer type");
		savedColumn["requirementType"]!.Value<int>().Should().Be((int)Terrasoft.Core.Entities.EntitySchemaColumnRequirementType.ApplicationLevel,
			because: "structured create-column specs should preserve required metadata");
		savedColumn["defValue"]!["valueSourceType"]!.Value<int>().Should().Be((int)Terrasoft.Core.Entities.EntitySchemaColumnDefSource.Const,
			because: "structured create-column specs should preserve the explicit default source");
		savedColumn["defValue"]!["value"]!.Value<string>().Should().Be("Draft",
			because: "structured create-column specs should preserve the requested default value");
		savedColumn["isMasked"]!.Value<bool>().Should().BeTrue(
			because: "structured create-column specs should preserve the optional masked flag");
		(savedColumn["isValueMasked"] ?? savedColumn["valueMasked"])!.Value<bool>().Should().BeTrue(
			because: "structured create-column specs should preserve schema-level value masking");
		savedColumn["valueMaskingSettings"]!["pattern"]!.Value<string>().Should().Be(".*",
			because: "masked create-column specs should synthesize a default masking regex accepted by core validation");
		savedColumn["valueMaskingSettings"]!["replacement"]!.Value<string>().Should().Be("********",
			because: "masked create-column specs should synthesize a default masked replacement accepted by core validation");
		savedColumn["valueMaskingSettings"]!["adminOperationCode"]!.Value<string>().Should().Be("UsrVehicle_Status_UnmaskedValue",
			because: "masked create-column specs should synthesize the conventional unmask admin operation code");
		saveDbStructureCalled.Should().BeTrue();
		runtimeVerifyCalled.Should().BeTrue();
	}

	[Test]
	[Description("Creates structured default-value-config metadata for create-column payloads that use system values.")]
	public void Create_CreatesSchema_WithStructuredSystemValueDefault() {
		// Arrange
		string saveBody = null;
		SetupApplicationClient((url, body) => {
			if (url.Contains("CreateNewSchema", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"package\":{\"uId\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"UsrPkg\"},\"columns\":[],\"inheritedColumns\":[],\"indexes\":[]}}";
			}
			if (url.Contains("CheckUniqueSchemaName", StringComparison.Ordinal)) {
				return "{\"success\":true,\"value\":true}";
			}
			if (url.Contains("SaveSchema", StringComparison.Ordinal)) {
				saveBody = body;
				return "{\"success\":true,\"schemaUid\":\"22222222-2222-2222-2222-222222222222\"}";
			}
			if (url.Contains("GetSystemValues", StringComparison.Ordinal)) {
				return "{\"success\":true,\"items\":[{\"displayValue\":\"Current Time and Date\",\"value\":\"d7c295d3-3146-4ee1-ac49-3a7bd0edc45d\"}]}";
			}
			if (url.Contains("SchemaDesignerRequest", StringComparison.Ordinal)) {
				return "{\"success\":true}";
			}
			if (url.Contains("RuntimeEntitySchemaRequest", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"name\":\"UsrVehicle\"}}";
			}
			throw new InvalidOperationException($"Unexpected url {url}");
		});
		string structuredColumn = JsonSerializer.Serialize(new {
			name = "UsrStartDate",
			type = "DateTime",
			title = "Start date",
			default_value_config = new {
				source = "SystemValue",
				value_source = "CurrentDateTime"
			}
		}).Replace("default_value_config", "default-value-config").Replace("value_source", "value-source");

		// Act
		_creator.Create(new CreateEntitySchemaOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Title = "Vehicle",
			Columns = [structuredColumn]
		});

		// Assert
		JToken savedColumn = JObject.Parse(saveBody)["columns"]!.Single(column => column["name"]!.Value<string>() == "UsrStartDate");
		savedColumn["defValue"]!["valueSourceType"]!.Value<int>().Should().Be((int)Terrasoft.Core.Entities.EntitySchemaColumnDefSource.SystemValue,
			because: "structured default-value-config should preserve non-legacy default sources");
		savedColumn["defValue"]!["valueSource"]!.Value<string>().Should().Be("d7c295d3-3146-4ee1-ac49-3a7bd0edc45d",
			because: "structured default-value-config should persist the canonical system value guid");
	}

	[Test]
	[Description("Creates schema and column captions using only the explicitly provided title-localizations without synthesizing additional cultures.")]
	public void Create_CreatesSchema_WithExplicitTitleLocalizations_WithoutCultureSynthesis() {
		// Arrange
		string saveBody = null;
		SetupApplicationClient((url, body) => {
			if (url.Contains("CreateNewSchema", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"package\":{\"uId\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"UsrPkg\"},\"columns\":[],\"inheritedColumns\":[],\"indexes\":[]}}";
			}
			if (url.Contains("CheckUniqueSchemaName", StringComparison.Ordinal)) {
				return "{\"success\":true,\"value\":true}";
			}
			if (url.Contains("SaveSchema", StringComparison.Ordinal)) {
				saveBody = body;
				return "{\"success\":true,\"schemaUid\":\"22222222-2222-2222-2222-222222222222\"}";
			}
			if (url.Contains("SchemaDesignerRequest", StringComparison.Ordinal)) {
				return "{\"success\":true}";
			}
			if (url.Contains("RuntimeEntitySchemaRequest", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"name\":\"UsrVehicle\"}}";
			}
			throw new InvalidOperationException($"Unexpected url {url}");
		});
		string structuredColumn = JsonSerializer.Serialize(new {
			name = "UsrStatus",
			type = "Text",
			title_localizations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
				["en-US"] = "Status"
			}
		}).Replace("title_localizations", "title-localizations");

		// Act
		_creator.Create(new CreateEntitySchemaOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Title = "Vehicle",
			TitleLocalizations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
				["en-US"] = "Vehicle"
			},
			Columns = [structuredColumn]
		});

		// Assert
		JObject json = JObject.Parse(saveBody);
		json["caption"]!.Should().Contain(token =>
				token["cultureName"]!.Value<string>() == "en-US"
				&& token["value"]!.Value<string>() == "Vehicle",
			because: "schema caption should include the provided en-US localization");
		json["caption"]!.Should().HaveCount(1,
			because: "Clio must not synthesize additional culture localizations beyond what was explicitly provided");
		JToken savedColumn = json["columns"]!.Single(column => column["name"]!.Value<string>() == "UsrStatus");
		savedColumn["caption"]!.Should().Contain(token =>
				token["cultureName"]!.Value<string>() == "en-US"
				&& token["value"]!.Value<string>() == "Status",
			because: "column caption should include the provided en-US localization");
		savedColumn["caption"]!.Should().HaveCount(1,
			because: "Clio must not synthesize additional culture localizations beyond what was explicitly provided");
	}

	[Test]
	[Description("Normalizes structured Settings defaults from display names to canonical setting codes before save.")]
	public void Create_CreatesSchema_WithStructuredSettingsDefault_UsingCanonicalCode() {
		// Arrange
		string saveBody = null;
		SetupApplicationClient((url, body) => {
			if (url.Contains("CreateNewSchema", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"package\":{\"uId\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"UsrPkg\"},\"columns\":[],\"inheritedColumns\":[],\"indexes\":[]}}";
			}
			if (url.Contains("CheckUniqueSchemaName", StringComparison.Ordinal)) {
				return "{\"success\":true,\"value\":true}";
			}
			if (url.Contains("SelectQuery", StringComparison.Ordinal)) {
				return "{\"success\":true,\"rows\":[{\"Id\":\"11111111-1111-1111-1111-111111111111\",\"Code\":\"UsrDefaultTitle\",\"Name\":\"Default Title\",\"ValueTypeName\":\"Text\"}]}";
			}
			if (url.Contains("SaveSchema", StringComparison.Ordinal)) {
				saveBody = body;
				return "{\"success\":true,\"schemaUid\":\"22222222-2222-2222-2222-222222222222\"}";
			}
			if (url.Contains("SchemaDesignerRequest", StringComparison.Ordinal)) {
				return "{\"success\":true}";
			}
			if (url.Contains("RuntimeEntitySchemaRequest", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"name\":\"UsrVehicle\"}}";
			}
			throw new InvalidOperationException($"Unexpected url {url}");
		});
		string structuredColumn = JsonSerializer.Serialize(new {
			name = "UsrTitle",
			type = "Text",
			title = "Title",
			default_value_config = new {
				source = "Settings",
				value_source = "Default Title"
			}
		}).Replace("default_value_config", "default-value-config").Replace("value_source", "value-source");

		// Act
		_creator.Create(new CreateEntitySchemaOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Title = "Vehicle",
			Columns = [structuredColumn]
		});

		// Assert
		JToken savedColumn = JObject.Parse(saveBody)["columns"]!.Single(column => column["name"]!.Value<string>() == "UsrTitle");
		savedColumn["defValue"]!["valueSourceType"]!.Value<int>().Should().Be((int)Terrasoft.Core.Entities.EntitySchemaColumnDefSource.Settings,
			because: "structured default-value-config should preserve Settings source metadata");
		savedColumn["defValue"]!["valueSource"]!.Value<string>().Should().Be("UsrDefaultTitle",
			because: "settings defaults must persist canonical setting codes after resolution");
	}

	[Test]
	[Description("Creates SecureText columns from structured JSON payloads and preserves masked=true for schema-level masking.")]
	public void Create_CreatesSchema_WithSecureTextMaskedColumn() {
		// Arrange
		string saveBody = null;
		SetupApplicationClient((url, body) => {
			if (url.Contains("CreateNewSchema", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"package\":{\"uId\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"UsrPkg\"},\"columns\":[],\"inheritedColumns\":[],\"indexes\":[]}}";
			}
			if (url.Contains("CheckUniqueSchemaName", StringComparison.Ordinal)) {
				return "{\"success\":true,\"value\":true}";
			}
			if (url.Contains("SaveSchema", StringComparison.Ordinal)) {
				saveBody = body;
				return "{\"success\":true,\"schemaUid\":\"22222222-2222-2222-2222-222222222222\"}";
			}
			if (url.Contains("SchemaDesignerRequest", StringComparison.Ordinal)) {
				return "{\"success\":true}";
			}
			if (url.Contains("RuntimeEntitySchemaRequest", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"name\":\"UsrVehicle\"}}";
			}
			throw new InvalidOperationException($"Unexpected url {url}");
		});
		string structuredColumn = JsonSerializer.Serialize(new {
			name = "UsrPassword",
			type = "SecureText",
			title = "Password",
			masked = true
		});

		// Act
		_creator.Create(new CreateEntitySchemaOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Title = "Vehicle",
			Columns = [structuredColumn]
		});

		// Assert
		JToken savedColumn = JObject.Parse(saveBody)["columns"]!.Single(column => column["name"]!.Value<string>() == "UsrPassword");
		savedColumn["type"]!.Value<int>().Should().Be(24,
			because: "SecureText create columns should map to runtime data value type 24");
		savedColumn["isMasked"]!.Value<bool>().Should().BeTrue(
			because: "strict schema-level masking requires masked=true for password columns");
		(savedColumn["isValueMasked"] ?? savedColumn["valueMasked"])!.Value<bool>().Should().BeTrue(
			because: "strict schema-level masking requires isValueMasked=true for password columns");
		savedColumn["valueMaskingSettings"]!["pattern"]!.Value<string>().Should().Be(".*",
			because: "masked secure text columns should synthesize a default masking regex accepted by core validation");
		savedColumn["valueMaskingSettings"]!["replacement"]!.Value<string>().Should().Be("********",
			because: "masked secure text columns should synthesize a default replacement accepted by core validation");
		savedColumn["valueMaskingSettings"]!["adminOperationCode"]!.Value<string>().Should().Be("UsrVehicle_UsrPassword_UnmaskedValue",
			because: "masked secure text columns should synthesize the conventional unmask admin operation code");
	}

	[Test]
	[Description("Creates SecureText columns from structured JSON payloads and keeps masked=false when omitted.")]
	public void Create_CreatesSchema_WithSecureTextMaskedFalseByDefault() {
		// Arrange
		string saveBody = null;
		SetupApplicationClient((url, body) => {
			if (url.Contains("CreateNewSchema", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"package\":{\"uId\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"UsrPkg\"},\"columns\":[],\"inheritedColumns\":[],\"indexes\":[]}}";
			}
			if (url.Contains("CheckUniqueSchemaName", StringComparison.Ordinal)) {
				return "{\"success\":true,\"value\":true}";
			}
			if (url.Contains("SaveSchema", StringComparison.Ordinal)) {
				saveBody = body;
				return "{\"success\":true,\"schemaUid\":\"22222222-2222-2222-2222-222222222222\"}";
			}
			if (url.Contains("SchemaDesignerRequest", StringComparison.Ordinal)) {
				return "{\"success\":true}";
			}
			if (url.Contains("RuntimeEntitySchemaRequest", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"name\":\"UsrVehicle\"}}";
			}
			throw new InvalidOperationException($"Unexpected url {url}");
		});
		string structuredColumn = JsonSerializer.Serialize(new {
			name = "UsrPassword",
			type = "SecureText",
			title = "Password"
		});

		// Act
		_creator.Create(new CreateEntitySchemaOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Title = "Vehicle",
			Columns = [structuredColumn]
		});

		// Assert
		JToken savedColumn = JObject.Parse(saveBody)["columns"]!.Single(column => column["name"]!.Value<string>() == "UsrPassword");
		savedColumn["isMasked"]!.Value<bool>().Should().BeFalse(
			because: "secure text create columns should not force schema-level masking unless explicitly requested");
		(savedColumn["isValueMasked"] ?? savedColumn["valueMasked"])!.Value<bool>().Should().BeFalse(
			because: "value masking should stay disabled when masked is omitted");
	}

	[Test]
	[Description("Rejects masked=true when create-column payload uses non Text and non SecureText types.")]
	public void Create_StopsBeforeSave_WhenMaskedUsedOnUnsupportedColumnType() {
		// Arrange
		SetupApplicationClient((url, _) => {
			if (url.Contains("CreateNewSchema", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"package\":{\"uId\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"UsrPkg\"},\"columns\":[],\"inheritedColumns\":[],\"indexes\":[]}}";
			}
			if (url.Contains("CheckUniqueSchemaName", StringComparison.Ordinal)) {
				return "{\"success\":true,\"value\":true}";
			}
			throw new InvalidOperationException($"Unexpected url {url}");
		});
		string structuredColumn = JsonSerializer.Serialize(new {
			name = "UsrCode",
			type = "Integer",
			title = "Code",
			masked = true
		});

		// Act
		Action act = () => _creator.Create(new CreateEntitySchemaOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Title = "Vehicle",
			Columns = [structuredColumn]
		});

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*can use masked only for Text or SecureText types*",
				because: "masked flag should stay constrained to supported column types");
		_applicationClient.DidNotReceive().ExecutePostRequest(
			Arg.Is<string>(url => url.Contains("SaveSchema", StringComparison.Ordinal)),
			Arg.Any<string>(),
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
	}

	[TestCase("Binary", 13)]
	[TestCase("Blob", 13)]
	[TestCase("Image", 14)]
	[TestCase("File", 25)]
	[Description("Creates schemas with Binary, Image, File, and Blob-alias columns and persists their runtime data value type ids.")]
	public void Create_CreatesSchema_With_BinaryLike_Column_Types(string typeName, int expectedDataValueType) {
		// Arrange
		string saveBody = null;
		SetupApplicationClient((url, body) => {
			if (url.Contains("CreateNewSchema", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"package\":{\"uId\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"UsrPkg\"},\"columns\":[],\"inheritedColumns\":[],\"indexes\":[]}}";
			}
			if (url.Contains("CheckUniqueSchemaName", StringComparison.Ordinal)) {
				return "{\"success\":true,\"value\":true}";
			}
			if (url.Contains("SaveSchema", StringComparison.Ordinal)) {
				saveBody = body;
				return "{\"success\":true,\"schemaUid\":\"22222222-2222-2222-2222-222222222222\"}";
			}
			if (url.Contains("SchemaDesignerRequest", StringComparison.Ordinal)) {
				return "{\"success\":true}";
			}
			if (url.Contains("RuntimeEntitySchemaRequest", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"name\":\"UsrVehicle\"}}";
			}
			throw new InvalidOperationException($"Unexpected url {url}");
		});

		// Act
		_creator.Create(new CreateEntitySchemaOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Title = "Vehicle",
			Columns = [$"Payload:{typeName}:Payload"]
		});

		// Assert
		JToken savedColumn = JObject.Parse(saveBody)["columns"]!.Single(column => column["name"]!.Value<string>() == "Payload");
		savedColumn["type"]!.Value<int>().Should().Be(expectedDataValueType,
			because: "supported binary-like create column types should be persisted with their expected runtime data value ids");
	}

	[TestCase("Binary")]
	[TestCase("Image")]
	[TestCase("File")]
	[Description("Rejects constant defaults for Binary, Image, and File create-column payloads because the command does not serialize binary defaults.")]
	public void Create_StopsBeforeSave_When_BinaryLike_Column_Uses_Const_Default(string typeName) {
		// Arrange
		SetupApplicationClient((url, _) => {
			if (url.Contains("CreateNewSchema", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"package\":{\"uId\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"UsrPkg\"},\"columns\":[],\"inheritedColumns\":[],\"indexes\":[]}}";
			}
			if (url.Contains("CheckUniqueSchemaName", StringComparison.Ordinal)) {
				return "{\"success\":true,\"value\":true}";
			}
			throw new InvalidOperationException($"Unexpected url {url}");
		});
		string structuredColumn = JsonSerializer.Serialize(new {
			name = "Payload",
			type = typeName,
			title = "Payload",
			default_value_source = "Const",
			default_value = "AAECAw=="
		}).Replace("default_value_source", "default-value-source").Replace("default_value", "default-value");

		// Act
		Action act = () => _creator.Create(new CreateEntitySchemaOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Title = "Vehicle",
			Columns = [structuredColumn]
		});

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*does not support default-value or default-value-source Const*",
				because: "binary-like create columns should reject unsupported constant default payloads before save");
		_applicationClient.DidNotReceive().ExecutePostRequest(
			Arg.Is<string>(url => url.Contains("SaveSchema", StringComparison.Ordinal)),
			Arg.Any<string>(),
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
	}

	[Test]
	[Description("Succeeds when GetSchemaDesignItem returns an HTML error page and falls back to runtime schema name verification.")]
	public void Create_Succeeds_WhenGetSchemaDesignItem_ReturnsHtml_AndFallsBackToRuntimeVerification() {
		bool runtimeVerifyCalled = false;
		bool designItemCalled = false;
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Any<string>(),
				Arg.Any<int>(),
				Arg.Any<int>(),
				Arg.Any<int>())
			.Returns(callInfo => {
				string url = callInfo.ArgAt<string>(0);
				if (url.Contains("CreateNewSchema", StringComparison.Ordinal)) {
					return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"package\":{\"uId\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"UsrPkg\"},\"columns\":[],\"inheritedColumns\":[],\"indexes\":[]}}";
				}
				if (url.Contains("CheckUniqueSchemaName", StringComparison.Ordinal)) {
					return "{\"success\":true,\"value\":true}";
				}
				if (url.Contains("SaveSchema", StringComparison.Ordinal)) {
					return "{\"success\":true,\"schemaUid\":\"22222222-2222-2222-2222-222222222222\"}";
				}
				if (url.Contains("SchemaDesignerRequest", StringComparison.Ordinal)) {
					return "{\"success\":true}";
				}
				if (url.Contains("RuntimeEntitySchemaRequest", StringComparison.Ordinal)) {
					runtimeVerifyCalled = true;
					return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"name\":\"UsrVehicle\"}}";
				}
				if (url.Contains("GetSchemaDesignItem", StringComparison.Ordinal)) {
					designItemCalled = true;
					return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Transitional//EN\" \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-transitional.dtd\">\r\n<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>Request Error</title></head><body>Service Unavailable</body></html>";
				}
				throw new InvalidOperationException($"Unexpected url {url}");
			});

		Action act = () => _creator.Create(new CreateEntitySchemaOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Title = "Vehicle"
		});

		act.Should().NotThrow(
			because: "when GetSchemaDesignItem returns HTML the creator should fall back to runtime verification and succeed");
		runtimeVerifyCalled.Should().BeTrue(
			because: "runtime schema must still be verified before falling back");
		designItemCalled.Should().BeTrue(
			because: "GetSchemaDesignItem must have been attempted");
		_logger.Received().WriteInfo(Arg.Is<string>(msg => msg.Contains("HTML response")));
	}

	[Test]
	[Description("Fails when GetSchemaDesignItem returns HTML and the runtime schema name does not match the requested schema name.")]
	public void Create_Fails_WhenGetSchemaDesignItem_ReturnsHtml_AndRuntimeNameMismatch() {
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Any<string>(),
				Arg.Any<int>(),
				Arg.Any<int>(),
				Arg.Any<int>())
			.Returns(callInfo => {
				string url = callInfo.ArgAt<string>(0);
				if (url.Contains("CreateNewSchema", StringComparison.Ordinal)) {
					return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"package\":{\"uId\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"UsrPkg\"},\"columns\":[],\"inheritedColumns\":[],\"indexes\":[]}}";
				}
				if (url.Contains("CheckUniqueSchemaName", StringComparison.Ordinal)) {
					return "{\"success\":true,\"value\":true}";
				}
				if (url.Contains("SaveSchema", StringComparison.Ordinal)) {
					return "{\"success\":true,\"schemaUid\":\"22222222-2222-2222-2222-222222222222\"}";
				}
				if (url.Contains("SchemaDesignerRequest", StringComparison.Ordinal)) {
					return "{\"success\":true}";
				}
				if (url.Contains("RuntimeEntitySchemaRequest", StringComparison.Ordinal)) {
					return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"name\":\"UsrWrongName\"}}";
				}
				if (url.Contains("GetSchemaDesignItem", StringComparison.Ordinal)) {
					return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Transitional//EN\" \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-transitional.dtd\">\r\n<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>Request Error</title></head><body>Service Unavailable</body></html>";
				}
				throw new InvalidOperationException($"Unexpected url {url}");
			});

		Action act = () => _creator.Create(new CreateEntitySchemaOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Title = "Vehicle"
		});

		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*runtime schema name*does not match*",
				because: "a name mismatch in runtime schema must still be reported as failure even when designer returns HTML");
	}

	[Test]
	[Description("Publishes the configuration after saving the DB structure so the new schema becomes visible to lookup pickers and sys-setting reference schema lists (ENG-90403).")]
	public void Create_ShouldPublishConfiguration_WhenSchemaIsSaved()
	{
		// Arrange
		var schemaDesignerBodies = new List<string>();
		SetupApplicationClient((url, body) => {
			if (url.Contains("CreateNewSchema", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"package\":{\"uId\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"UsrPkg\"},\"columns\":[],\"inheritedColumns\":[],\"indexes\":[]}}";
			}
			if (url.Contains("CheckUniqueSchemaName", StringComparison.Ordinal)) {
				return "{\"success\":true,\"value\":true}";
			}
			if (url.Contains("SaveSchema", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schemaUid\":\"22222222-2222-2222-2222-222222222222\"}";
			}
			if (url.Contains("SchemaDesignerRequest", StringComparison.Ordinal)) {
				schemaDesignerBodies.Add(body);
				return "{\"success\":true}";
			}
			if (url.Contains("RuntimeEntitySchemaRequest", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"name\":\"UsrVehicle\"}}";
			}
			throw new InvalidOperationException($"Unexpected url {url}");
		});

		// Act
		_creator.Create(new CreateEntitySchemaOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Title = "Vehicle",
			Columns = ["Name:Text:Vehicle name"]
		});

		// Assert
		schemaDesignerBodies.Should().HaveCount(2,
			because: "the creator must send one SchemaDesignerRequest for DDL and one for publication");
		JObject ddlBody = JObject.Parse(schemaDesignerBodies[0]);
		ddlBody["saveSchemaDBStructure"]!.Values<string>().Should()
			.Contain("22222222-2222-2222-2222-222222222222",
				because: "the first SchemaDesignerRequest must materialize the saved schema DB structure");
		JObject publishBody = JObject.Parse(schemaDesignerBodies[1]);
		publishBody["buildWorkspace"]!.Value<bool>().Should().BeTrue(
			because: "legacy runtimes publish saved schemas only through a workspace build");
		publishBody["buildChangedConfiguration"]!.Value<bool>().Should().BeTrue(
			because: "modern runtimes publish saved schemas through an incremental configuration build plus an EntitySchemaManager refresh");
		_logger.Received().WriteInfo(Arg.Is<string>(message =>
			message.Contains("UsrVehicle") && message.Contains("published")));
	}

	[Test]
	[Description("Surfaces an actionable error when the schema is saved but configuration publication fails, so callers know the schema exists yet stays invisible to reference lists until compiled.")]
	public void Create_ShouldThrowActionableError_WhenPublishFails()
	{
		// Arrange
		SetupApplicationClient((url, body) => {
			if (url.Contains("CreateNewSchema", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"package\":{\"uId\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"UsrPkg\"},\"columns\":[],\"inheritedColumns\":[],\"indexes\":[]}}";
			}
			if (url.Contains("CheckUniqueSchemaName", StringComparison.Ordinal)) {
				return "{\"success\":true,\"value\":true}";
			}
			if (url.Contains("SaveSchema", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schemaUid\":\"22222222-2222-2222-2222-222222222222\"}";
			}
			if (url.Contains("SchemaDesignerRequest", StringComparison.Ordinal)) {
				return JObject.Parse(body)["buildWorkspace"]?.Value<bool>() == true
					? "{\"success\":false,\"errorInfo\":{\"message\":\"Compilation failed.\"}}"
					: "{\"success\":true}";
			}
			throw new InvalidOperationException($"Unexpected url {url}");
		});

		// Act
		Action act = () => _creator.Create(new CreateEntitySchemaOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Title = "Vehicle",
			Columns = ["Name:Text:Vehicle name"]
		});

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*created and saved, but publishing the configuration failed*Compilation failed.*",
				because: "a publish failure must explain that the schema exists but stays invisible until the configuration is built");
	}

	[Test]
	[Description("Requests the OData entities rebuild after a successful publish so the new schema becomes reachable over OData without a manual full compile (ENG-92048).")]
	public void Create_ShouldRequestODataBuild_AfterPublish()
	{
		// Arrange
		bool publishCalled = false;
		bool odataBuildCalled = false;
		SetupApplicationClient((url, body) => {
			if (url.Contains("CreateNewSchema", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"package\":{\"uId\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"UsrPkg\"},\"columns\":[],\"inheritedColumns\":[],\"indexes\":[]}}";
			}
			if (url.Contains("CheckUniqueSchemaName", StringComparison.Ordinal)) {
				return "{\"success\":true,\"value\":true}";
			}
			if (url.Contains("SaveSchema", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schemaUid\":\"22222222-2222-2222-2222-222222222222\"}";
			}
			if (url.Contains("RunODataBuild", StringComparison.Ordinal)) {
				odataBuildCalled = true;
				return "{\"success\":true}";
			}
			if (url.Contains("SchemaDesignerRequest", StringComparison.Ordinal)) {
				publishCalled = true;
				return "{\"success\":true}";
			}
			if (url.Contains("RuntimeEntitySchemaRequest", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"name\":\"UsrVehicle\"}}";
			}
			throw new InvalidOperationException($"Unexpected url {url}");
		});

		// Act
		_creator.Create(new CreateEntitySchemaOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Title = "Vehicle",
			Columns = ["Name:Text:Vehicle name"]
		});

		// Assert
		publishCalled.Should().BeTrue(because: "the OData rebuild must follow a successful publish, not replace it");
		odataBuildCalled.Should().BeTrue(
			because: "create must request the OData entities rebuild so the schema is reachable over OData without a full compile");
		_applicationClient.Received(1).ExecutePostRequest(
			Arg.Is<string>(url => url.Contains("RunODataBuild", StringComparison.Ordinal)),
			Arg.Any<string>(),
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
		_logger.Received().WriteInfo(Arg.Is<string>(message =>
			message.Contains("OData entities rebuild requested") && message.Contains("UsrVehicle")));
	}

	[Test]
	[Description("Schema creation still succeeds (with a warning) when the OData rebuild request fails, because the schema is already published and usable (ENG-92048).")]
	public void Create_ShouldSucceedWithWarning_WhenODataBuildFails()
	{
		// Arrange
		SetupApplicationClient((url, body) => {
			if (url.Contains("CreateNewSchema", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"package\":{\"uId\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"UsrPkg\"},\"columns\":[],\"inheritedColumns\":[],\"indexes\":[]}}";
			}
			if (url.Contains("CheckUniqueSchemaName", StringComparison.Ordinal)) {
				return "{\"success\":true,\"value\":true}";
			}
			if (url.Contains("SaveSchema", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schemaUid\":\"22222222-2222-2222-2222-222222222222\"}";
			}
			if (url.Contains("RunODataBuild", StringComparison.Ordinal)) {
				return "{\"success\":false,\"errorInfo\":{\"message\":\"OData build refused.\"}}";
			}
			if (url.Contains("SchemaDesignerRequest", StringComparison.Ordinal)) {
				return "{\"success\":true}";
			}
			if (url.Contains("RuntimeEntitySchemaRequest", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"name\":\"UsrVehicle\"}}";
			}
			throw new InvalidOperationException($"Unexpected url {url}");
		});

		// Act
		Action act = () => _creator.Create(new CreateEntitySchemaOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Title = "Vehicle",
			Columns = ["Name:Text:Vehicle name"]
		});

		// Assert
		act.Should().NotThrow(
			because: "a failed OData rebuild must not undo or fail the already-published schema");
		_logger.Received().WriteWarning(Arg.Is<string>(message =>
			message.Contains(RemoteEntitySchemaCreator.ODataBuildRequestFailedWarningFragment) && message.Contains("UsrVehicle")));
		_logger.Received().WriteInfo(Arg.Is<string>(message =>
			message.Contains("UsrVehicle") && message.Contains("created")));
	}

	[Test]
	[Description("Schema creation still succeeds (with a warning) when the OData rebuild request fails with an AggregateException-wrapped transport fault, because the real HTTP client (Creatio.Client) executes via Task.Result and wraps transport/timeout faults in an AggregateException (ENG-92048).")]
	public void Create_ShouldSucceedWithWarning_WhenODataBuildThrowsAggregateException()
	{
		// Arrange — the real Creatio HTTP client runs the POST via Task.Result, so a transport/timeout fault on
		// the RunODataBuild request surfaces as an AggregateException wrapping the inner HttpRequestException.
		SetupApplicationClient((url, body) => {
			if (url.Contains("CreateNewSchema", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"package\":{\"uId\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"UsrPkg\"},\"columns\":[],\"inheritedColumns\":[],\"indexes\":[]}}";
			}
			if (url.Contains("CheckUniqueSchemaName", StringComparison.Ordinal)) {
				return "{\"success\":true,\"value\":true}";
			}
			if (url.Contains("SaveSchema", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schemaUid\":\"22222222-2222-2222-2222-222222222222\"}";
			}
			if (url.Contains("RunODataBuild", StringComparison.Ordinal)) {
				throw new AggregateException(
					new System.Net.Http.HttpRequestException("Connection refused while triggering the OData build."));
			}
			if (url.Contains("SchemaDesignerRequest", StringComparison.Ordinal)) {
				return "{\"success\":true}";
			}
			if (url.Contains("RuntimeEntitySchemaRequest", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"name\":\"UsrVehicle\"}}";
			}
			throw new InvalidOperationException($"Unexpected url {url}");
		});

		// Act
		Action act = () => _creator.Create(new CreateEntitySchemaOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Title = "Vehicle",
			Columns = ["Name:Text:Vehicle name"]
		});

		// Assert
		act.Should().NotThrow(
			because: "an AggregateException-wrapped transport fault on the OData rebuild must degrade to a warning, not escape and fail the already-published schema");
		_logger.Received().WriteWarning(Arg.Is<string>(message =>
			message.Contains(RemoteEntitySchemaCreator.ODataBuildRequestFailedWarningFragment) && message.Contains("UsrVehicle")));
		_logger.Received().WriteInfo(Arg.Is<string>(message =>
			message.Contains("UsrVehicle") && message.Contains("created")));
	}

	[TestCase(typeof(System.Net.Http.HttpRequestException))]
	[TestCase(typeof(System.Threading.Tasks.TaskCanceledException))]
	[TestCase(typeof(System.Net.WebException))]
	[TestCase(typeof(System.Net.Sockets.SocketException))]
	[TestCase(typeof(System.IO.IOException))]
	[TestCase(typeof(System.OperationCanceledException))]
	[TestCase(typeof(Newtonsoft.Json.JsonException))]
	[Description("Schema creation still succeeds (with a warning) when the OData rebuild request throws a raw, unwrapped transport / IO / parse fault — the likeliest real-world failure of a compile-class trigger — because the schema is already published and usable (ENG-92048).")]
	public void Create_ShouldSucceedWithWarning_WhenODataBuildThrowsTransportFault(Type transportFaultType)
	{
		// Arrange — a timeout or connection fault is thrown directly out of ExecutePostRequest (not wrapped),
		// exercising the non-aggregate branch of the production exception filter that the other failure tests
		// (success:false → InvalidOperationException, and AggregateException-wrapped) do not cover.
		Exception transportFault = (Exception)Activator.CreateInstance(transportFaultType)!;
		SetupApplicationClient((url, body) => {
			if (url.Contains("CreateNewSchema", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"package\":{\"uId\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"UsrPkg\"},\"columns\":[],\"inheritedColumns\":[],\"indexes\":[]}}";
			}
			if (url.Contains("CheckUniqueSchemaName", StringComparison.Ordinal)) {
				return "{\"success\":true,\"value\":true}";
			}
			if (url.Contains("SaveSchema", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schemaUid\":\"22222222-2222-2222-2222-222222222222\"}";
			}
			if (url.Contains("RunODataBuild", StringComparison.Ordinal)) {
				throw transportFault;
			}
			if (url.Contains("SchemaDesignerRequest", StringComparison.Ordinal)) {
				return "{\"success\":true}";
			}
			if (url.Contains("RuntimeEntitySchemaRequest", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"name\":\"UsrVehicle\"}}";
			}
			throw new InvalidOperationException($"Unexpected url {url}");
		});

		// Act
		Action act = () => _creator.Create(new CreateEntitySchemaOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Title = "Vehicle",
			Columns = ["Name:Text:Vehicle name"]
		});

		// Assert
		act.Should().NotThrow(
			because: "a thrown transport fault on the OData rebuild must degrade to a warning, not escape and fail the already-published schema");
		_logger.Received().WriteWarning(Arg.Is<string>(message =>
			message.Contains(RemoteEntitySchemaCreator.ODataBuildRequestFailedWarningFragment) && message.Contains("UsrVehicle")));
		_logger.Received().WriteInfo(Arg.Is<string>(message =>
			message.Contains("UsrVehicle") && message.Contains("created")));
	}

	[TestCase(typeof(NullReferenceException))]
	[TestCase(typeof(ArgumentException))]
	[TestCase(typeof(InvalidCastException))]
	[Description("Schema creation rethrows (never swallows) when the OData rebuild request throws a fault outside the expected transport/IO/parse allow-list — a genuine programming error must surface, not be downgraded to a warning (ENG-92048).")]
	public void Create_ShouldRethrow_WhenODataBuildThrowsUnexpectedFault(Type unexpectedFaultType)
	{
		// Arrange — a fault that is not on the IsExpectedODataBuildFault allow-list must escape PublishSchema so a
		// real bug is never masked by the best-effort warning path.
		Exception unexpectedFault = (Exception)Activator.CreateInstance(unexpectedFaultType)!;
		SetupClientThrowingOnODataBuild(unexpectedFault);

		// Act
		Action act = () => _creator.Create(new CreateEntitySchemaOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Title = "Vehicle",
			Columns = ["Name:Text:Vehicle name"]
		});

		// Assert
		act.Should().Throw<Exception>(
				because: "a fault outside the expected transport/IO/parse families must escape and fail the create, not be swallowed as a warning")
			.Which.Should().BeOfType(unexpectedFaultType,
				because: "the original, unexpected fault type must propagate unchanged");
		_logger.DidNotReceive().WriteWarning(Arg.Is<string>(message =>
			message.Contains(RemoteEntitySchemaCreator.ODataBuildRequestFailedWarningFragment)));
	}

	[Test]
	[Description("Schema creation rethrows when the OData rebuild request throws an AggregateException whose inner fault is outside the allow-list — a wrapped programming error must surface, not be downgraded to a warning (ENG-92048).")]
	public void Create_ShouldRethrow_WhenODataBuildThrowsAggregateWithUnexpectedInner()
	{
		// Arrange
		SetupClientThrowingOnODataBuild(new AggregateException(new NullReferenceException()));

		// Act
		Action act = () => _creator.Create(new CreateEntitySchemaOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Title = "Vehicle",
			Columns = ["Name:Text:Vehicle name"]
		});

		// Assert
		act.Should().Throw<AggregateException>(
			because: "an AggregateException whose inner fault is not an expected transport/IO/parse type must propagate, not be swallowed");
		_logger.DidNotReceive().WriteWarning(Arg.Is<string>(message =>
			message.Contains(RemoteEntitySchemaCreator.ODataBuildRequestFailedWarningFragment)));
	}

	[Test]
	[Description("Schema creation rethrows when the OData rebuild request throws an empty AggregateException (Count == 0) — the Count > 0 guard must let a contentless aggregate surface instead of vacuously treating it as an expected fault (ENG-92048).")]
	public void Create_ShouldRethrow_WhenODataBuildThrowsEmptyAggregateException()
	{
		// Arrange
		SetupClientThrowingOnODataBuild(new AggregateException());

		// Act
		Action act = () => _creator.Create(new CreateEntitySchemaOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Title = "Vehicle",
			Columns = ["Name:Text:Vehicle name"]
		});

		// Assert
		act.Should().Throw<AggregateException>(
			because: "an empty AggregateException carries no expected inner fault, so the Count > 0 guard must let it surface rather than swallow it via a vacuous All(...)");
		_logger.DidNotReceive().WriteWarning(Arg.Is<string>(message =>
			message.Contains(RemoteEntitySchemaCreator.ODataBuildRequestFailedWarningFragment)));
	}

	// Builds the standard create-schema success responses but throws the supplied fault when the OData rebuild
	// is requested, so a test can exercise how PublishSchema's exception filter treats that fault.
	private void SetupClientThrowingOnODataBuild(Exception odataBuildFault)
	{
		SetupApplicationClient((url, body) => {
			if (url.Contains("CreateNewSchema", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"package\":{\"uId\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"UsrPkg\"},\"columns\":[],\"inheritedColumns\":[],\"indexes\":[]}}";
			}
			if (url.Contains("CheckUniqueSchemaName", StringComparison.Ordinal)) {
				return "{\"success\":true,\"value\":true}";
			}
			if (url.Contains("SaveSchema", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schemaUid\":\"22222222-2222-2222-2222-222222222222\"}";
			}
			if (url.Contains("RunODataBuild", StringComparison.Ordinal)) {
				throw odataBuildFault;
			}
			if (url.Contains("SchemaDesignerRequest", StringComparison.Ordinal)) {
				return "{\"success\":true}";
			}
			if (url.Contains("RuntimeEntitySchemaRequest", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"name\":\"UsrVehicle\"}}";
			}
			throw new InvalidOperationException($"Unexpected url {url}");
		});
	}

	[Test]
	[Description("Anchors the schema caption to the explicit --caption-culture override when provided (override > profile > en-US).")]
	public void Create_ShouldAnchorCaptionToOverrideCulture_WhenCaptionCultureProvided()
	{
		// Arrange
		string saveBody = null;
		SetupStandardSchemaClient(body => saveBody = body);

		// Act
		_creator.Create(new CreateEntitySchemaOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Title = "Vehicle",
			CaptionCulture = "uk-UA",
			Columns = ["Name:Text:Vehicle name"]
		});

		// Assert
		JObject json = JObject.Parse(saveBody);
		json["caption"]![0]!["cultureName"]!.Value<string>().Should().Be("uk-UA",
			because: "an explicit --caption-culture override must anchor the generated caption to that culture");
	}

	[Test]
	[Description("Falls back to en-US for the caption culture when no override is supplied and the profile culture cannot be resolved (regression-safe; never host CurrentCulture).")]
	public void Create_ShouldAnchorCaptionToEnUs_WhenNoOverrideAndProfileUnresolved()
	{
		// Arrange — no environment is configured in the unit context, so profile resolution fails
		// and the effective culture must degrade to en-US (parity with the previous behavior), not
		// to the host machine's CultureInfo.CurrentCulture.
		string saveBody = null;
		SetupStandardSchemaClient(body => saveBody = body);

		// Act
		using (new CultureScope("uk-UA")) {
			_creator.Create(new CreateEntitySchemaOptions {
				Package = "UsrPkg",
				SchemaName = "UsrVehicle",
				Title = "Vehicle",
				Columns = ["Name:Text:Vehicle name"]
			});
		}

		// Assert
		JObject json = JObject.Parse(saveBody);
		json["caption"]![0]!["cultureName"]!.Value<string>().Should().Be("en-US",
			because: "with no override and no resolvable profile culture the caption must anchor to en-US, not the host uk-UA locale");
	}

	private void SetupStandardSchemaClient(Action<string> captureSaveBody)
	{
		SetupApplicationClient((url, body) => {
			if (url.Contains("CreateNewSchema", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"package\":{\"uId\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"UsrPkg\"},\"columns\":[],\"inheritedColumns\":[],\"indexes\":[]}}";
			}
			if (url.Contains("CheckUniqueSchemaName", StringComparison.Ordinal)) {
				return "{\"success\":true,\"value\":true}";
			}
			if (url.Contains("SaveSchema", StringComparison.Ordinal)) {
				captureSaveBody(body);
				return "{\"success\":true,\"schemaUid\":\"22222222-2222-2222-2222-222222222222\"}";
			}
			if (url.Contains("SchemaDesignerRequest", StringComparison.Ordinal)) {
				return "{\"success\":true}";
			}
			if (url.Contains("RuntimeEntitySchemaRequest", StringComparison.Ordinal)) {
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"name\":\"UsrVehicle\"}}";
			}
			return "{\"success\":true}";
		});
	}


	private void SetupApplicationClient(Func<string, string, string> handler)
	{
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Any<string>(),
				Arg.Any<int>(),
				Arg.Any<int>(),
				Arg.Any<int>())
			.Returns(callInfo => {
				string url = callInfo.ArgAt<string>(0);
				string body = callInfo.ArgAt<string>(1);
				if (url.Contains("GetSchemaDesignItem", StringComparison.Ordinal)) {
					string schemaName = JObject.Parse(body)["name"]!.Value<string>()!;
					return JsonSerializer.Serialize(new {
						success = true,
						schema = new {
							uId = "22222222-2222-2222-2222-222222222222",
							name = schemaName,
							package = new {
								uId = "11111111-1111-1111-1111-111111111111",
								name = "UsrPkg"
							},
							columns = Array.Empty<object>(),
							inheritedColumns = Array.Empty<object>(),
							indexes = Array.Empty<object>()
						}
					});
				}

				return handler(url, body);
			});
	}

	private sealed class CultureScope : IDisposable {
		private readonly CultureInfo _originalCurrentCulture;
		private readonly CultureInfo _originalCurrentUiCulture;

		public CultureScope(string cultureName) {
			_originalCurrentCulture = CultureInfo.CurrentCulture;
			_originalCurrentUiCulture = CultureInfo.CurrentUICulture;
			CultureInfo culture = CultureInfo.GetCultureInfo(cultureName);
			CultureInfo.CurrentCulture = culture;
			CultureInfo.CurrentUICulture = culture;
		}

		public void Dispose() {
			CultureInfo.CurrentCulture = _originalCurrentCulture;
			CultureInfo.CurrentUICulture = _originalCurrentUiCulture;
		}
	}
}
