using System;
using System.Net.Http;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests
{
    [TestFixture]
    [Description("Unit tests for cliogate presence and version check logic in RemoteCommand.")]
    public class RemoteCommandCliogateTests
    {
        private class TestOptions : RemoteCommandOptions { }

        private class TestRemoteCommand : RemoteCommand<TestOptions>
        {
            public bool VersionChecked { get; private set; }
            public string LastError { get; private set; }
            public TestRemoteCommand(string servicePath, string clioGateMinVersion, IClioGateway clioGateway)
            {
                ServicePath = servicePath;
                ClioGateMinVersion = clioGateMinVersion;
                ClioGateWay = clioGateway;
                Logger = Substitute.For<ILogger>();
                Logger
                    .When(x => x.WriteError(Arg.Any<string>()))
                    .Do(call => LastError = call.Arg<string>());
            }
            protected override void ExecuteRemoteCommand(TestOptions options) { }
            protected override void ProceedResponse(string response, TestOptions options) { }
            protected override string ClioGateMinVersion { get; } = "0.0.0.0";
            protected override string ServicePath { get; set; }
        }

        [Test]
        [Description("Should return error if cliogate is required by ServicePath and not installed.")]
        public void Execute_ShouldReturnError_WhenCliogateRequiredByServicePathAndNotInstalled()
        {
            var cmd = new TestRemoteCommand("/rest/CreatioApiGateway/GetSysInfo", "0.0.0.0", null);
            var result = cmd.Execute(new TestOptions());
            result.Should().Be(1, "cliogate is required but not installed");
            cmd.LastError.Should().Contain("cliogate is not installed", "error message should be correct");
        }

        [Test]
        [Description("Should return error if cliogate is required by ClioGateMinVersion and not installed.")]
        public void Execute_ShouldReturnError_WhenCliogateRequiredByMinVersionAndNotInstalled()
        {
            var cmd = new TestRemoteCommand("", "2.0.0.32", null);
            var result = cmd.Execute(new TestOptions());
            result.Should().Be(1, "cliogate is required by version but not installed");
            cmd.LastError.Should().Contain("cliogate is not installed", "error message should be correct");
        }

        [Test]
        [Description("Should check version if cliogate is installed and ClioGateMinVersion is set.")]
        public void Execute_ShouldCheckVersion_WhenCliogateInstalledAndMinVersionSet()
        {
            var gateway = Substitute.For<IClioGateway>();
            var cmd = new TestRemoteCommand("", "2.0.0.32", gateway);
            cmd.Execute(new TestOptions());
            gateway.Received(1).CheckCompatibleVersion("2.0.0.32");
        }

        [Test]
        [Description("Should not check version if ClioGateMinVersion is default.")]
        public void Execute_ShouldNotCheckVersion_WhenMinVersionIsDefault()
        {
            var gateway = Substitute.For<IClioGateway>();
            var cmd = new TestRemoteCommand("", "0.0.0.0", gateway);
            cmd.Execute(new TestOptions());
            gateway.DidNotReceive().CheckCompatibleVersion(Arg.Any<string>());
        }

        [Test]
        [Description("Should execute command if cliogate is present and requirements are met.")]
        public void Execute_ShouldRunCommand_WhenCliogatePresentAndRequirementsMet()
        {
            var gateway = Substitute.For<IClioGateway>();
            var cmd = new TestRemoteCommand("/rest/CreatioApiGateway/GetSysInfo", "2.0.0.32", gateway);
            var result = cmd.Execute(new TestOptions());
            result.Should().Be(0, "cliogate is present and requirements are met");
        }

        [Test]
        [Description("Should execute command if ServicePath is not empty and does not require cliogate.")]
        public void Execute_ShouldRunCommand_WhenServicePathIsNotEmptyAndDoesNotRequireCliogate()
        {
            var cmd = new TestRemoteCommand("/api/SomeOtherService", "0.0.0.0", null);
            var result = cmd.Execute(new TestOptions());
            result.Should().Be(0, "cliogate is not required for this ServicePath");
        }

        [Test]
        [Description("Should execute command if ServicePath is not empty and does not require cliogate, even if gateway is present.")]
        public void Execute_ShouldRunCommand_WhenServicePathIsNotEmptyAndDoesNotRequireCliogateWithGateway()
        {
            var gateway = Substitute.For<IClioGateway>();
            var cmd = new TestRemoteCommand("/api/SomeOtherService", "0.0.0.0", gateway);
            var result = cmd.Execute(new TestOptions());
            result.Should().Be(0, "cliogate is not required for this ServicePath, gateway present");
            gateway.DidNotReceive().CheckCompatibleVersion(Arg.Any<string>());
        }
    }
}
