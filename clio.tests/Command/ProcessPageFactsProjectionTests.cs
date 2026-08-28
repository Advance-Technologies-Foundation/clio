namespace Clio.Tests.Command;

using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

/// <summary>
/// Tests for <see cref="ProcessPageFactsProjection"/> — the projection of a merged Freedom UI page bundle into the
/// facts a Pre-configured page process element needs.
/// <para>These rules were transcribed from the shipped process-designer bundle and verified against a
/// designer-built element, so the tests encode the MEASURED behaviour. The scope filter and the caption
/// composition in particular are the two an independent re-derivation gets wrong.</para>
/// </summary>
[TestFixture]
public class ProcessPageFactsProjectionTests {

	#region Methods: Private

	private static JObject Bundle(string viewConfig = "[]", string dataSources = "{}", string strings = "{}") =>
		JObject.Parse($$"""
			{
				"viewConfig": {{viewConfig}},
				"modelConfig": { "dataSources": {{dataSources}} },
				"resources": { "strings": {{strings}} }
			}
			""");

	#endregion

	#region Methods: Tests — data sources

	[Test]
	[Description("Only PAGE-scoped entity data sources are reported. The view-element-scoped ones behind lists and detail grids are excluded — including them would generate element parameters the page never fills.")]
	public void Project_ShouldReturnOnlyPageScopedEntityDataSources() {
		// Arrange — the shape measured on a real record page: one page-scoped source plus list-scoped ones.
		JObject bundle = Bundle(dataSources: """
			{
				"PDS": { "type": "crt.EntityDataSource", "scope": "page",
					"config": { "entitySchemaName": "ServiceItem" } },
				"CasesListDS": { "type": "crt.EntityDataSource", "scope": "viewElement",
					"config": { "entitySchemaName": "Case" } },
				"AttachmentListDS": { "type": "crt.EntityDataSource", "scope": "viewElement",
					"config": { "entitySchemaName": "ServiceItemFile" } }
			}
			""");

		// Act
		(_, List<ProcessPageDataSource> dataSources) = ProcessPageFactsProjection.Project(bundle);

		// Assert
		dataSources.Should().HaveCount(1);
		dataSources[0].Name.Should().Be("PDS");
		dataSources[0].EntitySchemaName.Should().Be("ServiceItem");
	}

	[Test]
	[Description("A page-scoped data source of another type is not an entity data source and is skipped.")]
	public void Project_ShouldSkipNonEntityDataSources() {
		// Arrange
		JObject bundle = Bundle(dataSources: """
			{
				"Lookup": { "type": "crt.LookupDataSource", "scope": "page",
					"config": { "entitySchemaName": "Contact" } }
			}
			""");

		// Act
		(_, List<ProcessPageDataSource> dataSources) = ProcessPageFactsProjection.Project(bundle);

		// Assert
		dataSources.Should().BeEmpty();
	}

	#endregion

	#region Methods: Tests — buttons

	[Test]
	[Description("A button is reported with the designer's caption composition — the resolved caption and the element name joined — and with the clicked event and its request.")]
	public void Project_ShouldComposeCaptionFromResolvedTextAndElementName() {
		// Arrange
		JObject bundle = Bundle(
			viewConfig: """
				[{ "type": "crt.Button", "name": "SaveButton", "caption": "$Resources.Strings.SaveButton",
					"clicked": { "request": "crt.SaveRecordRequest" } }]
				""",
			strings: """{ "SaveButton": { "en-US": "Apply", "ru-RU": "Применить" } }""");

		// Act
		(List<ProcessPageButton> buttons, _) = ProcessPageFactsProjection.Project(bundle);

		// Assert
		buttons.Should().HaveCount(1);
		buttons[0].Name.Should().Be("SaveButton");
		buttons[0].Caption.Should().Be("Apply | SaveButton",
			because: "the designer stores the resolved caption and the element name together");
		buttons[0].Event.Should().Be("clicked");
		buttons[0].Requests.Should().Equal("crt.SaveRecordRequest");
	}

	[Test]
	[Description("The other shipped resource-macro form resolves too, and a requested culture is honoured.")]
	public void Project_ShouldResolveResourceStringMacroInRequestedCulture() {
		// Arrange
		JObject bundle = Bundle(
			viewConfig: """
				[{ "type": "crt.Button", "name": "BackButton", "caption": "#ResourceString(BackButton)#",
					"clicked": { "request": "crt.ClosePageRequest" } }]
				""",
			strings: """{ "BackButton": { "en-US": "Back", "de-DE": "Zurück" } }""");

		// Act
		(List<ProcessPageButton> buttons, _) = ProcessPageFactsProjection.Project(bundle, "de-DE");

		// Assert
		buttons[0].Caption.Should().Be("Zurück | BackButton");
	}

	[Test]
	[Description("A caption whose resource key is absent falls back to the raw text rather than reporting an empty caption.")]
	public void Project_ShouldFallBackToRawCaptionWhenResourceMissing() {
		// Arrange
		JObject bundle = Bundle(viewConfig: """
			[{ "type": "crt.Button", "name": "CustomButton", "caption": "$Resources.Strings.Absent",
				"clicked": { "request": "crt.SaveRecordRequest" } }]
			""");

		// Act
		(List<ProcessPageButton> buttons, _) = ProcessPageFactsProjection.Project(bundle);

		// Assert
		buttons[0].Caption.Should().Be("$Resources.Strings.Absent | CustomButton");
	}

	[Test]
	[Description("Buttons are found however deeply they are nested in the merged view config, since the merge flattens nothing.")]
	public void Project_ShouldFindNestedButtons() {
		// Arrange
		JObject bundle = Bundle(viewConfig: """
			{ "items": [ { "items": [
				{ "type": "crt.Button", "name": "SaveButton", "caption": "Save",
					"clicked": { "request": "crt.SaveRecordRequest" } } ] } ] }
			""");

		// Act
		(List<ProcessPageButton> buttons, _) = ProcessPageFactsProjection.Project(bundle);

		// Assert
		buttons.Should().HaveCount(1);
		buttons[0].Name.Should().Be("SaveButton");
	}

	[Test]
	[Description("A menu button contributes one entry per LEAF menu item, with the caption path carried down — it is the item the user presses that completes the page, not the menu itself.")]
	public void Project_ShouldExpandMenuButtonIntoLeafItems() {
		// Arrange
		JObject bundle = Bundle(viewConfig: """
			[{ "type": "crt.Button", "name": "ActionsButton", "caption": "Actions", "clickMode": "menu",
				"menuItems": [
					{ "name": "ApproveItem", "caption": "Approve",
						"clicked": { "request": "crt.SaveRecordRequest" } },
					{ "name": "RejectItem", "caption": "Reject",
						"clicked": { "request": "crt.ClosePageRequest" } } ] }]
			""");

		// Act
		(List<ProcessPageButton> buttons, _) = ProcessPageFactsProjection.Project(bundle);

		// Assert
		buttons.Select(button => button.Name).Should().Equal("ApproveItem", "RejectItem");
		buttons[0].Caption.Should().Be("Actions | Approve | ApproveItem");
		buttons.Should().NotContain(button => button.Name == "ActionsButton",
			because: "the menu itself is not pressable in the sense that completes the page");
	}

	[Test]
	[Description("A nested menu carries the whole caption path down to the leaf.")]
	public void Project_ShouldCarryCaptionPathThroughNestedMenus() {
		// Arrange
		JObject bundle = Bundle(viewConfig: """
			[{ "type": "crt.Button", "name": "ActionsButton", "caption": "Actions", "clickMode": "menu",
				"menuItems": [ { "name": "MoreMenu", "caption": "More", "items": [
					{ "name": "ArchiveItem", "caption": "Archive",
						"clicked": { "request": "crt.ClosePageRequest" } } ] } ] }]
			""");

		// Act
		(List<ProcessPageButton> buttons, _) = ProcessPageFactsProjection.Project(bundle);

		// Assert
		buttons.Should().HaveCount(1);
		buttons[0].Caption.Should().Be("Actions | More | Archive | ArchiveItem");
	}

	[Test]
	[Description("The same button reported from two containers collapses to ONE entry. Measured on a live 8.1.3 stand: Accounts_FormPage carries ActionButtonsContainer in BOTH MainHeaderTop and ActionContainer, so Save/Cancel/Close each appear twice (7 nodes, 4 distinct). The element identifies a button by name, and the server keys its id-reuse map by name, so emitting both would write two items that collapse onto one id.")]
	public void Project_ShouldCollapseTheSameButtonReportedFromTwoContainers() {
		// Arrange — the real shape, trimmed to the two containers that carry the duplicate.
		JObject bundle = Bundle(viewConfig: """
			[{ "type": "crt.FlexContainer", "name": "MainHeaderTop", "items": [
				{ "type": "crt.FlexContainer", "name": "ActionButtonsContainer", "items": [
					{ "type": "crt.Button", "name": "SaveButton", "caption": "Save",
						"clicked": { "request": "crt.SaveRecordRequest" } },
					{ "type": "crt.Button", "name": "CloseButton", "caption": "Close",
						"clicked": { "request": "crt.ClosePageRequest" } } ] } ] },
			 { "type": "crt.GridContainer", "name": "ActionContainer", "items": [
				{ "type": "crt.FlexContainer", "name": "ActionButtonsContainer", "items": [
					{ "type": "crt.Button", "name": "SaveButton", "caption": "Save",
						"clicked": { "request": "crt.SaveRecordRequest" } },
					{ "type": "crt.Button", "name": "CloseButton", "caption": "Close",
						"clicked": { "request": "crt.ClosePageRequest" } } ] } ] }]
			""");

		// Act
		(List<ProcessPageButton> buttons, _) = ProcessPageFactsProjection.Project(bundle);

		// Assert
		buttons.Select(button => button.Name).Should().Equal(["SaveButton", "CloseButton"],
			because: "two containers carrying the same button is one button, in the order first seen");
	}

	[Test]
	[Description("When a duplicated button's copies DIFFER, the collapse keeps the most informative one, not the first: walk order is JSON property order, which means nothing, and keep-first could drop a real completing button from the candidate list entirely — the first copy carries a non-completing request while its twin carries crt.SaveRecordRequest.")]
	public void Project_ShouldPreferTheInformativeCopyWhenDuplicatesDiffer() {
		// Arrange — first occurrence is NOT a candidate (a foreign request); the second is.
		JObject bundle = Bundle(viewConfig: """
			[{ "type": "crt.FlexContainer", "name": "Header", "items": [
				{ "type": "crt.Button", "name": "SaveButton", "caption": "Save",
					"clicked": { "request": "crt.PrintRequest" } } ] },
			 { "type": "crt.GridContainer", "name": "Actions", "items": [
				{ "type": "crt.Button", "name": "SaveButton", "caption": "Save",
					"clicked": { "request": "crt.SaveRecordRequest" } } ] }]
			""");

		// Act
		(List<ProcessPageButton> buttons, _) = ProcessPageFactsProjection.Project(bundle);

		// Assert
		buttons.Should().ContainSingle();
		buttons[0].Requests.Should().Equal(["crt.SaveRecordRequest"],
			because: "the completing copy must win the collapse, or the button vanishes from the candidates");
	}

	[Test]
	[Description("A button with no click handler reports no requests — which still leaves it eligible, matching the designer's rule for a custom button that only runs code.")]
	public void Project_ShouldReportNoRequestsForHandlerlessButton() {
		// Arrange
		JObject bundle = Bundle(viewConfig: """
			[{ "type": "crt.Button", "name": "CustomButton", "caption": "Do it" }]
			""");

		// Act
		(List<ProcessPageButton> buttons, _) = ProcessPageFactsProjection.Project(bundle);

		// Assert
		buttons[0].Requests.Should().BeEmpty();
		ProcessPageFactsProjection.IsCompletingCandidate(buttons[0]).Should().BeTrue();
	}

	[Test]
	[Description("A nameless button is skipped: the name is the identity the process element stores, so an entry without one could not be applied.")]
	public void Project_ShouldSkipNamelessButton() {
		// Arrange
		JObject bundle = Bundle(viewConfig: """
			[{ "type": "crt.Button", "caption": "Anonymous", "clicked": { "request": "crt.SaveRecordRequest" } }]
			""");

		// Act
		(List<ProcessPageButton> buttons, _) = ProcessPageFactsProjection.Project(bundle);

		// Assert
		buttons.Should().BeEmpty();
	}

	#endregion

	#region Methods: Tests — eligibility

	[TestCase("crt.SaveRecordRequest", true)]
	[TestCase("crt.ClosePageRequest", true)]
	[TestCase("crt.CancelRecordChangesRequest", true)]
	[TestCase("crt.CreateRecordRequest", false)]
	[TestCase("crt.UpdateRecordRequest", false)]
	[Description("Eligibility follows the designer's allow-list exactly: save, close and cancel-changes complete the page; any other request does not.")]
	public void IsCompletingCandidate_ShouldFollowRequestAllowList(string request, bool expected) {
		// Arrange
		ProcessPageButton button = new() { Name = "Button", Requests = [request] };

		// Act / Assert
		ProcessPageFactsProjection.IsCompletingCandidate(button).Should().Be(expected);
	}

	#endregion

}
