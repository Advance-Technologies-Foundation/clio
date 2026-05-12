using System;
using System.Collections.Generic;
using Clio.Command;
using Clio.Common;
using ConsoleTables;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class IdentityProviderManagementCommandTests {
	private IIdentityProviderManagementClient _client;
	private ILogger _logger;

	[SetUp]
	public void SetUp() {
		_client = Substitute.For<IIdentityProviderManagementClient>();
		_logger = Substitute.For<ILogger>();
	}

	[TearDown]
	public void TearDown() {
		_client.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
	}

	[Test]
	public void List_Should_Print_Table_By_Default() {
		_client.GetProviders(Arg.Any<int>()).Returns([
			new IdentityProviderInfo("id-1", "Main", null, "https://idp.example.com", "client", true)
		]);
		IdentityProviderListCommand command = new(_client, _logger);

		int result = command.Execute(new IdentityProviderListOptions());

		result.Should().Be(0);
		_client.Received(1).GetProviders(Arg.Any<int>());
		_logger.Received(1).PrintTable(Arg.Any<ConsoleTable>());
		_logger.DidNotReceive().WriteInfo(Arg.Any<string>());
	}

	[Test]
	public void List_Should_Write_Json_Without_Secrets_When_Json_Flag_Is_Set() {
		_client.GetProviders(Arg.Any<int>()).Returns([
			new IdentityProviderInfo("id-1", "Main", "Description", "https://idp.example.com", "client", true)
		]);
		IdentityProviderListCommand command = new(_client, _logger);

		int result = command.Execute(new IdentityProviderListOptions { JsonFormat = true });

		result.Should().Be(0);
		_logger.Received(1).WriteInfo(Arg.Is<string>(message =>
			message.Contains("\"clientId\"") && !message.Contains("secret", StringComparison.OrdinalIgnoreCase)));
		_logger.DidNotReceive().PrintTable(Arg.Any<ConsoleTable>());
	}

	[Test]
	public void Upsert_Should_Send_Secret_But_Not_Print_It() {
		const string secret = "top-secret-value";
		_client.SaveProvider(Arg.Any<IdentityProviderSaveModel>(), Arg.Any<int>())
			.Returns(new IdentityProviderInfo("id-1", "Main", null, "https://idp.example.com", "client", false));
		IdentityProviderUpsertCommand command = new(_client, _logger);

		int result = command.Execute(new IdentityProviderUpsertOptions {
			Name = "Main",
			ServerUrl = "https://idp.example.com",
			ClientId = "client",
			ClientSecret = secret
		});

		result.Should().Be(0);
		_client.Received(1).SaveProvider(
			Arg.Is<IdentityProviderSaveModel>(request => request.ClientSecret == secret),
			Arg.Any<int>());
		_logger.DidNotReceive().WriteInfo(Arg.Is<string>(message => message.Contains(secret)));
	}

	[Test]
	public void SetSecret_Should_Fail_When_Selector_Is_Missing() {
		IdentityProviderSetSecretCommand command = new(_client, _logger);

		int result = command.Execute(new IdentityProviderSetSecretOptions {
			ClientSecret = "secret"
		});

		result.Should().Be(1);
		_client.DidNotReceiveWithAnyArgs().SetProviderCredentials(default, default, default);
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("--id") && message.Contains("--name")));
	}

	[Test]
	public void SetDefault_Should_Fail_When_Both_Id_And_Name_Are_Set() {
		IdentityProviderSetDefaultCommand command = new(_client, _logger);

		int result = command.Execute(new IdentityProviderSetDefaultOptions {
			Id = Guid.NewGuid().ToString(),
			Name = "Main"
		});

		result.Should().Be(1);
		_client.DidNotReceiveWithAnyArgs().SetDefaultProvider(default, default);
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("exactly one")));
	}

	[Test]
	public void Bind_Should_Call_Client_With_Service_Code_And_Create_Flag() {
		_client.BindProviderToService(Arg.Any<BindProviderModel>(), Arg.Any<int>())
			.Returns(new BindingInfo("GlobalSearch", "provider-id", "service-id"));
		IdentityProviderBindCommand command = new(_client, _logger);

		int result = command.Execute(new IdentityProviderBindOptions {
			ProviderName = "Main",
			ServiceCode = "GlobalSearch",
			CreateService = true
		});

		result.Should().Be(0);
		_client.Received(1).BindProviderToService(
			Arg.Is<BindProviderModel>(request =>
				request.ProviderName == "Main" &&
				request.ServiceCode == "GlobalSearch" &&
				request.CreateServiceIfMissing),
			Arg.Any<int>());
	}

	[Test]
	public void Services_Should_Write_Json_When_Requested() {
		_client.GetServices(Arg.Any<int>()).Returns([
			new IdentityProviderServiceInfo("svc-id", "GlobalSearch", "GlobalSearch", "idp-id", "Main")
		]);
		IdentityProviderServicesCommand command = new(_client, _logger);

		int result = command.Execute(new IdentityProviderServicesOptions { JsonFormat = true });

		result.Should().Be(0);
		_logger.Received(1).WriteInfo(Arg.Is<string>(message =>
			message.Contains("\"code\"") && message.Contains("GlobalSearch")));
		_logger.DidNotReceive().PrintTable(Arg.Any<ConsoleTable>());
	}

	[Test]
	public void Delete_Should_Log_Service_Errors() {
		_client
			.When(client => client.DeleteProvider(Arg.Any<ProviderSelector>(), Arg.Any<int>()))
			.Do(_ => throw new InvalidOperationException("Default identity provider cannot be deleted."));
		IdentityProviderDeleteCommand command = new(_client, _logger);

		int result = command.Execute(new IdentityProviderDeleteOptions { Name = "Main" });

		result.Should().Be(1);
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("Default identity provider")));
	}
}

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class IdentityProviderManagementClientTests {
	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private IdentityProviderManagementClient _client;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_serviceUrlBuilder.Build(Arg.Any<string>()).Returns(callInfo => $"https://app.local{callInfo.Arg<string>()}");
		_client = new IdentityProviderManagementClient(_applicationClient, _serviceUrlBuilder);
	}

	[Test]
	public void SaveProvider_Should_Call_SaveProvider_Endpoint_And_Parse_Response() {
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("/rest/IdentityProviderManagementService/SaveProvider")),
				Arg.Is<string>(body => body.Contains("\"clientSecret\":\"secret\"")),
				1000,
				Arg.Any<int>(),
				Arg.Any<int>())
			.Returns("""
				{
				  "success": true,
				  "provider": {
				    "id": "id-1",
				    "name": "Main",
				    "serverUrl": "https://idp.example.com",
				    "clientId": "client",
				    "isDefault": false
				  }
				}
				""");

		IdentityProviderInfo provider = _client.SaveProvider(
			new IdentityProviderSaveModel(null, "Main", null, "https://idp.example.com", "client", "secret"),
			1000);

		provider.Id.Should().Be("id-1");
		provider.ClientId.Should().Be("client");
	}

	[Test]
	public void GetProviders_Should_Unwrap_Wcf_Result_Response() {
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("/rest/IdentityProviderManagementService/GetProviders")),
				Arg.Any<string>(),
				1000,
				Arg.Any<int>(),
				Arg.Any<int>())
			.Returns("""
				{
				  "result": {
				    "success": true,
				    "providers": [
				      {
				        "id": "id-1",
				        "name": "Main",
				        "serverUrl": "https://idp.example.com",
				        "clientId": "client",
				        "isDefault": true
				      }
				    ]
				  }
				}
				""");

		IReadOnlyList<IdentityProviderInfo> providers = _client.GetProviders(1000);

		providers.Should().ContainSingle().Which.IsDefault.Should().BeTrue();
	}

	[Test]
	public void BindProviderToService_Should_Call_Bind_Endpoint() {
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("/rest/IdentityProviderManagementService/BindProviderToService")),
				Arg.Is<string>(body =>
					body.Contains("\"providerName\":\"Main\"") &&
					body.Contains("\"serviceCode\":\"GlobalSearch\"") &&
					body.Contains("\"createServiceIfMissing\":true")),
				1000,
				Arg.Any<int>(),
				Arg.Any<int>())
			.Returns("""
				{
				  "success": true,
				  "binding": {
				    "serviceCode": "GlobalSearch",
				    "providerId": "provider-id",
				    "serviceId": "service-id"
				  }
				}
				""");

		BindingInfo binding = _client.BindProviderToService(
			new BindProviderModel(null, "Main", "GlobalSearch", true),
			1000);

		binding.ServiceCode.Should().Be("GlobalSearch");
	}

	[Test]
	public void Client_Should_Throw_When_Service_Returns_ErrorInfo() {
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Any<string>(),
				1000,
				Arg.Any<int>(),
				Arg.Any<int>())
			.Returns("""
				{
				  "success": false,
				  "errorInfo": {
				    "message": "Operation permission is required.",
				    "errorCode": "IdentityProviderManagementError"
				  }
				}
				""");

		Action action = () => _client.UnbindProviderFromService("GlobalSearch", 1000);

		action.Should().Throw<InvalidOperationException>().WithMessage("*permission*");
	}
}
