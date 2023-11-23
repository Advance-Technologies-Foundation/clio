<Project Sdk="Microsoft.NET.Sdk">
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
                <TargetFramework>net6.0</TargetFramework>
            </PropertyGroup>
        </When>
    </Choose>

    <PropertyGroup>
        <IsPackable>false</IsPackable>
        <RootNamespace>{{packageUnderTest}}.Tests</RootNamespace>
        <PlatformTarget>x64</PlatformTarget>
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
        <PackageReference Include="coverlet.msbuild" Version="3.2.0">
            <PrivateAssets>all</PrivateAssets>
            <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
        </PackageReference>
        <PackageReference Include="FluentAssertions" Version="5.6.0"/>
        <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="3.1.6"/>
        <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="3.1.6"/>
        <PackageReference Include="NUnit" Version="3.13.3"/>
        <PackageReference Include="NUnit3TestAdapter" Version="4.5.0"/>
        <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.7.2"/>
        <PackageReference Include="Castle.Core" version="4.4.0"/>
        <PackageReference Include="Ninject" version="3.3.3"/>
        <PackageReference Include="NSubstitute" version="3.1.0"/>
        <PackageReference Include="System.Runtime" version="4.3.0"/>
        <PackageReference Include="System.Threading.Tasks.Extensions" version="4.5.4"/>
        <Reference Include="*">
            <HintPath>.\Libs\Terrasoft.TestFramework.dll</HintPath>
        </Reference>
        <Reference Include="UnitTest">
            <HintPath>.\Libs\UnitTest.dll</HintPath>
        </Reference>
    </ItemGroup>
    
    <!-- 
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
     -->
    <ItemGroup>
        <ProjectReference Include="..\..\packages\{{packageUnderTest}}\Files\{{packageUnderTest}}.csproj"/>
    </ItemGroup>
</Project>