namespace Clio.Tests.Common;

using Clio.Common;
using NUnit.Framework;

[TestFixture, Author("Kirill Krylov")]
public class ArtifactFilterTests
{
	public ArtifactFilter sut;
	const string _baseDirectory = "\\\\tscrm.com\\dfs-ts\\builds-7";
	
	[SetUp]
	protected void SetUp()
	{
		sut = new ArtifactFilter(_baseDirectory);
	}

	[Test]
	public void GetDirectories_Returns_Directories()
	{
		var result = ArtifactFilter.GetDirectories(_baseDirectory);
		Assert.That(result, Is.Not.Null);
	}
}