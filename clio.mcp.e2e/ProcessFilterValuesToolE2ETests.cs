using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Common;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for ENG-105557 over the real MCP path: several records of one lookup column are ONE filter
/// condition (<c>values</c>), built the way the process designer stores its multi-select and read back whole by
/// describe, on a signal start, Read data and Modify data alike. NOT in CI - run manually against a stand carrying
/// CrtProcessBuilder 1.6.6.162 or newer (the cut that reads <c>values</c>, and the create and modify floor). The
/// records are the platform's own Country rows (Algeria, Andorra, Armenia), named by display name; the run check
/// takes the two countries that hold the most contacts on the stand instead, so it counts real records.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(CreateBusinessProcessTool.CreateBusinessProcessToolName)]
[NonParallelizable]
[Category(McpE2ECategories.ProcessDesigner)]
public sealed class ProcessFilterValuesToolE2ETests {

	private const string CreateToolName = CreateBusinessProcessTool.CreateBusinessProcessToolName;
	private const string ModifyToolName = ModifyBusinessProcessTool.ModifyBusinessProcessToolName;

	/// <summary>The cut that reads <c>values</c>; named in the skip message.</summary>
	private const string MinimumPackageVersion = "1.6.6.162";

	private const string Subject = "Process filter values";

	#region Methods: Tests

	[Test]
	[Description("A signal start, a Read data and a Modify data each built with 'values' - equal on two of them, notEqual on one - save without a warning, and describe reads every condition back whole: the records in 'values', the comparison as sent, and a complete decode, so the raw filter is left out.")]
	[AllureTag(CreateToolName)]
	[AllureName("a value list builds as one condition and reads back whole")]
	public async Task CreateBusinessProcess_Should_BuildAValueListAsOneCondition_AndDescribeItWhole() {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync(Subject, MinimumPackageVersion);
		string processName = $"UsrClioBpValues{Guid.NewGuid():N}";

		// Act
		CallToolResult buildCall = await ProcessDesignerE2EArrange.CallToolAsync(context, CreateToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["descriptor"] = ValuesDescriptor(processName)
			});
		string built = ToolText(buildCall);
		JsonObject graph = DescribedProcessGraph.Read(await ProcessDesignerE2EArrange.DescribeAsync(context, processName));

		// Assert
		built.Should().Contain("created (UId:", because: "a value list is a valid filter condition: {0}", built);
		Warnings(buildCall).Should().BeEmpty(because: "nothing in this descriptor warns: {0}", built);
		AssertOneValueListCondition(graph, "CountryChanged", "equal", 2, storesRawFilter: false);
		AssertOneValueListCondition(graph, "ReadFromCountries", "equal", 3, storesRawFilter: true);
		AssertOneValueListCondition(graph, "ReadOutsideCountries", "notEqual", 2, storesRawFilter: true);
		AssertOneValueListCondition(graph, "TagFromCountries", "equal", 2, storesRawFilter: true);
	}

	[Test]
	[Description("The describe -> setFilter loop keeps every record: the filter describe returns for a Read data with 'values' is sent back through setFilter as read, and the second describe reports the same records - the loop that silently kept one record of several before CrtProcessBuilder 1.6.6.162.")]
	[AllureTag(ModifyToolName)]
	[AllureName("describe -> setFilter keeps every record of a value list")]
	public async Task DescribeThenSetFilter_Should_KeepEveryRecordOfTheValueList() {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync(Subject, MinimumPackageVersion);
		string processName = $"UsrClioBpValuesRt{Guid.NewGuid():N}";
		string built = ToolText(await ProcessDesignerE2EArrange.CallToolAsync(context, CreateToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["descriptor"] = ValuesDescriptor(processName)
			}));
		built.Should().Contain("created (UId:", because: "the arrange step must have built the process: {0}", built);
		JsonObject before = FilterOf(DescribedProcessGraph.Read(
			await ProcessDesignerE2EArrange.DescribeAsync(context, processName)), "ReadFromCountries");
		Values(before).Should().HaveCount(3,
			because: "describe must return the three built records before they are re-sent, or the loop compares two "
				+ "empty lists: {0}", before.ToJsonString());
		JsonObject resent = JsonNode.Parse(before.ToJsonString())!.AsObject();
		foreach (JsonObject condition in resent["conditions"]!.AsArray().OfType<JsonObject>()) {
			condition.Remove("displayValue");
		}

		// Act
		string modified = ToolText(await ProcessDesignerE2EArrange.CallToolAsync(context, ModifyToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName,
				["operations"] = JsonSerializer.Serialize(new object[] {
					new Dictionary<string, object?> {
						["op"] = "setFilter", ["elementName"] = "ReadFromCountries", ["filter"] = resent
					}
				})
			}));
		JsonObject after = FilterOf(DescribedProcessGraph.Read(
			await ProcessDesignerE2EArrange.DescribeAsync(context, processName)), "ReadFromCountries");

		// Assert
		modified.Should().Contain("edited (", because: "re-sending the described filter is a valid edit: {0}", modified);
		Values(after).Should().Equal(Values(before),
			because: "every record survives the describe -> setFilter loop, in order: {0}", after.ToJsonString());
	}

	[Test]
	[Description("Two equal conditions on Country joined by AND, each naming a different record, still build - the warning never refuses - but the answer says the group never matches and names the element and the fix ('values'): the shape agents wrote for 'one of several' before this change.")]
	[AllureTag(CreateToolName)]
	[AllureName("an AND group that can never match is warned about")]
	public async Task CreateBusinessProcess_Should_WarnAboutAnAndGroupThatNeverMatches() {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync(Subject, MinimumPackageVersion);
		string processName = $"UsrClioBpValuesAnd{Guid.NewGuid():N}";
		Dictionary<string, object> contradictory = Filter("and",
			Condition("Country", "equal", value: "Algeria"), Condition("Country", "equal", value: "Andorra"));

		// Act
		CallToolResult buildCall = await ProcessDesignerE2EArrange.CallToolAsync(context, CreateToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["descriptor"] = SingleReadDescriptor(processName, contradictory)
			});
		string built = ToolText(buildCall);

		// Assert
		built.Should().Contain("created (UId:", because: "the warning never refuses the build: {0}", built);
		Warnings(buildCall).Should().Contain(warning => warning.Contains("'ReadContacts'")
				&& warning.Contains("'Country'") && warning.Contains("'values'"),
			because: "the answer names the element, the column and the fix: {0}", built);
	}

	[Test]
	[Description("A list in a condition's 'value' - the form agents reach for first - is refused by clio BEFORE anything is sent, pointing to 'values', and no process is created; before this change the package could not read such a request and clio reported only a response it could not read.")]
	[AllureTag(CreateToolName)]
	[AllureName("a list in value is refused before sending")]
	public async Task CreateBusinessProcess_Should_RefuseAListInValue_BeforeSending() {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync(Subject, MinimumPackageVersion);
		string processName = $"UsrClioBpValuesList{Guid.NewGuid():N}";
		Dictionary<string, object> listInValue = Filter("and",
			Condition("Country", "equal", value: new[] { "Algeria", "Andorra" }));

		// Act
		string built = ToolText(await ProcessDesignerE2EArrange.CallToolAsync(context, CreateToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["descriptor"] = SingleReadDescriptor(processName, listInValue)
			}));

		// Assert
		built.Should().Contain("Nothing was sent", because: "clio refuses the shape before the server sees it: {0}", built);
		built.Should().Contain("values", because: "the refusal points to the value list: {0}", built);
		built.Should().NotContain("created (UId:", because: "a refused descriptor creates nothing: {0}", built);
	}

	[Test]
	[Description("Done when, on a stand: a process whose two Read data elements count the contacts in, and outside, the two countries holding the most contacts - each built with 'values', equal and notEqual - returns from a run exactly what the platform's own query counts with an OR of equalities and an AND of inequalities on the same lookup column. The countries are read from the stand, so the count of the records in them is never zero by accident.")]
	[AllureTag(RunProcessTool.ToolName)]
	[AllureName("a value list selects what any-of and none-of select")]
	public async Task RunProcess_Should_CountExactlyTheRecordsTheValueListSelects() {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync(Subject, MinimumPackageVersion);
		IReadOnlyList<(string Id, string Name)> countries = await CountriesHoldingContactsAsync(context);
		if (countries.Count < 2) {
			Assert.Ignore($"This check needs contacts in at least two countries on '{context.EnvironmentName}'; "
				+ $"it found {countries.Count}.");
		}
		string processName = $"UsrClioBpValuesRun{Guid.NewGuid():N}";
		string built = ToolText(await ProcessDesignerE2EArrange.CallToolAsync(context, CreateToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["descriptor"] = CountingDescriptor(processName, countries.Select(country => country.Name).ToArray())
			}));
		built.Should().Contain("created (UId:", because: "the arrange step must have built the process: {0}", built);
		int expectedFrom = await CountContactsAsync(context, CountryIdFilter(countries, notEqual: false));
		int expectedOutside = await CountContactsAsync(context, CountryIdFilter(countries, notEqual: true));

		// Act
		RunProcessEnvelope run = EntitySchemaStructuredResultParser.Extract<RunProcessEnvelope>(
			await ProcessDesignerE2EArrange.CallToolAsync(context, RunProcessTool.ToolName,
				new Dictionary<string, object?> {
					["environment-name"] = context.EnvironmentName,
					["process-name"] = processName,
					["result-parameters"] = new[] { "PCountFrom", "PCountOutside" }
				}));

		// Assert
		run.ResultParameterValues.Should().NotBeNull(because: "the run must end and report its counts: {0}", run.Error);
		Convert.ToInt32(run.ResultParameterValues!["PCountFrom"].ToString()).Should().Be(expectedFrom,
			because: "equal over the two countries selects every contact in either of them, as an OR does");
		Convert.ToInt32(run.ResultParameterValues!["PCountOutside"].ToString()).Should().Be(expectedOutside,
			because: "notEqual over the two selects every contact in neither of them, as an AND of inequalities does");
	}

	#endregion

	#region Methods: Private

	/// <summary>
	/// The two countries holding the most contacts on the stand, read through execute-esq, so the count of the
	/// contacts in them selects real records whatever the stand's data is.
	/// </summary>
	private static async Task<IReadOnlyList<(string Id, string Name)>> CountriesHoldingContactsAsync(
			ProcessDesignerArrangeContext context) {
		JsonElement rows = await QueryContactsAsync(context, new Dictionary<string, object> {
			["CountryId"] = ColumnPath("Country.Id"),
			["CountryName"] = ColumnPath("Country.Name"),
			["Cnt"] = CountColumn(descending: true)
		}, new Dictionary<string, object> {
			["filterType"] = 6, ["logicalOperation"] = 0, ["isEnabled"] = true,
			["items"] = new Dictionary<string, object> {
				["filled"] = new Dictionary<string, object> {
					["filterType"] = 2, ["comparisonType"] = 2, ["isNull"] = false, ["isEnabled"] = true,
					["leftExpression"] = new Dictionary<string, object> {
						["expressionType"] = 0, ["columnPath"] = "Country"
					}
				}
			}
		});
		return rows.EnumerateArray().Take(2)
			.Select(row => (row.GetProperty("CountryId").GetString()!, row.GetProperty("CountryName").GetString()!))
			.ToList();
	}

	/// <summary>Counts contacts through execute-esq, the platform's own query path, independent of the builder.</summary>
	private static async Task<int> CountContactsAsync(ProcessDesignerArrangeContext context,
			Dictionary<string, object> filters) {
		JsonElement rows = await QueryContactsAsync(context,
			new Dictionary<string, object> { ["Cnt"] = CountColumn(descending: false) }, filters);
		return rows[0].GetProperty("Cnt").GetInt32();
	}

	private static async Task<JsonElement> QueryContactsAsync(ProcessDesignerArrangeContext context,
			Dictionary<string, object> columns, Dictionary<string, object> filters) {
		ExecuteEsqResponse response = EntitySchemaStructuredResultParser.Extract<ExecuteEsqResponse>(
			await ProcessDesignerE2EArrange.CallToolAsync(context, ExecuteEsqTool.ToolName,
				new Dictionary<string, object?> {
					["environment-name"] = context.EnvironmentName,
					["query"] = new Dictionary<string, object> {
						["rootSchemaName"] = "Contact", ["operationType"] = 0, ["allColumns"] = false,
						["columns"] = new Dictionary<string, object> { ["items"] = columns },
						["filters"] = filters
					}
				}));
		response.Success.Should().BeTrue(because: "the reference query must run: {0}", response.Error);
		return response.Rows!.Value;
	}

	private static Dictionary<string, object> ColumnPath(string path) =>
		new() { ["expression"] = new Dictionary<string, object> { ["expressionType"] = 0, ["columnPath"] = path } };

	private static Dictionary<string, object> CountColumn(bool descending) {
		Dictionary<string, object> column = new() {
			["expression"] = new Dictionary<string, object> {
				["expressionType"] = 1, ["functionType"] = 2, ["aggregationType"] = 1, ["aggregationEvalType"] = 2,
				["functionArgument"] = new Dictionary<string, object> { ["expressionType"] = 0, ["columnPath"] = "Id" }
			}
		};
		if (descending) {
			column["orderDirection"] = 2;
			column["orderPosition"] = 0;
		}
		return column;
	}

	/// <summary>
	/// Any of the countries (an OR of equalities) or none of them (an AND of inequalities), compared on the lookup
	/// column itself, by id, as the process compares it. Measured on a stand: the platform's notEqual on a lookup
	/// column also selects the records whose column is EMPTY, so a contact with no country is "in none of them",
	/// while a notEqual on the joined Country.Name leaves that contact out. The reference therefore never goes
	/// through the name.
	/// </summary>
	private static Dictionary<string, object> CountryIdFilter(IReadOnlyList<(string Id, string Name)> countries,
			bool notEqual) =>
		new() {
			["filterType"] = 6, ["logicalOperation"] = notEqual ? 0 : 1, ["isEnabled"] = true,
			["items"] = countries.ToDictionary(country => country.Id, country => (object)new Dictionary<string, object> {
				["filterType"] = 1, ["comparisonType"] = notEqual ? 4 : 3, ["isEnabled"] = true,
				["leftExpression"] = new Dictionary<string, object> { ["expressionType"] = 0, ["columnPath"] = "Country" },
				["rightExpression"] = new Dictionary<string, object> {
					["expressionType"] = 2,
					["parameter"] = new Dictionary<string, object> { ["dataValueType"] = 10, ["value"] = country.Id }
				}
			})
		};

	private static string CountingDescriptor(string processName, string[] countryNames) =>
		JsonSerializer.Serialize(new Dictionary<string, object> {
			["name"] = processName, ["caption"] = "Clio BP Filter Values Run E2E", ["packageName"] = "Custom",
			["parameters"] = new object[] {
				new Dictionary<string, object> { ["name"] = "PCountFrom", ["type"] = "Integer", ["direction"] = "Out" },
				new Dictionary<string, object> { ["name"] = "PCountOutside", ["type"] = "Integer", ["direction"] = "Out" }
			},
			["elements"] = new object[] {
				new Dictionary<string, object> { ["name"] = "StartCount", ["type"] = "startEvent", ["caption"] = "Start" },
				CountRead("CountFromCountries", Filter("and",
					Condition("Country", "equal", values: countryNames))),
				CountRead("CountOutsideCountries", Filter("and",
					Condition("Country", "notEqual", values: countryNames))),
				new Dictionary<string, object> { ["name"] = "EndCount", ["type"] = "endEvent", ["caption"] = "Counted" }
			},
			["flows"] = new[] {
				Flow("StartCount", "CountFromCountries"), Flow("CountFromCountries", "CountOutsideCountries"),
				Flow("CountOutsideCountries", "EndCount")
			},
			["mappings"] = new object[] {
				new Dictionary<string, object> {
					["targetProcessParameter"] = "PCountFrom", ["sourceElement"] = "CountFromCountries",
					["sourceElementParameter"] = "ResultCount"
				},
				new Dictionary<string, object> {
					["targetProcessParameter"] = "PCountOutside", ["sourceElement"] = "CountOutsideCountries",
					["sourceElementParameter"] = "ResultCount"
				}
			}
		});

	private static string ToolText(CallToolResult callResult) =>
		string.Join(" ", callResult.Content.OfType<TextContentBlock>().Select(block => block.Text));

	private static IEnumerable<string> Warnings(CallToolResult callResult) =>
		(McpCommandExecutionParser.Extract(callResult).Output ?? [])
			.Where(message => message.MessageType == LogDecoratorType.Warning)
			.Select(message => message.Value);

	private static JsonObject ElementOf(JsonObject graph, string elementName) {
		JsonObject? element = graph["elements"]!.AsArray().OfType<JsonObject>()
			.FirstOrDefault(candidate => candidate["name"]?.GetValue<string>() == elementName);
		element.Should().NotBeNull(because: "the element must be described: {0}", graph.ToJsonString());
		return element!;
	}

	private static JsonObject FilterOf(JsonObject graph, string elementName) {
		JsonObject? filter = ElementOf(graph, elementName)["filter"] as JsonObject;
		filter.Should().NotBeNull(because: "the element must carry its decoded filter: {0}", graph.ToJsonString());
		return filter!;
	}

	private static List<string> Values(JsonObject filter) =>
		filter["conditions"]!.AsArray().OfType<JsonObject>().Single()["values"]?.AsArray()
			.Select(entry => entry!.GetValue<string>()).ToList() ?? [];

	private static void AssertOneValueListCondition(JsonObject graph, string elementName, string comparison,
			int records, bool storesRawFilter) {
		JsonObject element = ElementOf(graph, elementName);
		JsonObject filter = FilterOf(graph, elementName);
		JsonObject condition = filter["conditions"]!.AsArray().OfType<JsonObject>()
			.Single(candidate => candidate["column"]?.GetValue<string>() == "Country");
		condition["comparison"]!.GetValue<string>().Should().Be(comparison,
			because: $"'{elementName}' keeps the comparison it was built with: {element.ToJsonString()}");
		condition["values"]!.AsArray().Should().HaveCount(records,
			because: $"'{elementName}' reads back every record as one condition: {element.ToJsonString()}");
		condition["value"].Should().BeNull(
			because: $"'{elementName}' reads back one right-hand source, the list: {element.ToJsonString()}");
		element["filterDecodedCompletely"]!.GetValue<bool>().Should().BeTrue(
			because: $"a value list is read whole, so nothing is left out: {element.ToJsonString()}");
		if (!storesRawFilter) {
			return;
		}
		JsonObject? raw = element["parameters"]?.AsArray().OfType<JsonObject>()
			.SingleOrDefault(parameter => parameter["name"]?.GetValue<string>() == "DataSourceFilters");
		raw.Should().NotBeNull(
			because: $"'{elementName}' stores its filter in DataSourceFilters: {element.ToJsonString()}");
		raw!["value"].Should().BeNull(
			because: $"a completely decoded filter leaves the raw value out: {element.ToJsonString()}");
		raw["valueOmitted"]!.GetValue<string>().Should().Be("decoded into the element's filter",
			because: $"the left-out raw value says where its content went: {element.ToJsonString()}");
	}

	private static Dictionary<string, object> Condition(string column, string comparison, object? value = null,
			string[]? values = null) {
		Dictionary<string, object> condition = new() { ["column"] = column, ["comparison"] = comparison };
		if (value is not null) {
			condition["value"] = value;
		}
		if (values is not null) {
			condition["values"] = values;
		}
		return condition;
	}

	private static Dictionary<string, object> Filter(string logical, params Dictionary<string, object>[] conditions) =>
		new() { ["object"] = "Contact", ["logicalOperation"] = logical, ["conditions"] = conditions };

	private static Dictionary<string, object> CountRead(string name, Dictionary<string, object> filter) =>
		new() {
			["name"] = name, ["type"] = "readData", ["caption"] = name,
			["readData"] = new Dictionary<string, object> { ["source"] = "Contact", ["mode"] = "count" },
			["filter"] = filter
		};

	private static string ValuesDescriptor(string processName) =>
		JsonSerializer.Serialize(new Dictionary<string, object> {
			["name"] = processName, ["caption"] = "Clio BP Filter Values E2E", ["packageName"] = "Custom",
			["elements"] = new object[] {
				new Dictionary<string, object> {
					["name"] = "CountryChanged", ["type"] = "signalStart", ["caption"] = "Contact country changed",
					["signal"] = new Dictionary<string, object> {
						["entity"] = "Contact", ["on"] = "modified", ["changedColumns"] = new[] { "Country" }
					},
					["filter"] = Filter("and",
						Condition("Country", "equal", values: new[] { "Algeria", "Andorra" }))
				},
				CountRead("ReadFromCountries", Filter("and",
					Condition("Country", "in", values: new[] { "Algeria", "Andorra", "Armenia" }))),
				CountRead("ReadOutsideCountries", Filter("and",
					Condition("Country", "notEqual", values: new[] { "Algeria", "Andorra" }))),
				new Dictionary<string, object> {
					["name"] = "TagFromCountries", ["type"] = "changeData", ["caption"] = "Tag contacts from countries",
					["changeData"] = new Dictionary<string, object> {
						["source"] = "Contact",
						["values"] = new[] { new Dictionary<string, object> { ["column"] = "JobTitle", ["value"] = "Tagged" } }
					},
					// The Name condition keeps the element away from real contacts should the process ever run.
					["filter"] = Filter("and",
						Condition("Country", "equal", values: new[] { "Algeria", "Andorra" }),
						Condition("Name", "equal", value: $"{processName} nobody"))
				},
				new Dictionary<string, object> { ["name"] = "EndValues", ["type"] = "endEvent", ["caption"] = "Values checked" }
			},
			["flows"] = new[] {
				Flow("CountryChanged", "ReadFromCountries"), Flow("ReadFromCountries", "ReadOutsideCountries"),
				Flow("ReadOutsideCountries", "TagFromCountries"), Flow("TagFromCountries", "EndValues")
			}
		});

	private static string SingleReadDescriptor(string processName, Dictionary<string, object> filter) =>
		JsonSerializer.Serialize(new Dictionary<string, object> {
			["name"] = processName, ["caption"] = "Clio BP Filter Values E2E", ["packageName"] = "Custom",
			["elements"] = new object[] {
				new Dictionary<string, object> { ["name"] = "StartValues", ["type"] = "startEvent", ["caption"] = "Start" },
				CountRead("ReadContacts", filter),
				new Dictionary<string, object> { ["name"] = "EndValues", ["type"] = "endEvent", ["caption"] = "Values checked" }
			},
			["flows"] = new[] { Flow("StartValues", "ReadContacts"), Flow("ReadContacts", "EndValues") }
		});

	private static Dictionary<string, object> Flow(string source, string target) =>
		new() { ["source"] = source, ["target"] = target };

	#endregion
}
