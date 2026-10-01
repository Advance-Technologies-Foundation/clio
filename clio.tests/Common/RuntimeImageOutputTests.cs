using Clio.Common;
using Clio.Common.OperatorBootstrap;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture, Category("Unit"), Property("Module", "Common")]
public class RuntimeImageOutputTests {
	[Test, Description("Image variants and database backup group into one distribution independently of deployed instances.")]
	public void Distributions_ShouldGroupVariantsAndDatabase() {
		// Arrange
		var images = JArray.Parse("""
		[{"repository":"registry/creatio-dev","reference":"registry/creatio-dev:10.2.301_studionet_softkey_postgresql_enu","tag":"10.2.301_studionet_softkey_postgresql_enu","creatioLabels":{"org.creatio.runtime.dotnet":"10.0"}},
		{"repository":"registry/creatio-prod","reference":"registry/creatio-prod:10.2.301_studionet_softkey_postgresql_enu","tag":"10.2.301_studionet_softkey_postgresql_enu"},
		{"repository":"registry/creatio-db","reference":"registry/creatio-db:10.2.301_studionet_softkey_postgresql_enu","tag":"10.2.301_studionet_softkey_postgresql_enu","isDatabaseImage":true}]
		""");
		// Act
		var rows = RuntimeImageOutput.Distributions(images);
		// Assert
		rows.Should().HaveCount(1, because: "one source ZIP is one distribution");
		rows[0].Value<string>("product").Should().Be("studio", because: "product excludes runtime and database suffixes");
		rows[0]["images"].Should().HaveCount(2, because: "both exact deploy references remain available");
		rows[0].Value<bool>("deployable").Should().BeTrue(because: "the backup image supplies its database");
	}
	[Test, Description("A runtime without a database source is explicitly marked incomplete.")]
	public void Distributions_ShouldMarkMissingDatabase() {
		// Arrange
		var images = JArray.Parse("""[{"repository":"creatio-dev","reference":"creatio-dev:10.1.1_studionet_enu","tag":"10.1.1_studionet_enu"}]""");
		// Act
		var rows = RuntimeImageOutput.Distributions(images);
		// Assert
		rows[0].Value<bool>("deployable").Should().BeFalse(because: "an app image alone cannot provision a database");
	}
	[Test, Description("Empty JSON output remains a parseable array with no prose.")]
	public void Write_ShouldEmitEmptyJsonArray() {
		// Arrange
		var logger = Substitute.For<ILogger>();
		// Act
		RuntimeImageOutput.Write([], true, logger);
		// Assert
		logger.Received(1).WriteLine("[]");
		logger.DidNotReceive().WriteInfo(Arg.Any<string>());
	}
}
