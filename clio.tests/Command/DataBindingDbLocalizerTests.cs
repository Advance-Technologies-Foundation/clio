using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Command.Localization;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// ENG-105291: checks and writes of lookup value translations in <c>create-data-binding-db</c>.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
internal sealed class DataBindingDbLocalizerTests {
	private const string SchemaName = "UsrRepairStatus";
	private const string RowId = "5f1c2f5a-6a4f-4c1e-9d1b-2b3c4d5e6f70";
	private static readonly Guid PackageUId = Guid.Parse("1d07fd0e-2ca4-4d20-93b4-eb5a795ea03f");

	private IApplicationClient _client = null!;
	private ICreatioCultureCatalog _cultureCatalog = null!;
	private IRemoteEntitySchemaDesignerClient _designerClient = null!;
	private IOwnedCurrentUserCultureResolver _cultureResolver = null!;
	private DataBindingDbLocalizer _sut = null!;
	private readonly List<(string Url, string Body)> _posts = [];

	private static readonly IReadOnlyList<DataBindingSchemaColumn> RuntimeColumns = [
		new(Guid.NewGuid(), "Id", 0, null),
		new(Guid.NewGuid(), "Name", 28, null),
		new(Guid.NewGuid(), "Description", 28, null),
		new(Guid.NewGuid(), "UsrCode", 27, null)
	];

	[SetUp]
	public void SetUp() {
		_posts.Clear();
		_client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>())
			.Returns(callInfo => $"route:{callInfo.ArgAt<ServiceUrlBuilder.KnownRoute>(0)}");
		_cultureCatalog = Substitute.For<ICreatioCultureCatalog>();
		_cultureCatalog.GetCultures().Returns([
			new CreatioCulture("en-US", true),
			new CreatioCulture("uk-UA", true),
			new CreatioCulture("de-DE", false)
		]);
		_designerClient = Substitute.For<IRemoteEntitySchemaDesignerClient>();
		_designerClient.GetSchemaDesignItem(Arg.Any<GetSchemaDesignItemRequestDto>(), Arg.Any<RemoteCommandOptions>())
			.Returns(new Clio.Command.EntitySchemaDesigner.DesignerResponse<EntityDesignSchemaDto> {
				Schema = new EntityDesignSchemaDto {
					Columns = [new EntitySchemaColumnDto { Name = "UsrCode", LocalizableText = false }],
					InheritedColumns = [
						new EntitySchemaColumnDto { Name = "Name", LocalizableText = true },
						new EntitySchemaColumnDto { Name = "Description", LocalizableText = true }
					]
				}
			});
		_cultureResolver = Substitute.For<IOwnedCurrentUserCultureResolver>();
		_cultureResolver.ResolveAsync(Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(Clio.Command.EntitySchemaDesigner.CultureResolution.Resolved("en-US")));
		ICurrentUserCultureResolverFactory resolverFactory = Substitute.For<ICurrentUserCultureResolverFactory>();
		resolverFactory.Create(Arg.Any<EnvironmentSettings>()).Returns(_cultureResolver);
		_sut = new DataBindingDbLocalizer(_client, serviceUrlBuilder, _cultureCatalog, _designerClient,
			resolverFactory, new EnvironmentSettings { Uri = "https://example.invalid" });
	}

	[TearDown]
	public void TearDown() {
		_cultureResolver.Dispose();
	}

	[Test]
	[Description("Returns no plan and reads nothing from the site when no row asks for a translation, so a create without localizations costs no extra requests.")]
	public void Validate_Should_Return_Null_Without_Requests_When_No_Row_Is_Localized() {
		// Arrange
		DataBindingRowLocalization row = Row("New");

		// Act
		DataBindingLocalizationPlan? plan = _sut.Validate(SchemaName, PackageUId, RuntimeColumns, [row]);

		// Assert
		plan.Should().BeNull(because: "there is nothing to translate");
		_cultureCatalog.DidNotReceive().GetCultures();
		_designerClient.DidNotReceiveWithAnyArgs().GetSchemaDesignItem(default!, default!);
	}

	[Test]
	[Description("Returns the profile culture of the connected user when every culture is active, every column is localizable and every value fits.")]
	public void Validate_Should_Return_Profile_Culture_When_Request_Is_Valid() {
		// Arrange
		DataBindingRowLocalization row = Row("New", ("uk-UA", "Name", "Нове"), ("uk-UA", "Description", "Новий ремонт"));

		// Act
		DataBindingLocalizationPlan? plan = _sut.Validate(SchemaName, PackageUId, RuntimeColumns, [row]);

		// Assert
		plan!.ProfileCulture.Should().Be("en-US", because: "the write needs the culture a plain insert wrote to");
		_designerClient.Received(1).GetSchemaDesignItem(
			Arg.Is<GetSchemaDesignItemRequestDto>(request =>
				request.Name == SchemaName && request.PackageUId == PackageUId && request.UseFullHierarchy),
			Arg.Any<RemoteCommandOptions>());
	}

	[Test]
	[Description("Refuses a culture that is not active on the site and names it with the active cultures (ENG-105291 TC-04).")]
	public void Validate_Should_Refuse_Inactive_Culture() {
		// Arrange
		DataBindingRowLocalization row = Row("New", ("de-DE", "Name", "Neu"));

		// Act
		Action act = () => _sut.Validate(SchemaName, PackageUId, RuntimeColumns, [row]);

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "an inactive culture is never shown to a user")
			.WithMessage("*nothing was written*Culture 'de-DE' is not active on the site (active cultures: en-US, uk-UA)*");
	}

	[Test]
	[Description("Refuses a culture the site does not have at all.")]
	public void Validate_Should_Refuse_Unknown_Culture() {
		// Arrange
		DataBindingRowLocalization row = Row("New", ("xx-XX", "Name", "?"));

		// Act
		Action act = () => _sut.Validate(SchemaName, PackageUId, RuntimeColumns, [row]);

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "a culture missing from SysCulture cannot hold a value")
			.WithMessage("*Culture 'xx-XX' is not active*");
	}

	[Test]
	[Description("Refuses a column that is not localizable and names it (ENG-105291 TC-05).")]
	public void Validate_Should_Refuse_Non_Localizable_Column() {
		// Arrange
		DataBindingRowLocalization row = Row("New", ("uk-UA", "UsrCode", "N"));

		// Act
		Action act = () => _sut.Validate(SchemaName, PackageUId, RuntimeColumns, [row]);

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "the platform has no per-culture value for that column")
			.WithMessage($"*Column 'UsrCode' of '{SchemaName}' is not localizable*");
	}

	[Test]
	[Description("Refuses a column the schema does not have.")]
	public void Validate_Should_Refuse_Unknown_Column() {
		// Arrange
		DataBindingRowLocalization row = Row("New", ("uk-UA", "UsrTitle", "N"));

		// Act
		Action act = () => _sut.Validate(SchemaName, PackageUId, RuntimeColumns, [row]);

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "a typo must not pass silently")
			.WithMessage($"*Column 'UsrTitle' is not a column of '{SchemaName}'*");
	}

	[TestCase(27, 50)]
	[TestCase(28, 250)]
	[TestCase(30, 500)]
	[Description("Accepts a value at the column's length limit and refuses one character more, naming the row, the culture and the column (ENG-105291 TC-07).")]
	public void Validate_Should_Refuse_Value_Longer_Than_Column(int dataValueType, int limit) {
		// Arrange
		IReadOnlyList<DataBindingSchemaColumn> runtimeColumns = [new(Guid.NewGuid(), "Name", dataValueType, null)];
		DataBindingRowLocalization atLimit = Row("New", ("uk-UA", "Name", new string('я', limit)));
		DataBindingRowLocalization overLimit = Row("New", ("uk-UA", "Name", new string('я', limit + 1)));

		// Act
		Action acceptAtLimit = () => _sut.Validate(SchemaName, PackageUId, runtimeColumns, [atLimit]);
		Action refuseOverLimit = () => _sut.Validate(SchemaName, PackageUId, runtimeColumns, [overLimit]);

		// Assert
		acceptAtLimit.Should().NotThrow(because: "a value of exactly the column length fits");
		refuseOverLimit.Should().Throw<InvalidOperationException>(because: "the database would cut or reject the value")
			.WithMessage($"*Row 'New' [uk-UA] column 'Name': {limit + 1} characters, the column allows {limit}*");
	}

	[Test]
	[Description("Does not limit a column whose runtime type has no fixed length, such as code 26 (Mapping).")]
	public void Validate_Should_Not_Limit_Column_Without_Fixed_Length() {
		// Arrange
		IReadOnlyList<DataBindingSchemaColumn> runtimeColumns = [new(Guid.NewGuid(), "Name", 26, null)];
		DataBindingRowLocalization row = Row("New", ("uk-UA", "Name", new string('я', 501)));

		// Act
		Action act = () => _sut.Validate(SchemaName, PackageUId, runtimeColumns, [row]);

		// Assert
		act.Should().NotThrow(because: "26 is Mapping, not a text type with a 500-character limit");
	}

	[Test]
	[Description("Lists every failure in one message instead of stopping at the first one.")]
	public void Validate_Should_Report_All_Failures_Together() {
		// Arrange
		DataBindingRowLocalization row = Row("New", ("de-DE", "Name", "Neu"), ("uk-UA", "UsrCode", "N"));

		// Act
		Action act = () => _sut.Validate(SchemaName, PackageUId, RuntimeColumns, [row]);

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "the agent fixes the whole request in one round")
			.Which.Message.Should().Contain("de-DE", because: "the culture failure is reported")
			.And.Contain("UsrCode", because: "the column failure is reported too");
	}

	[Test]
	[Description("Writes the translations through UpdateLocalizationQuery as a culture map that also carries the record's profile-culture value, because a required column rejects a map without it.")]
	public void Write_Should_Send_Culture_Map_With_Profile_Culture_Value() {
		// Arrange
		SetupReads(profileName: "New", localizedName: "Нове");
		DataBindingRowLocalization row = Row("New", ("uk-UA", "Name", "Нове"));

		// Act
		IReadOnlyList<DataBindingLocalizationWrite> result =
			_sut.Write(new DataBindingLocalizationPlan("en-US"), SchemaName, [(RowId, row)]);

		// Assert
		(string _, string body) = _posts.Single(post => post.Url == "route:UpdateLocalizationQuery");
		JsonNode request = JsonNode.Parse(body)!;
		request["rootSchemaName"]!.GetValue<string>().Should().Be(SchemaName, because: "the lookup itself is written");
		JsonNode parameter = request["columnValues"]!["items"]!["Name"]!["parameter"]!;
		parameter["dataValueType"]!.GetValue<int>().Should().Be(19, because: "19 is the localizable-string parameter");
		JsonNode cultures = JsonNode.Parse(parameter["value"]!.GetValue<string>())!;
		cultures["uk-UA"]!.GetValue<string>().Should().Be("Нове", because: "the requested translation is written");
		cultures["en-US"]!.GetValue<string>().Should().Be("New", because: "the profile-culture value is kept");
		request["columnValues"]!["items"]!.AsObject().Select(item => item.Key).Should().Equal(["Name"],
			because: "only the localized columns are written, the other columns of the record stay as they are");
		result.Should().ContainSingle(because: "one row was written in one culture")
			.Which.Should().BeEquivalentTo(new { RowId, RowLabel = "New", Culture = "uk-UA" },
				because: "the result names the record and the culture");
	}

	[Test]
	[Description("Maps a culture typed in another letter case to the site's spelling, so the write and the result use the name the site catalog has.")]
	public void Validate_Should_Map_Requested_Culture_To_Site_Spelling() {
		// Arrange
		DataBindingRowLocalization row = Row("New", ("uk-ua", "Name", "Нове"));

		// Act
		DataBindingLocalizationPlan? plan = _sut.Validate(SchemaName, PackageUId, RuntimeColumns, [row]);

		// Assert
		plan!.ToSiteCultureName("uk-ua").Should().Be("uk-UA", because: "the site catalog spells the culture uk-UA");
	}

	[Test]
	[Description("Sends and reports a culture under the site's spelling even when the request typed it in another letter case.")]
	public void Write_Should_Send_Site_Spelling_Of_Culture() {
		// Arrange
		SetupReads(profileName: "New", localizedName: "Нове");
		DataBindingRowLocalization row = Row("New", ("uk-ua", "Name", "Нове"));
		DataBindingLocalizationPlan plan = new("en-US",
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["uk-ua"] = "uk-UA" });

		// Act
		IReadOnlyList<DataBindingLocalizationWrite> result = _sut.Write(plan, SchemaName, [(RowId, row)]);

		// Assert
		(string _, string body) = _posts.Single(post => post.Url == "route:UpdateLocalizationQuery");
		JsonNode parameter = JsonNode.Parse(body)!["columnValues"]!["items"]!["Name"]!["parameter"]!;
		JsonNode.Parse(parameter["value"]!.GetValue<string>())!.AsObject().Select(culture => culture.Key)
			.Should().Contain("uk-UA", because: "the culture map carries the site's spelling")
			.And.NotContain("uk-ua", because: "the caller's spelling is not sent");
		result.Should().ContainSingle().Which.Culture.Should().Be("uk-UA",
			because: "the result lists the culture as the site names it");
	}

	[Test]
	[Description("Fails as saved but NOT verified when a culture read back differs from the request.")]
	public void Write_Should_Fail_When_Read_Back_Differs() {
		// Arrange
		SetupReads(profileName: "New", localizedName: "Інше");
		DataBindingRowLocalization row = Row("New", ("uk-UA", "Name", "Нове"));

		// Act
		Action act = () => _sut.Write(new DataBindingLocalizationPlan("en-US"), SchemaName, [(RowId, row)]);

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "a write that did not land must not report success")
			.WithMessage("*saved, but NOT verified*'New' Name [uk-UA]*");
	}

	[Test]
	[Description("Verifies a translation in the profile culture with a plain select, since that culture may have no localization row.")]
	public void Write_Should_Verify_Profile_Culture_With_Plain_Select() {
		// Arrange
		SetupReads(profileName: "Fresh", localizedName: null);
		DataBindingRowLocalization row = Row("New", ("en-US", "Name", "Fresh"));

		// Act
		Action act = () => _sut.Write(new DataBindingLocalizationPlan("en-US"), SchemaName, [(RowId, row)]);

		// Assert
		act.Should().NotThrow(because: "the profile-culture value read with a plain select matches the request");
	}

	[Test]
	[Description("Keeps the requested profile-culture value instead of overwriting it with the value the record already has.")]
	public void Write_Should_Not_Override_Requested_Profile_Culture_Value() {
		// Arrange
		SetupReads(profileName: "Fresh", localizedName: "Нове");
		DataBindingRowLocalization row = Row("New", ("en-US", "Name", "Fresh"), ("uk-UA", "Name", "Нове"));

		// Act
		_sut.Write(new DataBindingLocalizationPlan("en-US"), SchemaName, [(RowId, row)]);

		// Assert
		(string _, string body) = _posts.Single(post => post.Url == "route:UpdateLocalizationQuery");
		JsonNode cultures = JsonNode.Parse(JsonNode.Parse(body)!["columnValues"]!["items"]!["Name"]!["parameter"]!["value"]!
			.GetValue<string>())!;
		cultures["en-US"]!.GetValue<string>().Should().Be("Fresh",
			because: "a profile-culture value the caller asked for wins over the stored one");
	}

	[Test]
	[Description("Writes every row of the call and reports the mismatches of all rows together.")]
	public void Write_Should_Collect_Mismatches_Across_Rows() {
		// Arrange
		SetupReads(profileName: "New", localizedName: "Інше");
		DataBindingRowLocalization first = Row("New", ("uk-UA", "Name", "Нове"));
		DataBindingRowLocalization second = Row("Done", ("uk-UA", "Name", "Готово"));

		// Act
		Action act = () => _sut.Write(new DataBindingLocalizationPlan("en-US"), SchemaName,
			[(RowId, first), ("6f1c2f5a-6a4f-4c1e-9d1b-2b3c4d5e6f71", second)]);

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "neither value read back matches")
			.WithMessage("*'New' Name [uk-UA]*'Done' Name [uk-UA]*");
		_posts.Count(post => post.Url == "route:UpdateLocalizationQuery").Should().Be(2,
			because: "a mismatch on the first row does not stop the second row's write");
	}

	[Test]
	[Description("Reports the rows already written when a later row's write fails, so the caller knows which records carry translations (ENG-105291 AC-6).")]
	public void Write_Should_Report_Written_Rows_When_A_Later_Row_Fails() {
		// Arrange
		const string secondRowId = "6f1c2f5a-6a4f-4c1e-9d1b-2b3c4d5e6f71";
		SetupReads(profileName: "New", localizedName: "Нове", failingUpdateRowId: secondRowId);
		DataBindingRowLocalization first = Row("New", ("uk-UA", "Name", "Нове"));
		DataBindingRowLocalization second = Row("Done", ("uk-UA", "Name", "Готово"));

		// Act
		Action act = () => _sut.Write(new DataBindingLocalizationPlan("en-US"), SchemaName,
			[(RowId, first), (secondRowId, second)]);

		// Assert
		DataBindingLocalizationException failure = act.Should().Throw<DataBindingLocalizationException>(
				because: "the second row's write was refused by the server")
			.WithMessage("*'Done'*failed*").Which;
		failure.Written.Should().ContainSingle(because: "only the first row landed before the failure")
			.Which.Should().BeEquivalentTo(new { RowId, RowLabel = "New", Culture = "uk-UA" },
				because: "the failure names the record and culture already written");
		_posts.Count(post => post.Url == "route:UpdateLocalizationQuery").Should().Be(2,
			because: "both rows were attempted, the second one failed");
	}

	[Test]
	[Description("Sends no translation for a record that is not found and reports nothing as written.")]
	public void Write_Should_Not_Write_When_Record_Is_Not_Found() {
		// Arrange
		_client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>()).Returns(callInfo => {
			_posts.Add((callInfo.ArgAt<string>(0), callInfo.ArgAt<string>(1)));
			return """{"success":true,"rows":[]}""";
		});
		DataBindingRowLocalization row = Row("New", ("uk-UA", "Name", "Нове"));

		// Act
		Action act = () => _sut.Write(new DataBindingLocalizationPlan("en-US"), SchemaName, [(RowId, row)]);

		// Assert
		act.Should().Throw<DataBindingLocalizationException>(because: "a missing record cannot carry a translation")
			.WithMessage($"*Record '{RowId}' of '{SchemaName}' was not found*")
			.Which.Written.Should().BeEmpty(because: "nothing was written");
		_posts.Should().NotContain(post => post.Url == "route:UpdateLocalizationQuery",
			because: "no write is sent for a record that does not exist");
	}

	[Test]
	[Description("Refuses the localizations before any write when the connected user's culture cannot be read, because the profile-culture value could not be carried along.")]
	public void Validate_Should_Refuse_When_Profile_Culture_Is_Unknown() {
		// Arrange
		_cultureResolver.ResolveAsync(Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(Clio.Command.EntitySchemaDesigner.CultureResolution.Failed("endpoint unreachable")));
		DataBindingRowLocalization row = Row("New", ("uk-UA", "Name", "Нове"));

		// Act
		Action act = () => _sut.Validate(SchemaName, PackageUId, RuntimeColumns, [row]);

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "a required Name would otherwise lose its profile value")
			.WithMessage("*nothing was written*culture of the connected user could not be read (endpoint unreachable)*");
	}

	private void SetupReads(string profileName, string? localizedName, string? failingUpdateRowId = null) {
		_client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>()).Returns(callInfo => {
			string url = callInfo.ArgAt<string>(0);
			string body = callInfo.ArgAt<string>(1);
			_posts.Add((url, body));
			if (failingUpdateRowId is not null && url == "route:UpdateLocalizationQuery"
				&& body.Contains(failingUpdateRowId, StringComparison.OrdinalIgnoreCase)) {
				return """{"success":false,"errorInfo":{"message":"Update refused"}}""";
			}

			return url switch {
				"route:Select" => $$"""{"success":true,"rows":[{"Name":"{{profileName}}"}]}""",
				"route:SelectLocalizationQuery" => localizedName is null
					? """{"success":true,"rows":[]}"""
					: $$"""{"success":true,"rows":[{"CultureName":"uk-UA","Name":"{{localizedName}}"}]}""",
				_ => """{"success":true,"rowsAffected":1}"""
			};
		});
	}

	private static DataBindingRowLocalization Row(string label, params (string Culture, string Column, string Value)[] cells) =>
		new(label, cells
			.GroupBy(cell => cell.Culture)
			.ToDictionary(
				group => group.Key,
				group => (IReadOnlyDictionary<string, string>)group.ToDictionary(cell => cell.Column, cell => cell.Value)));
}
