using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture, Category("Integration"), Property("Module", "Common")]
public class OperatorBundleTests {
	[Test, Description("The actual shipped profile disables registry building and never includes a registry or node daemonset.")]
	public void Bundle_ShouldContainRegistryFreeOperator_WhenShipped() {
		// Arrange
		string path = Path.Combine(AppContext.BaseDirectory, "tpl", "operator", "rancher-desktop", "operator.json");
		// Act
		JObject bundle = JObject.Parse(File.ReadAllText(path));
		JToken[] items = bundle["items"].ToArray();
		JToken manager = items.Single(r => r.Value<string>("kind") == "Deployment")["spec"]["template"]["spec"]["containers"][0];
		var variables = manager["env"].ToDictionary(v => v.Value<string>("name"), v => v.Value<string>("value"));
		// Assert
		items.Should().NotContain(r => r.Value<string>("kind") == "DaemonSet", "local bootstrap does not need registry node agents or node tuning");
		items.Should().NotContain(r => r["metadata"].Value<string>("name").Contains("nexus"), "the user explicitly excludes local Nexus");
		variables["Operator__NexusRegistryEnabled"].Should().Be("false", "the registry catalog must be disabled");
		variables["Operator__ImageBuildEnabled"].Should().Be("false", "in-cluster builds require a registry which this profile does not have");
		variables["Operator__NexusNodeHostPort"].Should().Be("0", "no host registry port should be reserved");
		manager["readinessProbe"].Should().NotBeNull("rollout success must wait for an accepting server");
	}
}
