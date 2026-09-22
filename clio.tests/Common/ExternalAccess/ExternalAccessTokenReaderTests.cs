using System;
using System.Text;
using Clio.Common.ExternalAccess;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common.ExternalAccess;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
internal sealed class ExternalAccessTokenReaderTests {

	#region Methods: Private

	// Builds a token with the shape the grantor site issues: three base64url segments, only the
	// middle one of which this reader looks at.
	private static string BuildToken(string payloadJson) {
		string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(payloadJson))
			.TrimEnd('=').Replace('+', '-').Replace('/', '_');
		return $"header.{payload}.signature";
	}

	#endregion

	[Test]
	[Description("Read returns the grant terms the token states, including the string-valued restriction flags")]
	public void Read_ShouldReturnGrantTerms_WhenTokenCarriesThem() {
		// Arrange
		string token = BuildToken("{"
			+ "\"exp\":1789651713,"
			+ "\"prop:ResourceId\":\"62821f5d-8af8-492a-8a82-03b575a94e69\","
			+ "\"prop:OwnerClientId\":\"4bua\","
			+ "\"prop:IsDataIsolationEnabled\":\"True\","
			+ "\"prop:IsSystemOperationsRestricted\":\"False\","
			+ "\"prop:ExpirationDate\":\"2026-09-22T20:59:59.9999999Z\"}");

		// Act
		ExternalAccessGrant result = ExternalAccessTokenReader.Read(token);

		// Assert
		result.AccessId.Should().Be("62821f5d8af8492a8a8203b575a94e69",
			because: "the access id keys the session cache and identifies the grant to the caller");
		result.OwnerClientId.Should().Be("4bua",
			because: "the owner client id names the customer site the grant belongs to");
		result.IsDataIsolationEnabled.Should().BeTrue(
			because: "the platform writes this flag as the STRING \"True\", not as a JSON boolean");
		result.IsSystemOperationsRestricted.Should().BeFalse(
			because: "\"False\" must not be read as a truthy non-empty string");
		result.GrantExpiresOnUtc.Should().NotBeNull(
			because: "the grant window is what tells the caller how long access lasts at all");
		result.TokenExpiresOnUtc.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1789651713),
			because: "the token window is 120 seconds and separate from the grant window");
	}

	[Test]
	[Description("Read returns null for a value that is not a readable three-segment token")]
	public void Read_ShouldReturnNull_WhenTokenIsNotAJwt() {
		// Arrange
		string token = "not-a-token";

		// Act
		ExternalAccessGrant result = ExternalAccessTokenReader.Read(token);

		// Assert
		result.Should().BeNull(
			because: "an unreadable token cannot key a cache entry; it must go straight to the exchange, "
				+ "which rejects it with a message naming the real problem");
	}

	[Test]
	[Description("Read returns null when the token carries no access-id claim")]
	public void Read_ShouldReturnNull_WhenAccessIdClaimIsAbsent() {
		// Arrange
		string token = BuildToken("{\"exp\":1789651713,\"prop:OwnerClientId\":\"4bua\"}");

		// Act
		ExternalAccessGrant result = ExternalAccessTokenReader.Read(token);

		// Assert
		result.Should().BeNull(
			because: "without prop:ResourceId there is no grant identity, so two different grants on one "
				+ "site would otherwise share a cached session");
	}

	[Test]
	[Description("Read accepts a token that still carries its Bearer prefix")]
	public void Read_ShouldStripBearerPrefix_WhenPresent() {
		// Arrange
		string token = "Bearer " + BuildToken("{\"prop:ResourceId\":\"62821f5d-8af8-492a-8a82-03b575a94e69\"}");

		// Act
		ExternalAccessGrant result = ExternalAccessTokenReader.Read(token);

		// Assert
		result.Should().NotBeNull(
			because: "a token copied out of an Authorization header carries the scheme and must still parse");
	}

	[Test]
	[Description("The access id becomes part of the session-cache FILE NAME, and the payload is never signature-checked, so a claim that is not a plain grant id must be refused here rather than sanitized downstream. A traversal segment would otherwise let an attacker-chosen token read, overwrite and delete another environment's cached session — and re-home its live cookies onto the attacker's host.")]
	[TestCase("x/../../sessions/prod-creatio-com_ab12cd34ef5678", TestName = "TraversalSegment")]
	[TestCase("../../../etc/passwd", TestName = "ParentDirectory")]
	[TestCase("a\\b", TestName = "WindowsSeparator")]
	[TestCase("not-a-guid", TestName = "PlainText")]
	[TestCase("", TestName = "Empty")]
	public void Read_ShouldReturnNull_WhenTheAccessIdIsNotAGrantId(string accessId) {
		// Arrange
		string token = BuildToken("{\"prop:ResourceId\":\"" + accessId + "\"}");

		// Act
		ExternalAccessGrant result = ExternalAccessTokenReader.Read(token);

		// Assert
		result.Should().BeNull(
			because: "an unusable grant id must degrade to \"no cache entry, go straight to the exchange\", "
				+ "never to a cache key carrying a path separator");
	}

	[Test]
	[Description("A GUID in any accepted spelling normalizes to the same cache key, so braces or upper case cannot produce a second cached session for one grant.")]
	[TestCase("62821f5d-8af8-492a-8a82-03b575a94e69", TestName = "Dashed")]
	[TestCase("{62821F5D-8AF8-492A-8A82-03B575A94E69}", TestName = "BracedUpperCase")]
	[TestCase("62821f5d8af8492a8a8203b575a94e69", TestName = "Compact")]
	public void Read_ShouldNormalizeTheAccessId_WhateverTheGuidSpelling(string accessId) {
		// Arrange
		string token = BuildToken("{\"prop:ResourceId\":\"" + accessId + "\"}");

		// Act
		ExternalAccessGrant result = ExternalAccessTokenReader.Read(token);

		// Assert
		result.AccessId.Should().Be("62821f5d8af8492a8a8203b575a94e69");
		result.AccessId.Should().NotContainAny(["/", "\\", "..", ":"],
			because: "the value is concatenated into a session-cache file name");
	}

	[Test]
	[Description("Read is documented to return null for an unreadable token, and the session provider depends on that. A payload that is not a JSON object, a claim of the wrong JSON type, or a millisecond-based exp must not throw a stack trace at the operator before the site is ever contacted.")]
	[TestCase("[1,2,3]", TestName = "PayloadIsAnArray")]
	[TestCase("\"scalar\"", TestName = "PayloadIsAScalar")]
	[TestCase("{\"prop:ResourceId\":12345}", TestName = "AccessIdIsANumber")]
	[TestCase("{\"prop:ResourceId\":\"62821f5d-8af8-492a-8a82-03b575a94e69\",\"prop:OwnerClientId\":7}",
		TestName = "OwnerClientIdIsANumber")]
	public void Read_ShouldNotThrow_WhenThePayloadHasAnUnexpectedShape(string payload) {
		// Arrange
		string token = BuildToken(payload);

		// Act
		Func<ExternalAccessGrant> act = () => ExternalAccessTokenReader.Read(token);

		// Assert
		act.Should().NotThrow(
			because: "an unverified payload is attacker-shaped, and Read documents null rather than an exception");
	}

	[Test]
	[Description("A millisecond-based exp parses as a long and then makes FromUnixTimeSeconds throw. The grant still reads; only the expiry is unknown.")]
	public void Read_ShouldIgnoreAnOutOfRangeExpiry_WithoutThrowing() {
		// Arrange
		string token = BuildToken(
			"{\"prop:ResourceId\":\"62821f5d-8af8-492a-8a82-03b575a94e69\",\"exp\":1789651713000}");

		// Act
		ExternalAccessGrant result = ExternalAccessTokenReader.Read(token);

		// Assert
		result.Should().NotBeNull(because: "an unreadable expiry is not a reason to refuse the grant itself");
		result.TokenExpiresOnUtc.Should().BeNull(
			because: "a value outside the Unix-seconds range says nothing about when the token expires");
	}
}
