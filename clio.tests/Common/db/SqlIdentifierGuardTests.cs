using Clio.Common.db;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common.db;

[TestFixture]
[Property("Module", "Common")]
[Category("Unit")]
public class SqlIdentifierGuardTests {

	[TestCase("MyDatabase")]
	[TestCase("my_database_1")]
	[TestCase("my-database")]
	[TestCase("my.database")]
	[TestCase("db$1")]
	[TestCase("db#1")]
	[Description("Accepts identifiers built only from letters, digits, underscore, hyphen, period, dollar and hash.")]
	public void EnsureValidIdentifier_ShouldNotThrow_WhenNameIsAllowListedCharacters(string name) {
		// Arrange
		// Act
		System.Action act = () => SqlIdentifierGuard.EnsureValidIdentifier(name, "dbName");

		// Assert
		act.Should().NotThrow(because: $"'{name}' only contains characters from the SQL identifier allow-list");
	}

	[TestCase("my]database", Description = "Contains a closing bracket, which could break out of a T-SQL [name] identifier.")]
	[TestCase("my\"database", Description = "Contains a double quote, which could break out of a Postgres \"name\" identifier.")]
	[TestCase("my'database", Description = "Contains a single quote, a SQL string-literal delimiter.")]
	[TestCase("my;database", Description = "Contains a statement separator that could terminate the current statement.")]
	[TestCase("my database", Description = "Contains whitespace, which is not part of the identifier allow-list.")]
	[Description("Rejects identifiers containing SQL metacharacters that could break out of a bracketed/quoted identifier position.")]
	public void EnsureValidIdentifier_ShouldThrowArgumentException_WhenNameContainsDisallowedCharacters(string name) {
		// Arrange
		// Act
		System.Action act = () => SqlIdentifierGuard.EnsureValidIdentifier(name, "dbName");

		// Assert
		act.Should().Throw<System.ArgumentException>(because: $"'{name}' contains a character outside the SQL identifier allow-list");
	}

	[Test]
	[Description("Rejects a null identifier instead of letting it reach string interpolation as the literal text 'null'.")]
	public void EnsureValidIdentifier_ShouldThrowArgumentException_WhenNameIsNull() {
		// Arrange
		// Act
		System.Action act = () => SqlIdentifierGuard.EnsureValidIdentifier(null, "dbName");

		// Assert
		act.Should().Throw<System.ArgumentException>(because: "a null identifier is not a valid database name");
	}

	[Test]
	[Description("Rejects an empty identifier since an empty database name is never valid.")]
	public void EnsureValidIdentifier_ShouldThrowArgumentException_WhenNameIsEmpty() {
		// Arrange
		// Act
		System.Action act = () => SqlIdentifierGuard.EnsureValidIdentifier(string.Empty, "dbName");

		// Assert
		act.Should().Throw<System.ArgumentException>(because: "an empty identifier is not a valid database name");
	}

	[Test]
	[Description("Rejects an identifier longer than the 128-character bound to keep the allow-list regex evaluation cheap and bounded.")]
	public void EnsureValidIdentifier_ShouldThrowArgumentException_WhenNameExceedsMaxLength() {
		// Arrange
		string tooLongName = new('a', 129);

		// Act
		System.Action act = () => SqlIdentifierGuard.EnsureValidIdentifier(tooLongName, "dbName");

		// Assert
		act.Should().Throw<System.ArgumentException>(because: "identifiers longer than 128 characters are rejected by the allow-list bound");
	}

	[Test]
	[Description("Accepts an identifier exactly at the 128-character boundary.")]
	public void EnsureValidIdentifier_ShouldNotThrow_WhenNameIsExactlyMaxLength() {
		// Arrange
		string maxLengthName = new('a', 128);

		// Act
		System.Action act = () => SqlIdentifierGuard.EnsureValidIdentifier(maxLengthName, "dbName");

		// Assert
		act.Should().NotThrow(because: "a 128-character identifier is within the allow-list bound");
	}

	[Test]
	[Description("Includes the offending parameter name in the thrown exception so callers can identify which argument failed validation.")]
	public void EnsureValidIdentifier_ShouldIncludeParamName_WhenNameIsInvalid() {
		// Arrange
		// Act
		System.Action act = () => SqlIdentifierGuard.EnsureValidIdentifier("bad]name", "dbName");

		// Assert
		act.Should().Throw<System.ArgumentException>()
			.Which.ParamName.Should().Be("dbName", because: "the exception should identify which parameter failed validation");
	}
}
