using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Package.NuGet;
using Clio.Project.NuGet;
using Clio.Tests.Command;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Package.NuGet;

[TestFixture]
[Property("Module", "Package")]
public class NugetPackagesProviderTests : BaseClioModuleTests{
	#region Fields: Private

	private StubDelegatingHandler _handler;

	private ILogger _logger;

	#endregion

	#region Class: Nested

	private sealed class StubDelegatingHandler : DelegatingHandler{
		#region Properties: Public

		public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } =
			_ => new HttpResponseMessage(HttpStatusCode.OK) {
				Content = new StringContent("""{"versions":[]}""")
			};

		public List<Uri> RequestUris { get; } = [];

		#endregion

		#region Methods: Protected

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
			CancellationToken cancellationToken) {
			RequestUris.Add(request.RequestUri);
			return Task.FromResult(Responder(request));
		}

		#endregion
	}

	#endregion

	#region Methods: Private

	private static int CountLogCalls(ILogger logger, string methodName, Func<string, bool> messagePredicate = null) {
		return logger.ReceivedCalls()
					 .Where(call => string.Equals(call.GetMethodInfo().Name, methodName, StringComparison.Ordinal))
					 .Count(call => {
						 if (messagePredicate is null) {
							 return true;
						 }

						 object[] arguments = call.GetArguments();
						 string message = arguments.Length > 0 ? arguments[0] as string : string.Empty;
						 return messagePredicate(message ?? string.Empty);
					 });
	}

	#endregion

	#region Methods: Protected

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		_logger = Substitute.For<ILogger>();
		_handler = new StubDelegatingHandler();
		containerBuilder.AddSingleton(_logger);
		containerBuilder.AddSingleton(_handler);
		containerBuilder.AddHttpClient<INugetPackagesProvider, NugetPackagesProvider>()
						.ConfigurePrimaryHttpMessageHandler(sp => sp.GetRequiredService<StubDelegatingHandler>());
	}

	#endregion

	#region Methods: Public

	[Test]
	[Description("Ensures provider logs error and returns null when HTTP call fails.")]
	public async Task GetLastVersionPackages_LogsError_WhenHttpRequestFails() {
		// Arrange
		_handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError) {
			Content = new StringContent("server failure")
		};
		INugetPackagesProvider provider = Container.GetRequiredService<INugetPackagesProvider>();

		// Act
		LastVersionNugetPackages result =
			await provider.GetLastVersionPackages("broken.package", "https://api.nuget.org");

		// Assert
		result.Should().BeNull("because non-success HTTP status should be handled as a failure path");
		CountLogCalls(_logger, nameof(ILogger.WriteError),
				msg => msg.Contains("Error fetching package versions:", StringComparison.Ordinal))
			.Should().Be(1, "because HTTP failure should emit exactly one error log entry");
	}

	[Test]
	[Description("Ensures provider logs warning and returns null when NuGet API response contains no versions.")]
	public async Task GetLastVersionPackages_LogsWarning_WhenNoVersionsReturned() {
		// Arrange
		_handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.OK) {
			Content = new StringContent("""{"versions":[]}""")
		};
		INugetPackagesProvider provider = Container.GetRequiredService<INugetPackagesProvider>();

		// Act
		LastVersionNugetPackages result =
			await provider.GetLastVersionPackages("empty.package", "https://api.nuget.org");

		// Assert
		result.Should().BeNull("because provider cannot build package versions when response list is empty");
		CountLogCalls(_logger, nameof(ILogger.WriteWarning),
				msg => msg.Contains("No versions found for package: empty.package", StringComparison.Ordinal))
			.Should().Be(1, "because empty versions payload should produce exactly one warning");
	}

	[Test]
	[Description(
		"Ensures provider uses DI HttpClient handler and returns parsed NuGet versions for a successful response.")]
	public async Task GetLastVersionPackages_ReturnsLastAndStable_FromDiHttpClient() {
		// Arrange
		_handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.OK) {
			Content = new StringContent("""{"versions":["1.0.0","1.1.0-rc","1.1.0"]}""")
		};
		INugetPackagesProvider provider = Container.GetRequiredService<INugetPackagesProvider>();

		// Act
		LastVersionNugetPackages result =
			await provider.GetLastVersionPackages("Test.Package", "https://api.nuget.org");

		// Assert
		result.Should().NotBeNull("because a valid versions payload should produce a result");
		result.Last.Version.ToString().Should()
			  .Be("1.1.0-rc", "because current PackageVersion ordering treats rc suffix as latest");
		result.Stable.Version.ToString().Should().Be("1.1.0-rc",
			"because stable version is detected by rc suffix in current domain logic");
		_handler.RequestUris.Should()
				.ContainSingle("because provider should perform exactly one request for one package");
		_handler.RequestUris[0].AbsoluteUri.Should().Be(
			"https://api.nuget.org/v3-flatcontainer/test.package/index.json",
			"because provider should call the NuGet flat container endpoint with lower-cased package name");
		CountLogCalls(_logger, nameof(ILogger.WriteWarning)).Should().Be(0,
			"because successful response with versions should not emit warnings");
		CountLogCalls(_logger, nameof(ILogger.WriteError)).Should().Be(0,
			"because successful response should not emit errors");
	}

	#endregion
}
