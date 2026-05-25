using System;
using System.Collections.Generic;
using System.Text.Json;
using Clio.Command.BusinessRules.Filters;
using Clio.Command.BusinessRules.Filters.Esq;
using Clio.Command.EntitySchemaDesigner;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.BusinessRules.Filters.Esq;

[TestFixture]
[Property("Module", "Command.BusinessRules.Filters.Esq")]
public sealed class LocalEsqFilterBuilderDisplayNameTests {

	private const int LookupDataValueType = 10;

	[Test]
	[Category("Unit")]
	[Description("Non-GUID string on a Lookup column is resolved via ILookupValueResolver; the parameter carries the full Freedom UI shape {Name, Id, value, displayValue} so the rule editor renders the lookup by name.")]
	public void Build_Should_Resolve_Display_Name_To_Guid_When_Resolver_Is_Wired() {
		Guid resolvedId = Guid.NewGuid();
		IFilterSchemaProvider provider = StubProvider("Lead", new[] {
			LookupColumn("Source", "LeadSource")
		});
		ILookupValueResolver resolver = Substitute.For<ILookupValueResolver>();
		resolver.Resolve("LeadSource", "Google", Arg.Any<string>())
			.Returns(new LookupResolution(resolvedId, "Google"));
		LocalEsqFilterBuilder builder = new(provider, resolver);
		StaticFilterGroup filter = new("AND",
			[new StaticFilterLeaf("Source", "EQUAL", JsonElement("\"Google\""))],
			[]);

		string envelope = builder.ConvertToEsqFilter("Lead", filter);

		using JsonDocument doc = JsonDocument.Parse(envelope);
		JsonElement paramValue = doc.RootElement.GetProperty("items").GetProperty("Filter_0")
			.GetProperty("rightExpressions")[0]
			.GetProperty("parameter").GetProperty("value");
		paramValue.GetProperty("value").GetGuid().Should().Be(resolvedId);
		paramValue.GetProperty("Id").GetGuid().Should().Be(resolvedId,
			because: "Freedom UI lookup parameter expects Id and value to be the same GUID");
		paramValue.GetProperty("Name").GetString().Should().Be("Google",
			because: "display name resolved by ILookupValueResolver must round-trip into the Name field");
		paramValue.GetProperty("displayValue").GetString().Should().Be("Google",
			because: "Name and displayValue both carry the lookup record's primary display value");
		resolver.Received(1).Resolve("LeadSource", "Google", Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("Each non-GUID element of an array value is resolved separately so multi-value IN can accept a mix of GUIDs and display names; the display-name branch emits {Name, Id, value, displayValue}, the GUID branch tries TryResolveDisplayName to enrich the rendered UI.")]
	public void Build_Should_Resolve_Display_Names_Inside_Array() {
		Guid guidValue = Guid.NewGuid();
		Guid resolvedFacebook = Guid.NewGuid();
		IFilterSchemaProvider provider = StubProvider("Lead", new[] {
			LookupColumn("Source", "LeadSource")
		});
		ILookupValueResolver resolver = Substitute.For<ILookupValueResolver>();
		resolver.Resolve("LeadSource", "Facebook", Arg.Any<string>())
			.Returns(new LookupResolution(resolvedFacebook, "Facebook"));
		resolver.TryResolveDisplayName("LeadSource", guidValue).Returns("Web");
		LocalEsqFilterBuilder builder = new(provider, resolver);
		StaticFilterGroup filter = new("AND",
			[new StaticFilterLeaf("Source", "EQUAL",
				JsonElement($"[\"{guidValue}\",\"Facebook\"]"))],
			[]);

		string envelope = builder.ConvertToEsqFilter("Lead", filter);

		using JsonDocument doc = JsonDocument.Parse(envelope);
		JsonElement rightExpressions = doc.RootElement.GetProperty("items")
			.GetProperty("Filter_0").GetProperty("rightExpressions");
		JsonElement first = rightExpressions[0].GetProperty("parameter").GetProperty("value");
		first.GetProperty("value").GetGuid().Should().Be(guidValue);
		first.GetProperty("Name").GetString().Should().Be("Web",
			because: "GUID-input case enriches Name via TryResolveDisplayName so the rule UI still renders");
		JsonElement second = rightExpressions[1].GetProperty("parameter").GetProperty("value");
		second.GetProperty("value").GetGuid().Should().Be(resolvedFacebook);
		second.GetProperty("Name").GetString().Should().Be("Facebook");
	}

	[Test]
	[Category("Unit")]
	[Description("GUID input on a Lookup column with no resolver wired keeps only the GUID-bearing fields (no Name / displayValue) — filter still works at the SQL level even if the rule UI shows '<?>'.")]
	public void Build_Should_Omit_Name_And_DisplayValue_When_Resolver_Is_Missing_On_Guid_Input() {
		Guid guidValue = Guid.NewGuid();
		IFilterSchemaProvider provider = StubProvider("Lead", new[] {
			LookupColumn("Source", "LeadSource")
		});
		LocalEsqFilterBuilder builder = new(provider, lookupValueResolver: null);
		StaticFilterGroup filter = new("AND",
			[new StaticFilterLeaf("Source", "EQUAL", JsonElement($"\"{guidValue}\""))],
			[]);

		string envelope = builder.ConvertToEsqFilter("Lead", filter);

		using JsonDocument doc = JsonDocument.Parse(envelope);
		JsonElement paramValue = doc.RootElement.GetProperty("items").GetProperty("Filter_0")
			.GetProperty("rightExpressions")[0]
			.GetProperty("parameter").GetProperty("value");
		paramValue.GetProperty("value").GetGuid().Should().Be(guidValue);
		paramValue.GetProperty("Id").GetGuid().Should().Be(guidValue);
		paramValue.TryGetProperty("Name", out _).Should().BeFalse(
			because: "Name is JsonIgnore(WhenWritingNull) and no resolver was available to enrich the GUID input");
		paramValue.TryGetProperty("displayValue", out _).Should().BeFalse();
	}

	[Test]
	[Category("Unit")]
	[Description("Without a resolver wired, the converter rejects a non-GUID string with filter.lookup-value-not-guid pointing at the value field.")]
	public void Build_Should_Reject_Display_Name_When_Resolver_Is_Missing() {
		IFilterSchemaProvider provider = StubProvider("Lead", new[] {
			LookupColumn("Source", "LeadSource")
		});
		LocalEsqFilterBuilder builder = new(provider, lookupValueResolver: null);
		StaticFilterGroup filter = new("AND",
			[new StaticFilterLeaf("Source", "EQUAL", JsonElement("\"Google\""))],
			[]);

		Action act = () => builder.ConvertToEsqFilter("Lead", filter);

		act.Should().Throw<BusinessRuleFilterException>()
			.Where(ex => ex.ErrorCode == BusinessRuleFilterErrorCodes.LookupValueNotGuid)
			.WithMessage("*To pass the display name 'Google'*");
	}

	private static IFilterSchemaProvider StubProvider(string schemaName, IReadOnlyList<EntitySchemaColumnDto> columns) {
		IFilterSchemaProvider provider = Substitute.For<IFilterSchemaProvider>();
		Dictionary<string, EntitySchemaColumnDto> map = new(StringComparer.Ordinal);
		foreach (EntitySchemaColumnDto column in columns) {
			map[column.Name] = column;
		}
		provider.GetSchemaColumns(schemaName).Returns(map);
		return provider;
	}

	private static EntitySchemaColumnDto LookupColumn(string name, string referenceSchemaName) =>
		new() {
			Name = name,
			DataValueType = LookupDataValueType,
			ReferenceSchema = new EntityDesignSchemaDto { Name = referenceSchemaName }
		};

	private static JsonElement JsonElement(string raw) =>
		JsonDocument.Parse(raw).RootElement.Clone();
}
