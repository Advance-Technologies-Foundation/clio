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
	[Description("Non-GUID string on a Lookup column is resolved via ILookupValueResolver and the resolved Guid lands in the InFilter parameter.")]
	public void Build_Should_Resolve_Display_Name_To_Guid_When_Resolver_Is_Wired() {
		Guid resolvedId = Guid.NewGuid();
		IFilterSchemaProvider provider = StubProvider("Lead", new[] {
			LookupColumn("Source", "LeadSource")
		});
		ILookupValueResolver resolver = Substitute.For<ILookupValueResolver>();
		resolver.Resolve("LeadSource", "Google", Arg.Any<string>()).Returns(resolvedId);
		LocalEsqFilterBuilder builder = new(provider, resolver);
		StaticFilterGroup filter = new("AND",
			[new StaticFilterLeaf("Source", "EQUAL", JsonElement("\"Google\""))],
			[]);

		string envelope = builder.ConvertToEsqFilter("Lead", filter);

		using JsonDocument doc = JsonDocument.Parse(envelope);
		JsonElement leaf = doc.RootElement.GetProperty("items").GetProperty("Filter_0");
		leaf.GetProperty("rightExpressions")[0]
			.GetProperty("parameter").GetProperty("value").GetProperty("value")
			.GetGuid().Should().Be(resolvedId);
		resolver.Received(1).Resolve("LeadSource", "Google", Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("Each non-GUID element of an array value is resolved separately so multi-value IN can accept a mix of GUIDs and display names.")]
	public void Build_Should_Resolve_Display_Names_Inside_Array() {
		Guid guidValue = Guid.NewGuid();
		Guid resolvedFacebook = Guid.NewGuid();
		IFilterSchemaProvider provider = StubProvider("Lead", new[] {
			LookupColumn("Source", "LeadSource")
		});
		ILookupValueResolver resolver = Substitute.For<ILookupValueResolver>();
		resolver.Resolve("LeadSource", "Facebook", Arg.Any<string>()).Returns(resolvedFacebook);
		LocalEsqFilterBuilder builder = new(provider, resolver);
		StaticFilterGroup filter = new("AND",
			[new StaticFilterLeaf("Source", "EQUAL",
				JsonElement($"[\"{guidValue}\",\"Facebook\"]"))],
			[]);

		string envelope = builder.ConvertToEsqFilter("Lead", filter);

		using JsonDocument doc = JsonDocument.Parse(envelope);
		JsonElement rightExpressions = doc.RootElement.GetProperty("items")
			.GetProperty("Filter_0").GetProperty("rightExpressions");
		rightExpressions[0].GetProperty("parameter").GetProperty("value").GetProperty("value")
			.GetGuid().Should().Be(guidValue);
		rightExpressions[1].GetProperty("parameter").GetProperty("value").GetProperty("value")
			.GetGuid().Should().Be(resolvedFacebook);
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
