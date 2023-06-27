namespace Clio.Tests.YAML;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Clio.YAML;
using FluentAssertions;
using NUnit.Framework;
using OneOf;
using OneOf.Types;
using YamlDotNet.Serialization;

[TestFixture(Category = "YAML", Author = "Kirill Krylov")]
public class OptionsTests
{

	private readonly IDeserializer _deserializer;
	
	public OptionsTests() {
		_deserializer = new DeserializerBuilder().Build();
	}
	
	[TestCase("redis.passwords.passwordOne1")]
	[TestCase("redis.passwords1.passwordOne")]
	[TestCase("redis1.passwords.passwordOne")]
	[TestCase("redis2.passwords2.passwordTwo")]
	[TestCase("values1")]
	[TestCase("postgresql.password.256.356.3456.")]
	[TestCase("mssql.password.9oiuhgdf")]
	[TestCase("mssql.password.9oiuhgdf.9oiuhgdf")]
	public void GetSecretByKey_Returns_None_When_KeyDoesNotExists(string key) {
		
		// Arrange
		const string fileName = @"YAML/Yaml-Samples/secrets.yaml";
		
		var section = _deserializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(fileName));
		var secretSection =  (section.Where(s=> s.Key=="secrets")
				.Select(s=> s.Value as Dictionary<object, object>)
				.FirstOrDefault() ?? new Dictionary<object, object>())
			.ToDictionary(v=> v.Key.ToString(), v=> v.Value);

		// Act
		var secret = Options.GetOptionByKey(key, secretSection);
		
		// Assert
		secret.Value.Should().BeOfType<None>();
	}

	[TestCase("redis.passwords.passwordOne","passwordOneValue")]
	[TestCase("redis.passwords.passwordTwo","passwordTwoValue")]
	[TestCase("postgresql.password","pgPassword")]
	[TestCase("mssql.password","msPassword")]
	public void GetSecretByKey_Returns_SecretFromComplexKey_When_KeyExists(string key, string expected) {
		// Arrange
		const string fileName = @"YAML/Yaml-Samples/secrets.yaml";
		
		var section = _deserializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(fileName));
		var secretSection =  (section.Where(s=> s.Key=="secrets")
				.Select(s=> s.Value as Dictionary<object, object>)
				.FirstOrDefault() ?? new Dictionary<object, object>())
			.ToDictionary(v=> v.Key.ToString(), v=> v.Value);
		
		// Act
		OneOf<object, None> secret = Options.GetOptionByKey(key, secretSection);
		
		// Assert
		secret.Value.Should().NotBeOfType<None>();
		secret.Value.Should().Be(expected);
	}
}