using Clio.Command.McpServer;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class CredentialContextAccessorTests
{
	private static CredentialContext SampleContext() =>
		new(
			"https://env.creatio.com",
			CredentialMaterial.FromAccessToken("token", "Bearer"),
			McpTransport.Http,
			PassthroughModeEnabled: true);

	[Test]
	[Description("A context set via the accessor round-trips back through Current when an HttpContext is active.")]
	public void Current_ShouldRoundTripContext_WhenHttpContextIsActive() {
		// Arrange
		IHttpContextAccessor httpContextAccessor = new HttpContextAccessor {
			HttpContext = new DefaultHttpContext()
		};
		ICredentialContextAccessor sut = new CredentialContextAccessor(httpContextAccessor);
		CredentialContext expected = SampleContext();

		// Act
		sut.Current = expected;
		CredentialContext actual = sut.Current;

		// Assert
		actual.Should().BeSameAs(expected, because: "the context is stored in and read from HttpContext.Items");
	}

	[Test]
	[Description("Current returns null when there is no active HttpContext (stdio / no request).")]
	public void Current_ShouldReturnNull_WhenHttpContextIsNull() {
		// Arrange
		IHttpContextAccessor httpContextAccessor = new HttpContextAccessor {
			HttpContext = null
		};
		ICredentialContextAccessor sut = new CredentialContextAccessor(httpContextAccessor);

		// Act
		CredentialContext actual = sut.Current;

		// Assert
		actual.Should().BeNull(because: "there is no request scope to read a context from");
	}

	[Test]
	[Description("Setting Current is a no-op when there is no active HttpContext, and does not throw.")]
	public void Current_ShouldIgnoreSet_WhenHttpContextIsNull() {
		// Arrange
		IHttpContextAccessor httpContextAccessor = new HttpContextAccessor {
			HttpContext = null
		};
		ICredentialContextAccessor sut = new CredentialContextAccessor(httpContextAccessor);

		// Act
		System.Action act = () => sut.Current = SampleContext();

		// Assert
		act.Should().NotThrow(because: "the setter must tolerate the absence of an HttpContext");
		sut.Current.Should().BeNull(because: "nothing was stored when no HttpContext was present");
	}

	[Test]
	[Description("Current returns null when an HttpContext is active but no context has been set.")]
	public void Current_ShouldReturnNull_WhenNoContextStored() {
		// Arrange
		IHttpContextAccessor httpContextAccessor = new HttpContextAccessor {
			HttpContext = new DefaultHttpContext()
		};
		ICredentialContextAccessor sut = new CredentialContextAccessor(httpContextAccessor);

		// Act
		CredentialContext actual = sut.Current;

		// Assert
		actual.Should().BeNull(because: "no credential header was captured for this request");
	}
}
