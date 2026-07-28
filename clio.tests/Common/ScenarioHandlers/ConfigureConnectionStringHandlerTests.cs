using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Clio.Common.ScenarioHandlers;
using Clio.Tests.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common.ScenarioHandlers;

[Property("Module", "Common")]
public sealed class ConfigureConnectionStringHandlerTests : BaseClioModuleTests {
	private IConfigureConnectionStringHandler _sut;

	public override void Setup() {
		base.Setup();
		_sut = Container.GetRequiredService<IConfigureConnectionStringHandler>();
	}

	[Test]
	[Description("Connection strings are persisted without returning database or Redis credentials in command output.")]
	public async Task Handle_ShouldNotExposeConnectionSecrets_WhenConfigurationSucceeds() {
		// Arrange
		string directory = Path.Combine(Path.GetTempPath(), $"clio-connection-redaction-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directory);
		const string databaseSecret = "db-super-secret-value";
		const string redisSecret = "redis-super-secret-value";
		string configPath = Path.Combine(directory, "ConnectionStrings.config");
		await File.WriteAllTextAsync(configPath,
			"<connectionStrings><add name=\"db\" connectionString=\"old\"/><add name=\"redis\" connectionString=\"old\"/></connectionStrings>");
		ConfigureConnectionStringRequest request = new() {
			Arguments = new Dictionary<string, string> {
				["folderPath"] = directory,
				["dbString"] = $"Host=localhost;Password={databaseSecret}",
				["redis"] = $"host=localhost;password={redisSecret}",
				["isNetFramework"] = bool.TrueString
			}
		};

		try {
			// Act
			var result = await _sut.Handle(request);

			// Assert
			result.IsT0.Should().BeTrue(because: "a valid ConnectionStrings.config must be updated successfully");
			result.AsT0.Description.Should().NotContainAny([databaseSecret, redisSecret],
				because: "deploy output and MCP logs must never expose connection credentials");
			string persisted = await File.ReadAllTextAsync(configPath);
			persisted.Should().Contain(databaseSecret,
				because: "redaction must affect diagnostics only, not the deployed database configuration");
			persisted.Should().Contain(redisSecret,
				because: "redaction must affect diagnostics only, not the deployed Redis configuration");
		}
		finally {
			Directory.Delete(directory, recursive: true);
		}
	}
}
