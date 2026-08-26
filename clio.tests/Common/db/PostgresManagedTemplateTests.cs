using System;
using Clio.Common.db;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common.db;

[TestFixture]
[Property("Module", "Common")]
public sealed class PostgresManagedTemplateTests {
	[Test]
	[Category("Unit")]
	[Description("Parses the metadata written by clio when a PostgreSQL template is created.")]
	public void TryParseManagedTemplateMetadata_ValidComment_ReturnsFields() {
		// Arrange
		const string comment = "sourceFile:Studio.zip|createdDate:2026-08-22T10:20:30.0000000+00:00|version:1.0";

		// Act
		bool parsed = Postgres.TryParseManagedTemplateMetadata(comment, out string sourceFile,
			out DateTimeOffset createdDate, out string version);

		// Assert
		parsed.Should().BeTrue(because: "all required clio metadata fields are valid");
		sourceFile.Should().Be("Studio.zip", because: "the source file identifies the template origin");
		createdDate.Should().Be(DateTimeOffset.Parse("2026-08-22T10:20:30+00:00"),
			because: "the recorded creation timestamp should be preserved");
		version.Should().Be("1.0", because: "the metadata version should be exposed to callers");
	}

	[TestCase(null)]
	[TestCase("")]
	[TestCase("sourceFile:Studio.zip|version:1.0")]
	[TestCase("sourceFile:Studio.zip|createdDate:not-a-date|version:1.0")]
	[Category("Unit")]
	[Description("Rejects comments that do not contain complete valid clio template metadata.")]
	public void TryParseManagedTemplateMetadata_MalformedComment_ReturnsFalse(string comment) {
		// Arrange

		// Act
		bool parsed = Postgres.TryParseManagedTemplateMetadata(comment, out _, out _, out _);

		// Assert
		parsed.Should().BeFalse(because: "unmarked or malformed databases must never become deletion candidates");
	}

	[Test]
	[Category("Unit")]
	[Description("Quotes PostgreSQL database identifiers and escapes embedded quote characters.")]
	public void QuoteIdentifier_EmbeddedQuote_EscapesIdentifier() {
		// Arrange
		const string databaseName = "template\"special";

		// Act
		string quoted = Postgres.QuoteIdentifier(databaseName);

		// Assert
		quoted.Should().Be("\"template\"\"special\"",
			because: "database names must be safe when used in PostgreSQL DDL statements");
	}

	[Test]
	[Category("Unit")]
	[Description("Builds PostgreSQL connection strings without parsing credential punctuation as options.")]
	public void BuildConnectionString_PasswordContainsSemicolon_PreservesPassword() {
		// Arrange
		const string password = "part-one;part-two";

		// Act
		Npgsql.NpgsqlConnectionStringBuilder parsed = new(
			Postgres.BuildConnectionString("localhost", 5432, "postgres", password));

		// Assert
		parsed.Password.Should().Be(password,
			because: "valid credential punctuation must not break template inventory or pruning");
	}
}
