using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
internal class ListPrintablesCommandTests : BaseCommandTests<ListPrintablesOptions>
{
	private ListPrintablesCommand _command;
	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private ILogger _logger;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<ListPrintablesCommand>();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_logger = Substitute.For<ILogger>();
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select).Returns("http://localhost/select");
		containerBuilder.AddSingleton(_applicationClient);
		containerBuilder.AddSingleton(_serviceUrlBuilder);
		containerBuilder.AddSingleton(_logger);
	}

	private sealed record PrintableRow(
		string Id,
		string Caption,
		bool ConvertInPDF,
		bool ShowInCard,
		bool ShowInSection,
		string? SysEntitySchemaName,
		string? SysModuleEntityName);

	private static string BuildSuccessJson(IReadOnlyList<PrintableRow> rows) {
		object payload = new {
			success = true,
			rows = rows.Select(row => new {
				Id = row.Id,
				Caption = row.Caption,
				ConvertInPDF = row.ConvertInPDF,
				ShowInCard = row.ShowInCard,
				ShowInSection = row.ShowInSection,
				SysEntitySchemaName = row.SysEntitySchemaName,
				SysModuleEntityName = row.SysModuleEntityName
			}).ToArray()
		};
		return JsonSerializer.Serialize(payload);
	}

	[Test]
	[Description("Maps every SysModuleReport row field onto the printable summary and orders results by caption.")]
	public void TryGetPrintables_ShouldMapRowsOrderedByCaption_WhenNoEntityFilterProvided() {
		// Arrange
		ListPrintablesOptions options = new();
		string json = BuildSuccessJson([
			new PrintableRow("id-2", "Zeta report", false, false, true, "Order", null),
			new PrintableRow("id-1", "Alpha report", true, true, false, "Contact", null)
		]);
		_applicationClient.ExecutePostRequest("http://localhost/select", Arg.Any<string>()).Returns(json);

		// Act
		bool ok = _command.TryGetPrintables(options, out ListPrintablesResponse response);

		// Assert
		ok.Should().BeTrue(because: "a successful DataService response yields a successful probe result");
		response.Success.Should().BeTrue(because: "the probe envelope must report success");
		response.Count.Should().Be(2, because: "no entity filter was supplied, so both printables are returned");
		response.Printables[0].PrintableCaption.Should().Be("Alpha report",
			because: "printables are ordered by caption for stable output");
		response.Printables[0].TemplateId.Should().Be("id-1",
			because: "templateId maps from the SysModuleReport Id column");
		response.Printables[0].ConvertInPdf.Should().BeTrue(
			because: "the PDF flag maps from the ConvertInPDF column");
		response.Printables[0].ShowInCard.Should().BeTrue(
			because: "the card visibility flag maps from ShowInCard");
		response.Printables[0].EntitySchemaName.Should().Be("Contact",
			because: "the direct entity linkage maps from SysEntitySchemaName");
		response.ResolutionFailed.Should().BeNull(
			because: "a list probe has no resolution phase, so the hard-failure marker is omitted");
	}

	[Test]
	[Description("Matches printables by either entity linkage path — direct SysEntitySchema or the section module's entity — mirroring the runtime's OR filter.")]
	public void TryGetPrintables_ShouldMatchEitherLinkagePath_WhenEntityFilterProvided() {
		// Arrange
		ListPrintablesOptions options = new() { EntityName = "Contact" };
		string json = BuildSuccessJson([
			new PrintableRow("id-1", "Direct link", false, true, false, "Contact", null),
			new PrintableRow("id-2", "Module link", false, false, true, null, "Contact"),
			new PrintableRow("id-3", "Other entity", false, true, true, "Order", "Order")
		]);
		_applicationClient.ExecutePostRequest("http://localhost/select", Arg.Any<string>()).Returns(json);

		// Act
		_command.TryGetPrintables(options, out ListPrintablesResponse response);

		// Assert
		response.Count.Should().Be(2,
			because: "both linkage paths must match, and the unrelated entity must be filtered out");
		response.Printables.Select(printable => printable.TemplateId).Should().BeEquivalentTo(["id-1", "id-2"],
			because: "direct and module-linked printables both belong to the requested entity");
	}

	[Test]
	[Description("Matches the entity name case-insensitively — a deliberate agent-friendliness deviation from the runtime's strict comparison; schema names are unique case-insensitively so no wrong match is possible.")]
	public void TryGetPrintables_ShouldMatchCaseInsensitively_WhenEntityCaseDiffers() {
		// Arrange
		ListPrintablesOptions options = new() { EntityName = "contact" };
		string json = BuildSuccessJson([
			new PrintableRow("id-1", "Contact card", false, true, false, "Contact", null)
		]);
		_applicationClient.ExecutePostRequest("http://localhost/select", Arg.Any<string>()).Returns(json);

		// Act
		_command.TryGetPrintables(options, out ListPrintablesResponse response);

		// Assert
		response.Count.Should().Be(1,
			because: "the entity filter must tolerate agent-typed casing variants");
	}

	[Test]
	[Description("Returns a successful empty result when no printable matches the entity — a definitive answer, not an error.")]
	public void TryGetPrintables_ShouldReturnEmptySuccess_WhenNothingMatches() {
		// Arrange
		ListPrintablesOptions options = new() { EntityName = "Activity" };
		string json = BuildSuccessJson([
			new PrintableRow("id-1", "Contact card", false, true, false, "Contact", null)
		]);
		_applicationClient.ExecutePostRequest("http://localhost/select", Arg.Any<string>()).Returns(json);

		// Act
		bool ok = _command.TryGetPrintables(options, out ListPrintablesResponse response);

		// Assert
		ok.Should().BeTrue(because: "an empty match set is a valid probe outcome");
		response.Success.Should().BeTrue(because: "the environment answered; there is simply nothing registered");
		response.Count.Should().Be(0, because: "no printable is attached to the requested entity");
		response.Printables.Should().BeEmpty(because: "the list mirrors the zero count");
	}

	[Test]
	[Description("Sends a SelectQuery filtered to MS Word printables (Type.Id equals the MSWord printable type) with the two entity-linkage columns, mirroring the Freedom UI runtime query.")]
	public void TryGetPrintables_ShouldSendMsWordTypeFilterAndLinkageColumns_WhenCalled() {
		// Arrange
		ListPrintablesOptions options = new();
		string? requestJson = null;
		_applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Do<string>(request => requestJson = request))
			.Returns(BuildSuccessJson([]));

		// Act
		_command.TryGetPrintables(options, out _);

		// Assert
		requestJson.Should().NotBeNullOrWhiteSpace(
			because: "the command must send a SelectQuery request to the DataService");
		using JsonDocument document = JsonDocument.Parse(requestJson!);
		document.RootElement.GetProperty("rootSchemaName").GetString().Should().Be("SysModuleReport",
			because: "printables live in the SysModuleReport table");
		JsonElement typeFilter = document.RootElement
			.GetProperty("filters").GetProperty("items").EnumerateObject().First().Value;
		typeFilter.GetProperty("leftExpression").GetProperty("columnPath").GetString().Should().Be("Type.Id",
			because: "the query filters by the printable type lookup, mirroring the runtime ESQ");
		typeFilter.GetProperty("rightExpression").GetProperty("parameter").GetProperty("value").GetString()
			.Should().Be(ListPrintablesCommand.MsWordPrintableTypeId,
				because: "only MS Word printables are offered by the Freedom UI print button");
		string columns = document.RootElement.GetProperty("columns").GetProperty("items").GetRawText();
		columns.Should().Contain("SysEntitySchema.Name",
			because: "the direct entity linkage column must be selected");
		columns.Should().Contain("SysModule.SysModuleEntity.[SysSchema:UId:SysEntitySchemaUId].Name",
			because: "the section-module entity linkage column must be selected");
	}

	[Test]
	[Description("Returns a transient failure envelope (success false, error set, resolutionFailed omitted) when the DataService transport throws.")]
	public void TryGetPrintables_ShouldReturnTransientFailure_WhenTransportThrows() {
		// Arrange
		ListPrintablesOptions options = new();
		_applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns<string>(_ => throw new InvalidOperationException("connection refused"));

		// Act
		bool ok = _command.TryGetPrintables(options, out ListPrintablesResponse response);

		// Assert
		ok.Should().BeFalse(because: "the probe could not reach the environment");
		response.Success.Should().BeFalse(because: "a transport failure is not a usable result");
		response.Error.Should().Contain("connection refused",
			because: "the failure detail must surface to the agent");
		response.ResolutionFailed.Should().BeNull(
			because: "a transport failure is transient, never the hard resolution-failed marker");
	}

	[Test]
	[Description("Maps a DataService failure envelope (success:false) to a failed probe (Success=false + error), NOT a false empty-success — a server-side query rejection (e.g. permission denied) must be distinguishable from 'no printables registered'.")]
	public void TryGetPrintables_ShouldReturnFailure_WhenDataServiceReportsFailure() {
		// Arrange — the DataService responds, but with success:false (ExecuteSelectQuery throws on !Success).
		ListPrintablesOptions options = new();
		string failureJson = JsonSerializer.Serialize(new {
			success = false,
			errorInfo = new { message = "Access denied for SysModuleReport" }
		});
		_applicationClient.ExecutePostRequest("http://localhost/select", Arg.Any<string>()).Returns(failureJson);

		// Act
		bool ok = _command.TryGetPrintables(options, out ListPrintablesResponse response);

		// Assert
		ok.Should().BeFalse(because: "a DataService success:false envelope is a failed query, not a usable result");
		response.Success.Should().BeFalse(
			because: "the probe must report failure, not a false empty-success that reads to the agent as 'no printables registered'");
		response.Error.Should().Contain("Access denied for SysModuleReport",
			because: "the server-side failure detail must surface to the agent so the rejection is actionable");
		response.Count.Should().Be(0, because: "a failed query returns no printables");
		response.Printables.Should().BeEmpty(because: "a failed query yields no printable rows");
		response.ResolutionFailed.Should().BeNull(
			because: "a list probe has no resolution phase, so the hard-failure marker stays omitted even on a query rejection");
	}
}
