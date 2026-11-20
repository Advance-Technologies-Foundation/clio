using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>
/// Option B, condition 1: the frozen master chain (<see cref="SensitiveErrorTextRedactor.MasterChain"/>) is master
/// <c>1915d7543a0f3612cfce32769c3b289d14d1daaa</c>'s chain, pattern for pattern, option for option and in master's order.
/// </summary>
[TestFixture]
[Property("Module", "Common")]
public sealed class MasterChainHashTests {

	/// <summary>The master commit the constants below were recorded from.</summary>
	internal const string MasterCommit = "1915d7543a0f3612cfce32769c3b289d14d1daaa";

	// Recorded from master's own build (clio/bin/Debug/net10.0/clio.dll at 1915d7543): the private GeneratedRegex members
	// of SensitiveErrorTextRedactor, read by reflection in the order master's Redact, RedactCredentials and
	// RedactJsonCredentialProperties call them, hashed with ChainHash below.
	internal const string MasterRedactChainHash = "8753c39c070d71f4b19d1bbfdffe06ec0af22dfcdacda9bf2330dcc4eb967055";

	internal const string MasterCredentialsChainHash = "44da14ed11ca3c58df5cd9a691d7fb89cbe3bacb374f86afc010a3bf85741e16";

	internal const string MasterJsonChainHash = "62cda4d0b6b722900c9b9bd2bacafc3eb991fb26b750f89c7c981945572b2ed7";

	/// <summary>The names of master's patterns in master's Redact order, as master's file declares them.</summary>
	internal static readonly string[] MasterRedactOrder = [
		"JsonCredentialPropertyRegex", "UriRegex", "JwtRegex", "BearerTokenRegex", "EmailRegex", "HostPortRegex",
		"WindowsPathRegex", "PosixPathRegex", "CredentialPairRegex"
	];

	/// <summary>The names of master's patterns in master's RedactCredentials order.</summary>
	internal static readonly string[] MasterCredentialsOrder = [
		"JsonCredentialPropertyRegex", "UriRegex", "JwtRegex", "BearerTokenRegex", "CredentialPairRegex"
	];

	/// <summary>The hash of an ordered chain: each pattern string, its options and its timeout, in order.</summary>
	internal static string ChainHash(IEnumerable<Regex> chain) {
		const char unit = (char)0x1f;
		const char record = (char)0x1e;
		string text = string.Join(record, chain.Select(rule =>
			rule.ToString() + unit + (int)rule.Options + unit + (int)rule.MatchTimeout.TotalMilliseconds));
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
	}

	[Test]
	[Category("Unit")]
	[Description("The frozen chain's pattern strings, options, timeouts and order hash to the constants recorded from "
		+ "master 1915d7543's build. A pattern edited in MasterChain fails here: clio's additions belong in Extra, Site or "
		+ "ExtraCredentials, never in the frozen copy.")]
	public void MasterChain_ShouldBeMastersPatternsOptionsAndOrder() {
		// Arrange

		// Act
		string redact = ChainHash(SensitiveErrorTextRedactor.MasterChain.RedactOrder);
		string credentials = ChainHash(SensitiveErrorTextRedactor.MasterChain.CredentialsOrder);
		string json = ChainHash(SensitiveErrorTextRedactor.MasterChain.JsonOrder);

		// Assert
		redact.Should().Be(MasterRedactChainHash, because: $"Redact's chain is master {MasterCommit}'s");
		credentials.Should().Be(MasterCredentialsChainHash, because: $"RedactCredentials' chain is master {MasterCommit}'s");
		json.Should().Be(MasterJsonChainHash, because: $"RedactJsonCredentialProperties' chain is master {MasterCommit}'s");
	}
}
