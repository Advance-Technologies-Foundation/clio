using System;
using System.Net.WebSockets;
using System.Threading;
using Clio.Common;
using Clio.Common.Responses;
using Creatio.Client.Dto;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>
/// Compile-time fixture for the stable <see cref="IApplicationClient"/> contract.
/// <see cref="LegacyApplicationClient"/> deliberately implements every member the interface had
/// BEFORE PUT was introduced and nothing more: if PUT is ever made abstract again, this file stops
/// compiling with CS0535 - which is exactly what a downstream implementer would hit, and what the
/// in-tree <c>ApplicationClientLease</c> already hit once.
/// </summary>
[TestFixture]
[Property("Module", "Common")]
[Category("Unit")]
public class LegacyApplicationClientCompatibilityTests {

	[Test]
	[Description("A client written before PUT existed still satisfies IApplicationClient, so adding PUT is not source-breaking")]
	public void LegacyImplementer_ShouldSatisfyTheContract_WithoutImplementingPut() {
		// Act
		IApplicationClient client = new LegacyApplicationClient();

		// Assert
		client.Should().BeAssignableTo<IApplicationClient>(
			because: "the fixture below omits ExecutePutRequest on purpose; had the member stayed "
				+ "abstract, this file would not compile at all");
	}

	[Test]
	[Description("Calling PUT on a client that does not support it fails loudly at the call site instead of silently doing nothing")]
	public void LegacyImplementer_ShouldThrowNotSupported_WhenPutIsCalled() {
		// Arrange
		IApplicationClient client = new LegacyApplicationClient();

		// Act
		Action act = () => client.ExecutePutRequest("/x", "data");

		// Assert
		act.Should().Throw<NotSupportedException>(
			because: "a default that quietly returned null would let a failed write look like a "
				+ "successful one")
			.WithMessage($"*{nameof(LegacyApplicationClient)}*");
	}

	/// <summary>An IApplicationClient frozen at the shape it had before PUT was added.</summary>
	private sealed class LegacyApplicationClient : IApplicationClient {

		public event EventHandler<WebSocketState> ConnectionStateChanged;

		public event EventHandler<WsMessage> MessageReceived;

		public string CallConfigurationService(string serviceName, string serviceMethod,
			string requestData, int requestTimeout = 10000) => string.Empty;

		public void DownloadFile(string url, string filePath, string requestData) { }

		public string ExecuteDeleteRequest(string url, string requestData,
			int requestTimeout = Timeout.Infinite, int maxAttempts = 1, int delaySec = 1) => string.Empty;

		public string ExecuteGetRequest(string url, int requestTimeout = Timeout.Infinite,
			int maxAttempts = 1, int delaySec = 1) => string.Empty;

		public string ExecutePostRequest(string url, string requestData,
			int requestTimeout = Timeout.Infinite, int maxAttempts = 1, int delaySec = 1) => string.Empty;

		public T ExecutePostRequest<T>(string url, string requestData,
			int requestTimeout = Timeout.Infinite, int maxAttempts = 1, int delaySec = 1)
			where T : BaseResponse, new() => new();

		public string ExecutePatchRequest(string url, string requestData,
			int requestTimeout = Timeout.Infinite, int maxAttempts = 1, int delaySec = 1) => string.Empty;

		public void Listen(CancellationToken cancellationToken) { }

		public void Login() { }

		public string UploadAlmFile(string url, string filePath) => string.Empty;

		public string UploadAlmFileByChunk(string url, string filePath) => string.Empty;

		public string UploadFile(string url, string filePath) => string.Empty;

		// Referenced so the compiler does not warn the events are never raised.
		internal void RaiseForCompilerOnly() {
			ConnectionStateChanged?.Invoke(this, WebSocketState.Open);
			MessageReceived?.Invoke(this, null);
		}
	}

}
