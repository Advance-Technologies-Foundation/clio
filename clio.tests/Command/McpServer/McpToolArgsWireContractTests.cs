using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Locks the advertised MCP wire contract (the <see cref="JsonPropertyNameAttribute"/> values) for the
/// deploy-creatio and restore-db argument records. The behavioral unit tests assert only the C# property
/// mapping, which is invariant under a JSON casing change, so they cannot catch kebab-case/camelCase wire
/// drift between a tool's args record and the schema MCP clients (and the e2e suite) actually send. This
/// reflection test does, by reading the wire names directly.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class McpToolArgsWireContractTests {
	private static readonly Dictionary<string, string> DeployCreatioWireNames = new() {
		[nameof(DeployCreatioArgs.SiteName)] = "siteName",
		[nameof(DeployCreatioArgs.ZipFile)] = "zipFile",
		[nameof(DeployCreatioArgs.SitePort)] = "sitePort",
		[nameof(DeployCreatioArgs.DbServerName)] = "dbServerName",
		[nameof(DeployCreatioArgs.RedisServerName)] = "redisServerName",
		[nameof(DeployCreatioArgs.UseHttps)] = "useHttps"
	};

	private static readonly Dictionary<string, string> RestoreDbByEnvironmentWireNames = new() {
		[nameof(RestoreDbByEnvironmentArgs.EnvironmentName)] = "environmentName",
		[nameof(RestoreDbByEnvironmentArgs.BackupPath)] = "backupPath",
		[nameof(RestoreDbByEnvironmentArgs.DbName)] = "dbName",
		[nameof(RestoreDbByEnvironmentArgs.Force)] = "force",
		[nameof(RestoreDbByEnvironmentArgs.AsTemplate)] = "asTemplate",
		[nameof(RestoreDbByEnvironmentArgs.DisableResetPassword)] = "disableResetPassword"
	};

	private static readonly Dictionary<string, string> RestoreDbByCredentialsWireNames = new() {
		[nameof(RestoreDbByCredentialsArgs.DbServerUri)] = "dbServerUri",
		[nameof(RestoreDbByCredentialsArgs.DbUser)] = "dbUser",
		[nameof(RestoreDbByCredentialsArgs.DbPassword)] = "dbPassword",
		[nameof(RestoreDbByCredentialsArgs.DbWorkingFolder)] = "dbWorkingFolder",
		[nameof(RestoreDbByCredentialsArgs.BackupPath)] = "backupPath",
		[nameof(RestoreDbByCredentialsArgs.DbName)] = "dbName",
		[nameof(RestoreDbByCredentialsArgs.Force)] = "force",
		[nameof(RestoreDbByCredentialsArgs.AsTemplate)] = "asTemplate",
		[nameof(RestoreDbByCredentialsArgs.DisableResetPassword)] = "disableResetPassword"
	};

	private static readonly Dictionary<string, string> RestoreDbToLocalServerWireNames = new() {
		[nameof(RestoreDbToLocalServerArgs.DbServerName)] = "dbServerName",
		[nameof(RestoreDbToLocalServerArgs.BackupPath)] = "backupPath",
		[nameof(RestoreDbToLocalServerArgs.DbName)] = "dbName",
		[nameof(RestoreDbToLocalServerArgs.DropIfExists)] = "dropIfExists",
		[nameof(RestoreDbToLocalServerArgs.AsTemplate)] = "asTemplate",
		[nameof(RestoreDbToLocalServerArgs.DisableResetPassword)] = "disableResetPassword"
	};

	private static IEnumerable<TestCaseData> WireContracts() {
		yield return new TestCaseData(typeof(DeployCreatioArgs), DeployCreatioWireNames)
			.SetName("deploy-creatio args advertise the camelCase wire contract");
		yield return new TestCaseData(typeof(RestoreDbByEnvironmentArgs), RestoreDbByEnvironmentWireNames)
			.SetName("restore-db-by-environment args advertise the camelCase wire contract");
		yield return new TestCaseData(typeof(RestoreDbByCredentialsArgs), RestoreDbByCredentialsWireNames)
			.SetName("restore-db-by-credentials args advertise the camelCase wire contract");
		yield return new TestCaseData(typeof(RestoreDbToLocalServerArgs), RestoreDbToLocalServerWireNames)
			.SetName("restore-db-to-local-server args advertise the camelCase wire contract");
	}

	[Test]
	[Category("Unit")]
	[TestCaseSource(nameof(WireContracts))]
	[Description("Asserts the JsonPropertyName wire names for deploy-creatio and restore-db args match the camelCase MCP contract that clients and the e2e suite send.")]
	public void McpArgs_Should_Advertise_Expected_CamelCase_Wire_Names(
		Type argsType,
		Dictionary<string, string> expectedWireNames) {
		// Arrange
		IReadOnlyDictionary<string, string> actualWireNames = ReadWireNames(argsType);

		// Assert
		actualWireNames.Should().BeEquivalentTo(expectedWireNames,
			because: $"{argsType.Name} must advertise the agreed MCP wire contract so client and e2e payloads bind");
		actualWireNames.Values.Should().OnlyContain(name => !name.Contains('-'),
			because: "deploy-creatio and restore-db args use camelCase wire names, not kebab-case");
	}

	private static IReadOnlyDictionary<string, string> ReadWireNames(Type argsType) {
		return argsType
			.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Select(property => new {
				property.Name,
				WireName = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
			})
			.Where(entry => entry.WireName is not null)
			.ToDictionary(entry => entry.Name, entry => entry.WireName!);
	}
}
