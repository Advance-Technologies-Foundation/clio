using System.Collections.Generic;
using Clio.Common;
using Clio.Common.Responses;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public class CommandEnvelopeTests {

	#region Fields: Private

	private JsonResponseFormater _formater;

	#endregion

	[SetUp]
	public void Setup() {
		// Real serializer (Newtonsoft, honors [DataContract]/[DataMember]) — no I/O, so this stays a Unit test.
		_formater = new JsonResponseFormater(new JsonConverter());
	}

	[Test]
	[Description("FormatEnvelope(success) should produce a single object with schemaVersion, ok=true, command, data, and error=null")]
	public void FormatEnvelope_ShouldProduceSuccessEnvelope_WhenDataProvided() {
		// Arrange
		var data = new List<string> { "Foo", "Bar" };

		// Act
		string json = _formater.FormatEnvelope("list-packages", data);
		JObject parsed = JObject.Parse(json);

		// Assert
		parsed["schemaVersion"]!.Value<string>().Should().Be("1.0", because: "the envelope is versioned at 1.0");
		parsed["ok"]!.Value<bool>().Should().BeTrue(because: "success sets ok=true");
		parsed["command"]!.Value<string>().Should().Be("list-packages", because: "the canonical command name is echoed back");
		parsed["data"]!.Type.Should().Be(JTokenType.Array, because: "the payload is carried in data");
		parsed["error"]!.Type.Should().Be(JTokenType.Null, because: "a success envelope has no error");
	}

	[Test]
	[Description("FormatEnvelope(error) should produce ok=false, data=null, and an error object with stable code and message")]
	public void FormatEnvelope_ShouldProduceErrorEnvelope_WhenErrorProvided() {
		// Act
		string json = _formater.FormatEnvelope("list-packages", CommandErrorCodes.UnexpectedError, "boom");
		JObject parsed = JObject.Parse(json);

		// Assert
		parsed["ok"]!.Value<bool>().Should().BeFalse(because: "failure sets ok=false");
		parsed["data"]!.Type.Should().Be(JTokenType.Null, because: "an error envelope carries no data");
		parsed["error"]!["code"]!.Value<string>().Should().Be("unexpected-error", because: "the stable code is surfaced for automation");
		parsed["error"]!["message"]!.Value<string>().Should().Be("boom", because: "the human-readable message is preserved");
		parsed["error"]!["stackTrace"].Should().BeNull(because: "the envelope must never leak stack traces");
	}

	[Test]
	[Description("The unified envelope must NOT reuse the legacy field names (value/success/errorInfo)")]
	public void FormatEnvelope_ShouldNotUseLegacyFieldNames_WhenSerialized() {
		// Act
		string json = _formater.FormatEnvelope("list-packages", new List<string>());
		JObject parsed = JObject.Parse(json);

		// Assert
		parsed["value"].Should().BeNull(because: "the unified envelope renames value -> data");
		parsed["success"].Should().BeNull(because: "the unified envelope renames success -> ok");
		parsed["errorInfo"].Should().BeNull(because: "the unified envelope renames errorInfo -> error");
	}

	[Test]
	[Description("Format (legacy) should keep emitting the historical {value,success,errorInfo} shape for --legacy-form consumers")]
	public void Format_ShouldKeepLegacyShape_WhenCalled() {
		// Act
		string json = _formater.Format(new List<string> { "Foo" });
		JObject parsed = JObject.Parse(json);

		// Assert
		parsed["value"]!.Type.Should().Be(JTokenType.Array, because: "the legacy shape carries the payload in value");
		parsed["success"]!.Value<bool>().Should().BeTrue(because: "the legacy shape uses success, not ok");
		parsed.ContainsKey("schemaVersion").Should().BeFalse(because: "the legacy shape predates the versioned envelope");
	}

	[Test]
	[Description("ToExitCode should return the generic failure code 1 for the unexpected-error code")]
	public void ToExitCode_ShouldReturnOne_WhenUnexpectedError() {
		CommandErrorCodes.ToExitCode(CommandErrorCodes.UnexpectedError)
			.Should().Be(1, because: "an unclassified failure maps to the generic exit code 1");
	}

	[Test]
	[Description("ToExitCode should return the distinct version-gate exit code 78 for version-* codes")]
	public void ToExitCode_ShouldReturnSeventyEight_WhenVersionCode() {
		CommandErrorCodes.ToExitCode(CommandErrorCodes.VersionTooOld)
			.Should().Be(78, because: "version-requirement refusals use the distinct exit code 78");
	}

	[Test]
	[Description("ToExitCode should fall back to the generic failure code 1 for unknown or null codes")]
	public void ToExitCode_ShouldReturnOne_WhenCodeUnknownOrNull() {
		CommandErrorCodes.ToExitCode("not-a-registered-code").Should().Be(1, because: "unknown codes fall back to 1");
		CommandErrorCodes.ToExitCode(null).Should().Be(1, because: "a null code falls back to 1");
	}

	[Test]
	[Description("Envelope with non-ASCII data must serialize as strict-parseable JSON with no BOM and no raw control chars, and round-trip Unicode — cross-platform (F1/F2) invariant guard")]
	public void FormatEnvelope_ShouldBeStrictParseableUnicodeSafe_WhenDataHasNonAscii() {
		// Arrange — Cyrillic + an embedded quote + an embedded tab (control char) in the payload
		var data = new List<string> { "Пакет-Тест", "with \"quote\"", "таб\tвнутри" };

		// Act
		string json = _formater.FormatEnvelope("list-packages", data);

		// Assert: strict parse works regardless of platform newline (CRLF/LF are valid JSON whitespace)
		JObject parsed = JObject.Parse(json);
		parsed["ok"]!.Value<bool>().Should().BeTrue(because: "the payload serialized successfully");
		var values = (JArray)parsed["data"]!;
		values[0]!.Value<string>().Should().Be("Пакет-Тест", because: "Unicode must round-trip byte-for-byte");
		values[1]!.Value<string>().Should().Be("with \"quote\"", because: "quotes must be escaped, not corrupt the document");
		values[2]!.Value<string>().Should().Be("таб\tвнутри", because: "control chars must be escaped and round-trip");
		// No BOM at the start (a leading U+FEFF breaks jq / strict parsers on Windows).
		json[0].Should().NotBe('﻿', because: "the JSON string must not begin with a byte-order mark");
		// No RAW (unescaped) control characters below 0x20 except structural whitespace \r \n \t
		// (this also covers a raw NUL byte).
		foreach (char c in json) {
			if (c < 0x20) {
				(c is '\r' or '\n' or '\t').Should().BeTrue(
					because: $"only structural whitespace is allowed raw; found control char U+{(int)c:X4}");
			}
		}
	}

	[Test]
	[Description("The re-exported version codes must match the authoritative taxonomy on CreatioVersionRequirementException")]
	public void VersionCodes_ShouldMatchAuthoritativeTaxonomy_WhenReExported() {
		CommandErrorCodes.VersionTooOld.Should().Be(CreatioVersionRequirementException.VersionTooOldCode,
			because: "the registry re-exports rather than redefines the taxonomy");
		CommandErrorCodes.VersionUndeterminable.Should().Be(CreatioVersionRequirementException.VersionUndeterminableCode,
			because: "the registry re-exports rather than redefines the taxonomy");
		CommandErrorCodes.VersionCheckFailed.Should().Be(CreatioVersionRequirementException.VersionCheckFailedCode,
			because: "the registry re-exports rather than redefines the taxonomy");
	}

}
