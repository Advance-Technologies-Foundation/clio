using System;
using Clio.Common.Studio;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture, Category("Unit"), Property("Module", "Common")]
public class StudioInputsTests {
	[Test, Description("Generated credentials are stable on reruns and explicit recipient values take precedence.")]
	public void Resolve_ShouldReuseCredentials_AndHonorExplicitValues() {
		JObject profile = JObject.Parse("""{"inputs":{"password":{"generate":"password","required":true},"host":{"value":"localhost"}}}""");
		var first = StudioInputs.Resolve(profile, new(), new(), true);
		var second = StudioInputs.Resolve(profile, new() { ["host"] = "recipient" }, first.Values, true);
		second.Values.Value<string>("password").Should().Be(first.Values.Value<string>("password"), "retries must not rotate database credentials");
		second.Values.Value<string>("host").Should().Be("recipient", "the recipient's explicit values override producer defaults");
		second.Missing.Should().BeEmpty("all required inputs are resolved");
	}

	[Test, Description("Null defaults do not hide previously saved credentials on a deployment retry.")]
	public void Resolve_ShouldReusePrevious_WhenDefaultIsJsonNull() {
		var profile = JObject.Parse("""{"inputs":{"password":{"value":null,"generate":"password","required":true}}}""");
		var result = StudioInputs.Resolve(profile, new(), new() { ["password"] = "saved" }, true);
		result.Values.Value<string>("password").Should().Be("saved", "a JSON null placeholder must not rotate the database credential");
	}

	[Test, Description("Escaped shell variables stay literal while handoff inputs expand without altering supplied values.")]
	public void Expand_ShouldPreserveEscapedShellVariables() {
		var result = StudioInputs.Expand(new JValue("path=$${PGDATA};password=${password}"), new() { ["password"] = "$${literal}" });
		result.Value<string>().Should().Be("path=${PGDATA};password=$${literal}", "shell syntax is escaped in the template, never reinterpreted inside recipient values");
	}

	[Test, Description("Missing and blank required inputs are reported together without secret values.")]
	public void Resolve_ShouldInventoryMissingInputs() {
		var result = StudioInputs.Resolve(JObject.Parse("""{"inputs":{"token":{"required":true,"secret":true},"host":{"required":true,"value":"  "}}}"""), new(), new(), false);
		result.Missing.Should().HaveCount(2, "an agent should collect all missing inputs in one interaction");
		result.Values.Should().BeEmpty("blank required strings are unresolved");
		result.Missing[0].Value<bool>("secret").Should().BeTrue("the agent must know that the omitted token is confidential");
	}

	[Test, Description("Expansion preserves types and inserts punctuation as data rather than JSON syntax.")]
	public void Expand_ShouldPreserveScalars_AndEscapeSecrets() {
		var result = StudioInputs.Expand(JObject.Parse("""{"port":"${port}","connection":"Password=${password};"}"""), new() { ["port"] = 5432, ["password"] = "a\"\n${other}" });
		result["port"].Type.Should().Be(JTokenType.Integer, "numeric Kubernetes fields must stay numeric");
		result.Value<string>("connection").Should().Be("Password=a\"\n${other};", "secret values are never recursively interpreted");
		JObject.Parse(result.ToString()).Should().NotBeNull("embedded punctuation must produce valid JSON");
	}

	[TestCase("../outside"), TestCase("C:/outside"), TestCase("repo/../../outside"), TestCase("NUL"), TestCase("repo/.git")]
	[Description("Handoff sources cannot escape the destination or write Git metadata paths.")]
	public void Sources_ShouldRejectUnsafePath(string path) {
		JObject profile = new() { ["sources"] = new JArray(new JObject { ["name"] = "repo", ["url"] = "https://example.test/repo.git", ["commit"] = new string('a', 40), ["branch"] = "main", ["path"] = path }) };
		Action act = () => StudioProfile.Sources(profile);
		act.Should().Throw<ArgumentException>("all path validation happens before Git or filesystem writes");
	}
}
