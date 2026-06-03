using System;
using System.Collections.Generic;
using System.Text.Json;
using Clio.Command.BusinessRules.Filters.Schema;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.BusinessRules;

[TestFixture]
[Property("Module", "Command")]
public sealed class LookupValueResolverTests {

	private const string SelectUrl = "https://creatio/0/DataService/json/SyncReply/SelectQuery";

	[Test]
	[Category("Unit")]
	[Description("TryResolveDisplayNameById filters by Id using the Guid data value type (0), not Text (1) — regression for ENG-88588 missing Name/displayValue.")]
	public void TryResolveDisplayNameById_Should_Filter_Id_As_Guid() {
		Guid id = Guid.Parse("5b0c7aed-d0d8-4862-8daa-70c17c04bda4");
		IFilterSchemaProvider schema = Substitute.For<IFilterSchemaProvider>();
		schema.GetPrimaryDisplayColumnName("LeadMedium").Returns("Name");
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>()).Returns(SelectUrl);
		string? capturedBody = null;
		client.ExecutePostRequest(SelectUrl, Arg.Do<string>(b => capturedBody = b),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success":true,"rows":[{"Display":"Partner referral"}]}""");

		LookupValueResolver resolver = new(schema, client, urlBuilder);
		bool ok = resolver.TryResolveDisplayNameById("LeadMedium", id, out string? displayName);

		ok.Should().BeTrue();
		displayName.Should().Be("Partner referral");
		capturedBody.Should().NotBeNull();
		JsonElement filter0 = JsonDocument.Parse(capturedBody!).RootElement
			.GetProperty("filters").GetProperty("items").GetProperty("filter0");
		filter0.GetProperty("leftExpression").GetProperty("columnPath").GetString().Should().Be("Id");
		filter0.GetProperty("rightExpression").GetProperty("parameter")
			.GetProperty("dataValueType").GetInt32().Should()
			.Be(SelectQueryHelperGuidDataValueType,
				because: "Id is a Guid column; filtering it as Text returns 0 rows and Name/displayValue are dropped from the envelope.");
		filter0.GetProperty("rightExpression").GetProperty("parameter")
			.GetProperty("value").GetString().Should().Be(id.ToString("D"));
	}

	[Test]
	[Category("Unit")]
	[Description("Cache returns the previously resolved display name without re-querying the server.")]
	public void TryResolveDisplayNameById_Should_Cache_Result() {
		Guid id = Guid.NewGuid();
		IFilterSchemaProvider schema = Substitute.For<IFilterSchemaProvider>();
		schema.GetPrimaryDisplayColumnName("LeadMedium").Returns("Name");
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>()).Returns(SelectUrl);
		client.ExecutePostRequest(SelectUrl, Arg.Any<string>(),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success":true,"rows":[{"Display":"X"}]}""");

		LookupValueResolver resolver = new(schema, client, urlBuilder);
		resolver.TryResolveDisplayNameById("LeadMedium", id, out _).Should().BeTrue();
		resolver.TryResolveDisplayNameById("LeadMedium", id, out string? second).Should().BeTrue();

		second.Should().Be("X");
		client.Received(1).ExecutePostRequest(SelectUrl, Arg.Any<string>(),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("Returns false when the schema has no primary display column (e.g. junction table without a display field).")]
	public void TryResolveDisplayNameById_Should_Return_False_When_Primary_Display_Missing() {
		IFilterSchemaProvider schema = Substitute.For<IFilterSchemaProvider>();
		schema.GetPrimaryDisplayColumnName("X").Returns((string?)null);
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();

		LookupValueResolver resolver = new(schema, client, urlBuilder);
		bool ok = resolver.TryResolveDisplayNameById("X", Guid.NewGuid(), out string? displayName);

		ok.Should().BeFalse();
		displayName.Should().BeNull();
	}

	// Mirrors Clio.Package.SelectQueryHelper.GuidDataValueType (internal const).
	private const int SelectQueryHelperGuidDataValueType = 0;
}
