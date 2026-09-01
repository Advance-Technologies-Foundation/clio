using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using DataBindingValueConverter = Clio.Command.DataBindingValueConverter;
using Clio.Command.ProcessModel;
using Clio.Common;
using ErrorOr;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.ProcessModel;

[TestFixture]
[Property("Module", "ProcessModel")]
[Category("Unit")]
[Category("ProcessSchemaParser")]

public class SchemaTestFixture{
	private readonly ILogger _logger = Substitute.For<ILogger>();
	private readonly List<string> _warnings = [];
	
	[SetUp]
	public void Setup() {
		_logger.When(f=> f.WriteWarning(Arg.Any<string>()))
			.Do(info => {
				
				info.ArgAt<string>(0).Should().NotBeNullOrWhiteSpace();
				string capturedWarningMessage  = info[0]?.ToString() ?? string.Empty;
				_warnings.Add(!string.IsNullOrWhiteSpace(capturedWarningMessage)
					? capturedWarningMessage
					: "Added empty message");
			});
	}
	
	private static Func<string, string> GetExampleFilePath => filename => Path.Join("Examples", "ProcessSchema", filename);

	[Test]
	[Description("Maps Creatio runtime dataValueType 18 to the native Color data type and keeps its native CLR type, so process signatures and codegen stay System.Drawing.Color.")]
	public void FromRuntimeValueType_Should_Map_Color_To_NativeColorDataType() {
		// Arrange
		const int colorRuntimeDataValueType = 18;

		// Act
		Guid dataValueTypeUId = DataValueTypeMap.FromRuntimeValueType(colorRuntimeDataValueType);

		// Assert
		dataValueTypeUId.Should().Be(Guid.Parse("dafb71f9-ee9f-4e0b-a4d7-37aa15987155"),
			because: "Creatio Color columns use the native Color data-value-type UId");
		DataValueTypeMap.Resolve(dataValueTypeUId).Should().Be(typeof(System.Drawing.Color),
			because: "the trunk defines ColorDataValueType.ValueType as System.Drawing.Color; mapping the "
				+ "UId to string here would emit Color process parameters as System.String, because this "
				+ "same map drives process-model and signature generation");
		DataValueTypeMap.IsColor(dataValueTypeUId).Should().BeTrue(
			because: "the hex-string wire format is asked for separately, by the data-binding path only");
	}

	[Test]
	[Description("A Color column is not string-like, so it cannot be admitted into a localization row - the trunk marks Color non-localizable.")]
	public void IsStringLike_Should_Reject_Color_So_It_Stays_Out_Of_Localization_Rows() {
		// Arrange
		Guid colorUId = DataValueTypeMap.FromRuntimeValueType(18);
		Guid shortTextUId = DataValueTypeMap.FromRuntimeValueType(1);
		DataBindingValueConverter converter = new(Substitute.For<IFileSystem>());

		// Assert
		converter.IsStringLike(colorUId).Should().BeFalse(
			because: "ColorDataValueType.IsLocalizableText is false in the trunk, and a Color column in a "
				+ "localization row would ship a per-culture hex value that the platform never reads");
		converter.IsStringLike(shortTextUId).Should().BeTrue(
			because: "a real text column must still be localizable, so the guard is not simply off");
	}

	[Test]
	[Description("A Color value still crosses the data-binding wire as its hex literal, even though its CLR type is System.Drawing.Color.")]
	public void ConvertValue_Should_Pass_Through_The_Hex_Literal_For_A_Color_Column() {
		// Arrange
		Guid colorUId = DataValueTypeMap.FromRuntimeValueType(18);
		DataBindingValueConverter converter = new(Substitute.For<IFileSystem>());
		JsonNode valueNode = JsonValue.Create("#FF6900");

		// Act
		object converted = converter.ConvertValue(valueNode, colorUId, "UsrColor", allowEmptyString: false);

		// Assert
		converted.Should().Be("#FF6900",
			because: "the binding row carries the literal verbatim; keeping the native CLR mapping must not "
				+ "break the wire format");
	}

	[Test]
	[Description("A numeric wire value for a Color column is rejected instead of being stringified into an invalid Color.")]
	public void ConvertValue_Should_Reject_A_Numeric_Value_For_A_Color_Column() {
		// Arrange
		Guid colorUId = DataValueTypeMap.FromRuntimeValueType(18);
		DataBindingValueConverter converter = new(Substitute.For<IFileSystem>());
		JsonNode valueNode = JsonValue.Create(123);

		// Act
		Action act = () => converter.ConvertValue(valueNode, colorUId, "UsrColor", allowEmptyString: false);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "a valid Color arrives as a hex string; turning 123 into \"123\" would ship an invalid "
				+ "Color to the platform instead of failing type validation")
			.WithMessage("*UsrColor*");
	}

	[Test]
	[Description("An object wire value for a Color column is rejected instead of being serialized as JSON text.")]
	public void ConvertValue_Should_Reject_An_Object_Value_For_A_Color_Column() {
		// Arrange
		Guid colorUId = DataValueTypeMap.FromRuntimeValueType(18);
		DataBindingValueConverter converter = new(Substitute.For<IFileSystem>());
		JsonNode valueNode = new JsonObject {
			["r"] = 0,
			["g"] = 157,
			["b"] = 227
		};

		// Act
		Action act = () => converter.ConvertValue(valueNode, colorUId, "UsrColor", allowEmptyString: false);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "serializing the object would send {\"r\":0,\"g\":157,\"b\":227} as the Color wire value; "
				+ "the type-validation error is the correct outcome")
			.WithMessage("*UsrColor*");
	}

	[TestCase("ProcessSchemaResponse0.json")]
	[TestCase("ProcessSchemaResponse1.json")]
	public async Task Should_Parse_ProcessSchemaResponse(string fileName) {

		// Arrange
		string exampleFilePath = GetExampleFilePath(fileName);
		string jsonPayload = await File.ReadAllTextAsync(exampleFilePath);

		// Act
		ErrorOr<ProcessSchemaResponse> actualOrError = ProcessSchemaResponse.FromJson(jsonPayload, _logger);

		// Assert
		_warnings.Should().BeEmpty("do not expect any warnings during parsing in happy path");
		actualOrError.IsError.Should().BeFalse();
		ProcessSchemaResponse actual = actualOrError.Value;
		const string becauseMessage = "the response should find correct value for {0} from provided sample file {1}";
		actual.Success.Should().BeTrue(becauseMessage, nameof(actual.Success), exampleFilePath);
		actual.MaxEntitySchemaNameLength.Should().Be(0, becauseMessage, nameof(actual.MaxEntitySchemaNameLength), exampleFilePath);
		actual.Schema.Should().NotBeNull(becauseMessage, nameof(actual.Schema), exampleFilePath);
	}
	
	
	[TestCase("ProcessSchemaResponse1.json")]
	public async Task Should_Parse_Schema(string fileName) {

		// Arrange
		string exampleFilePath = GetExampleFilePath(fileName);
		string jsonPayload = await File.ReadAllTextAsync(exampleFilePath);

		// Act
		ErrorOr<ProcessSchemaResponse> actualOrError = ProcessSchemaResponse.FromJson(jsonPayload, _logger);

		// Assert
		actualOrError.IsError.Should().BeFalse();
		ProcessSchemaResponse actual = actualOrError.Value;
		const string becauseMessage = "the response should find correct value for {0} from provided sample file {1}";
		
		actual.Schema.Should().NotBeNull(becauseMessage, nameof(actual.Schema), exampleFilePath);
		Clio.Command.ProcessModel.Schema schema = actual.Schema;

		schema.ParentSchemaUId.Should().Be(Guid.Parse("bb4d6607-026b-4b27-b640-8f5c77c1e89d"), becauseMessage, nameof(schema.ParentSchemaUId), exampleFilePath);
		schema.UId.Should().Be(Guid.Parse("f6ce72a1-6262-4c0e-aaf4-481be87f78bf"), becauseMessage, nameof(schema.UId), exampleFilePath);
		schema.RealUId.Should().Be(Guid.Parse("f6ce72a1-6262-4c0e-aaf4-481be87f78bf"), becauseMessage, nameof(schema.RealUId), exampleFilePath);
		schema.PackageUId.Should().Be(Guid.Parse("eddbda06-7f08-4f7a-ae0c-2ebe26cc6a1b"), becauseMessage, nameof(schema.RealUId), exampleFilePath);
		schema.Name.Should().Be("labProcess_WorkatoContactUpdated", becauseMessage, nameof(schema.ParentSchemaUId), exampleFilePath);
		schema.ExtendParent.Should().BeFalse(becauseMessage, nameof(schema.ParentSchemaUId), exampleFilePath);
		schema.Description.Should().HaveCount(0, becauseMessage, nameof(schema.Description), exampleFilePath);
		schema.LazyProperties.Should().HaveCount(0, becauseMessage, nameof(schema.LazyProperties), exampleFilePath);
		schema.LoadedLazyProperties.Should().HaveCount(0, becauseMessage, nameof(schema.LoadedLazyProperties), exampleFilePath);
		schema.Caption.Should().HaveCount(28, becauseMessage, nameof(schema.Caption), exampleFilePath);
		
		schema.Caption.Should().ContainKey("en-US", becauseMessage, "the caption should contain key 'en-US'");
		schema.Caption.Should().ContainKey("ru-RU", becauseMessage, "the caption should contain key 'ru-RU'");
		schema.Caption.Should().ContainKey("id-ID", becauseMessage, "the caption should contain key 'id-ID'");
		schema.Caption.Should().ContainKey("zh-TW", becauseMessage, "the caption should contain key 'zh-TW'");
		schema.Caption.Should().ContainKey("lv-LV", becauseMessage, "the caption should contain key 'lv-LV'");
		schema.Caption.Should().ContainKey("tr-TR", becauseMessage, "the caption should contain key 'tr-TR'");
		schema.Caption.Should().ContainKey("cs-CZ", becauseMessage, "the caption should contain key 'cs-CZ'");
		schema.Caption.Should().ContainKey("de-DE", becauseMessage, "the caption should contain key 'de-DE'");
		schema.Caption.Should().ContainKey("es-ES", becauseMessage, "the caption should contain key 'es-ES'");
		schema.Caption.Should().ContainKey("fr-FR", becauseMessage, "the caption should contain key 'fr-FR'");
		schema.Caption.Should().ContainKey("it-IT", becauseMessage, "the caption should contain key 'it-IT'");
		schema.Caption.Should().ContainKey("nl-NL", becauseMessage, "the caption should contain key 'nl-NL'");
		schema.Caption.Should().ContainKey("pt-BR", becauseMessage, "the caption should contain key 'pt-BR'");
		schema.Caption.Should().ContainKey("uk-UA", becauseMessage, "the caption should contain key 'uk-UA'");
		schema.Caption.Should().ContainKey("he-IL", becauseMessage, "the caption should contain key 'he-IL'");
		schema.Caption.Should().ContainKey("ar-SA", becauseMessage, "the caption should contain key 'ar-SA'");
		schema.Caption.Should().ContainKey("ro-RO", becauseMessage, "the caption should contain key 'ro-RO'");
		schema.Caption.Should().ContainKey("pl-PL", becauseMessage, "the caption should contain key 'pl-PL'");
		schema.Caption.Should().ContainKey("sv-SE", becauseMessage, "the caption should contain key 'sv-SE'");
		schema.Caption.Should().ContainKey("ko-KR", becauseMessage, "the caption should contain key 'ko-KR'");
		schema.Caption.Should().ContainKey("sq-AL", becauseMessage, "the caption should contain key 'sq-AL'");
		schema.Caption.Should().ContainKey("pt-PT", becauseMessage, "the caption should contain key 'pt-PT'");
		schema.Caption.Should().ContainKey("vi-VN", becauseMessage, "the caption should contain key 'vi-VN'");
		schema.Caption.Should().ContainKey("th-TH", becauseMessage, "the caption should contain key 'th-TH'");
		schema.Caption.Should().ContainKey("ja-JP", becauseMessage, "the caption should contain key 'ja-JP'");
		schema.Caption.Should().ContainKey("hu-HU", becauseMessage, "the caption should contain key 'hu-HU'");
		schema.Caption.Should().ContainKey("hr-HR", becauseMessage, "the caption should contain key 'hr-HR'");
		schema.Caption.Should().ContainKey("bg-BG", becauseMessage, "the caption should contain key 'bg-BG'");

		const string captionBecauseMessage = "Caption should have correct value {0} for culture:{1}";
		const string captionValue = "Workato - On Contact Updated";
		List<string> cultures = ["en-US", "ru-RU", "id-ID", "zh-TW", "lv-LV", "tr-TR", "cs-CZ", "de-DE", "es-ES", "fr-FR",
			"it-IT", "nl-NL", "pt-BR", "uk-UA", "he-IL", "ar-SA", "ro-RO", "pl-PL", "sv-SE", "ko-KR", "sq-AL", "pt-PT",
			"vi-VN", "th-TH", "ja-JP", "hu-HU", "hr-HR", "bg-BG"];
		cultures.ForEach(culture => {
			schema.Caption?[culture].Should().Be(captionValue, captionBecauseMessage, captionValue, culture);
		});
	}
	
	
	[TestCase("ProcessSchemaResponse1.json")]
	public async Task Should_Parse_SchemaResources(string fileName) {

		// Arrange
		string exampleFilePath = GetExampleFilePath(fileName);
		string jsonPayload = await File.ReadAllTextAsync(exampleFilePath);

		// Act
		ErrorOr<ProcessSchemaResponse> actualOrError = ProcessSchemaResponse.FromJson(jsonPayload, _logger);

		// Assert
		actualOrError.IsError.Should().BeFalse();
		ProcessSchemaResponse actual = actualOrError.Value;
		const string becauseMessage = "the response should find correct value for {0} from provided sample file {1}";
		
		actual.Schema.Should().NotBeNull(becauseMessage, nameof(actual.Schema), exampleFilePath);
		Clio.Command.ProcessModel.Schema schema = actual.Schema;

		schema.Resources.Caption.Should().NotBeNullOrEmpty();
		schema.Resources.Caption.Should().HaveCount(28, becauseMessage, nameof(schema.Resources.Caption), exampleFilePath);
		
		schema.Resources.Caption.Should().ContainKey("en-US", becauseMessage, "the caption should contain key 'en-US'");
		schema.Resources.Caption.Should().ContainKey("ru-RU", becauseMessage, "the caption should contain key 'ru-RU'");
		schema.Resources.Caption.Should().ContainKey("id-ID", becauseMessage, "the caption should contain key 'id-ID'");
		schema.Resources.Caption.Should().ContainKey("zh-TW", becauseMessage, "the caption should contain key 'zh-TW'");
		schema.Resources.Caption.Should().ContainKey("lv-LV", becauseMessage, "the caption should contain key 'lv-LV'");
		schema.Resources.Caption.Should().ContainKey("tr-TR", becauseMessage, "the caption should contain key 'tr-TR'");
		schema.Resources.Caption.Should().ContainKey("cs-CZ", becauseMessage, "the caption should contain key 'cs-CZ'");
		schema.Resources.Caption.Should().ContainKey("de-DE", becauseMessage, "the caption should contain key 'de-DE'");
		schema.Resources.Caption.Should().ContainKey("es-ES", becauseMessage, "the caption should contain key 'es-ES'");
		schema.Resources.Caption.Should().ContainKey("fr-FR", becauseMessage, "the caption should contain key 'fr-FR'");
		schema.Resources.Caption.Should().ContainKey("it-IT", becauseMessage, "the caption should contain key 'it-IT'");
		schema.Resources.Caption.Should().ContainKey("nl-NL", becauseMessage, "the caption should contain key 'nl-NL'");
		schema.Resources.Caption.Should().ContainKey("pt-BR", becauseMessage, "the caption should contain key 'pt-BR'");
		schema.Resources.Caption.Should().ContainKey("uk-UA", becauseMessage, "the caption should contain key 'uk-UA'");
		schema.Resources.Caption.Should().ContainKey("he-IL", becauseMessage, "the caption should contain key 'he-IL'");
		schema.Resources.Caption.Should().ContainKey("ar-SA", becauseMessage, "the caption should contain key 'ar-SA'");
		schema.Resources.Caption.Should().ContainKey("ro-RO", becauseMessage, "the caption should contain key 'ro-RO'");
		schema.Resources.Caption.Should().ContainKey("pl-PL", becauseMessage, "the caption should contain key 'pl-PL'");
		schema.Resources.Caption.Should().ContainKey("sv-SE", becauseMessage, "the caption should contain key 'sv-SE'");
		schema.Resources.Caption.Should().ContainKey("ko-KR", becauseMessage, "the caption should contain key 'ko-KR'");
		schema.Resources.Caption.Should().ContainKey("sq-AL", becauseMessage, "the caption should contain key 'sq-AL'");
		schema.Resources.Caption.Should().ContainKey("pt-PT", becauseMessage, "the caption should contain key 'pt-PT'");
		schema.Resources.Caption.Should().ContainKey("vi-VN", becauseMessage, "the caption should contain key 'vi-VN'");
		schema.Resources.Caption.Should().ContainKey("th-TH", becauseMessage, "the caption should contain key 'th-TH'");
		schema.Resources.Caption.Should().ContainKey("ja-JP", becauseMessage, "the caption should contain key 'ja-JP'");
		schema.Resources.Caption.Should().ContainKey("hu-HU", becauseMessage, "the caption should contain key 'hu-HU'");
		schema.Resources.Caption.Should().ContainKey("hr-HR", becauseMessage, "the caption should contain key 'hr-HR'");
		schema.Resources.Caption.Should().ContainKey("bg-BG", becauseMessage, "the caption should contain key 'bg-BG'");

		const string captionBecauseMessage = "Caption should have correct value {0} for culture:{1}";
		const string captionValue = "Workato - On Contact Updated";
		List<string> cultures = ["en-US", "ru-RU", "id-ID", "zh-TW", "lv-LV", "tr-TR", "cs-CZ", "de-DE", "es-ES", "fr-FR",
			"it-IT", "nl-NL", "pt-BR", "uk-UA", "he-IL", "ar-SA", "ro-RO", "pl-PL", "sv-SE", "ko-KR", "sq-AL", "pt-PT",
			"vi-VN", "th-TH", "ja-JP", "hu-HU", "hr-HR", "bg-BG"];
		cultures.ForEach(culture => {
			schema.Resources.Caption?[culture].Should().Be(captionValue, captionBecauseMessage, captionValue, culture);
		});
	}

	[TestCase("ProcessSchemaResponse2.json")]
	public async Task Should_Parse_SchemaMetadata(string fileName) {

		// Arrange
		string exampleFilePath = GetExampleFilePath(fileName);
		string jsonPayload = await File.ReadAllTextAsync(exampleFilePath);

		// Act
		ErrorOr<ProcessSchemaResponse> actualOrError = ProcessSchemaResponse.FromJson(jsonPayload, _logger);
		
		//Assert
		_warnings.Should().BeEmpty("do not expect any warnings during parsing in happy path");
		actualOrError.IsError.Should().BeFalse();
		ProcessSchemaResponse actual = actualOrError.Value;
		const string becauseMessage = "the response should find correct value for {0} from provided sample file {1}";
		
		actual.Schema.Metadata.Should().NotBeNull();
		actual.Schema.MetaDataSchema.Should().NotBeNull();
		MetaDataSchema metadata = actual.Schema.MetaDataSchema;
		metadata.CreatedInVersion.Should().Be(Version.Parse("8.3.0.3074"), becauseMessage, nameof(metadata.CreatedInVersion), exampleFilePath);
		metadata.ManagerName.Should().Be("ProcessSchemaManager", becauseMessage, nameof(metadata.ManagerName), exampleFilePath);
		metadata.PackageUId.Should().Be(Guid.Parse("eddbda06-7f08-4f7a-ae0c-2ebe26cc6a1b"), becauseMessage, nameof(metadata.ManagerName), exampleFilePath);
		
		metadata.Parameters.Should().HaveCount(9, becauseMessage, nameof(actual.Schema.MetaDataSchema.Parameters), exampleFilePath);
		const string dirMessage = "Parameter:{0}, is expected to have direction:{1}"; 
		metadata.Parameters?.ForEach(p=> {
			p.Direction.Should().Be(ProcessParameterDirection.Input, dirMessage, p.Name, ProcessParameterDirection.Input);
		});

		const string dataValueTypeResolvedBecauseMessage = "Parameter:{0} should be of type {1}, in SampleFile:{2}";
		
		string pName = "Contact";
		metadata.Parameters!
			.FirstOrDefault(p => p.Name == pName)?
			.DataValueTypeResolved
			.Should()
			.Be<Guid>(dataValueTypeResolvedBecauseMessage, pName, nameof(Guid), exampleFilePath);
		
		
		pName = "AbsenceId";
		metadata.Parameters!
			.FirstOrDefault(p => p.Name == pName)?
			.DataValueTypeResolved
			.Should()
			.Be<int>(dataValueTypeResolvedBecauseMessage, pName, nameof(Int32), exampleFilePath);
		
		
		pName = "Request";
		metadata.Parameters!
			.FirstOrDefault(p => p.Name == pName)?
			.DataValueTypeResolved
			.Should()
			.Be<Guid>(dataValueTypeResolvedBecauseMessage, pName, nameof(Guid), exampleFilePath);
		
		pName = "DateFrom";
		metadata.Parameters!
			.FirstOrDefault(p => p.Name == pName)?
			.DataValueTypeResolved
			.Should()
			.Be<DateTime>(dataValueTypeResolvedBecauseMessage, pName, nameof(DateTime), exampleFilePath);
		
		pName = "DateTill";
		metadata.Parameters!
			.FirstOrDefault(p => p.Name == pName)?
			.DataValueTypeResolved
			.Should()
			.Be<DateTime>(dataValueTypeResolvedBecauseMessage, pName, nameof(DateTime), exampleFilePath);
		
		
		pName = "Hours";
		metadata.Parameters!
			.FirstOrDefault(p => p.Name == pName)?
			.DataValueTypeResolved
			.Should()
			.Be<int>(dataValueTypeResolvedBecauseMessage, pName, nameof(Int32), exampleFilePath);
		
		
		pName = "LeaveReason";
		metadata.Parameters!
			.FirstOrDefault(p => p.Name == pName)?
			.DataValueTypeResolved
			.Should()
			.Be<string>(dataValueTypeResolvedBecauseMessage, pName, nameof(String), exampleFilePath);
		
		pName = "OccasionalLeaveReason";
		metadata.Parameters!
			.FirstOrDefault(p => p.Name == pName)?
			.DataValueTypeResolved
			.Should()
			.Be<string>(dataValueTypeResolvedBecauseMessage, pName, nameof(String), exampleFilePath);
		
		pName = "WorkDays";
		metadata.Parameters!
			.FirstOrDefault(p => p.Name == pName)?
			.DataValueTypeResolved
			.Should()
			.Be<float>(dataValueTypeResolvedBecauseMessage, pName, "Float", exampleFilePath);
	}

	[Test]
	[Description("Returns null when TimeZoneOffset is missing, instead of resolving against a fallback zone (sonar csharpsquid:S2583 simplification must not change this behavior).")]
	public void FlowElement_TimeZoneInfo_Should_Be_Null_When_TimeZoneOffset_Is_Missing() {
		// Arrange
		FlowElement flowElement = new() { TimeZoneOffset = null };

		// Act
		TimeZoneInfo? result = flowElement.TimeZoneInfo;

		// Assert
		result.Should().BeNull(
			because: "a missing TimeZoneOffset must resolve to no timezone rather than a hardcoded fallback");
	}

	[Test]
	[Description("Resolves the exact TimeZoneOffset value when present, confirming the removed '?? \"UTC\"' fallback was genuinely dead code (sonar csharpsquid:S2583).")]
	public void FlowElement_TimeZoneInfo_Should_Resolve_The_Exact_Offset_When_Present() {
		// Arrange
		FlowElement flowElement = new() { TimeZoneOffset = "UTC" };

		// Act
		TimeZoneInfo? result = flowElement.TimeZoneInfo;

		// Assert
		result.Should().NotBeNull(because: "a present TimeZoneOffset must resolve to a real TimeZoneInfo");
		result!.Id.Should().Be("UTC", because: "the exact configured offset must be resolved, not a fallback");
	}
}
