using System;
using System.Collections.Generic;
using Clio.Command.Localization;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class CreatioCultureCatalogTests : BaseClioModuleTests {

	private const string SysCultureRows =
		"""{"success":true,"rows":[{"Name":"en-US","Active":true},{"Name":"es-ES","Active":false},{"Name":"de-DE","Active":true}]}""";

	private IApplicationClient _applicationClient;
	private ICreatioCultureCatalog _catalog;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_applicationClient = Substitute.For<IApplicationClient>();
		containerBuilder.AddTransient(_ => _applicationClient);
	}

	public override void Setup() {
		base.Setup();
		_catalog = Container.GetRequiredService<ICreatioCultureCatalog>();
	}

	public override void TearDown() {
		_applicationClient.ClearReceivedCalls();
		base.TearDown();
	}

	private void StubSelectQuery(string response) =>
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(response);

	[Test]
	[Description("TC-U-17: SysCulture rows are read through a DataService SelectQuery on SysCulture and parsed into Name/Active pairs.")]
	public void GetCultures_ShouldReturnNameAndActive_WhenSelectQuerySucceeds() {
		// Arrange
		StubSelectQuery(SysCultureRows);

		// Act
		IReadOnlyList<CreatioCulture> cultures = _catalog.GetCultures();

		// Assert
		cultures.Should().Equal(
			[new CreatioCulture("en-US", true), new CreatioCulture("es-ES", false), new CreatioCulture("de-DE", true)],
			because: "every SysCulture row is returned with its Name and Active flag, in server order");
		_applicationClient.Received(1).ExecutePostRequest(
			Arg.Is<string>(url => url.EndsWith("DataService/json/SyncReply/SelectQuery", StringComparison.Ordinal)),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"SysCulture\"", StringComparison.Ordinal)),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("TC-U-18: an absent culture is reported as not found with the available list, an inactive one as found with Active=false, and a case-insensitive match returns the canonical SysCulture.Name spelling.")]
	public void Find_ShouldReportAbsentAndInactive_WhenCultureMissingOrInactive() {
		// Arrange
		StubSelectQuery(SysCultureRows);

		// Act
		CultureLookupResult absent = _catalog.Find("fi-FI");
		CultureLookupResult inactive = _catalog.Find("es-es");

		// Assert
		absent.Found.Should().BeFalse(because: "fi-FI is not a SysCulture row of this environment");
		absent.Available.Should().HaveCount(3, because: "the caller needs the available cultures for its error message");
		inactive.Found.Should().BeTrue(because: "es-ES is a SysCulture row, only inactive");
		inactive.Culture.Name.Should().Be("es-ES", because: "the lookup canonicalizes to the SysCulture.Name spelling");
		inactive.Culture.Active.Should().BeFalse(because: "the row's Active flag is passed through");
	}

	[Test]
	[Description("TC-U-19: a failed SysCulture read surfaces as an exception carrying the server's message, never as an empty culture list.")]
	public void GetCultures_ShouldThrowWithServerMessage_WhenSelectQueryFails() {
		// Arrange
		StubSelectQuery("""{"success":false,"errorInfo":{"message":"Access denied to SysCulture"}}""");

		// Act
		Action act = () => _catalog.GetCultures();

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "an empty list would read as 'no cultures' and re-open the silent-drop path")
			.WithMessage("*Access denied to SysCulture*");
	}
}
