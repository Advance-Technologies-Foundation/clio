using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Clio.Command.McpServer.Progress;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Contract tests for the <see cref="ClioStageEvent"/> envelope. The committed JSON fixture
/// (<see cref="FixtureRelativePath"/>) is the cross-repo compatibility anchor: the Ring mirror
/// asserts against the byte-identical copy in story 6. Any field change requires bumping
/// <see cref="ClioStageEventContract.SchemaVersion"/> and updating both repos' fixtures.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public class ClioStageEventContractTests {

	private const string FixtureRelativePath = "Command/McpServer/Fixtures/ClioStageEvent.contract.ndjson";
	private const string CanonicalRunId = "8a1b0c2d-3e4f-4a6b-8c8d-9e0f1a2b3c4d";

	private static string FixturePath =>
		Path.Combine(TestContext.CurrentContext.TestDirectory, FixtureRelativePath);

	[Test]
	[Category("Unit")]
	[Description("TC-U-01: a manifest ClioStageEvent serializes to the versioned manifest shape with the exact ADR D2 field names.")]
	public void Serialize_ShouldEmitVersionedManifestShape_WhenEventTypeIsManifest() {
		// Arrange
		ClioStageEvent manifest = CanonicalManifest();

		// Act
		string json = JsonSerializer.Serialize(manifest, ClioStageEventContract.SerializerOptions);

		// Assert
		json.Should().Contain("\"schemaVersion\":1", "because the manifest carries the versioned contract gate")
			.And.Contain("\"eventType\":\"manifest\"", "because the discriminator identifies a manifest event")
			.And.Contain($"\"runId\":\"{CanonicalRunId}\"", "because every event carries the run identifier")
			.And.Contain("\"sequence\":0", "because the manifest is the first event of the run")
			.And.Contain("\"operation\":\"deploy\"", "because the operation kind travels on every event")
			.And.Contain("\"stages\":[", "because a manifest carries the ordered stages array");
		json.Should().Contain("\"stageId\":\"restore-db\",\"name\":\"Restore database\",\"index\":3,\"total\":8,\"conditional\":false",
			"because each manifest entry exposes stageId, name, index, total and conditional in that order");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-02: a stage ClioStageEvent serializes the stage shape and omits null optional members.")]
	public void Serialize_ShouldEmitStageShapeAndOmitNulls_WhenEventTypeIsStage() {
		// Arrange
		ClioStageEvent stage = CanonicalStage();

		// Act
		string json = JsonSerializer.Serialize(stage, ClioStageEventContract.SerializerOptions);

		// Assert
		json.Should().Contain("\"eventType\":\"stage\"", "because the discriminator identifies a stage event")
			.And.Contain("\"stage\":{", "because a stage event carries the single stage payload")
			.And.Contain("\"status\":\"done\"", "because the stage status travels on the wire")
			.And.Contain("\"startedAtUtc\":\"2026-07-11T10:15:30+00:00\"", "because a running/done stage records when it started")
			.And.Contain("\"durationMs\":42123", "because a completed stage records its duration")
			.And.Contain("\"message\":\"Restore database\"", "because the human message is always present");
		json.Should().NotContain("\"detail\"", "because null optional members are omitted (WhenWritingNull)")
			.And.NotContain("\"errorCode\"", "because null optional members are omitted (WhenWritingNull)")
			.And.NotContain("\"skipReason\"", "because null optional members are omitted (WhenWritingNull)");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-03: a run-completed ClioStageEvent serializes the run-completed shape.")]
	public void Serialize_ShouldEmitRunCompletedShape_WhenEventTypeIsRunCompleted() {
		// Arrange
		ClioStageEvent runCompleted = CanonicalRunCompleted();

		// Act
		string json = JsonSerializer.Serialize(runCompleted, ClioStageEventContract.SerializerOptions);

		// Assert
		json.Should().Contain("\"eventType\":\"run-completed\"", "because the discriminator identifies a run-completed event")
			.And.Contain("\"runCompleted\":{", "because a run-completed event carries the terminal payload")
			.And.Contain("\"outcome\":\"success\"", "because the terminal outcome travels on the wire")
			.And.Contain("\"summary\":\"Deployment completed\"", "because a human summary is always present")
			.And.Contain("\"derivedUrl\":\"http://localhost:40000/0\"", "because a successful deploy exposes the derived URL")
			.And.Contain("\"derivedPath\":\"C:\\\\inetpub\\\\wwwroot\\\\creatio\"", "because a successful deploy exposes the derived path");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-04: StageIds exposes stable kebab-case string constants for every deploy and uninstall stage.")]
	public void StageIds_ShouldExposeKebabStringConstants_ForAllDeployAndUninstallStages() {
		// Arrange
		string[] expectedDeploy = [
			"stage-build", "unzip", "copy-files", "restore-db",
			"deploy-app", "configure-conn-strings", "register-env", "wait-ready"
		];
		string[] expectedUninstall = [
			"read-config", "stop-iis", "delete-iis", "drop-db",
			"delete-files", "unregister", "delete-apppool-profile"
		];

		// Act
		string[] actualDeploy = [
			StageIds.StageBuild, StageIds.Unzip, StageIds.CopyFiles, StageIds.RestoreDb,
			StageIds.DeployApp, StageIds.ConfigureConnStrings, StageIds.RegisterEnv, StageIds.WaitReady
		];
		string[] actualUninstall = [
			StageIds.ReadConfig, StageIds.StopIis, StageIds.DeleteIis, StageIds.DropDb,
			StageIds.DeleteFiles, StageIds.Unregister, StageIds.DeleteApppoolProfile
		];

		// Assert
		actualDeploy.Should().Equal(expectedDeploy, "because deploy stage ids are stable kebab-case string keys, not enum ordinals");
		actualUninstall.Should().Equal(expectedUninstall, "because uninstall stage ids are stable kebab-case string keys, not enum ordinals");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-05: the committed JSON fixture round-trips byte-identically through deserialize then re-serialize (cross-repo anchor).")]
	public void RoundTrip_ShouldBeByteIdenticalToFixture_WhenDeserializedAndReserialized() {
		// Arrange
		string fixture = NormalizeNewlines(File.ReadAllText(FixturePath)).TrimEnd('\n');
		string[] lines = fixture.Split('\n');

		// Act
		List<string> reserialized = [];
		foreach (string line in lines) {
			ClioStageEvent evt = JsonSerializer.Deserialize<ClioStageEvent>(line, ClioStageEventContract.SerializerOptions)!;
			reserialized.Add(JsonSerializer.Serialize(evt, ClioStageEventContract.SerializerOptions));
		}
		string roundTripped = string.Join('\n', reserialized);

		// Assert
		lines.Should().HaveCount(3, "because the fixture carries a representative manifest, stage and run-completed triple");
		roundTripped.Should().Be(fixture, "because the envelope must round-trip byte-identically to the committed cross-repo fixture");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-06: an unknown extra property in the payload is tolerated on deserialize (FR-12 unknown-field tolerance).")]
	public void Deserialize_ShouldTolerateUnknownField_WhenFixtureHasExtraProperty() {
		// Arrange
		const string jsonWithUnknownField =
			"{\"schemaVersion\":1,\"eventType\":\"stage\",\"runId\":\"" + CanonicalRunId + "\",\"sequence\":3," +
			"\"operation\":\"deploy\",\"futureField\":\"ignored\"," +
			"\"stage\":{\"stageId\":\"restore-db\",\"name\":\"Restore database\",\"index\":3,\"total\":8," +
			"\"status\":\"done\",\"message\":\"Restore database\",\"anotherUnknown\":42}}";

		// Act
		Action deserialize = () => JsonSerializer.Deserialize<ClioStageEvent>(jsonWithUnknownField, ClioStageEventContract.SerializerOptions);

		// Assert
		deserialize.Should().NotThrow("because unknown fields must be tolerated so forward-compatible producers do not break older consumers");
		ClioStageEvent evt = JsonSerializer.Deserialize<ClioStageEvent>(jsonWithUnknownField, ClioStageEventContract.SerializerOptions)!;
		evt.Stage!.StageId.Should().Be("restore-db", "because the known fields still deserialize correctly alongside the unknown ones");
	}

	[Test]
	[Category("Unit")]
	[Description("TC-U-07: schemaVersion is readable when it differs from the emitter's version so a consumer can gate on it.")]
	public void SchemaVersion_ShouldBeReadable_WhenEnvelopeVersionDiffersFromEmitter() {
		// Arrange
		const string futureVersionJson =
			"{\"schemaVersion\":999,\"eventType\":\"manifest\",\"runId\":\"" + CanonicalRunId + "\"," +
			"\"sequence\":0,\"operation\":\"deploy\",\"stages\":[]}";

		// Act
		ClioStageEvent evt = JsonSerializer.Deserialize<ClioStageEvent>(futureVersionJson, ClioStageEventContract.SerializerOptions)!;

		// Assert
		evt.SchemaVersion.Should().Be(999, "because schemaVersion is the compatibility gate a consumer reads before trusting the payload");
		evt.SchemaVersion.Should().NotBe(ClioStageEventContract.SchemaVersion, "because this envelope was emitted by a newer, incompatible schema version");
	}

	private static string NormalizeNewlines(string text) => text.Replace("\r\n", "\n").Replace("\r", "\n");

	private static ClioStageEvent CanonicalManifest() {
		IReadOnlyList<ClioStageManifestEntry> stages = [
			new(StageIds.StageBuild, "Build source", 0, 8, true),
			new(StageIds.Unzip, "Unzip distribution", 1, 8, false),
			new(StageIds.CopyFiles, "Copy files", 2, 8, false),
			new(StageIds.RestoreDb, "Restore database", 3, 8, false),
			new(StageIds.DeployApp, "Deploy application", 4, 8, false),
			new(StageIds.ConfigureConnStrings, "Configure connection strings", 5, 8, false),
			new(StageIds.RegisterEnv, "Register environment", 6, 8, false),
			new(StageIds.WaitReady, "Wait until ready", 7, 8, false)
		];
		return new ClioStageEvent(
			ClioStageEventContract.SchemaVersion,
			ClioStageEventContract.EventTypes.Manifest,
			Guid.Parse(CanonicalRunId),
			0,
			ClioStageEventContract.Operations.Deploy,
			Stages: stages);
	}

	private static ClioStageEvent CanonicalStage() =>
		new(
			ClioStageEventContract.SchemaVersion,
			ClioStageEventContract.EventTypes.Stage,
			Guid.Parse(CanonicalRunId),
			3,
			ClioStageEventContract.Operations.Deploy,
			Stage: new ClioStageDetail(
				StageIds.RestoreDb,
				"Restore database",
				3,
				8,
				ClioStageEventContract.StageStatuses.Done,
				StartedAtUtc: new DateTimeOffset(2026, 7, 11, 10, 15, 30, TimeSpan.Zero),
				DurationMs: 42123,
				Message: "Restore database"));

	private static ClioStageEvent CanonicalRunCompleted() =>
		new(
			ClioStageEventContract.SchemaVersion,
			ClioStageEventContract.EventTypes.RunCompleted,
			Guid.Parse(CanonicalRunId),
			9,
			ClioStageEventContract.Operations.Deploy,
			RunCompleted: new ClioRunCompleted(
				ClioStageEventContract.RunOutcomes.Success,
				"Deployment completed",
				DerivedUrl: "http://localhost:40000/0",
				DerivedPath: @"C:\inetpub\wwwroot\creatio"));
}
