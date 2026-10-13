using System;
using System.Text;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Behaviour of <see cref="WorkplaceRepository"/> that does not depend on a live environment: the client-type
/// fallback for a translated profile, and finding a row in a binding's bound data.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class WorkplaceRepositoryTests {

	private const string IdColumnUId = "ae0e45ca-c495-4fe7-a39d-3ab7278e1617";
	private const string MobileId = "be8ab9ee-344e-4a56-8c97-41c84f22bd88";
	private const string WebId = "195785b4-f55a-4e72-ace3-6480b54c8fa5";

	[TestCase("Mobile", MobileId)]
	[TestCase("Web", WebId)]
	[Description("Falls back to the seeded client-type id when the profile culture translates SysApplicationClientType.Name.")]
	public void FindClientTypeId_ShouldUseSeededId_WhenNameIsTranslated(string name, string expected) {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($$"""
				{"success":true,"rows":[{"Id":"{{MobileId}}","Name":"Мобільний"},{"Id":"{{WebId}}","Name":"Веб"}]}
				""");
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>()).Returns("http://host/DataService/Select");
		WorkplaceRepository repository = new(client, urlBuilder);

		// Act
		Guid? id = repository.FindClientTypeId(name);

		// Assert
		id.Should().Be(Guid.Parse(expected), because: "the seeded row exists even when its name is translated");
	}

	[Test]
	[Description("Finds the row by its Id cell in the bound data, also behind a byte-order mark.")]
	public void BindingDataShipsRow_ShouldMatchIdCell() {
		// Arrange
		Guid row = Guid.NewGuid();
		string data = "﻿" + BoundData(row, Guid.NewGuid());

		// Act
		bool? ships = WorkplaceRepository.BindingDataShipsRow(data, row);

		// Assert
		ships.Should().BeTrue(because: "the row's Id cell carries the id");
	}

	[Test]
	[Description("Decodes base64 bound data before matching.")]
	public void BindingDataShipsRow_ShouldDecodeBase64() {
		// Arrange
		Guid row = Guid.NewGuid();
		string data = Convert.ToBase64String(Encoding.UTF8.GetBytes(BoundData(row, Guid.NewGuid())));

		// Act
		bool? ships = WorkplaceRepository.BindingDataShipsRow(data, row);

		// Assert
		ships.Should().BeTrue(because: "the binary column may arrive base64-encoded");
	}

	[Test]
	[Description("Does not match an id that appears only as a lookup value, and reports unreadable data as unknown rather than as not shipped.")]
	public void BindingDataShipsRow_ShouldIgnoreLookupValuesAndBadData() {
		// Arrange
		Guid lookup = Guid.NewGuid();
		string data = BoundData(Guid.NewGuid(), lookup);

		// Act
		bool? lookupMatches = WorkplaceRepository.BindingDataShipsRow(data, lookup);
		bool? badDataMatches = WorkplaceRepository.BindingDataShipsRow("not json", lookup);
		bool? missingDataMatches = WorkplaceRepository.BindingDataShipsRow(null, lookup);

		// Assert
		lookupMatches.Should().BeFalse(because: "only the Id cell identifies the bound row");
		badDataMatches.Should().BeNull(because: "a safety check must not read unparseable data as 'not shipped'");
		missingDataMatches.Should().BeNull(because: "missing data says nothing about the binding");
	}

	private static string BoundData(Guid id, Guid lookup) => $$"""
		{"PackageData":[{"Row":[
		  {"SchemaColumnUId":"{{IdColumnUId}}","Value":"{{id}}"},
		  {"SchemaColumnUId":"0e3d3857-2203-40e4-b1ec-8e972007650e","Value":"{{lookup}}","DisplayValue":"My applications"}
		]}]}
		""";
}
