// using System;
// using System.Collections.Generic;
// using Clio.Command;
// using Clio.Common;
// using Clio.Package;
// using FluentAssertions;
// using FluentValidation;
// using FluentValidation.Results;
// using NSubstitute;
// using NUnit.Framework;
// using IFileSystem = Clio.Common.IFileSystem;

// namespace Clio.Tests.Command;

// [TestFixture]
// public class LinkPackageStoreCommandTests : BaseCommandTests<LinkPackageStoreOptions> {

// 	#region Fields: Private

// 	private IValidator<LinkPackageStoreOptions> _validator;
// 	private IFileSystem _fileSystem;
// 	private IJsonConverter _jsonConverter;
// 	private ILogger _logger;
// 	private LinkPackageStoreCommand _command;

// 	#endregion

// 	#region Setup

// 	[SetUp]
// 	public override void Setup() {
// 		base.Setup();
// 		_validator = Substitute.For<IValidator<LinkPackageStoreOptions>>();
// 		_fileSystem = Substitute.For<IFileSystem>();
// 		_jsonConverter = Substitute.For<IJsonConverter>();
// 		_logger = Substitute.For<ILogger>();

// 		_command = new LinkPackageStoreCommand(_logger, _fileSystem, _jsonConverter, _validator);
// 	}

// 	#endregion

// 	#region Tests

// 	[Test]
// 	[Description("Verifies that command fails when validation fails")]
// 	public void Execute_ReturnsFail_WhenValidationFails() {
// 		// Arrange
// 		var options = new LinkPackageStoreOptions {
// 			PackageStorePath = "",
// 			EnvPkgPath = ""
// 		};
// 		var validationResult = new ValidationResult(
// 			new[] {
// 				new ValidationFailure("PackageStorePath", "Required")
// 			}
// 		);
// 		_validator.Validate(options).Returns(validationResult);

// 		// Act
// 		int result = _command.Execute(options);

// 		// Assert
// 		result.Should().Be(1, "because validation should fail and return 1");
// 	}

// 	[Test]
// 	[Description("Verifies that command returns error when PackageStore path does not exist")]
// 	public void Execute_ReturnsError_WhenPackageStorePathNotExists() {
// 		// Arrange
// 		var options = new LinkPackageStoreOptions {
// 			PackageStorePath = "/store",
// 			EnvPkgPath = "/env/pkg"
// 		};
// 		_validator.Validate(options).Returns(new ValidationResult());
// 		_fileSystem.ExistsDirectory("/store").Returns(false);

// 		// Act
// 		int result = _command.Execute(options);

// 		// Assert
// 		result.Should().Be(1, "because PackageStore path doesn't exist");
// 		_logger.Received().WriteError(Arg.Is<string>(x => x.Contains("does not exist")));
// 	}

// 	[Test]
// 	[Description("Verifies that command skips packages not found in PackageStore")]
// 	public void Execute_SkipsPackages_WhenNotInStore() {
// 		// Arrange
// 		var options = new LinkPackageStoreOptions {
// 			PackageStorePath = "/store",
// 			EnvPkgPath = "/env/pkg"
// 		};
// 		_validator.Validate(options).Returns(new ValidationResult());

// 		// Setup file system mocks
// 		_fileSystem.ExistsDirectory("/store").Returns(true);
// 		_fileSystem.ExistsDirectory("/env/pkg").Returns(true);
// 		_fileSystem.GetDirectories("/store").Returns(new string[] { });
// 		_fileSystem.GetDirectories("/env/pkg").Returns(new string[] { "/env/pkg/mypackage" });

// 		// Setup descriptor.json for environment package
// 		_fileSystem.ExistsFile("/env/pkg/mypackage/descriptor.json").Returns(true);
// 		var descriptor = new PackageDescriptorDto {
// 			Descriptor = new PackageDescriptor {
// 				Name = "mypackage",
// 				PackageVersion = "1.0.0.0"
// 			}
// 		};
// 		_jsonConverter.DeserializeObjectFromFile<PackageDescriptorDto>("/env/pkg/mypackage/descriptor.json")
// 			.Returns(descriptor);

// 		// Act
// 		int result = _command.Execute(options);

// 		// Assert
// 		result.Should().Be(1, "because no packages were found in store, should return error");
// 		_logger.Received().WriteError(Arg.Is<string>(x => x.Contains("No packages found in PackageStore")));
// 	}

// 	[Test]
// 	[Description("Verifies that command links package when found in both locations with matching version")]
// 	public void Execute_LinksPackage_WhenFoundInBothLocations() {
// 		// Arrange
// 		var options = new LinkPackageStoreOptions {
// 			PackageStorePath = "/store",
// 			EnvPkgPath = "/env/pkg"
// 		};
// 		_validator.Validate(options).Returns(new ValidationResult());

// 		// Setup store packages: {Package_name}/{branches}/{version}/{content}
// 		_fileSystem.ExistsDirectory("/store").Returns(true);
// 		_fileSystem.GetDirectories("/store").Returns(new string[] { "/store/mypackage" });
// 		_fileSystem.GetDirectories("/store/mypackage").Returns(new string[] { "/store/mypackage/main" });
// 		_fileSystem.GetDirectories("/store/mypackage/main").Returns(new string[] { "/store/mypackage/main/1.0.0.0" });
// 		_fileSystem.ExistsDirectory("/store/mypackage/main/1.0.0.0").Returns(true);

// 		// Setup environment packages
// 		_fileSystem.ExistsDirectory("/env/pkg").Returns(true);
// 		_fileSystem.GetDirectories("/env/pkg").Returns(new string[] { "/env/pkg/mypackage" });
// 		_fileSystem.ExistsFile("/env/pkg/mypackage/descriptor.json").Returns(true);
		
// 		var descriptor = new PackageDescriptorDto {
// 			Descriptor = new PackageDescriptor {
// 				Name = "mypackage",
// 				PackageVersion = "1.0.0.0"
// 			}
// 		};
// 		_jsonConverter.DeserializeObjectFromFile<PackageDescriptorDto>("/env/pkg/mypackage/descriptor.json")
// 			.Returns(descriptor);

// 		// Setup directory operations
// 		_fileSystem.ExistsDirectory(Arg.Any<string>()).Returns(true);

// 		// Act
// 		int result = _command.Execute(options);

// 		// Assert
// 		result.Should().Be(0, "because package should be linked successfully");
// 		_fileSystem.Received(1).CreateSymLink(
// 			Arg.Is<string>(s => s.Contains("/store/mypackage/main/1.0.0.0")),
// 			Arg.Is<string>(s => s.Contains("/env/pkg/mypackage"))
// 		);
// 	}

// 	[Test]
// 	[Description("Verifies that command skips package when version doesn't match")]
// 	public void Execute_SkipsPackage_WhenVersionDoesntMatch() {
// 		// Arrange
// 		var options = new LinkPackageStoreOptions {
// 			PackageStorePath = "/store",
// 			EnvPkgPath = "/env/pkg"
// 		};
// 		_validator.Validate(options).Returns(new ValidationResult());

// 		// Setup store packages with version 2.0.0.0: {Package}/{branches}/{version}
// 		_fileSystem.ExistsDirectory("/store").Returns(true);
// 		_fileSystem.GetDirectories("/store").Returns(new string[] { "/store/mypackage" });
// 		_fileSystem.GetDirectories("/store/mypackage").Returns(new string[] { "/store/mypackage/main" });
// 		_fileSystem.GetDirectories("/store/mypackage/main").Returns(new string[] { "/store/mypackage/main/2.0.0.0" });

// 		// Setup environment packages with version 1.0.0.0
// 		_fileSystem.ExistsDirectory("/env/pkg").Returns(true);
// 		_fileSystem.GetDirectories("/env/pkg").Returns(new string[] { "/env/pkg/mypackage" });
// 		_fileSystem.ExistsFile("/env/pkg/mypackage/descriptor.json").Returns(true);
		
// 		var descriptor = new PackageDescriptorDto {
// 			Descriptor = new PackageDescriptor {
// 				Name = "mypackage",
// 				PackageVersion = "1.0.0.0"
// 			}
// 		};
// 		_jsonConverter.DeserializeObjectFromFile<PackageDescriptorDto>("/env/pkg/mypackage/descriptor.json")
// 			.Returns(descriptor);

// 		// Act
// 		int result = _command.Execute(options);

// 		// Assert
// 		result.Should().Be(0, "because version doesn't match, should skip and return success");
// 		_logger.Received().WriteWarning(Arg.Is<string>(x => x.Contains("version") && x.Contains("not found")));
// 		_fileSystem.DidNotReceive().CreateSymLink(Arg.Any<string>(), Arg.Any<string>());
// 	}

// 	[Test]
// 	[Description("Verifies that command handles invalid descriptor.json gracefully")]
// 	public void Execute_HandlesInvalidDescriptor_Gracefully() {
// 		// Arrange
// 		var options = new LinkPackageStoreOptions {
// 			PackageStorePath = "/store",
// 			EnvPkgPath = "/env/pkg"
// 		};
// 		_validator.Validate(options).Returns(new ValidationResult());

// 		// Setup store packages - empty store
// 		_fileSystem.ExistsDirectory("/store").Returns(true);
// 		_fileSystem.GetDirectories("/store").Returns(new string[] { });

// 		// Setup environment packages
// 		_fileSystem.ExistsDirectory("/env/pkg").Returns(true);
// 		_fileSystem.GetDirectories("/env/pkg").Returns(new string[] { "/env/pkg/badpackage" });
// 		_fileSystem.ExistsFile("/env/pkg/badpackage/descriptor.json").Returns(false);

// 		// Act
// 		int result = _command.Execute(options);

// 		// Assert
// 		result.Should().Be(1, "because no packages found in store returns error before checking descriptors");
// 		_logger.Received().WriteError(Arg.Is<string>(x => x.Contains("No packages found in PackageStore")));
// 	}

// 	[Test]
// 	[Description("Verifies that command returns error when environment path doesn't exist")]
// 	public void Execute_ReturnsError_WhenEnvPathNotExists() {
// 		// Arrange
// 		var options = new LinkPackageStoreOptions {
// 			PackageStorePath = "/store",
// 			EnvPkgPath = "/env/pkg"
// 		};
// 		_validator.Validate(options).Returns(new ValidationResult());

// 		// Setup store exists but environment doesn't
// 		_fileSystem.ExistsDirectory("/store").Returns(true);
// 		_fileSystem.ExistsDirectory("/env/pkg").Returns(false);

// 		// Act
// 		int result = _command.Execute(options);

// 		// Assert
// 		result.Should().Be(1, "because environment path doesn't exist");
// 		_logger.Received().WriteError(Arg.Is<string>(x => x.Contains("does not exist")));
// 	}

// 	[Test]
// 	[Description("Verifies that command returns 1 when symlink creation fails")]
// 	public void Execute_ReturnsFail_WhenSymlinkCreationFails() {
// 		// Arrange
// 		var options = new LinkPackageStoreOptions {
// 			PackageStorePath = "/store",
// 			EnvPkgPath = "/env/pkg"
// 		};
// 		_validator.Validate(options).Returns(new ValidationResult());

// 		// Setup store packages: {Package}/{branches}/{version}
// 		_fileSystem.ExistsDirectory("/store").Returns(true);
// 		_fileSystem.GetDirectories("/store").Returns(new string[] { "/store/mypackage" });
// 		_fileSystem.GetDirectories("/store/mypackage").Returns(new string[] { "/store/mypackage/main" });
// 		_fileSystem.GetDirectories("/store/mypackage/main").Returns(new string[] { "/store/mypackage/main/1.0.0.0" });
// 		_fileSystem.ExistsDirectory("/store/mypackage/main/1.0.0.0").Returns(true);

// 		// Setup environment packages
// 		_fileSystem.ExistsDirectory("/env/pkg").Returns(true);
// 		_fileSystem.GetDirectories("/env/pkg").Returns(new string[] { "/env/pkg/mypackage" });
// 		_fileSystem.ExistsFile("/env/pkg/mypackage/descriptor.json").Returns(true);
		
// 		var descriptor = new PackageDescriptorDto {
// 			Descriptor = new PackageDescriptor {
// 				Name = "mypackage",
// 				PackageVersion = "1.0.0.0"
// 			}
// 		};
// 		_jsonConverter.DeserializeObjectFromFile<PackageDescriptorDto>("/env/pkg/mypackage/descriptor.json")
// 			.Returns(descriptor);

// 		// Setup directory operations to fail
// 		_fileSystem.ExistsDirectory(Arg.Any<string>()).Returns(true);
// 		_fileSystem.When(x => x.CreateSymLink(Arg.Any<string>(), Arg.Any<string>()))
// 			.Throw(new Exception("Permission denied"));

// 		// Act
// 		int result = _command.Execute(options);

// 		// Assert
// 		result.Should().Be(1, "because symlink creation should fail");
// 		_logger.Received().WriteError(Arg.Is<string>(x => x.Contains("Failed to create symbolic link")));
// 	}

// 	[Test]
// 	[Description("Verifies that command returns error when required PackageStorePath is missing")]
// 	public void Execute_ReturnsFail_WhenPackageStorePathMissing() {
// 		// Arrange
// 		var options = new LinkPackageStoreOptions {
// 			PackageStorePath = "",
// 			EnvPkgPath = "/env/pkg"
// 		};
// 		var validationResult = new ValidationResult(
// 			new[] {
// 				new ValidationFailure("PackageStorePath", "PackageStorePath is required")
// 			}
// 		);
// 		_validator.Validate(options).Returns(validationResult);

// 		// Act
// 		int result = _command.Execute(options);

// 		// Assert
// 		result.Should().Be(1, "because PackageStorePath is required");
// 		_logger.Received().PrintValidationFailureErrors(Arg.Any<IEnumerable<ValidationFailure>>());
// 	}

// 	[Test]
// 	[Description("Verifies that command links multiple packages successfully")]
// 	public void Execute_LinksMultiplePackages_Successfully() {
// 		// Arrange
// 		var options = new LinkPackageStoreOptions {
// 			PackageStorePath = "/store",
// 			EnvPkgPath = "/env/pkg"
// 		};
// 		_validator.Validate(options).Returns(new ValidationResult());

// 		// Setup store with multiple packages: {Package}/{branches}/{version}
// 		_fileSystem.ExistsDirectory("/store").Returns(true);
// 		_fileSystem.GetDirectories("/store").Returns(new string[] { "/store/pkg1", "/store/pkg2" });
// 		_fileSystem.GetDirectories("/store/pkg1").Returns(new string[] { "/store/pkg1/main" });
// 		_fileSystem.GetDirectories("/store/pkg2").Returns(new string[] { "/store/pkg2/main" });
// 		_fileSystem.GetDirectories("/store/pkg1/main").Returns(new string[] { "/store/pkg1/main/1.0.0.0" });
// 		_fileSystem.GetDirectories("/store/pkg2/main").Returns(new string[] { "/store/pkg2/main/1.0.0.0" });
// 		_fileSystem.ExistsDirectory("/store/pkg1/main/1.0.0.0").Returns(true);
// 		_fileSystem.ExistsDirectory("/store/pkg2/main/1.0.0.0").Returns(true);

// 		// Setup environment with multiple packages
// 		_fileSystem.ExistsDirectory("/env/pkg").Returns(true);
// 		_fileSystem.GetDirectories("/env/pkg").Returns(new string[] { "/env/pkg/pkg1", "/env/pkg/pkg2" });
		
// 		// Setup descriptors
// 		_fileSystem.ExistsFile("/env/pkg/pkg1/descriptor.json").Returns(true);
// 		_fileSystem.ExistsFile("/env/pkg/pkg2/descriptor.json").Returns(true);
		
// 		var descriptor1 = new PackageDescriptorDto {
// 			Descriptor = new PackageDescriptor { Name = "pkg1", PackageVersion = "1.0.0.0" }
// 		};
// 		var descriptor2 = new PackageDescriptorDto {
// 			Descriptor = new PackageDescriptor { Name = "pkg2", PackageVersion = "1.0.0.0" }
// 		};
		
// 		_jsonConverter.DeserializeObjectFromFile<PackageDescriptorDto>("/env/pkg/pkg1/descriptor.json")
// 			.Returns(descriptor1);
// 		_jsonConverter.DeserializeObjectFromFile<PackageDescriptorDto>("/env/pkg/pkg2/descriptor.json")
// 			.Returns(descriptor2);

// 		// Setup directory operations
// 		_fileSystem.ExistsDirectory(Arg.Any<string>()).Returns(true);

// 		// Act
// 		int result = _command.Execute(options);

// 		// Assert
// 		result.Should().Be(0, "because both packages should be linked successfully");
// 		_fileSystem.Received(2).CreateSymLink(Arg.Any<string>(), Arg.Any<string>());
// 	}

// 	#endregion

// }
