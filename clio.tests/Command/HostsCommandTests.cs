using Clio.Command;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Description("Tests for HostsCommand - verifies documentation exists for hosts command")]
[Property("Module", "Command")]
public class HostsCommandTests : BaseCommandTests<HostsOptions>
{
}
