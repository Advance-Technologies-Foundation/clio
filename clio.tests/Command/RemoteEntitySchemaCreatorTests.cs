using System;
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
internal class RemoteEntitySchemaCreatorTests : BaseClioModuleTests
{
	private IApplicationClient _applicationClient;
	private IApplicationPackageListProvider _packageListProvider;
	private ILogger _logger;
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
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder)
	{
		base.AdditionalRegistrations(containerBuilder);
		_applicationClient = Substitute.For<IApplicationClient>();
		_packageListProvider = Substitute.For<IApplicationPackageListProvider>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddTransient(_ => _applicationClient);
		containerBuilder.AddTransient(_ => _packageListProvider);
		containerBuilder.AddTransient(_ => _logger);
	}

	[Test]
	[Description("Creates a root entity schema, auto-adds Id when needed, and persists the requested text column metadata.")]
	public void Create_CreatesSchemaWithoutParent_AndShapesSavePayload()
	{
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
		json["primaryColumn"]!["name"]!.Value<string>().Should().Be("Id");
		json["primaryDisplayColumn"]!["name"]!.Value<string>().Should().Be("Name");
		json["columns"]!.Select(column => column["name"]!.Value<string>()).Should().Contain(["Id", "Name"]);
		json["columns"]!.Single(column => column["name"]!.Value<string>() == "Id")["type"]!.Value<int>().Should().Be(0);
	}

	[Test]
	[Description("Creates an entity schema with an assigned parent schema before saving the final designer payload.")]
	public void Create_CreatesSchemaWithParent_AndCallsAssignParentSchema()
	{
		bool saveDbStructureCalled = false;
		bool runtimeVerifyCalled = false;
		SetupApplicationClient((url, _) => {
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
				return "{\"success\":true,\"schema\":{\"uId\":\"22222222-2222-2222-2222-222222222222\",\"package\":{\"uId\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"UsrPkg\"},\"parentSchema\":{\"uId\":\"33333333-3333-3333-3333-333333333333\",\"name\":\"Account\"},\"columns\":[],\"inheritedColumns\":[],\"indexes\":[]}}";
			}
			if (url.Contains("SaveSchema", StringComparison.Ordinal)) {
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

		_creator.Create(new CreateEntitySchemaOptions {
			Package = "UsrPkg",
			SchemaName = "UsrAccount",
			Title = "Account",
			ParentSchemaName = "Account"
		});

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
		saveDbStructureCalled.Should().BeTrue();
		runtimeVerifyCalled.Should().BeTrue();
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
}
