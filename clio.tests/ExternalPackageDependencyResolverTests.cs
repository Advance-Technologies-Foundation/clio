using System.Collections.Generic;
using System.IO;
using System.Linq;
using Clio.Common;
using Clio.Package;
using Clio.Workspaces;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests;

[TestFixture]
[Category("Unit")]
[Property("Module", "Core")]
[Description("Tests for external package dependency resolution functionality")]
public class ExternalPackageDependencyResolverTests
{
	private IFileSystem _mockFileSystem;
	private IJsonConverter _mockJsonConverter;
	private ILogger _mockLogger;
	private ExternalPackageDependencyResolver _resolver;

	[SetUp]
	public void SetUp() {
		_mockFileSystem = Substitute.For<IFileSystem>();
		_mockJsonConverter = Substitute.For<IJsonConverter>();
		_mockLogger = Substitute.For<ILogger>();
		_resolver = new ExternalPackageDependencyResolver(_mockFileSystem, _mockJsonConverter, _mockLogger);
	}

	[Test]
	[Description("Returns empty when external packages list is null")]
	public void ResolveDependencies_NullExternalPackages_ReturnsEmpty() {
		// Arrange
		// Act
		List<string> result = _resolver.ResolveDependencies(
			null, new List<string>(), new List<string>(), "/ext/packages").ToList();

		// Assert
		result.Should().BeEmpty("null external packages should return empty");
	}

	[Test]
	[Description("Returns empty when external packages list is empty")]
	public void ResolveDependencies_EmptyExternalPackages_ReturnsEmpty() {
		// Arrange
		// Act
		List<string> result = _resolver.ResolveDependencies(
			new List<string>(), new List<string>(), new List<string>(), "/ext/packages").ToList();

		// Assert
		result.Should().BeEmpty("empty external packages should return empty");
	}

	[Test]
	[Description("Returns empty when external packages path is null or empty")]
	public void ResolveDependencies_NullOrEmptyPath_ReturnsEmpty() {
		// Arrange
		List<string> externals = new() { "Pkg1" };

		// Act
		List<string> resultNull = _resolver.ResolveDependencies(
			externals, new List<string>(), new List<string>(), null).ToList();
		List<string> resultEmpty = _resolver.ResolveDependencies(
			externals, new List<string>(), new List<string>(), "").ToList();

		// Assert
		resultNull.Should().BeEmpty("null path should return empty");
		resultEmpty.Should().BeEmpty("empty path should return empty");
	}

	[Test]
	[Description("Returns empty when external package has no dependencies")]
	public void ResolveDependencies_NoDependencies_ReturnsEmpty() {
		// Arrange
		string extPath = "/ext/packages";
		string descriptorPath = Path.Combine(extPath, "CommonModule", "descriptor.json");
		_mockFileSystem.ExistsFile(descriptorPath).Returns(true);
		_mockJsonConverter.DeserializeObjectFromFile<PackageDescriptorDto>(descriptorPath)
			.Returns(new PackageDescriptorDto {
				Descriptor = new PackageDescriptor { DependsOn = new List<PackageDependency>() }
			});

		// Act
		List<string> result = _resolver.ResolveDependencies(
			new List<string> { "CommonModule" },
			new List<string>(),
			new List<string>(),
			extPath).ToList();

		// Assert
		result.Should().BeEmpty("package with no dependencies should return empty");
	}

	[Test]
	[Description("Returns dependencies that exist in external packages folder")]
	public void ResolveDependencies_WithExistingDeps_ReturnsThem() {
		// Arrange
		string extPath = "/ext/packages";
		string descriptorPath = Path.Combine(extPath, "CommonModule", "descriptor.json");
		_mockFileSystem.ExistsFile(descriptorPath).Returns(true);
		_mockFileSystem.ExistsDirectory(Path.Combine(extPath, "BaseDep")).Returns(true);
		_mockFileSystem.ExistsDirectory(Path.Combine(extPath, "CoreDep")).Returns(true);
		_mockJsonConverter.DeserializeObjectFromFile<PackageDescriptorDto>(descriptorPath)
			.Returns(new PackageDescriptorDto {
				Descriptor = new PackageDescriptor {
					DependsOn = new List<PackageDependency> {
						new("BaseDep", "1.0.0"),
						new("CoreDep", "2.0.0")
					}
				}
			});

		// Act
		List<string> result = _resolver.ResolveDependencies(
			new List<string> { "CommonModule" },
			new List<string>(),
			new List<string>(),
			extPath).ToList();

		// Assert
		result.Should().HaveCount(2, "both dependencies exist in external packages folder");
		result.Should().Contain("BaseDep", "BaseDep exists and should be included");
		result.Should().Contain("CoreDep", "CoreDep exists and should be included");
	}

	[Test]
	[Description("Excludes dependencies that do not exist in external packages folder")]
	public void ResolveDependencies_MissingDeps_ExcludesMissing() {
		// Arrange
		string extPath = "/ext/packages";
		string descriptorPath = Path.Combine(extPath, "CommonModule", "descriptor.json");
		_mockFileSystem.ExistsFile(descriptorPath).Returns(true);
		_mockFileSystem.ExistsDirectory(Path.Combine(extPath, "ExistingDep")).Returns(true);
		_mockFileSystem.ExistsDirectory(Path.Combine(extPath, "MissingDep")).Returns(false);
		_mockJsonConverter.DeserializeObjectFromFile<PackageDescriptorDto>(descriptorPath)
			.Returns(new PackageDescriptorDto {
				Descriptor = new PackageDescriptor {
					DependsOn = new List<PackageDependency> {
						new("ExistingDep", "1.0.0"),
						new("MissingDep", "1.0.0")
					}
				}
			});

		// Act
		List<string> result = _resolver.ResolveDependencies(
			new List<string> { "CommonModule" },
			new List<string>(),
			new List<string>(),
			extPath).ToList();

		// Assert
		result.Should().HaveCount(1, "only existing dependency should be included");
		result.Should().Contain("ExistingDep", "existing dependency should be included");
		result.Should().NotContain("MissingDep", "missing dependency should be excluded");
	}

	[Test]
	[Description("Excludes dependencies that match IgnorePackages patterns")]
	public void ResolveDependencies_IgnoredDeps_ExcludesIgnored() {
		// Arrange
		string extPath = "/ext/packages";
		string descriptorPath = Path.Combine(extPath, "CommonModule", "descriptor.json");
		_mockFileSystem.ExistsFile(descriptorPath).Returns(true);
		_mockFileSystem.ExistsDirectory(Path.Combine(extPath, "GoodDep")).Returns(true);
		_mockFileSystem.ExistsDirectory(Path.Combine(extPath, "CrtCustomer360")).Returns(true);
		_mockJsonConverter.DeserializeObjectFromFile<PackageDescriptorDto>(descriptorPath)
			.Returns(new PackageDescriptorDto {
				Descriptor = new PackageDescriptor {
					DependsOn = new List<PackageDependency> {
						new("GoodDep", "1.0.0"),
						new("CrtCustomer360", "1.0.0")
					}
				}
			});

		// Act
		List<string> result = _resolver.ResolveDependencies(
			new List<string> { "CommonModule" },
			new List<string> { "CrtCustomer360" },
			new List<string>(),
			extPath).ToList();

		// Assert
		result.Should().HaveCount(1, "ignored dependency should be excluded");
		result.Should().Contain("GoodDep", "non-ignored dependency should be included");
		result.Should().NotContain("CrtCustomer360", "ignored dependency should be excluded");
	}

	[Test]
	[Description("Excludes dependencies that match IgnorePackages wildcard patterns")]
	public void ResolveDependencies_WildcardIgnore_ExcludesMatching() {
		// Arrange
		string extPath = "/ext/packages";
		string descriptorPath = Path.Combine(extPath, "CommonModule", "descriptor.json");
		_mockFileSystem.ExistsFile(descriptorPath).Returns(true);
		_mockFileSystem.ExistsDirectory(Path.Combine(extPath, "ProductionLib")).Returns(true);
		_mockFileSystem.ExistsDirectory(Path.Combine(extPath, "TestHelper")).Returns(true);
		_mockFileSystem.ExistsDirectory(Path.Combine(extPath, "DemoData")).Returns(true);
		_mockJsonConverter.DeserializeObjectFromFile<PackageDescriptorDto>(descriptorPath)
			.Returns(new PackageDescriptorDto {
				Descriptor = new PackageDescriptor {
					DependsOn = new List<PackageDependency> {
						new("ProductionLib", "1.0.0"),
						new("TestHelper", "1.0.0"),
						new("DemoData", "1.0.0")
					}
				}
			});

		// Act
		List<string> result = _resolver.ResolveDependencies(
			new List<string> { "CommonModule" },
			new List<string> { "Test*", "Demo*" },
			new List<string>(),
			extPath).ToList();

		// Assert
		result.Should().HaveCount(1, "wildcards should exclude matching packages");
		result.Should().Contain("ProductionLib", "non-matching package should be included");
	}

	[Test]
	[Description("Excludes dependencies already in workspace packages")]
	public void ResolveDependencies_AlreadyIncluded_ExcludesDuplicates() {
		// Arrange
		string extPath = "/ext/packages";
		string descriptorPath = Path.Combine(extPath, "CommonModule", "descriptor.json");
		_mockFileSystem.ExistsFile(descriptorPath).Returns(true);
		_mockFileSystem.ExistsDirectory(Path.Combine(extPath, "NewDep")).Returns(true);
		_mockFileSystem.ExistsDirectory(Path.Combine(extPath, "ExistingPkg")).Returns(true);
		_mockJsonConverter.DeserializeObjectFromFile<PackageDescriptorDto>(descriptorPath)
			.Returns(new PackageDescriptorDto {
				Descriptor = new PackageDescriptor {
					DependsOn = new List<PackageDependency> {
						new("NewDep", "1.0.0"),
						new("ExistingPkg", "1.0.0")
					}
				}
			});

		// Act
		List<string> result = _resolver.ResolveDependencies(
			new List<string> { "CommonModule" },
			new List<string>(),
			new List<string> { "ExistingPkg" },
			extPath).ToList();

		// Assert
		result.Should().HaveCount(1, "already-included packages should be excluded");
		result.Should().Contain("NewDep", "new dependency should be included");
		result.Should().NotContain("ExistingPkg", "already-included package should be excluded");
	}

	[Test]
	[Description("Handles missing descriptor.json gracefully")]
	public void ResolveDependencies_MissingDescriptor_ReturnsEmptyAndLogs() {
		// Arrange
		string extPath = "/ext/packages";
		_mockFileSystem.ExistsFile(Path.Combine(extPath, "CommonModule", "descriptor.json")).Returns(false);

		// Act
		List<string> result = _resolver.ResolveDependencies(
			new List<string> { "CommonModule" },
			new List<string>(),
			new List<string>(),
			extPath).ToList();

		// Assert
		result.Should().BeEmpty("missing descriptor should return empty");
		_mockLogger.Received(1).WriteWarning(Arg.Is<string>(s => s.Contains("Descriptor not found")));
	}

	[Test]
	[Description("Handles malformed descriptor.json gracefully")]
	public void ResolveDependencies_MalformedDescriptor_ReturnsEmptyAndLogs() {
		// Arrange
		string extPath = "/ext/packages";
		string descriptorPath = Path.Combine(extPath, "CommonModule", "descriptor.json");
		_mockFileSystem.ExistsFile(descriptorPath).Returns(true);
		_mockJsonConverter.DeserializeObjectFromFile<PackageDescriptorDto>(descriptorPath)
			.Returns(_ => throw new System.Exception("JSON parse error"));

		// Act
		List<string> result = _resolver.ResolveDependencies(
			new List<string> { "CommonModule" },
			new List<string>(),
			new List<string>(),
			extPath).ToList();

		// Assert
		result.Should().BeEmpty("malformed descriptor should return empty");
		_mockLogger.Received(1).WriteWarning(Arg.Is<string>(s => s.Contains("Failed to read descriptor")));
	}

	[Test]
	[Description("Handles multiple external packages with overlapping dependencies without duplicates")]
	public void ResolveDependencies_OverlappingDeps_NoDuplicates() {
		// Arrange
		string extPath = "/ext/packages";
		string pkgADescriptor = Path.Combine(extPath, "PkgA", "descriptor.json");
		string pkgBDescriptor = Path.Combine(extPath, "PkgB", "descriptor.json");
		_mockFileSystem.ExistsFile(pkgADescriptor).Returns(true);
		_mockFileSystem.ExistsFile(pkgBDescriptor).Returns(true);
		_mockFileSystem.ExistsDirectory(Path.Combine(extPath, "SharedDep")).Returns(true);
		_mockFileSystem.ExistsDirectory(Path.Combine(extPath, "DepA")).Returns(true);
		_mockFileSystem.ExistsDirectory(Path.Combine(extPath, "DepB")).Returns(true);

		_mockJsonConverter.DeserializeObjectFromFile<PackageDescriptorDto>(pkgADescriptor)
			.Returns(new PackageDescriptorDto {
				Descriptor = new PackageDescriptor {
					DependsOn = new List<PackageDependency> {
						new("SharedDep", "1.0.0"),
						new("DepA", "1.0.0")
					}
				}
			});
		_mockJsonConverter.DeserializeObjectFromFile<PackageDescriptorDto>(pkgBDescriptor)
			.Returns(new PackageDescriptorDto {
				Descriptor = new PackageDescriptor {
					DependsOn = new List<PackageDependency> {
						new("SharedDep", "1.0.0"),
						new("DepB", "1.0.0")
					}
				}
			});

		// Act
		List<string> result = _resolver.ResolveDependencies(
			new List<string> { "PkgA", "PkgB" },
			new List<string>(),
			new List<string>(),
			extPath).ToList();

		// Assert
		result.Should().HaveCount(3, "shared dependency should appear only once");
		result.Should().Contain("SharedDep", "shared dep should be included");
		result.Should().Contain("DepA", "DepA should be included");
		result.Should().Contain("DepB", "DepB should be included");
	}

	[Test]
	[Description("Resolves recursive dependencies (deps of deps)")]
	public void ResolveDependencies_RecursiveDeps_ResolvesTransitively() {
		// Arrange
		string extPath = "/ext/packages";
		string commonDescriptor = Path.Combine(extPath, "CommonModule", "descriptor.json");
		string level1Descriptor = Path.Combine(extPath, "Level1Dep", "descriptor.json");
		_mockFileSystem.ExistsFile(commonDescriptor).Returns(true);
		_mockFileSystem.ExistsFile(level1Descriptor).Returns(true);
		_mockFileSystem.ExistsDirectory(Path.Combine(extPath, "Level1Dep")).Returns(true);
		_mockFileSystem.ExistsDirectory(Path.Combine(extPath, "Level2Dep")).Returns(true);

		_mockJsonConverter.DeserializeObjectFromFile<PackageDescriptorDto>(commonDescriptor)
			.Returns(new PackageDescriptorDto {
				Descriptor = new PackageDescriptor {
					DependsOn = new List<PackageDependency> {
						new("Level1Dep", "1.0.0")
					}
				}
			});
		_mockJsonConverter.DeserializeObjectFromFile<PackageDescriptorDto>(level1Descriptor)
			.Returns(new PackageDescriptorDto {
				Descriptor = new PackageDescriptor {
					DependsOn = new List<PackageDependency> {
						new("Level2Dep", "1.0.0")
					}
				}
			});

		// Act
		List<string> result = _resolver.ResolveDependencies(
			new List<string> { "CommonModule" },
			new List<string>(),
			new List<string>(),
			extPath).ToList();

		// Assert
		result.Should().HaveCount(2, "recursive dependencies should be resolved");
		result.Should().Contain("Level1Dep", "first-level dependency should be included");
		result.Should().Contain("Level2Dep", "second-level dependency should be included");
	}

	[Test]
	[Description("Skips dependencies that are themselves external packages")]
	public void ResolveDependencies_DepIsExternalPackage_Skips() {
		// Arrange
		string extPath = "/ext/packages";
		string pkgADescriptor = Path.Combine(extPath, "PkgA", "descriptor.json");
		_mockFileSystem.ExistsFile(pkgADescriptor).Returns(true);
		_mockFileSystem.ExistsDirectory(Path.Combine(extPath, "PkgB")).Returns(true);
		_mockFileSystem.ExistsDirectory(Path.Combine(extPath, "RegularDep")).Returns(true);

		_mockJsonConverter.DeserializeObjectFromFile<PackageDescriptorDto>(pkgADescriptor)
			.Returns(new PackageDescriptorDto {
				Descriptor = new PackageDescriptor {
					DependsOn = new List<PackageDependency> {
						new("PkgB", "1.0.0"),
						new("RegularDep", "1.0.0")
					}
				}
			});

		// Act
		List<string> result = _resolver.ResolveDependencies(
			new List<string> { "PkgA", "PkgB" },
			new List<string>(),
			new List<string>(),
			extPath).ToList();

		// Assert
		result.Should().HaveCount(1, "external package should not appear as a dependency");
		result.Should().Contain("RegularDep", "regular dependency should be included");
		result.Should().NotContain("PkgB", "PkgB is an external package, not a dependency to add");
	}

	[Test]
	[Description("Handles null descriptor Descriptor property gracefully")]
	public void ResolveDependencies_NullDescriptor_ReturnsEmpty() {
		// Arrange
		string extPath = "/ext/packages";
		string descriptorPath = Path.Combine(extPath, "Pkg", "descriptor.json");
		_mockFileSystem.ExistsFile(descriptorPath).Returns(true);
		_mockJsonConverter.DeserializeObjectFromFile<PackageDescriptorDto>(descriptorPath)
			.Returns(new PackageDescriptorDto { Descriptor = null });

		// Act
		List<string> result = _resolver.ResolveDependencies(
			new List<string> { "Pkg" },
			new List<string>(),
			new List<string>(),
			extPath).ToList();

		// Assert
		result.Should().BeEmpty("null descriptor should return empty");
	}

	[Test]
	[Description("Handles null DependsOn list gracefully")]
	public void ResolveDependencies_NullDependsOn_ReturnsEmpty() {
		// Arrange
		string extPath = "/ext/packages";
		string descriptorPath = Path.Combine(extPath, "Pkg", "descriptor.json");
		_mockFileSystem.ExistsFile(descriptorPath).Returns(true);
		_mockJsonConverter.DeserializeObjectFromFile<PackageDescriptorDto>(descriptorPath)
			.Returns(new PackageDescriptorDto {
				Descriptor = new PackageDescriptor { DependsOn = null }
			});

		// Act
		List<string> result = _resolver.ResolveDependencies(
			new List<string> { "Pkg" },
			new List<string>(),
			new List<string>(),
			extPath).ToList();

		// Assert
		result.Should().BeEmpty("null DependsOn should return empty");
	}
}
