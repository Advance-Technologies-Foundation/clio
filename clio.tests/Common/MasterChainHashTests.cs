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

	// The calls of each chain - pattern, options, timeout and what the call writes (the placeholder, or the evaluator's
	// declaring type and name) - in order, hashed with StepHash below. Master's source (clio/Common/SensitiveErrorTextRedactor.cs
	// at 1915d7543: Redact from line 524, RedactJsonCredentialProperties from 591, RedactCredentials from 625) makes these
	// calls in this order with these placeholders and evaluators; the pair lambda is named CredentialPairToPlaceholder
	// here. What the evaluators write is pinned by the output pin
	// (RedactionMasterDifferentialTests.MasterChain_ShouldReproduceMastersOutput_ForEveryCorpusString), recorded from
	// master's build.
	internal const string MasterRedactStepsHash = "9681156d4443ab34465f6b8c762d92964e826413f7a3520a1e66d4217cb5478d";

	internal const string MasterCredentialsStepsHash = "d4418d66d7d2f476cfa7aa428d7888be35799a9edcf4233cef0e08cfe81a32c8";

	internal const string MasterJsonStepsHash = "b8c2395c54b1d6fb5026741410c15959856d1b8371aa2ab76b7add4feb8b9338";

	/// <summary>The hash of a chain's calls: each pattern, its options, its timeout and what it writes, in order.</summary>
	internal static string StepHash(IEnumerable<SensitiveErrorTextRedactor.MasterChain.ChainStep> steps) {
		const char unit = (char)0x1f;
		const char record = (char)0x1e;
		string text = string.Join(record, steps.Select(step => step.Rule.ToString() + unit + (int)step.Rule.Options + unit
			+ (int)step.Rule.MatchTimeout.TotalMilliseconds + unit + step.Writes));
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
	}

	[Test]
	[Category("Unit")]
	[Description("Condition 1, the CALLS: Redact, RedactCredentials and RedactJsonCredentialProperties of the frozen chain "
		+ "run their step lists and nothing else, and the lists hash - pattern, options, timeout and what each call writes, "
		+ "in order - to the recorded constants. A reordered, dropped or added call, or a changed placeholder or evaluator, "
		+ "fails here; what an evaluator's body writes is pinned by the output pin.")]
	public void MasterChain_ShouldMakeMastersCallsInMastersOrder() {
		// Arrange

		// Act
		string redact = StepHash(SensitiveErrorTextRedactor.MasterChain.RedactSteps);
		string credentials = StepHash(SensitiveErrorTextRedactor.MasterChain.CredentialsSteps);
		string json = StepHash(SensitiveErrorTextRedactor.MasterChain.JsonSteps);

		// Assert
		redact.Should().Be(MasterRedactStepsHash, because: $"Redact makes master {MasterCommit}'s calls in its order");
		credentials.Should().Be(MasterCredentialsStepsHash, because: $"RedactCredentials makes master {MasterCommit}'s calls in its order");
		json.Should().Be(MasterJsonStepsHash, because: $"RedactJsonCredentialProperties makes master {MasterCommit}'s call");
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
