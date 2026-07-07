using System.Text.Json;
using System.Text.RegularExpressions;
using Clio.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

// Tests for the standalone, temporary json-differ key-order workaround (TODO(ENG-91251, ENG-92198)).
// Delete this fixture together with ChartConfigKeyOrderPreprocessor once the platform needFlatten fix ships.
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class ChartConfigKeyOrderPreprocessorTests {

	private static readonly IPageBodyPreprocessor Preprocessor = new ChartConfigKeyOrderPreprocessor();

	private const string Prefix = "define(\"UsrTest\", [], function() { return { viewConfigDiff: ";
	private const string Suffix = " }; });";

	private const string DoughnutSeries =
		"""{"type":"doughnut","label":"L","data":{"providing":{"schemaName":"Account","attribute":"A"}}}""";

	// config = { title, series, scales{..yAxis.name} } → last-key chain (scales → yAxis.name) crashes.
	private static readonly string ScalesLastWithCaptions =
		"""{"title":"T","series":[__S__],"scales":{"stacked":false,"xAxis":{"name":"Category"},"yAxis":{"name":"Revenue"}}}"""
			.Replace("__S__", DoughnutSeries);

	// config = { title, scales{..captions}, series } → series last → safe even WITH captions.
	private static readonly string SeriesLastWithCaptions =
		"""{"title":"T","scales":{"stacked":false,"xAxis":{"name":"Category"},"yAxis":{"name":"Revenue"}},"series":[__S__]}"""
			.Replace("__S__", DoughnutSeries);

	// config = { title, series, scales{empty axis names} } → scales last but no name in chain → safe.
	private static readonly string ScalesLastEmptyCaptions =
		"""{"title":"T","series":[__S__],"scales":{"stacked":false,"xAxis":{"name":""},"yAxis":{"name":""}}}"""
			.Replace("__S__", DoughnutSeries);

	private static string ChartEntry(string name, string configJson) =>
		"""{"operation":"insert","name":"__NAME__","parentName":"Main","propertyName":"items","index":0,"values":{"type":"crt.ChartWidget","config":__CONFIG__}}"""
			.Replace("__NAME__", name)
			.Replace("__CONFIG__", configJson);

	private static string WebBody(string vcdJson) =>
		Prefix + "/**SCHEMA_VIEW_CONFIG_DIFF*/" + vcdJson + "/**SCHEMA_VIEW_CONFIG_DIFF*/" + Suffix;

	private static string ExtractVcd(string body) =>
		Regex.Match(body, @"/\*\*SCHEMA_VIEW_CONFIG_DIFF\*/(?<c>[\s\S]*?)/\*\*SCHEMA_VIEW_CONFIG_DIFF\*/")
			.Groups["c"].Value;

	private static JsonElement ChartConfig(string body, string chartName) {
		using JsonDocument doc = JsonDocument.Parse(ExtractVcd(body));
		foreach (JsonElement entry in doc.RootElement.EnumerateArray()) {
			if (entry.TryGetProperty("name", out JsonElement n) && n.GetString() == chartName &&
			    entry.TryGetProperty("values", out JsonElement v) &&
			    v.TryGetProperty("config", out JsonElement config)) {
				return config.Clone();
			}
		}
		throw new AssertionException($"chart '{chartName}' not found in body");
	}

	private static string LastKey(JsonElement config) {
		string last = null;
		foreach (JsonProperty property in config.EnumerateObject()) {
			last = property.Name;
		}
		return last;
	}

	private static string AxisName(JsonElement config, string axis) =>
		config.GetProperty("scales").GetProperty(axis).GetProperty("name").GetString();

	[Test]
	[Description("A config whose last-key chain ends at a non-empty axis name is reordered so 'series' is last — the crash-safe order — while the axis captions are preserved verbatim.")]
	public void Preprocess_ConfigEndsAtNonEmptyAxisName_MovesSeriesLast_PreservingCaptions() {
		string body = WebBody("[" + ChartEntry("TestChart", ScalesLastWithCaptions) + "]");

		string result = Preprocessor.Preprocess(body);

		result.Should().NotBe(body, "the crash-shaped key order must be normalized");
		JsonElement config = ChartConfig(result, "TestChart");
		LastKey(config).Should().Be("series", "moving series last makes the differ leave config inline (no crash)");
		AxisName(config, "yAxis").Should().Be("Revenue", "only key order changes — the caption value is preserved");
		AxisName(config, "xAxis").Should().Be("Category", "the other caption is preserved too");
		result.Should().StartWith(Prefix).And.EndWith(Suffix, "only the viewConfigDiff section is rewritten");
	}

	[Test]
	[Description("When 'series' is already the last key of config, the body is returned unchanged even with non-empty axis captions (order is already crash-safe).")]
	public void Preprocess_SeriesAlreadyLast_ReturnsBodyUnchanged() {
		string body = WebBody("[" + ChartEntry("TestChart", SeriesLastWithCaptions) + "]");

		Preprocessor.Preprocess(body).Should().Be(body, "a crash-safe order needs no reordering");
	}

	[Test]
	[Description("Empty axis names never make needFlatten true, so a scales-last config with empty captions is left unchanged.")]
	public void Preprocess_EmptyAxisNames_ReturnsBodyUnchanged() {
		string body = WebBody("[" + ChartEntry("TestChart", ScalesLastEmptyCaptions) + "]");

		Preprocessor.Preprocess(body).Should().Be(body, "no non-empty name in the last-key chain → nothing to fix");
	}

	[Test]
	[Description("A body with no chart widget is returned unchanged.")]
	public void Preprocess_NoChartWidget_ReturnsBodyUnchanged() {
		string body = WebBody("""[{"operation":"insert","name":"Btn","values":{"type":"crt.Button","caption":"Hi"}}]""");

		Preprocessor.Preprocess(body).Should().Be(body);
	}

	[Test]
	[Description("A body without the viewConfigDiff AMD markers (e.g. a mobile JSON body) is returned unchanged — the fix is web-only.")]
	public void Preprocess_NoMarkers_ReturnsBodyUnchanged() {
		string body =
			"""{"viewConfigDiff":[{"type":"crt.ChartWidget","config":{"series":[],"scales":{"yAxis":{"name":"X"}}}}]}""";

		Preprocessor.Preprocess(body).Should().Be(body);
	}

	[Test]
	[Description("Fail-safe: a malformed (unparseable) viewConfigDiff section never throws and returns the body unchanged.")]
	public void Preprocess_MalformedSection_ReturnsBodyUnchanged() {
		string body = WebBody("[ this is not valid json ]");

		Preprocessor.Preprocess(body).Should().Be(body);
	}

	[Test]
	[Description("Idempotent: re-running the preprocessor on an already-normalized body is a no-op.")]
	public void Preprocess_IsIdempotent() {
		string body = WebBody("[" + ChartEntry("TestChart", ScalesLastWithCaptions) + "]");

		string once = Preprocessor.Preprocess(body);
		Preprocessor.Preprocess(once).Should().Be(once, "the second pass finds nothing left to reorder");
	}

	[Test]
	[Description("In a multi-chart body only the crash-shaped chart is reordered; both configs end crash-safe (series last) and the already-safe chart's captions are preserved.")]
	public void Preprocess_MultipleCharts_ReordersOnlyTheCrashShapedOne() {
		string vcd = "[" +
			ChartEntry("BadChart", ScalesLastWithCaptions) + "," +
			ChartEntry("SafeChart", SeriesLastWithCaptions) + "]";
		string body = WebBody(vcd);

		string result = Preprocessor.Preprocess(body);

		LastKey(ChartConfig(result, "BadChart")).Should().Be("series", "the crash-shaped chart is reordered");
		LastKey(ChartConfig(result, "SafeChart")).Should().Be("series", "the already-safe chart stays series-last");
		AxisName(ChartConfig(result, "SafeChart"), "yAxis").Should().Be("Revenue", "captions are preserved");
	}

	[Test]
	[Description("Hardening: the key moved to the end must have NO 'name' slot at all, not merely an empty one — an empty 'name' could be filled later and re-introduce the crash. With no 'series', the primitive 'title' is chosen over a present-but-empty-name object that comes first in the config.")]
	public void Preprocess_PrefersNameFreeKey_OverEmptyNameObject() {
		// No 'series'. 'emptyNamed' has an empty (fragile) name and comes first; 'title' is a primitive with no
		// name slot; the crash-shaped last key is 'badScales' (a filled yAxis.name).
		string config =
			"""{"emptyNamed":{"name":""},"title":"T","badScales":{"stacked":false,"yAxis":{"name":"Revenue"}}}""";
		string body = WebBody("[" + ChartEntry("TestChart", config) + "]");

		string result = Preprocessor.Preprocess(body);

		LastKey(ChartConfig(result, "TestChart")).Should().Be("title",
			"the moved key must be name-free (the primitive 'title'), never the fragile empty-name object");
	}
}
