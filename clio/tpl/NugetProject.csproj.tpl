<!-- https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props#copylocallockfileassemblies -->
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>

  		<Version>1.0.0</Version>
  		<Authors>Clio</Authors>
  		<Company>ATF</Company>

		<!-- https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props#targetframeworks
			Use the TargetFrameworks property when you want your app to target multiple platforms.
			See list of possible target frameworks
			https://learn.microsoft.com/en-us/dotnet/standard/frameworks#supported-target-frameworks-->
		<TargetFrameworks>net472;netstandard2.0</TargetFrameworks>

		<!-- https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props#copylocallockfileassemblies
			The CopyLocalLockFileAssemblies property is useful for plugin projects that have dependencies on other libraries.
			If you set this property to true, any NuGet package dependencies are copied to the output directory. 
			That means you can use the output of dotnet build to run your plugin on any machine.-->
		<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
		
		<!-- https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props#appendtargetframeworktooutputpath
			The AppendTargetFrameworkToOutputPath property controls whether the target framework moniker (TFM) is appended to the output path
			(which is defined by OutputPath). The .NET SDK automatically appends the target framework and, if present, the runtime identifier 
			to the output path. Setting AppendTargetFrameworkToOutputPath to false prevents the TFM from being appended to the output path. 
			However, without the TFM in the output path, multiple build artifacts may overwrite each other.
			For example, for a .NET 5 app, the output path changes from bin\Debug\net5.0 to bin\Debug-->
		<AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>

		<!-- https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props#appendruntimeidentifiertooutputpath
			The AppendRuntimeIdentifierToOutputPath property controls whether the runtime identifier (RID) is appended to the output path. 
			The .NET SDK automatically appends the target framework and, if present, the runtime identifier to the output path.
			Setting AppendRuntimeIdentifierToOutputPath to false prevents the RID from being appended to the output path.-->
		<AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>
	</PropertyGroup>

	<Choose>
		<When Condition="$(TargetFramework)== 'netstandard2.0'">
			<PropertyGroup>
				<OutputPath>bin\netstandard</OutputPath>
			</PropertyGroup>
		</When>
		<Otherwise>
			<PropertyGroup>
				<OutputPath>bin\$(TargetFramework)</OutputPath>
			</PropertyGroup>
		</Otherwise>
	</Choose>
</Project>