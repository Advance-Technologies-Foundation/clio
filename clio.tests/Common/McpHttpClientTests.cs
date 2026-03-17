using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Common.McpProtocol;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
public class McpHttpClientTests
{
	private ILogger _mockLogger;

	[SetUp]
	public void Setup()
	{
		_mockLogger = Substitute.For<ILogger>();
	}

	[Test]
	public void Constructor_CreatesClient()
	{
		var client = new McpHttpClient(_mockLogger);
		Assert.IsNotNull(client);
	}

	[Test]
	public async Task InitializeAsync_ThrowsException_WhenSessionIdNotReturned()
	{
		var client = new McpHttpClient(_mockLogger);
		
		Assert.ThrowsAsync<InvalidOperationException>(async () =>
		{
			await client.InitializeAsync("http://invalid-endpoint", "user", "pass");
		});
	}

	[Test]
	public void CallToolAsync_ThrowsException_WhenNotInitialized()
	{
		var client = new McpHttpClient(_mockLogger);
		var args = new Dictionary<string, object>();
		
		Assert.ThrowsAsync<InvalidOperationException>(async () =>
		{
			await client.CallToolAsync("test-tool", args);
		});
	}

	[Test]
	public void Dispose_DisposesHttpClient()
	{
		var client = new McpHttpClient(_mockLogger);
		
		Assert.DoesNotThrow(() => client.Dispose());
	}
}
