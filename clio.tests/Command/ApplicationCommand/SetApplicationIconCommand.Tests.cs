using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Json;
using Autofac;
using Clio.Command.ApplicationCommand;
using Clio.ComposableApplication;
using FluentAssertions;
using ICSharpCode.SharpZipLib.Zip;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.ApplicationCommand
{

	internal class SetApplicationIconCommandTestCase : BaseCommandTests<SetApplicationVersionOption>
	{
		private static string mockPackageFolderPath = Path.Combine("C:", "MockPackageFolder");
		private static string mockPackageAppDescriptorPath = Path.Combine(mockPackageFolderPath, "Files", "app-descriptor.json");
		private static string mockWorspacePath = Path.Combine("C:", "MockWorkspaceFolder");
		private static string mockWorkspaceAppPackageFolderPath = Path.Combine(mockWorspacePath, "packages", "IFrameSample");
		private static string mockWorkspaceAppDescriptorPath = Path.Combine(mockWorkspaceAppPackageFolderPath, "Files", "app-descriptor.json");

		private static MockFileSystem CreateFs(string filePath, string packagePath) {
			string originClioSourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory);
			string appDescriptorExamplesDescriptorPath = Path.Combine(originClioSourcePath, "Examples", "AppDescriptors", filePath);
			string mockAppDescriptorFilePath = Path.Combine(packagePath, "Files", "app-descriptor.json");
			return new MockFileSystem(new Dictionary<string, MockFileData> {
				{
					mockAppDescriptorFilePath,
					new MockFileData(File.ReadAllText(appDescriptorExamplesDescriptorPath))
				}
			});
		}

		private static MockFileSystem CreateFs(Dictionary<string, string> appDescriptors) {
			string originClioSourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory);
			MockFileSystem mockFileSystem = new MockFileSystem();
			foreach (var appDescriptor in appDescriptors) {
				string appDescriptorExamplesDescriptorPath = Path.Combine(originClioSourcePath, "Examples", "AppDescriptors", appDescriptor.Value);
				string mockAppDescriptorJsonPath = Path.Combine(mockWorspacePath, "packages", appDescriptor.Key, "Files", "app-descriptor.json");
				mockFileSystem.AddFile(mockAppDescriptorJsonPath, new MockFileData(File.ReadAllText(appDescriptorExamplesDescriptorPath)));
			}
			return mockFileSystem;
		}

		private MockFileSystem _fileSystem;
		private IComposableApplicationManager composableApplicationManager;

		protected override void AdditionalRegistrations(ContainerBuilder containerBuilder) {
			composableApplicationManager = Substitute.For<IComposableApplicationManager>();
			containerBuilder.RegisterInstance(composableApplicationManager);
			base.AdditionalRegistrations(containerBuilder);
		}

		[Test]
		public void SetApplicationIconCommand_CallsComposableAppmanager() {
			string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Files", "icon.svg");
			string appName = "ExampleAppName";
			var command = Container.Resolve<SetApplicationIconCommand>();
			command.Execute(new SetApplicationIconOption {
				IconPath = iconPath,
				WorspaceFolderPath = mockWorspacePath,
				PackageFolderPath = mockWorkspaceAppPackageFolderPath,
				AppName = appName
			});
			composableApplicationManager.Received(1).SetIcon(mockWorkspaceAppPackageFolderPath, iconPath, appName);
		}

	}
}
