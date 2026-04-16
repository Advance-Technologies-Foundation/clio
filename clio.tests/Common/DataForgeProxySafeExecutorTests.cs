using System;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common.DataForge;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Property("Module", "Common")]
[NonParallelizable]
public sealed class DataForgeProxySafeExecutorTests {
	private DataForgeProxySafeExecutor _executor = null!;

	[SetUp]
	public void SetUp() {
		_executor = new DataForgeProxySafeExecutor();
	}

	[TearDown]
	public void TearDown() {
		// Ensure we clean up any env vars that might leak if a test fails.
		Environment.SetEnvironmentVariable("HTTP_PROXY", null);
		Environment.SetEnvironmentVariable("HTTPS_PROXY", null);
		Environment.SetEnvironmentVariable("ALL_PROXY", null);
		Environment.SetEnvironmentVariable("http_proxy", null);
		Environment.SetEnvironmentVariable("https_proxy", null);
		Environment.SetEnvironmentVariable("all_proxy", null);
		Environment.SetEnvironmentVariable("NO_PROXY", null);
		Environment.SetEnvironmentVariable("no_proxy", null);
	}

	[Test]
	[Category("Unit")]
	[Description("Poisoned loopback proxy env vars are cleared during execution and restored after.")]
	public async Task ExecuteAsync_Should_Clear_And_Restore_Poisoned_Proxy_Env_Vars() {
		// Arrange
		Environment.SetEnvironmentVariable("HTTP_PROXY", "http://127.0.0.1:9");
		Environment.SetEnvironmentVariable("HTTPS_PROXY", "http://127.0.0.1:9");
		Environment.SetEnvironmentVariable("ALL_PROXY", "http://127.0.0.1:9");

		string? httpProxyDuringExec = null;
		string? httpsProxyDuringExec = null;
		string? allProxyDuringExec = null;

		// Act
		await _executor.ExecuteAsync(async () => {
			httpProxyDuringExec = Environment.GetEnvironmentVariable("HTTP_PROXY");
			httpsProxyDuringExec = Environment.GetEnvironmentVariable("HTTPS_PROXY");
			allProxyDuringExec = Environment.GetEnvironmentVariable("ALL_PROXY");
			return await Task.FromResult("ok");
		});

		// Assert
		httpProxyDuringExec.Should().BeNull(because: "HTTP_PROXY should be cleared during execution");
		httpsProxyDuringExec.Should().BeNull(because: "HTTPS_PROXY should be cleared during execution");
		allProxyDuringExec.Should().BeNull(because: "ALL_PROXY should be cleared during execution");

		Environment.GetEnvironmentVariable("HTTP_PROXY").Should().Be("http://127.0.0.1:9",
			because: "HTTP_PROXY should be restored after execution");
		Environment.GetEnvironmentVariable("HTTPS_PROXY").Should().Be("http://127.0.0.1:9",
			because: "HTTPS_PROXY should be restored after execution");
		Environment.GetEnvironmentVariable("ALL_PROXY").Should().Be("http://127.0.0.1:9",
			because: "ALL_PROXY should be restored after execution");
	}

	[Test]
	[Category("Unit")]
	[Description("Valid non-loopback proxy env vars remain enabled during execution so corporate proxies keep working.")]
	public async Task ExecuteAsync_Should_Keep_Valid_Proxy_Env_Vars() {
		// Arrange
		Environment.SetEnvironmentVariable("HTTP_PROXY", "http://proxy.corp.example:8080");
		Environment.SetEnvironmentVariable("HTTPS_PROXY", "http://proxy.corp.example:8080");
		Environment.SetEnvironmentVariable("ALL_PROXY", "http://proxy.corp.example:8080");

		string? httpProxyDuringExec = null;
		string? httpsProxyDuringExec = null;
		string? allProxyDuringExec = null;
		string? noProxyDuringExec = null;

		// Act
		await _executor.ExecuteAsync(async () => {
			httpProxyDuringExec = Environment.GetEnvironmentVariable("HTTP_PROXY");
			httpsProxyDuringExec = Environment.GetEnvironmentVariable("HTTPS_PROXY");
			allProxyDuringExec = Environment.GetEnvironmentVariable("ALL_PROXY");
			noProxyDuringExec = Environment.GetEnvironmentVariable("NO_PROXY");
			return await Task.FromResult("ok");
		}, "dataforge.example.com");

		// Assert
		httpProxyDuringExec.Should().Be("http://proxy.corp.example:8080",
			because: "valid corporate HTTP proxies must remain available during Data Forge execution");
		httpsProxyDuringExec.Should().Be("http://proxy.corp.example:8080",
			because: "valid corporate HTTPS proxies must remain available during Data Forge execution");
		allProxyDuringExec.Should().Be("http://proxy.corp.example:8080",
			because: "valid corporate ALL_PROXY settings must remain available during Data Forge execution");
		noProxyDuringExec.Should().Be("dataforge.example.com",
			because: "the target host should still be bypassed explicitly through NO_PROXY");
	}

	[Test]
	[Category("Unit")]
	[Description("Target host is appended to NO_PROXY during execution.")]
	public async Task ExecuteAsync_Should_Append_Host_To_NoProxy() {
		// Arrange
		Environment.SetEnvironmentVariable("NO_PROXY", "localhost");
		string? noProxyDuringExec = null;

		// Act
		await _executor.ExecuteAsync(async () => {
			noProxyDuringExec = Environment.GetEnvironmentVariable("NO_PROXY");
			return await Task.FromResult("ok");
		}, "data-forge-stage.bpmonline.com");

		// Assert
		noProxyDuringExec.Should().Be("localhost,data-forge-stage.bpmonline.com",
			because: "the target host should be appended to the existing NO_PROXY value");

		Environment.GetEnvironmentVariable("NO_PROXY").Should().Be("localhost",
			because: "NO_PROXY should be restored to the original value after execution");
	}

	[Test]
	[Category("Unit")]
	[Description("When NO_PROXY is empty, target host becomes the sole entry.")]
	public void Execute_Should_Set_NoProxy_When_Empty() {
		// Arrange
		Environment.SetEnvironmentVariable("NO_PROXY", null);
		string? noProxyDuringExec = null;

		// Act
		_executor.Execute(() => {
			noProxyDuringExec = Environment.GetEnvironmentVariable("NO_PROXY");
			return "ok";
		}, "dataforge.example.com");

		// Assert
		noProxyDuringExec.Should().Be("dataforge.example.com",
			because: "the target host should become the only NO_PROXY entry when the variable was originally empty");
		Environment.GetEnvironmentVariable("NO_PROXY").Should().BeNull(
			because: "NO_PROXY should be restored to the original empty state after execution");
	}

	[Test]
	[Category("Unit")]
	[Description("Proxy vars are restored even when the action throws.")]
	public void Execute_Should_Restore_Proxy_Vars_On_Exception() {
		// Arrange
		Environment.SetEnvironmentVariable("HTTP_PROXY", "http://proxy:8080");

		// Act
		Action act = () => _executor.Execute<string>(() => throw new InvalidOperationException("test"), "host");

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "the executor should rethrow the original action failure");
		Environment.GetEnvironmentVariable("HTTP_PROXY").Should().Be("http://proxy:8080",
			because: "proxy vars must be restored even when the action throws");
	}

	[Test]
	[Category("Unit")]
	[Description("Concurrent calls are serialized through the internal lock.")]
	public async Task ExecuteAsync_Should_Serialize_Concurrent_Calls() {
		// Arrange
		int concurrentCount = 0;
		int maxConcurrent = 0;
		object counterLock = new();

		async Task<string> Action() {
			int current;
			lock (counterLock) {
				concurrentCount++;
				current = concurrentCount;
				if (current > maxConcurrent) maxConcurrent = current;
			}

			await Task.Delay(50);

			lock (counterLock) {
				concurrentCount--;
			}

			return "ok";
		}

		// Act
		Task<string>[] tasks = [
			_executor.ExecuteAsync(Action),
			_executor.ExecuteAsync(Action),
			_executor.ExecuteAsync(Action)
		];
		await Task.WhenAll(tasks);

		// Assert
		maxConcurrent.Should().Be(1,
			because: "the proxy-safe executor should serialize concurrent calls through a lock");
	}
}
