using System;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class AutoUpdateSettingsSerializationTests {
	[Test]
	[Description("Omits next-run entirely for a policy that was never scheduled, instead of writing a year-0001 timestamp with a local-mean-time offset.")]
	public void Serialize_Should_Omit_NextRun_When_Policy_Was_Never_Scheduled() {
		// Arrange
		Settings settings = new();

		// Act
		string json = JsonConvert.SerializeObject(settings,
			new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

		// Assert
		json.Should().NotContain("0001-01-01",
			because: "an unscheduled policy must not be persisted as a year-0001 timestamp such as 0001-01-01T01:24:00+01:24");
		json.Should().NotContain("next-run",
			because: "omitting the property is what tells every reader, old and new, that the policy has never run");
	}

	[Test]
	[Description("Reads a scheduled next-run back with the offset the file carries, without shifting it to the local time zone.")]
	public void Deserialize_Should_Preserve_The_Offset_Written_In_The_File() {
		// Arrange
		const string json = """
			{
			  "autoupdate": {
			    "clio": { "enabled": true, "frequency-minutes": 480, "next-run": "2026-09-12T10:00:00+00:00" }
			  }
			}
			""";

		// Act
		Settings settings = JsonConvert.DeserializeObject<Settings>(json);

		// Assert
		settings.Autoupdate.Clio.NextRun.Should().Be(new DateTimeOffset(2026, 9, 12, 10, 0, 0, TimeSpan.Zero),
			because: "the stored instant and its offset must survive the round trip regardless of the host time zone");
	}

	[Test]
	[Description("Round-trips a scheduled policy through serialization without rewriting its offset.")]
	public void Serialize_Should_RoundTrip_A_Scheduled_Policy() {
		// Arrange
		Settings settings = new();
		settings.Autoupdate.Clio.NextRun = new DateTimeOffset(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);

		// Act
		Settings roundTripped = JsonConvert.DeserializeObject<Settings>(
			JsonConvert.SerializeObject(settings));

		// Assert
		roundTripped.Autoupdate.Clio.NextRun.Should().Be(settings.Autoupdate.Clio.NextRun,
			because: "a schedule written by clio must be read back by clio as the same instant");
	}

	[Test]
	[Description("Still reads a legacy year-0001 next-run written with a local-mean-time offset, and treats that policy as due.")]
	public void Deserialize_Should_Accept_A_Legacy_Year_0001_NextRun() {
		// Arrange
		const string json = """
			{
			  "autoupdate": {
			    "clio": { "enabled": true, "frequency-minutes": 480, "next-run": "0001-01-01T01:24:00+01:24" }
			  }
			}
			""";

		// Act
		Settings settings = JsonConvert.DeserializeObject<Settings>(json);

		// Assert
		settings.Autoupdate.Clio.NextRun.Should().Be(DateTimeOffset.MinValue,
			because: "the malformed-looking legacy value is still the minimum instant and must keep reading as one");
		(settings.Autoupdate.Clio.NextRun < DateTimeOffset.UtcNow).Should().BeTrue(
			because: "a file written by the old code must leave the update due, not silently postponed");
	}

	[Test]
	[Description("Still accepts the legacy scalar autoupdate form that older settings files use.")]
	public void Deserialize_Should_Accept_The_Legacy_Scalar_Form() {
		// Arrange
		const string json = """{ "autoupdate": true }""";

		// Act
		Settings settings = JsonConvert.DeserializeObject<Settings>(json);

		// Assert
		settings.Autoupdate.Clio.Enabled.Should().BeTrue(
			because: "the legacy boolean form maps onto the clio policy and must keep doing so");
		settings.Autoupdate.Clio.NextRun.Should().BeNull(
			because: "a legacy file carries no schedule, which means the policy is due");
	}
}
