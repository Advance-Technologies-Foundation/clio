using Clio.Command.McpServer;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class SensitiveErrorTextRedactorTests {

	[Test]
	[Category("Unit")]
	[Description("Returns an empty string for null/empty input so callers can concatenate the result unconditionally.")]
	public void Redact_ShouldReturnEmptyString_WhenInputIsNullOrEmpty() {
		// Arrange

		// Act
		string fromNull = SensitiveErrorTextRedactor.Redact(null);
		string fromEmpty = SensitiveErrorTextRedactor.Redact(string.Empty);

		// Assert
		fromNull.Should().BeEmpty(because: "null must degrade to an empty, safe-to-concatenate string");
		fromEmpty.Should().BeEmpty(because: "empty input has nothing to redact");
	}

	[Test]
	[Category("Unit")]
	[Description("Leaves a clean logical message unchanged so the agent's self-correction signal is preserved.")]
	public void Redact_ShouldReturnTextUnchanged_WhenNoSensitiveTokenIsPresent() {
		// Arrange
		const string message = "Environment 'Foo' not found. Package 'Bar' is missing.";

		// Act
		string result = SensitiveErrorTextRedactor.Redact(message);

		// Assert
		result.Should().Be(message, because: "messages without paths/URIs/credentials must pass through verbatim");
	}

	[Test]
	[Category("Unit")]
	[Description("Redacts a full URI (including an embedded user:password authority and the target host) used by *-by-credentials flows.")]
	public void Redact_ShouldRedactUriWithEmbeddedCredentialsAndHost() {
		// Arrange
		const string message = "POST https://admin:s3cret@crm.contoso.com/0/ServiceModel/EntityDataService.svc returned 401.";

		// Act
		string result = SensitiveErrorTextRedactor.Redact(message);

		// Assert
		result.Should().NotContain("crm.contoso.com", because: "the target host must not leak");
		result.Should().NotContain("s3cret", because: "the embedded credential must not leak");
		result.Should().Contain("[redacted-uri]", because: "the URI is replaced by a stable placeholder");
		result.Should().Contain("returned 401", because: "the trailing logical detail must survive");
	}

	[Test]
	[Category("Unit")]
	[Description("Redacts a Windows drive-rooted absolute path.")]
	public void Redact_ShouldRedactWindowsAbsolutePath() {
		// Arrange
		const string message = @"Cannot read C:\Users\alex\AppData\Roaming\clio\appsettings.json.";

		// Act
		string result = SensitiveErrorTextRedactor.Redact(message);

		// Assert
		result.Should().NotContain(@"C:\Users\alex", because: "absolute Windows paths must be redacted");
		result.Should().Contain("[redacted-path]", because: "the path is replaced by a stable placeholder");
	}

	[Test]
	[Category("Unit")]
	[Description("Redacts a POSIX absolute path under a well-known home root.")]
	public void Redact_ShouldRedactPosixAbsolutePath() {
		// Arrange
		const string message = "Config /Users/alex/.clio/appsettings.json could not be parsed.";

		// Act
		string result = SensitiveErrorTextRedactor.Redact(message);

		// Assert
		result.Should().NotContain("/Users/alex", because: "absolute POSIX paths under home roots must be redacted");
		result.Should().Contain("[redacted-path]", because: "the path is replaced by a stable placeholder");
		result.Should().Contain("could not be parsed", because: "the trailing logical detail must survive");
	}

	[Test]
	[Category("Unit")]
	[Description("Does not redact a relative URL path fragment that is not an absolute filesystem path.")]
	public void Redact_ShouldNotRedactRelativeUrlPathFragment() {
		// Arrange
		const string message = "Endpoint /rest/CreatioApiGateway/GetSysInfo returned no body.";

		// Act
		string result = SensitiveErrorTextRedactor.Redact(message);

		// Assert
		result.Should().Be(message,
			because: "a relative URL fragment is not a sensitive absolute filesystem path and must be left intact");
	}

	[Test]
	[Category("Unit")]
	[Description("Redacts a credential key=value pair while keeping the key so the message still reads sensibly.")]
	public void Redact_ShouldRedactCredentialValueButKeepKey() {
		// Arrange
		const string message = "Auth rejected: password=hunter2 token=abc.def.ghi";

		// Act
		string result = SensitiveErrorTextRedactor.Redact(message);

		// Assert
		result.Should().NotContain("hunter2", because: "the password value must be redacted");
		result.Should().NotContain("abc.def.ghi", because: "the token value must be redacted");
		result.Should().Contain("password=[redacted]", because: "the key is kept and only the value is redacted");
		result.Should().Contain("token=[redacted]", because: "every credential key/value pair is redacted");
	}

	[Test]
	[Category("Unit")]
	[Description("Redacts the host/database values inside a connection-string-style message.")]
	public void Redact_ShouldRedactConnectionStringHostAndDatabase() {
		// Arrange
		const string message = "DB error. Server=sql-prod-01;Database=Creatio_Prod;Uid=sa;Password=p@ss";

		// Act
		string result = SensitiveErrorTextRedactor.Redact(message);

		// Assert
		result.Should().NotContain("sql-prod-01", because: "the connection-string host must not leak");
		result.Should().NotContain("Creatio_Prod", because: "the database name must not leak");
		result.Should().NotContain("p@ss", because: "the connection-string password must not leak");
	}
}
