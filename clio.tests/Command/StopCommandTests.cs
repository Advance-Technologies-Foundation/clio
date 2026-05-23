using Clio.Command;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Description("Tests for StopCommand - verifies documentation exists for stop command")]
[Property("Module", "Command")]
public class StopCommandTests : BaseCommandTests<StopOptions>
{
}
