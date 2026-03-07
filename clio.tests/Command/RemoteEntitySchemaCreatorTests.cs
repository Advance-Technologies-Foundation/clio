using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
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
			throw new InvalidOperationException($"Unexpected url {url}");
		});

		_creator.Create(new CreateEntitySchemaOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Title = "Vehicle",
			Columns = ["Name:Text:Vehicle name"]
		});

		_applicationClient.Received().ExecutePostRequest(
			Arg.Is<string>(url => url.Contains("/ServiceModel/EntitySchemaDesignerService.svc/CreateNewSchema")),
			Arg.Any<string>(),
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
		_applicationClient.Received().ExecutePostRequest(
			Arg.Is<string>(url => url.Contains("/ServiceModel/EntitySchemaDesignerService.svc/CheckUniqueSchemaName")),
			Arg.Any<string>(),
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
		_applicationClient.Received().ExecutePostRequest(
			Arg.Is<string>(url => url.Contains("/ServiceModel/EntitySchemaDesignerService.svc/SaveSchema")),
			Arg.Any<string>(),
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());

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
			throw new InvalidOperationException($"Unexpected url {url}");
		});

		_creator.Create(new CreateEntitySchemaOptions {
			Package = "UsrPkg",
			SchemaName = "UsrAccount",
			Title = "Account",
			ParentSchemaName = "Account"
		});

		_applicationClient.Received().ExecutePostRequest(
			Arg.Is<string>(url => url.Contains("/ServiceModel/EntitySchemaDesignerService.svc/GetAvailableParentSchemas")),
			Arg.Any<string>(),
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
		_applicationClient.Received().ExecutePostRequest(
			Arg.Is<string>(url => url.Contains("/ServiceModel/EntitySchemaDesignerService.svc/AssignParentSchema")),
			Arg.Any<string>(),
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
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
			Arg.Is<string>(url => url.Contains("/ServiceModel/EntitySchemaDesignerService.svc/SaveSchema")),
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
			Arg.Is<string>(url => url.Contains("/ServiceModel/EntitySchemaDesignerService.svc/SaveSchema")),
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
			.Returns(callInfo => handler(callInfo.ArgAt<string>(0), callInfo.ArgAt<string>(1)));
	}
}
