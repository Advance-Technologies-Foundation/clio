using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using cliogate.Files.cs;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Terrasoft.Configuration.Tests;
using Terrasoft.Core;
using Terrasoft.Core.Entities;
using Terrasoft.Core.Factories;
using Terrasoft.TestFramework;
using Terrasoft.Web.Http.Abstractions;

namespace cliogate.tests
{
	[Category("Unit")]
	[Author("Kirill Krylov")]
	[MockSettings(RequireMock.All)]
	public class CreatioApiGatewayTestFixture : BaseMarketplaceTestFixture
	{

		#region Constants: Private

		private const string SysSettingCode = "SysSettingOne_Code";
		private const string SysSettingValue = "SysSettingOne_Value";

		#endregion

		
		private void MockSysSettingsEntity(string code, string valueTypeName) {
			const string schemaName = "SysSettings";
			MockEntitySchemaWithColumns(schemaName, new Dictionary<string, DataValueType> {
				{"Code", DataValueType.Text},
				{"ValueTypeName", DataValueType.Text}
			});

			SetUpTestData(schemaName, new Dictionary<string, object> {
				{"Code", code},
				{"ValueTypeName", valueTypeName}
			});
		}
		
		private void MockSysSetting(string code, object value){
			UserConnection.SettingsValues.Add(code, value);
            GlobalAppSettings.FeatureUseSysSettingsEngine = true;
            FakeSysSettings settings = new FakeSysSettings {
            	Code = code
            };
            FakeSysSettings.Setup(new[] {settings});
            FakeSysSettingsEngine engine = Substitute.For<FakeSysSettingsEngine>();
            FakeSysSettingsEngine.Setup(engine);
            engine.TryGetSettingsValue(Arg.Is(code), Arg.Any<Guid>(),
            		out object vv)
            	.Returns(x => {
            		x[2] = value;
            		return true;
            	});
		}

		private CreatioApiGateway CreateGatewayWithHttpContext(){
			HttpContext context = Substitute.For<HttpContext>();
			HttpSessionState session = Substitute.For<HttpSessionState>();
			context.Session.Returns(session);
			return new CreatioApiGateway {
				HttpContextAccessor = CustomSetupHttpContextAccessor(context, UserConnection)
			};
		}
		
		#region Methods: Protected

		protected override void SetUp(){
			base.SetUp();
			ClassFactory.RebindWithFactoryMethod(() => (UserConnection)UserConnection);
		}

		protected override void SetupSysSettings(){
			base.SetupSysSettings();
			MockSysSetting(SysSettingCode, SysSettingValue);
		}

		#endregion

		[Test]
		public void GetSysSettingValueByCode_Returns_EmptyString_When_CodeDoesNotExist(){
			//Arrange
			UserConnection.DBSecurityEngine.GetCanExecuteOperation("CanManageSysSettings")
				.Returns(true);

			CreatioApiGateway sut = new CreatioApiGateway();
			const string sysSettingCode = "fake_code";
			MockSysSettingsEntity(sysSettingCode, "Text");
			
			//Act
			string actual = sut.GetSysSettingValueByCode(sysSettingCode);
			
			//Assert
			actual.Should().Be("");
		}

		[Test]
		public void GetSysSettingValueByCode_Returns_When_CheckCanManageSolution(){
			//Arrange
			UserConnection.DBSecurityEngine.GetCanExecuteOperation("CanManageSysSettings")
				.Returns(true);
			CreatioApiGateway sut = new CreatioApiGateway();

			MockSysSettingsEntity(SysSettingCode, "Text");
			
			//Act
			string actual = sut.GetSysSettingValueByCode(SysSettingCode);

			//Assert
			actual.Should().Be(SysSettingValue);
		}

		[Test]
		public void GetSysSettingValueByCode_Trows_When_Not_CheckCanManageSolution(){
			//Arrange
			CreatioApiGateway sut = new CreatioApiGateway();
			UserConnection.DBSecurityEngine.GetCanExecuteOperation("CanManageSysSettings")
				.Returns(false);
			//Act
			Action act = () => sut.GetSysSettingValueByCode(SysSettingCode);

			//Assert
			act.Should()
				.Throw<Exception>()
				.WithMessage("You don't have permission for operation CanManageSysSettings");
		}

		[TestCaseSource("DateTimeData")]
		public void GetSysSettingValueByCode_Returns_PrettyValue(TestDataItem testItem){
			//Arrange
			UserConnection.DBSecurityEngine.GetCanExecuteOperation("CanManageSysSettings")
				.Returns(true);
			CreatioApiGateway sut = new CreatioApiGateway();
			const string code = "SysSetting_DateTime";
			MockSysSetting(code, testItem.Value);
			MockSysSettingsEntity(code, testItem.ValueTypeName);

			//Act
			string actual = sut.GetSysSettingValueByCode(code);

			//Assert
			actual.Should().Be(testItem.Value.ToString(testItem.FormatString));
		}
		
		[Test]
		[Description("UnlockPackages completes successfully when the package list is null " +
			"(the normal 'unlock all packages by maintainer code' case).")]
		public void UnlockPackages_ShouldReturnTrue_WhenPackagesNull(){
			//Arrange
			UserConnection.DBSecurityEngine.GetCanExecuteOperation("CanManageSolution")
				.Returns(true);
			MockSysSetting("Maintainer", "CustomMaintainer");
			CreatioApiGateway sut = CreateGatewayWithHttpContext();

			//Act
			bool actual = sut.UnlockPackages(null);

			//Assert
			actual.Should().BeTrue(
				because: "a null payload is the supported 'unlock all by maintainer' signal and must complete successfully");
		}

		[Description("LockPackages completes successfully when the package list is null " +
			"(the 'lock all packages by maintainer code' case).")]
		[Test]
		public void LockPackages_ShouldReturnTrue_WhenPackagesNull(){
			//Arrange
			UserConnection.DBSecurityEngine.GetCanExecuteOperation("CanManageSolution")
				.Returns(true);
			MockSysSetting("Maintainer", "CustomMaintainer");
			CreatioApiGateway sut = CreateGatewayWithHttpContext();

			//Act
			bool actual = sut.LockPackages(null);

			//Assert
			actual.Should().BeTrue(
				because: "a null payload is the supported 'lock all by maintainer' signal and must complete successfully");
		}

		[Test]
		[Description("UnlockPackages returns false when an explicitly requested package does not exist and the update affects zero rows.")]
		public void UnlockPackages_ShouldReturnFalse_WhenRequestedPackageDoesNotExist(){
			//Arrange
			UserConnection.DBSecurityEngine.GetCanExecuteOperation("CanManageSolution")
				.Returns(true);
			MockSysSetting("Maintainer", "CustomMaintainer");
			CreatioApiGateway sut = CreateGatewayWithHttpContext();

			//Act
			bool actual = sut.UnlockPackages(new[] {"MissingPackage"});

			//Assert
			actual.Should().BeFalse(
				because: "a requested package that was not updated must not be reported as successfully unlocked");
		}

		[Test]
		[Description("LockPackages returns false when an explicitly requested package does not exist and the update affects zero rows.")]
		public void LockPackages_ShouldReturnFalse_WhenRequestedPackageDoesNotExist(){
			//Arrange
			UserConnection.DBSecurityEngine.GetCanExecuteOperation("CanManageSolution")
				.Returns(true);
			MockSysSetting("Maintainer", "CustomMaintainer");
			CreatioApiGateway sut = CreateGatewayWithHttpContext();

			//Act
			bool actual = sut.LockPackages(new[] {"MissingPackage"});

			//Assert
			actual.Should().BeFalse(
				because: "a requested package that was not updated must not be reported as successfully locked");
		}

		[Test]
		[Description("BuildUnlockDescription treats a null Description column as empty and appends the maintainer marker instead of throwing.")]
		public void BuildUnlockDescription_ShouldAppendMarkerWithMaintainer_WhenDescriptionIsNull(){
			//Arrange
			//Act
			string actual = CreatioApiGateway.BuildUnlockDescription(null, "Vendor", "#OriginalMaintainer:");

			//Assert
			actual.Should().Be("#OriginalMaintainer:Vendor",
				because: "a null Description must be treated as empty and receive the maintainer marker, not cause an NRE");
		}

		[Test]
		[Description("BuildUnlockDescription preserves the original Description when it already carries the maintainer marker.")]
		public void BuildUnlockDescription_ShouldReturnOriginal_WhenMarkerAlreadyPresent(){
			//Arrange
			const string original = "Notes#OriginalMaintainer:Vendor";

			//Act
			string actual = CreatioApiGateway.BuildUnlockDescription(original, "Other", "#OriginalMaintainer:");

			//Assert
			actual.Should().Be(original,
				because: "an already-marked Description must be preserved untouched");
		}

		[Test]
		[Description("BuildUnlockDescription truncates only the human description so the reversible maintainer marker fits the SysPackage column.")]
		public void BuildUnlockDescription_ShouldTruncateDescriptionAndPreserveMarker_WhenValueWouldOverflow(){
			// Arrange
			string originalDescription = new string('D', 250);
			const string marker = "#OriginalMaintainer:";
			const string maintainer = "Vendor";

			// Act
			string actual = CreatioApiGateway.BuildUnlockDescription(originalDescription, maintainer, marker);

			// Assert
			actual.Should().HaveLength(250,
				because: "the value written to SysPackage.Description must respect its varchar(250) limit");
			actual.Should().EndWith(marker + maintainer,
				because: "lock-package needs the complete marker and maintainer to restore the original owner");
		}

		[Test]
		[Description("BuildUnlockDescription avoids leaving an unmatched UTF-16 surrogate when truncation lands inside an emoji.")]
		public void BuildUnlockDescription_ShouldNotSplitSurrogatePair_WhenTruncatingDescription(){
			// Arrange
			const string marker = "#OriginalMaintainer:";
			const string maintainer = "Vendor";
			string originalDescription = new string('D', 223) + "\U0001F600";

			// Act
			string actual = CreatioApiGateway.BuildUnlockDescription(originalDescription, maintainer, marker);

			// Assert
			actual.Should().Be(new string('D', 223) + marker + maintainer,
				because: "truncation must remove the whole surrogate pair rather than persist an invalid half-character");
		}

		[Test]
		[Description("BuildUnlockDescription rejects a maintainer marker that cannot fit even after the human description is removed.")]
		public void BuildUnlockDescription_ShouldThrow_WhenMaintainerMarkerExceedsColumnLimit(){
			// Arrange
			const string marker = "#OriginalMaintainer:";
			string maintainer = new string('M', 231);

			// Act
			Action act = () => CreatioApiGateway.BuildUnlockDescription("Description", maintainer, marker);

			// Assert
			act.Should().Throw<InvalidOperationException>(
				because: "silently truncating the maintainer would make lock-package restore the wrong owner")
				.WithMessage("*requires 251 characters*allows only 250*",
					because: "the Creatio Error.log should identify the exact storage constraint");
		}

		[Test]
		[Description("SplitLockDescription treats a null Description column as empty, returning a single empty segment instead of throwing.")]
		public void SplitLockDescription_ShouldReturnSingleEmptySegment_WhenDescriptionIsNull(){
			//Arrange
			//Act
			string[] actual = CreatioApiGateway.SplitLockDescription(null, "#OriginalMaintainer:");

			//Assert
			actual.Should().ContainSingle(
				because: "splitting a null (treated as empty) Description must yield one segment, not an NRE");
			actual[0].Should().BeEmpty(
				because: "the single segment of an empty Description is an empty string");
		}

		[Test]
		[Description("SplitLockDescription separates the human description from the preserved original maintainer marker.")]
		public void SplitLockDescription_ShouldSeparateDescriptionAndMaintainer_WhenMarkerPresent(){
			//Arrange
			//Act
			string[] actual = CreatioApiGateway.SplitLockDescription("Notes#OriginalMaintainer:Vendor", "#OriginalMaintainer:");

			//Assert
			actual.Should().HaveCount(2,
				because: "the marker splits the stored value into description and original maintainer");
			actual[0].Should().Be("Notes",
				because: "segment 0 is the human-readable description");
			actual[1].Should().Be("Vendor",
				because: "segment 1 is the preserved original maintainer");
		}

		[Test]
		[Description("FormatPackageNamesForLog replaces control, formatting, and separator characters so package input cannot forge log lines.")]
		public void FormatPackageNamesForLog_ShouldReplaceUnsafeCharacters_WhenPackageNameContainsFormatting(){
			//Arrange
			string[] packageNames = {"Safe\r\nForged\u202E\u2028Line\u2029Paragraph", "Second"};

			//Act
			string actual = CreatioApiGateway.FormatPackageNamesForLog(packageNames);

			//Assert
			actual.Should().Be("Safe  Forged  Line Paragraph, Second",
				because: "control, bidirectional formatting, and Unicode separator characters must not alter gateway log structure");
		}

		[Test]
		[Description("PackageExplorer rejects rooted file paths so package-file reads cannot escape the package Files directory.")]
		public void GetPackageFileContent_ShouldRejectPath_WhenPathIsRooted(){
			// Arrange
			string baseDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
			PackageExplorer sut = new PackageExplorer("TestPackage", baseDirectory);
			string rootedPath = Path.Combine(Path.GetPathRoot(baseDirectory), "outside.txt");

			// Act
			Action act = () => sut.GetPackageFileContent(rootedPath);

			// Assert
			act.Should().Throw<ArgumentException>()
				.WithMessage("*relative to the package Files directory*",
					because: "a rooted path would otherwise make Path.Combine discard the package root");
		}

		[Test]
		[Description("PackageExplorer rejects parent traversal so package-file reads stay inside the package Files directory.")]
		public void GetPackageFileContent_ShouldRejectPath_WhenPathTraversesParent(){
			// Arrange
			string baseDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
			PackageExplorer sut = new PackageExplorer("TestPackage", baseDirectory);

			// Act
			Action act = () => sut.GetPackageFileContent("../outside.txt");

			// Assert
			act.Should().Throw<ArgumentException>()
				.WithMessage("*stay inside the package Files directory*",
					because: "canonical containment must reject traversal after resolving the full path");
		}

		[Test]
		[Description("PackageExplorer returns stable forward-slash relative paths for files below the package Files directory.")]
		public void GetPackageFilesDirectoryContent_ShouldReturnNormalizedRelativePaths_WhenFilesExist(){
			// Arrange
			string baseDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
			string filesDirectory = Path.Combine(baseDirectory, "Terrasoft.Configuration", "Pkg", "TestPackage", "Files");
			string sourceDirectory = Path.Combine(filesDirectory, "src", "cs");
			Directory.CreateDirectory(sourceDirectory);
			File.WriteAllText(Path.Combine(sourceDirectory, "Probe.cs"), "public class Probe {}");
			File.WriteAllText(Path.Combine(filesDirectory, "TestPackage.csproj"), "<Project />");
			PackageExplorer sut = new PackageExplorer("TestPackage", baseDirectory);

			try {
				// Act
				string[] actual = sut.GetPackageFilesDirectoryContent().ToArray();

				// Assert
				actual.Should().Equal(new[] {"src/cs/Probe.cs", "TestPackage.csproj"},
					because: "agents need deterministic relative paths that can be passed back to get-package-file");
			}
			finally {
				Directory.Delete(baseDirectory, recursive: true);
			}
		}

		[Test]
		[Description("PackageExplorer rejects package names that contain directory separators so package selection cannot escape the package root.")]
		public void Constructor_ShouldRejectPackageName_WhenNameContainsDirectorySeparator(){
			// Arrange
			string invalidPackageName = $"Parent{Path.DirectorySeparatorChar}Child";

			// Act
			Action act = () => new PackageExplorer(invalidPackageName, Path.GetTempPath());

			// Assert
			act.Should().Throw<ArgumentException>()
				.WithMessage("*single directory name*",
					because: "the package name is one path segment below Terrasoft.Configuration/Pkg");
		}

		[Test]
		[Description("PackageExplorer rejects child junctions for both direct reads and recursive listing so reparse points cannot escape or cycle below Files.")]
		public void PackageFileOperations_ShouldRejectPath_WhenChildDirectoryIsJunction(){
			if (Path.DirectorySeparatorChar != '\\') {
				Assert.Ignore("Directory-junction coverage runs on Windows; other platforms use the same ReparsePoint guard.");
			}

			// Arrange
			string testRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
			string baseDirectory = Path.Combine(testRoot, "application");
			string filesDirectory = Path.Combine(baseDirectory, "Terrasoft.Configuration", "Pkg", "TestPackage", "Files");
			string outsideDirectory = Path.Combine(testRoot, "outside");
			string junctionPath = Path.Combine(filesDirectory, "linked");
			Directory.CreateDirectory(filesDirectory);
			Directory.CreateDirectory(outsideDirectory);
			File.WriteAllText(Path.Combine(outsideDirectory, "secret.txt"), "outside");
			CreateDirectoryJunction(junctionPath, outsideDirectory);
			PackageExplorer sut = new PackageExplorer("TestPackage", baseDirectory);

			try {
				// Act
				Action read = () => sut.GetPackageFileContent("linked/secret.txt");
				Action list = () => sut.GetPackageFilesDirectoryContent().ToArray();

				// Assert
				read.Should().Throw<ArgumentException>().WithMessage("*symbolic links*",
					because: "canonical string containment does not resolve a child junction's external target");
				list.Should().Throw<InvalidOperationException>().WithMessage("*symbolic link*",
					because: "recursive listing must fail closed before descending into an external tree or cycle");
			}
			finally {
				if (Directory.Exists(junctionPath)) {
					Directory.Delete(junctionPath);
				}
				Directory.Delete(testRoot, recursive: true);
			}
		}

		[Test]
		[Description("PackageExplorer rejects a package-root junction so the package name cannot redirect the trusted Files root outside Pkg.")]
		public void GetPackageFileContent_ShouldRejectPath_WhenPackageDirectoryIsJunction(){
			if (Path.DirectorySeparatorChar != '\\') {
				Assert.Ignore("Directory-junction coverage runs on Windows; other platforms use the same ReparsePoint guard.");
			}

			// Arrange
			string testRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
			string baseDirectory = Path.Combine(testRoot, "application");
			string packagesDirectory = Path.Combine(baseDirectory, "Terrasoft.Configuration", "Pkg");
			string outsidePackage = Path.Combine(testRoot, "outside-package");
			string outsideFiles = Path.Combine(outsidePackage, "Files");
			string packageJunction = Path.Combine(packagesDirectory, "TestPackage");
			Directory.CreateDirectory(packagesDirectory);
			Directory.CreateDirectory(outsideFiles);
			File.WriteAllText(Path.Combine(outsideFiles, "secret.txt"), "outside");
			CreateDirectoryJunction(packageJunction, outsidePackage);
			PackageExplorer sut = new PackageExplorer("TestPackage", baseDirectory);

			try {
				// Act
				Action act = () => sut.GetPackageFileContent("secret.txt");

				// Assert
				act.Should().Throw<ArgumentException>().WithMessage("*symbolic links*",
					because: "the package directory itself is attacker-selectable below the trusted Pkg root");
			}
			finally {
				if (Directory.Exists(packageJunction)) {
					Directory.Delete(packageJunction);
				}
				Directory.Delete(testRoot, recursive: true);
			}
		}

		[Test]
		[Description("PackageExplorer rejects a junction used as the shared Pkg root so trusted application-relative resolution cannot be redirected.")]
		public void GetPackageFileContent_ShouldRejectPath_WhenPackagesRootIsJunction(){
			if (Path.DirectorySeparatorChar != '\\') {
				Assert.Ignore("Directory-junction coverage runs on Windows; other platforms use the same ReparsePoint guard.");
			}

			// Arrange
			string testRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
			string baseDirectory = Path.Combine(testRoot, "application");
			string configurationDirectory = Path.Combine(baseDirectory, "Terrasoft.Configuration");
			string packagesJunction = Path.Combine(configurationDirectory, "Pkg");
			string outsidePackages = Path.Combine(testRoot, "outside-packages");
			string outsideFiles = Path.Combine(outsidePackages, "TestPackage", "Files");
			Directory.CreateDirectory(configurationDirectory);
			Directory.CreateDirectory(outsideFiles);
			File.WriteAllText(Path.Combine(outsideFiles, "secret.txt"), "outside");
			CreateDirectoryJunction(packagesJunction, outsidePackages);
			PackageExplorer sut = new PackageExplorer("TestPackage", baseDirectory);

			try {
				// Act
				Action act = () => sut.GetPackageFileContent("secret.txt");

				// Assert
				act.Should().Throw<ArgumentException>().WithMessage("*symbolic links*",
					because: "the configured Pkg root must remain below the trusted application directory");
			}
			finally {
				if (Directory.Exists(packagesJunction)) {
					Directory.Delete(packagesJunction);
				}
				Directory.Delete(testRoot, recursive: true);
			}
		}

		[Test]
		[Description("PackageExplorer refuses an oversized text file before decoding it so package reads cannot amplify memory and response size without a bound.")]
		public void GetPackageFileContent_ShouldRejectFile_WhenFileExceedsReadLimit(){
			// Arrange
			string baseDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
			string filesDirectory = Path.Combine(baseDirectory, "Terrasoft.Configuration", "Pkg", "TestPackage", "Files");
			string largeFilePath = Path.Combine(filesDirectory, "large.txt");
			Directory.CreateDirectory(filesDirectory);
			using (FileStream stream = File.Create(largeFilePath)) {
				stream.SetLength(PackageExplorer.MaxPackageTextFileBytes + 1);
			}
			PackageExplorer sut = new PackageExplorer("TestPackage", baseDirectory);

			try {
				// Act
				Action act = () => sut.GetPackageFileContent("large.txt");

				// Assert
				act.Should().Throw<InvalidOperationException>().WithMessage("*10 MiB read limit*",
					because: "the service must reject an oversized response before allocating decoded content");
			}
			finally {
				Directory.Delete(baseDirectory, recursive: true);
			}
		}

		private static void CreateDirectoryJunction(string junctionPath, string targetPath) {
			var startInfo = new ProcessStartInfo("cmd.exe",
				$"/c mklink /J \"{junctionPath}\" \"{targetPath}\"") {
				CreateNoWindow = true,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			using (Process process = Process.Start(startInfo)) {
				process.Should().NotBeNull(
					because: "the Windows junction setup process must start for the reparse-point test");
				process.WaitForExit();
				process.ExitCode.Should().Be(0,
					because: $"the junction fixture must be created before testing it: {process.StandardError.ReadToEnd()}");
			}
		}

		public static IEnumerable<TestDataItem> DateTimeData = new List<TestDataItem> {
			new TestDataItem("DateTime", "dd-MMM-yyyy HH:mm:ss"),
			new TestDataItem("Date", "dd-MMM-yyyy"),
			new TestDataItem("Time", "HH:mm:ss")
		};
	}
	public class TestDataItem
	{
		public TestDataItem(string valueTypeName, string formatString){
			Value = DateTime.Now;
			ValueTypeName = valueTypeName;
			FormatString = formatString;
		}
		public DateTime Value {get;}
		public string ValueTypeName {get;}
		public string FormatString {get;}
		
	}
}
