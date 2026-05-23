using Clio.Command;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public class TurnFsmCommandTests : BaseCommandTests<TurnFsmCommandOptions> { }
