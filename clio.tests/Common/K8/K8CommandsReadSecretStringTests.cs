using System.Collections.Generic;
using System.Text;
using Clio.Common.K8;
using FluentAssertions;
using k8s.Models;
using NUnit.Framework;

namespace Clio.Tests.Common.K8;

[TestFixture]
[Property("Module", "Common")]
[Category("Unit")]
public class K8CommandsReadSecretStringTests {

	[Test]
	[Description("Reads and UTF-8-decodes the value for an existing key, mirroring the previous raw-indexer + Encoding.UTF8.GetString behavior.")]
	public void ReadSecretString_Should_ReturnDecodedValue_WhenKeyExists() {
		// Arrange
		V1Secret secret = new() {
			Data = new Dictionary<string, byte[]> {
				["POSTGRES_PASSWORD"] = Encoding.UTF8.GetBytes("s3cr3t")
			}
		};

		// Act
		string result = k8Commands.ReadSecretString(secret, "POSTGRES_PASSWORD");

		// Assert
		result.Should().Be("s3cr3t", because: "the byte value under the requested key must be UTF-8 decoded verbatim");
	}

	[Test]
	[Description("Returns empty string instead of throwing when the secret itself is null (e.g. no matching V1Secret found in the namespace).")]
	public void ReadSecretString_Should_ReturnEmpty_WhenSecretIsNull() {
		// Act
		string result = k8Commands.ReadSecretString(null, "POSTGRES_PASSWORD");

		// Assert
		result.Should().BeEmpty(because: "a missing secret must degrade to an empty value rather than crash the connection-string lookup");
	}

	[Test]
	[Description("Returns empty string instead of throwing KeyNotFoundException when the secret exists but does not contain the requested key (sonar-adjacent fix found while extracting this helper: the prior code indexed Data[key] directly).")]
	public void ReadSecretString_Should_ReturnEmpty_WhenKeyIsMissing() {
		// Arrange
		V1Secret secret = new() {
			Data = new Dictionary<string, byte[]> {
				["SOME_OTHER_KEY"] = Encoding.UTF8.GetBytes("irrelevant")
			}
		};

		// Act
		string result = k8Commands.ReadSecretString(secret, "POSTGRES_PASSWORD");

		// Assert
		result.Should().BeEmpty(because: "a missing key must degrade to an empty value instead of throwing KeyNotFoundException");
	}

	[Test]
	[Description("Returns empty string instead of throwing when the secret's Data map itself is null.")]
	public void ReadSecretString_Should_ReturnEmpty_WhenDataMapIsNull() {
		// Arrange
		V1Secret secret = new() { Data = null };

		// Act
		string result = k8Commands.ReadSecretString(secret, "POSTGRES_PASSWORD");

		// Assert
		result.Should().BeEmpty(because: "a secret with no Data map must degrade to an empty value rather than throwing NullReferenceException");
	}
}
