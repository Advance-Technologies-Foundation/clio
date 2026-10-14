using System;
using System.Collections.Generic;
using System.Net;
using Clio.Common.BrowserSession;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common.BrowserSession;

/// <summary>
/// ENG-103287: the headless-browser download goes through HTTPS_PROXY / HTTP_PROXY with NO_PROXY even in MCP mode,
/// where the default proxy is cleared for calls to Creatio.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class AmbientProxyTests {

	private static AmbientProxy Proxy(Dictionary<string, string> environment, IWebProxy systemProxy = null) =>
		new(name => environment.GetValueOrDefault(name), systemProxy);

	private static IWebProxy SystemProxy(string address) => new WebProxy(address);

	[Test]
	[Description("An https download uses HTTPS_PROXY, with the credentials its URL carries.")]
	public void GetProxy_Should_Use_Https_Proxy_With_Its_Credentials() {
		// Arrange
		AmbientProxy proxy = Proxy(new() { ["HTTPS_PROXY"] = "http://agent:p%40ss@proxy.corp:3128" });

		// Act
		Uri used = proxy.GetProxy(new Uri("https://storage.googleapis.com/x.zip"));

		// Assert
		used.Host.Should().Be("proxy.corp", because: "the corporate proxy is the way out");
		used.Port.Should().Be(3128, because: "its port is kept");
		((NetworkCredential)proxy.Credentials).Password.Should().Be("p@ss", because: "the proxy's credentials are decoded");
	}

	[Test]
	[Description("The lowercase spellings work, HTTP_PROXY is the fallback for https, and a bare host:port is accepted.")]
	public void GetProxy_Should_Accept_Lowercase_And_Fallback_Spellings() {
		// Arrange
		AmbientProxy proxy = Proxy(new() { ["http_proxy"] = "proxy.corp:8080" });

		// Act
		Uri used = proxy.GetProxy(new Uri("https://storage.googleapis.com/x.zip"));

		// Assert
		used.Should().Be(new Uri("http://proxy.corp:8080"), because: "Unix tools set the lowercase spelling, often without a scheme");
	}

	[TestCase("corp.example", "mirror.corp.example", true)]
	[TestCase(".corp.example", "corp.example", true)]
	[TestCase("*", "storage.googleapis.com", true)]
	[TestCase("corp.example", "storage.googleapis.com", false)]
	[Description("NO_PROXY bypasses the proxy for the hosts and subdomains it lists.")]
	public void IsBypassed_Should_Follow_No_Proxy(string noProxy, string host, bool expected) {
		// Arrange
		AmbientProxy proxy = Proxy(new() { ["HTTPS_PROXY"] = "http://proxy.corp:3128", ["NO_PROXY"] = noProxy });

		// Act
		bool bypassed = proxy.IsBypassed(new Uri($"https://{host}/x.zip"));

		// Assert
		bypassed.Should().Be(expected, because: "NO_PROXY decides which hosts go direct");
	}

	[Test]
	[Description("Without proxy variables the download goes direct.")]
	public void IsBypassed_Should_Be_True_Without_A_Proxy() {
		// Arrange
		AmbientProxy proxy = Proxy(new());

		// Act
		bool bypassed = proxy.IsBypassed(new Uri("https://storage.googleapis.com/x.zip"));

		// Assert
		bypassed.Should().BeTrue(because: "there is no proxy to use");
	}

	[Test]
	[Description("Without proxy variables the system proxy (Windows / macOS settings, PAC included) is used, with the default credentials.")]
	public void GetProxy_Should_Fall_Back_To_The_System_Proxy() {
		// Arrange
		AmbientProxy proxy = Proxy(new(), SystemProxy("http://pac-chosen.corp:8080"));

		// Act
		Uri used = proxy.GetProxy(new Uri("https://storage.googleapis.com/x.zip"));

		// Assert
		used.Should().Be(new Uri("http://pac-chosen.corp:8080"), because: "a machine configured through its OS settings reaches the internet that way");
		proxy.Credentials.Should().BeSameAs(CredentialCache.DefaultCredentials, because: "a system proxy authenticates as the user");
	}

	[Test]
	[Description("NO_PROXY bypasses the system proxy too.")]
	public void IsBypassed_Should_Follow_No_Proxy_Over_The_System_Proxy() {
		// Arrange
		AmbientProxy proxy = Proxy(new() { ["NO_PROXY"] = "mirror.corp" }, SystemProxy("http://pac-chosen.corp:8080"));

		// Act
		bool bypassed = proxy.IsBypassed(new Uri("https://mirror.corp/cft/x.zip"));
		bool proxied = proxy.IsBypassed(new Uri("https://storage.googleapis.com/x.zip"));

		// Assert
		bypassed.Should().BeTrue(because: "NO_PROXY applies whichever proxy would be used");
		proxied.Should().BeFalse(because: "other hosts still go through the system proxy");
	}
}
