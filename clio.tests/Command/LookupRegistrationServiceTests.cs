using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public sealed class LookupRegistrationServiceTests {
	private const string PackageName = "TestPkg";
	private static readonly Guid PackageUId = Guid.Parse("1d07fd0e-2ca4-4d20-93b4-eb5a795ea03f");
	private static readonly Guid LookupSchemaUId = Guid.Parse("2d07fd0e-2ca4-4d20-93b4-eb5a795ea03f");
	private static readonly Guid ExistingBindingUId = Guid.Parse("3d07fd0e-2ca4-4d20-93b4-eb5a795ea03f");
	private static readonly Guid ExistingLookupRowId = Guid.Parse("4d07fd0e-2ca4-4d20-93b4-eb5a795ea03f");

	[Test]
	[Category("Unit")]
	[Description("Creates a Lookup row and a canonical Lookup_<schema> package schema data record when the lookup is not registered yet.")]
	public void EnsureLookupRegistration_Should_Create_Lookup_Row_And_Save_Canonical_Binding() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = CreateServiceUrlBuilder();
		IApplicationPackageListProvider packageListProvider = CreatePackageListProvider();
		IDataBindingSchemaClient schemaClient = CreateSchemaClient();
		ILogger logger = Substitute.For<ILogger>();
		string? insertBody = null;
		string? saveBody = null;
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(callInfo => BuildResponse(
				callInfo.ArgAt<string>(0),
				callInfo.ArgAt<string>(1),
				insertCapture => insertBody = insertCapture,
				saveCapture => saveBody = saveCapture));
		LookupRegistrationService sut = new(
			applicationClient,
			serviceUrlBuilder,
			packageListProvider,
			schemaClient,
			logger);

		// Act
		sut.EnsureLookupRegistration(PackageName, "UsrOrderStatus", "Order status");

		// Assert
		insertBody.Should().NotBeNull(
			because: "an unregistered lookup must create a Lookup row before saving package schema data");
		saveBody.Should().NotBeNull(
			because: "lookup registration must persist the canonical package schema data binding");
		string insertedRowId = ReadJsonString(insertBody!, "columnValues", "items", "Id", "parameter", "value");
		ReadJsonString(insertBody!, "columnValues", "items", "Name", "parameter", "value").Should().Be("Order status",
			because: "the Lookup row caption should reuse the generated business title");
		ReadJsonString(insertBody!, "columnValues", "items", "SysEntitySchemaUId", "parameter", "value")
			.Should().Be(LookupSchemaUId.ToString(),
				because: "the Lookup row should point to the created lookup schema");
		ReadJsonString(saveBody!, "name").Should().Be("Lookup_UsrOrderStatus",
			because: "lookup registration should use the canonical deterministic binding name");
		ReadJsonString(saveBody!, "entitySchemaName").Should().Be("Lookup",
			because: "lookup registration bindings must target the Lookup entity");
		ReadJsonArray(saveBody!, "boundRecordIds").Should().Equal([insertedRowId],
			because: "the saved package schema data should bind only the created Lookup row");
		ReadColumnNames(saveBody!).Should().Contain("Id")
			.And.Contain("Name")
			.And.Contain("SysEntitySchemaUId")
			.And.NotContain("CreatedBy")
			.And.NotContain("ModifiedBy",
				because: "lookup registration should reuse the canonical Lookup binding column set");
		logger.Received(1).WriteInfo("Lookup 'UsrOrderStatus' registered in Lookups.");
	}

	[Test]
	[Category("Unit")]
	[Description("Reuses the existing Lookup row and package schema data UId instead of creating duplicates when registration already exists.")]
	public void EnsureLookupRegistration_Should_ReUse_Existing_Lookup_Row_And_Binding() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = CreateServiceUrlBuilder();
		IApplicationPackageListProvider packageListProvider = CreatePackageListProvider();
		IDataBindingSchemaClient schemaClient = CreateSchemaClient();
		ILogger logger = Substitute.For<ILogger>();
		string? updateBody = null;
		string? saveBody = null;
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(callInfo => BuildExistingRegistrationResponse(
				callInfo.ArgAt<string>(0),
				callInfo.ArgAt<string>(1),
				updateCapture => updateBody = updateCapture,
				saveCapture => saveBody = saveCapture));
		LookupRegistrationService sut = new(
			applicationClient,
			serviceUrlBuilder,
			packageListProvider,
			schemaClient,
			logger);

		// Act
		sut.EnsureLookupRegistration(PackageName, "UsrOrderStatus", "Order status");

		// Assert
		applicationClient.DidNotReceive().ExecutePostRequest(
			"http://localhost/0/DataService/json/SyncReply/InsertQuery",
			Arg.Any<string>(),
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
		updateBody.Should().NotBeNull(
			because: "existing lookup registrations with a stale caption should be updated in place");
		saveBody.Should().NotBeNull(
			because: "existing bindings should be updated in place instead of recreated");
		ReadJsonString(updateBody!, "columnValues", "items", "Name", "parameter", "value").Should().Be("Order status",
			because: "the existing Lookup row caption should be synchronized to the requested title");
		ReadJsonString(updateBody!, "filters", "items", "primaryFilter", "rightExpression", "parameter", "value")
			.Should().Be(ExistingLookupRowId.ToString(),
				because: "the existing Lookup row should be updated in place");
		ReadJsonString(saveBody!, "uId").Should().Be(ExistingBindingUId.ToString(),
			because: "lookup registration should reuse the existing package schema data record");
		ReadJsonArray(saveBody!, "boundRecordIds").Should().Equal([ExistingLookupRowId.ToString()],
			because: "the canonical binding should point only to the existing Lookup row");
		logger.Received(1).WriteInfo("Lookup 'UsrOrderStatus' registered in Lookups.");
	}

	private static IServiceUrlBuilder CreateServiceUrlBuilder() {
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select)
			.Returns("http://localhost/0/DataService/json/SyncReply/SelectQuery");
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Insert)
			.Returns("http://localhost/0/DataService/json/SyncReply/InsertQuery");
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Update)
			.Returns("http://localhost/0/DataService/json/SyncReply/UpdateQuery");
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.SaveSchemaData)
			.Returns("http://localhost/0/ServiceModel/SchemaDataDesignerService.svc/SaveSchema");
		return serviceUrlBuilder;
	}

	private static IApplicationPackageListProvider CreatePackageListProvider() {
		IApplicationPackageListProvider packageListProvider = Substitute.For<IApplicationPackageListProvider>();
		packageListProvider.GetPackages().Returns([
			new PackageInfo(
				new PackageDescriptor {
					Name = PackageName,
					UId = PackageUId
				},
				string.Empty,
				Enumerable.Empty<string>())
		]);
		return packageListProvider;
	}

	private static IDataBindingSchemaClient CreateSchemaClient() {
		IDataBindingSchemaClient schemaClient = Substitute.For<IDataBindingSchemaClient>();
		schemaClient.Fetch("Lookup").Returns(new DataBindingSchema(
			Guid.Parse("5d07fd0e-2ca4-4d20-93b4-eb5a795ea03f"),
			"Lookup",
			Guid.Parse("6d07fd0e-2ca4-4d20-93b4-eb5a795ea03f"),
			[
				new DataBindingSchemaColumn(Guid.Parse("6d07fd0e-2ca4-4d20-93b4-eb5a795ea03f"), "Id", 0, null),
				new DataBindingSchemaColumn(Guid.Parse("7d07fd0e-2ca4-4d20-93b4-eb5a795ea03f"), "Name", 28, null),
				new DataBindingSchemaColumn(Guid.Parse("8d07fd0e-2ca4-4d20-93b4-eb5a795ea03f"), "SysEntitySchemaUId", 0, null),
				new DataBindingSchemaColumn(Guid.Parse("9d07fd0e-2ca4-4d20-93b4-eb5a795ea03f"), "CreatedBy", 0, "Contact"),
				new DataBindingSchemaColumn(Guid.Parse("ad07fd0e-2ca4-4d20-93b4-eb5a795ea03f"), "ModifiedBy", 0, "Contact")
			]));
		schemaClient.Fetch("UsrOrderStatus").Returns(new DataBindingSchema(
			LookupSchemaUId,
			"UsrOrderStatus",
			Guid.Parse("bd07fd0e-2ca4-4d20-93b4-eb5a795ea03f"),
			[
				new DataBindingSchemaColumn(Guid.Parse("bd07fd0e-2ca4-4d20-93b4-eb5a795ea03f"), "Id", 0, null),
				new DataBindingSchemaColumn(Guid.Parse("cd07fd0e-2ca4-4d20-93b4-eb5a795ea03f"), "Name", 28, null)
			]));
		return schemaClient;
	}

	private static string BuildResponse(
		string url,
		string body,
		Action<string> insertCapture,
		Action<string> saveCapture) {
		if (url.Contains("SelectQuery", StringComparison.Ordinal) &&
			body.Contains("\"rootSchemaName\":\"Lookup\"", StringComparison.Ordinal)) {
			return """{"success":true,"rows":[]}""";
		}
		if (url.Contains("SelectQuery", StringComparison.Ordinal) &&
			body.Contains("\"rootSchemaName\":\"SysPackageSchemaData\"", StringComparison.Ordinal)) {
			return """{"success":true,"rows":[]}""";
		}
		if (url.Contains("InsertQuery", StringComparison.Ordinal)) {
			insertCapture(body);
			return """{"success":true,"rowsAffected":1}""";
		}
		if (url.Contains("SaveSchema", StringComparison.Ordinal)) {
			saveCapture(body);
			return """{"success":true}""";
		}

		throw new InvalidOperationException($"Unexpected request URL: {url}");
	}

	private static string BuildExistingRegistrationResponse(
		string url,
		string body,
		Action<string> updateCapture,
		Action<string> saveCapture) {
		if (url.Contains("SelectQuery", StringComparison.Ordinal) &&
			body.Contains("\"rootSchemaName\":\"Lookup\"", StringComparison.Ordinal)) {
			return $$"""
				{"success":true,"rows":[{"Id":"{{ExistingLookupRowId}}","Name":"Old title"}]}
				""";
		}
		if (url.Contains("SelectQuery", StringComparison.Ordinal) &&
			body.Contains("\"rootSchemaName\":\"SysPackageSchemaData\"", StringComparison.Ordinal)) {
			return $$"""
				{"success":true,"rows":[{"UId":"{{ExistingBindingUId}}","EntitySchemaName":"Lookup"}]}
				""";
		}
		if (url.Contains("UpdateQuery", StringComparison.Ordinal)) {
			updateCapture(body);
			return """{"success":true,"rowsAffected":1}""";
		}
		if (url.Contains("SaveSchema", StringComparison.Ordinal)) {
			saveCapture(body);
			return """{"success":true}""";
		}

		throw new InvalidOperationException($"Unexpected request URL: {url}");
	}

	private static string ReadJsonString(string json, params string[] propertyPath) {
		using JsonDocument document = JsonDocument.Parse(json);
		JsonElement current = document.RootElement;
		foreach (string segment in propertyPath) {
			current = current.GetProperty(segment);
		}

		return current.GetString() ?? string.Empty;
	}

	private static string[] ReadJsonArray(string json, string propertyName) {
		using JsonDocument document = JsonDocument.Parse(json);
		return document.RootElement
			.GetProperty(propertyName)
			.EnumerateArray()
			.Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : item.GetRawText())
			.ToArray();
	}

	private static string[] ReadColumnNames(string json) {
		using JsonDocument document = JsonDocument.Parse(json);
		return document.RootElement
			.GetProperty("columns")
			.EnumerateArray()
			.Select(item => item.GetProperty("name").GetString() ?? string.Empty)
			.ToArray();
	}
}
