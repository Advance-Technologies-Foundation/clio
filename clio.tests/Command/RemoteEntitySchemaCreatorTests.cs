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
			default_value = "Draft"
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
		saveDbStructureCalled.Should().BeTrue();
		runtimeVerifyCalled.Should().BeTrue();
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
