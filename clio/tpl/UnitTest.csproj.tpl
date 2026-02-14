<Project Sdk="Microsoft.NET.Sdk">
    <Import Project="..\..\.build-props\env.$(Configuration).props" Condition="Exists('..\..\.build-props\env.$(Configuration).props')" />
    <Choose>
        <When Condition="'$(CoreTargetFramework)' == 'net472'">
            <PropertyGroup>
                <TestCoreLibPath Condition="'$(TestCoreLibPath)' == ''">../../.application/net-framework/core-bin</TestCoreLibPath>
                <TargetFramework>net472</TargetFramework>
            </PropertyGroup>
        </When>
        <When Condition="'$(CoreTargetFramework)' == 'netstandard2.0'">
            <PropertyGroup>
                <TestCoreLibPath Condition="'$(TestCoreLibPath)' == ''">../../.application/net-core/core-bin</TestCoreLibPath>
                <TargetFramework>net8.0</TargetFramework>
            </PropertyGroup>
        </When>
    </Choose>

    <PropertyGroup>
        <IsPackable>false</IsPackable>
        <RootNamespace>{{packageUnderTest}}.Tests</RootNamespace>
        <PlatformTarget>x64</PlatformTarget>
        <Configurations>Debug;Release;dev-n8;dev-nf</Configurations>
    </PropertyGroup>

    <PropertyGroup Label="SonarQube">
        <!-- Exclude the project from analysis -->
        <SonarQubeExclude>true</SonarQubeExclude>
    </PropertyGroup>

    <ItemGroup>
        <Reference Include="System.Web"/>
        <Reference Include="Terrasoft.Configuration">
            <HintPath>$(TestCoreLibPath)\..\bin\Terrasoft.Configuration.dll</HintPath>
        </Reference>
    </ItemGroup>

    <ItemGroup Label="Nuget packages">
        <PackageReference Include="coverlet.msbuild" Version="6.0.4">
            <PrivateAssets>all</PrivateAssets>
            <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
        </PackageReference>
        <PackageReference Include="FluentAssertions" Version="[7.2.0]" />
        <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="8.0.0"/>
        <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.0"/>
        <PackageReference Include="NUnit" Version="4.4.0" />
        <PackageReference Include="NUnit3TestAdapter" Version="6.1.0" />
        <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.0.1" />
        <PackageReference Include="Castle.Core" version="5.2.1" />
        <PackageReference Include="Ninject" Version="3.3.6" />
        <PackageReference Condition="'$(TargetFramework)' == 'net8.0'" Include="System.Net.Http.Json" Version="8.0.1" />
        <Reference Condition="'$(TargetFramework)' == 'net472'" Include="System.Net.Http.Json" Version="8.0.1">
            <HintPath>$(TestCoreLibPath)/System.Net.Http.Json.dll</HintPath>
        </Reference>
        <PackageReference Include="NSubstitute" version="5.3.0" />
        <PackageReference Include="System.Runtime" version="4.3.1"/>
        <PackageReference Include="System.Threading.Tasks.Extensions" version="4.5.4"/>
        <Reference Include="*">
            <HintPath>.\Libs\Terrasoft.TestFramework.dll</HintPath>
        </Reference>
        <Reference Include="UnitTest">
            <HintPath>.\Libs\UnitTest.dll</HintPath>
        </Reference>
        <Reference Include="Atf.Repository.Mock">
            <HintPath>.\Libs\Atf.Repository.Mock.dll</HintPath>
        </Reference>
        
        <!-- Included in NET472 Build-->
        <Reference Condition="'$(TargetFramework)' == 'net472'" Include="Creatio.FeatureToggling.TestKit">
            <HintPath>$(TestCoreLibPath)/Creatio.FeatureToggling.TestKit.dll</HintPath>
        </Reference>
        
        <!-- NOT Included in NET8 Build -->
        <Reference Condition="'$(TargetFramework)' != 'net472'" Include="Creatio.FeatureToggling.TestKit">
            <HintPath>.\Libs\Creatio.FeatureToggling.TestKit.dll</HintPath>
        </Reference>

        <!-- Included in NET472 Build-->
        <Reference Condition="'$(TargetFramework)' == 'net472'" Include="Creatio.FeatureToggling.TestKit.NUnit">
            <HintPath>$(TestCoreLibPath)/Creatio.FeatureToggling.TestKit.NUnit.dll</HintPath>
        </Reference>
        <!-- NOT Included in NET8 Build -->
        <Reference Condition="'$(TargetFramework)' != 'net472'" Include="Creatio.FeatureToggling.TestKit.NUnit">
            <HintPath>.\Libs\Creatio.FeatureToggling.TestKit.NUnit.dll</HintPath>
        </Reference>
    </ItemGroup>
    
    <ItemGroup Label="Core References">
        <Reference Include="Terrasoft.Common">
            <HintPath>$(TestCoreLibPath)/Terrasoft.Common.dll</HintPath>
            <SpecificVersion>False</SpecificVersion>
            <Private>True</Private>
        </Reference>
        <Reference Include="Terrasoft.Core">
            <HintPath>$(TestCoreLibPath)/Terrasoft.Core.dll</HintPath>
            <SpecificVersion>False</SpecificVersion>
            <Private>True</Private>
        </Reference>
        <Reference Include="Terrasoft.Web.Common">
            <HintPath>$(TestCoreLibPath)/Terrasoft.Web.Common.dll</HintPath>
            <SpecificVersion>False</SpecificVersion>
            <Private>True</Private>
        </Reference>
        <Reference Include="Terrasoft.GlobalSearch">
            <HintPath>$(TestCoreLibPath)/Terrasoft.GlobalSearch.dll</HintPath>
            <Private>True</Private>
        </Reference>
        <Reference Include="Terrasoft.Nui.ServiceModel">
            <HintPath>$(TestCoreLibPath)/Terrasoft.Nui.ServiceModel.dll</HintPath>
            <Private>True</Private>
        </Reference>
        <Reference Include="Terrasoft.Web.Http.Abstractions">
            <HintPath>$(TestCoreLibPath)/Terrasoft.Web.Http.Abstractions.dll</HintPath>
            <SpecificVersion>False</SpecificVersion>
            <Private>True</Private>
        </Reference>
        <Reference Include="Terrasoft.Core.ConfigurationBuild">
            <HintPath>$(TestCoreLibPath)/Terrasoft.Core.ConfigurationBuild.dll</HintPath>
            <SpecificVersion>False</SpecificVersion>
            <Private>True</Private>
        </Reference>
        <Reference Include="Terrasoft.Core.DI">
            <HintPath>$(TestCoreLibPath)/Terrasoft.Core.DI.dll</HintPath>
            <SpecificVersion>False</SpecificVersion>
            <Private>True</Private>
        </Reference>
        <Reference Include="Terrasoft.Core.Packages">
            <HintPath>$(TestCoreLibPath)/Terrasoft.Core.Packages.dll</HintPath>
            <SpecificVersion>False</SpecificVersion>
            <Private>True</Private>
        </Reference>
        <Reference Include="Terrasoft.Core.Process">
            <HintPath>$(TestCoreLibPath)/Terrasoft.Core.Process.dll</HintPath>
            <SpecificVersion>False</SpecificVersion>
            <Private>True</Private>
        </Reference>
        <Reference Include="Terrasoft.Core.Scheduler">
            <HintPath>$(TestCoreLibPath)/Terrasoft.Core.Scheduler.dll</HintPath>
            <SpecificVersion>False</SpecificVersion>
            <Private>True</Private>
        </Reference>
        <Reference Include="Terrasoft.Core.ScriptEngine">
            <HintPath>$(TestCoreLibPath)/Terrasoft.Core.ScriptEngine.dll</HintPath>
            <SpecificVersion>False</SpecificVersion>
            <Private>True</Private>
        </Reference>
        <Reference Include="Terrasoft.Core.Translation">
            <HintPath>$(TestCoreLibPath)/Terrasoft.Core.Translation.dll</HintPath>
            <SpecificVersion>False</SpecificVersion>
            <Private>True</Private>
        </Reference>
        <Reference Include="Terrasoft.File.Abstractions">
            <HintPath>$(TestCoreLibPath)/Terrasoft.File.Abstractions.dll</HintPath>
            <SpecificVersion>False</SpecificVersion>
            <Private>True</Private>
        </Reference>
        <Reference Include="Terrasoft.File">
            <HintPath>$(TestCoreLibPath)/Terrasoft.File.dll</HintPath>
            <SpecificVersion>False</SpecificVersion>
            <Private>True</Private>
        </Reference>
        <Reference Include="Terrasoft.GoogleServerConnector">
            <HintPath>$(TestCoreLibPath)/Terrasoft.GoogleServerConnector.dll</HintPath>
            <SpecificVersion>False</SpecificVersion>
            <Private>True</Private>
        </Reference>
        <Reference Include="Terrasoft.IO">
            <HintPath>$(TestCoreLibPath)/Terrasoft.IO.dll</HintPath>
            <SpecificVersion>False</SpecificVersion>
            <Private>True</Private>
        </Reference>
        <Reference Include="Terrasoft.Monitoring">
            <HintPath>$(TestCoreLibPath)/Terrasoft.Monitoring.dll</HintPath>
            <SpecificVersion>False</SpecificVersion>
            <Private>True</Private>
        </Reference>
        <Reference Include="Terrasoft.Nui">
            <HintPath>$(TestCoreLibPath)/Terrasoft.Nui.dll</HintPath>
            <SpecificVersion>False</SpecificVersion>
            <Private>True</Private>
        </Reference>
        <Reference Include="Terrasoft.Messaging.Common">
            <HintPath>$(TestCoreLibPath)/Terrasoft.Messaging.Common.dll</HintPath>
            <SpecificVersion>False</SpecificVersion>
            <Private>True</Private>
        </Reference>
        <Reference Include="Terrasoft.Mobile">
            <HintPath>$(TestCoreLibPath)/Terrasoft.Mobile.dll</HintPath>
            <SpecificVersion>False</SpecificVersion>
            <Private>True</Private>
        </Reference>
        <Reference Include="Terrasoft.Services">
            <HintPath>$(TestCoreLibPath)/Terrasoft.Services.dll</HintPath>
            <SpecificVersion>False</SpecificVersion>
            <Private>True</Private>
        </Reference>
        <Reference Include="Terrasoft.Social">
            <HintPath>$(TestCoreLibPath)/Terrasoft.Social.dll</HintPath>
            <SpecificVersion>False</SpecificVersion>
            <Private>True</Private>
        </Reference>
        <Reference Include="Terrasoft.Sync">
            <HintPath>$(TestCoreLibPath)/Terrasoft.Sync.dll</HintPath>
            <SpecificVersion>False</SpecificVersion>
            <Private>True</Private>
        </Reference>
        <Reference Include="Terrasoft.GlobalSearch">
            <HintPath>$(TestCoreLibPath)/Terrasoft.GlobalSearch.dll</HintPath>
            <SpecificVersion>False</SpecificVersion>
            <Private>True</Private>
        </Reference>
        <Reference Include="Creatio.FeatureToggling">
            <HintPath>$(TestCoreLibPath)/Creatio.FeatureToggling.dll</HintPath>
            <SpecificVersion>False</SpecificVersion>
            <Private>True</Private>
        </Reference>
        <Reference Include="Common.Logging">
            <HintPath>$(TestCoreLibPath)/Common.Logging.dll</HintPath>
            <SpecificVersion>False</SpecificVersion>
            <Private>True</Private>
        </Reference>
        <Reference Include="Common.Logging.Core">
            <HintPath>$(TestCoreLibPath)/Common.Logging.Core.dll</HintPath>
            <SpecificVersion>False</SpecificVersion>
            <Private>True</Private>
        </Reference>
    </ItemGroup>
    <ItemGroup>
        <ProjectReference Include="..\..\packages\{{packageUnderTest}}\Files\{{packageUnderTest}}.csproj"/>
    </ItemGroup>
</Project>
