using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// ENG-90576 story 4 (ADR D11, F10-F13): requests the section localization client sends.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class ApplicationSectionLocalizationClientTests {
	private IApplicationClient _client = null!;
	private EnvironmentSettings _settings = null!;
	private ApplicationSectionLocalizationClient _sut = null!;

	[SetUp]
	public void SetUp() {
		_client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>(), Arg.Any<EnvironmentSettings>())
			.Returns(callInfo => $"route:{callInfo.ArgAt<ServiceUrlBuilder.KnownRoute>(0)}");
		_settings = new EnvironmentSettings { Uri = "https://example.invalid" };
		_sut = new ApplicationSectionLocalizationClient(serviceUrlBuilder);
	}

	[Test]
	[Description("Reads the section's localization rows through SelectLocalizationQuery on SysModule filtered by Record, with the culture name.")]
	public void ReadLocalizations_Should_Query_SysModule_LocalizationRows() {
		// Arrange
		string? body = null;
		_client.ExecutePostRequest("route:SelectLocalizationQuery", Arg.Do<string>(value => body = value))
			.Returns("""{"success":true,"rows":[{"CultureName":"es-ES","Caption":"Pedidos","Description":"","ModuleHeader":""}]}""");

		// Act
		IReadOnlyList<SectionLocalizationRow> rows = _sut.ReadLocalizations(_client, _settings, "section-id");

		// Assert
		rows.Should().Equal([new SectionLocalizationRow("es-ES", "Pedidos", "", "")],
			because: "each non-default culture of the section is one row");
		body.Should().Contain("\"rootSchemaName\":\"SysModule\"", because: "the section title is SysModule.Caption (F10)")
			.And.Contain("\"columnPath\":\"Record\"", because: "localization rows are keyed by Record")
			.And.Contain("\"columnPath\":\"SysCulture.Name\"", because: "the culture name is needed to match cultures");
	}

	[Test]
	[Description("Writes per-culture values through UpdateLocalizationQuery with the localizable-string parameter type 19 and a culture map.")]
	public void WriteLocalizations_Should_Send_LocalizableString_Parameter() {
		// Arrange
		string? body = null;
		_client.ExecutePostRequest("route:UpdateLocalizationQuery", Arg.Do<string>(value => body = value))
			.Returns("""{"success":true,"rowsAffected":1}""");
		Dictionary<string, IReadOnlyDictionary<string, string>> values = new() {
			["Caption"] = new Dictionary<string, string> { ["en-US"] = "Orders", ["es-ES"] = "Pedidos" }
		};

		// Act
		_sut.WriteLocalizations(_client, _settings, "section-id", values);

		// Assert
		JsonNode parameter = JsonNode.Parse(body!)!["columnValues"]!["items"]!["Caption"]!["parameter"]!;
		parameter["dataValueType"]!.GetValue<int>().Should().Be(19,
			because: "DataService reads a localizable value only with the LocalizableStringDataValueType parameter");
		JsonNode.Parse(parameter["value"]!.GetValue<string>())!["es-ES"]!.GetValue<string>().Should().Be("Pedidos",
			because: "the value is a JSON culture map");
	}

	[Test]
	[Description("A failed localization read quotes the server body bounded and redacted, not a whole server page.")]
	public void ReadLocalizations_Should_BoundAndRedactServerBody_WhenQueryFails() {
		// Arrange
		string body = "{\"success\":false,\"detail\":\"password=hunter2 " + new string('x', 5000) + "\"}";
		_client.ExecutePostRequest("route:SelectLocalizationQuery", Arg.Any<string>()).Returns(body);

		// Act
		Action action = () => _sut.ReadLocalizations(_client, _settings, "section-id");

		// Assert
		string message = action.Should().Throw<InvalidOperationException>(because: "the server reported a failure")
			.Which.Message;
		message.Should().StartWith("SelectLocalizationQuery failed:", because: "the failed operation must be named")
			.And.NotContain("hunter2", because: "credential values must not reach the caller");
		message.Length.Should().BeLessThan(1000, because: "the body is cut to a short preview");
	}

	[Test]
	[Description("The localization write targets the section by Id sent as a GUID parameter.")]
	public void WriteLocalizations_Should_FilterById_AsGuid() {
		// Arrange
		string? body = null;
		_client.ExecutePostRequest("route:UpdateLocalizationQuery", Arg.Do<string>(value => body = value))
			.Returns("""{"success":true,"rowsAffected":1}""");
		Dictionary<string, IReadOnlyDictionary<string, string>> values = new() {
			["Caption"] = new Dictionary<string, string> { ["en-US"] = "Orders" }
		};

		// Act
		_sut.WriteLocalizations(_client, _settings, "section-id", values);

		// Assert
		JsonNode filter = JsonNode.Parse(body!)!["filters"]!["items"]!["primaryFilter"]!;
		filter["leftExpression"]!["columnPath"]!.GetValue<string>().Should().Be("Id", because: "one section is written");
		filter["rightExpression"]!["parameter"]!["dataValueType"]!.GetValue<int>().Should().Be(0,
			because: "the stand-verified write sends the Id as a GUID");
		filter["rightExpression"]!["parameter"]!["value"]!.GetValue<string>().Should().Be("section-id",
			because: "the filter carries the section id");
	}

	[Test]
	[Description("Throws when the server rejects the localization write, carrying the server message.")]
	public void WriteLocalizations_Should_Throw_WhenServerRejects() {
		// Arrange
		_client.ExecutePostRequest("route:UpdateLocalizationQuery", Arg.Any<string>())
			.Returns("""{"success":false,"responseStatus":{"Message":"Title field must be filled in"}}""");
		Dictionary<string, IReadOnlyDictionary<string, string>> values = new() {
			["Caption"] = new Dictionary<string, string> { ["es-ES"] = "Pedidos" }
		};

		// Act
		Action action = () => _sut.WriteLocalizations(_client, _settings, "section-id", values);

		// Assert
		action.Should().Throw<InvalidOperationException>(because: "a rejected write must not look like a success")
			.WithMessage("*Title field must be filled in*", because: "the server's reason must reach the caller");
	}

	[Test]
	[Description("TC-U-46: re-saves the SysModule_<code> binding unchanged: GetSchema, GetBoundSchemaData, then SaveSchema with the same DTO and the bound record ids.")]
	public void RefreshSectionPackageBinding_Should_Resave_Binding_WithBoundRecords() {
		// Arrange
		string? selectBody = null;
		string? saveBody = null;
		_client.ExecutePostRequest("route:Select", Arg.Do<string>(value => selectBody = value))
			.Returns("""{"success":true,"rows":[{"UId":"binding-uid"}]}""");
		_client.ExecutePostRequest("route:GetSchemaDataDesignItem", Arg.Any<string>())
			.Returns("""{"success":true,"schema":{"uId":"binding-uid","name":"SysModule_UsrOrders","columns":[{"name":"Caption"}],"boundRecordIds":null}}""");
		_client.ExecutePostRequest("route:GetBoundSchemaData", Arg.Any<string>())
			.Returns("""{"success":true,"items":"[{\"Id\":\"section-id\",\"Caption\":\"Orders\"}]"}""");
		_client.ExecutePostRequest("route:SaveSchemaData", Arg.Do<string>(value => saveBody = value))
			.Returns("""{"success":true}""");

		// Act
		bool refreshed = _sut.RefreshSectionPackageBinding(_client, _settings, "pkg-uid", "UsrOrders");

		// Assert
		refreshed.Should().BeTrue(because: "the binding exists and was re-saved");
		selectBody.Should().Contain("SysModule_UsrOrders", because: "the binding is found by its conventional name");
		JsonNode saved = JsonNode.Parse(saveBody!)!;
		saved["boundRecordIds"]!.AsArray().Should().ContainSingle(because: "the binding keeps its record")
			.Which!.GetValue<string>().Should().Be("section-id", because: "the record id comes from GetBoundSchemaData");
		saved["columns"]!.AsArray().Should().ContainSingle(because: "the columns are sent back unchanged");
	}

	[Test]
	[Description("Returns false without further requests when the package has no SysModule_<code> binding.")]
	public void RefreshSectionPackageBinding_Should_ReturnFalse_WhenBindingIsMissing() {
		// Arrange
		_client.ExecutePostRequest("route:Select", Arg.Any<string>()).Returns("""{"success":true,"rows":[]}""");

		// Act
		bool refreshed = _sut.RefreshSectionPackageBinding(_client, _settings, "pkg-uid", "UsrOrders");

		// Assert
		refreshed.Should().BeFalse(because: "there is nothing to re-save");
		_client.DidNotReceive().ExecutePostRequest("route:SaveSchemaData", Arg.Any<string>());
	}

	[Test]
	[Description("Rejects a binding whose bound rows carry no record id instead of re-saving it with an empty record set.")]
	public void RefreshSectionPackageBinding_Should_Throw_WhenBoundRowsHaveNoRecordIds() {
		// Arrange
		_client.ExecutePostRequest("route:Select", Arg.Any<string>())
			.Returns("""{"success":true,"rows":[{"UId":"binding-uid"}]}""");
		_client.ExecutePostRequest("route:GetSchemaDataDesignItem", Arg.Any<string>())
			.Returns("""{"success":true,"schema":{"uId":"binding-uid","name":"SysModule_UsrOrders","columns":[{"name":"Caption"}],"boundRecordIds":null}}""");
		_client.ExecutePostRequest("route:GetBoundSchemaData", Arg.Any<string>())
			.Returns("""{"success":true,"items":"[{\"Id\":\"\",\"Caption\":\"Orders\"},{\"Caption\":\"Other\"}]"}""");

		// Act
		Action act = () => _sut.RefreshSectionPackageBinding(_client, _settings, "pkg-uid", "UsrOrders");

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "an empty bound-record set would wipe the binding's data")
			.WithMessage("*no record ids*SysModule_UsrOrders*");
		_client.DidNotReceive().ExecutePostRequest("route:SaveSchemaData", Arg.Any<string>());
	}
}
