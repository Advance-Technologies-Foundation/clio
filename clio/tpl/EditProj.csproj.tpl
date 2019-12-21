<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props" Condition="Exists('$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props')" />
  <PropertyGroup>
    <Configuration Condition=" '$(Configuration)' == '' ">Debug</Configuration>
    <Platform Condition=" '$(Platform)' == '' ">AnyCPU</Platform>
    <ProjectGuid>{$guid1$}</ProjectGuid>
    <OutputType>Library</OutputType>
    <AppDesignerFolder>Properties</AppDesignerFolder>
    <RootNamespace>$safeprojectname$</RootNamespace>
    <AssemblyName>$safeprojectname$</AssemblyName>
    <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>
    <FileAlignment>512</FileAlignment>
  </PropertyGroup>
  <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' ">
    <DebugSymbols>true</DebugSymbols>
    <DebugType>full</DebugType>
    <Optimize>false</Optimize>
    <OutputPath>Files\Bin\</OutputPath>
    <DefineConstants>DEBUG;TRACE</DefineConstants>
    <ErrorReport>prompt</ErrorReport>
    <WarningLevel>4</WarningLevel>
  </PropertyGroup>
  <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Release|AnyCPU' ">
    <DebugType>pdbonly</DebugType>
    <Optimize>true</Optimize>
    <OutputPath>Files\Bin\</OutputPath>
    <DefineConstants>TRACE</DefineConstants>
    <ErrorReport>prompt</ErrorReport>
    <WarningLevel>4</WarningLevel>
  </PropertyGroup>
  <PropertyGroup Label="TemplateBuilder">
    <TemplateBuilderTargets Condition=" '$(TemplateBuilderTargets)'=='' ">$([System.IO.Path]::GetFullPath( $(MSBuildProjectDirectory)\..\packages\TemplateBuilder.1.1.6.1\tools\ligershark.templates.targets ))</TemplateBuilderTargets>
  </PropertyGroup>
  <ItemGroup>
$ref1$
   <Reference Include="Terrasoft.Core, Version=7.15.2.501, Culture=neutral, PublicKeyToken=edaadfbc3b0bb879, processorArchitecture=MSIL">
      <HintPath>packages\CreatioSDK.7.15.2.501\lib\net40\Terrasoft.Core.dll</HintPath>
    </Reference>
    <Reference Include="Terrasoft.Core.ConfigurationBuild, Version=7.15.2.501, Culture=neutral, PublicKeyToken=edaadfbc3b0bb879, processorArchitecture=MSIL">
      <HintPath>packages\CreatioSDK.7.15.2.501\lib\net40\Terrasoft.Core.ConfigurationBuild.dll</HintPath>
    </Reference>
    <Reference Include="Terrasoft.Core.DI, Version=7.15.2.501, Culture=neutral, PublicKeyToken=edaadfbc3b0bb879, processorArchitecture=MSIL">
      <HintPath>packages\CreatioSDK.7.15.2.501\lib\net40\Terrasoft.Core.DI.dll</HintPath>
    </Reference>
    <Reference Include="Terrasoft.Core.Packages, Version=7.15.2.501, Culture=neutral, PublicKeyToken=edaadfbc3b0bb879, processorArchitecture=MSIL">
      <HintPath>packages\CreatioSDK.7.15.2.501\lib\net40\Terrasoft.Core.Packages.dll</HintPath>
    </Reference>
    <Reference Include="Terrasoft.Core.Process, Version=7.15.2.501, Culture=neutral, PublicKeyToken=edaadfbc3b0bb879, processorArchitecture=MSIL">
      <HintPath>packages\CreatioSDK.7.15.2.501\lib\net40\Terrasoft.Core.Process.dll</HintPath>
    </Reference>
    <Reference Include="Terrasoft.Core.Scheduler, Version=7.15.2.501, Culture=neutral, PublicKeyToken=edaadfbc3b0bb879, processorArchitecture=MSIL">
      <HintPath>packages\CreatioSDK.7.15.2.501\lib\net40\Terrasoft.Core.Scheduler.dll</HintPath>
    </Reference>
    <Reference Include="Terrasoft.Core.ScriptEngine, Version=7.15.2.501, Culture=neutral, PublicKeyToken=edaadfbc3b0bb879, processorArchitecture=MSIL">
      <HintPath>packages\CreatioSDK.7.15.2.501\lib\net40\Terrasoft.Core.ScriptEngine.dll</HintPath>
    </Reference>
    <Reference Include="Terrasoft.Core.Translation, Version=7.15.2.501, Culture=neutral, PublicKeyToken=edaadfbc3b0bb879, processorArchitecture=MSIL">
      <HintPath>packages\CreatioSDK.7.15.2.501\lib\net40\Terrasoft.Core.Translation.dll</HintPath>
    </Reference>
    <Reference Include="Terrasoft.GlobalSearch, Version=7.15.2.501, Culture=neutral, PublicKeyToken=edaadfbc3b0bb879, processorArchitecture=MSIL">
      <HintPath>packages\CreatioSDK.7.15.2.501\lib\net40\Terrasoft.GlobalSearch.dll</HintPath>
    </Reference>
    <Reference Include="Terrasoft.GoogleServerConnector, Version=7.15.2.501, Culture=neutral, PublicKeyToken=edaadfbc3b0bb879, processorArchitecture=MSIL">
      <HintPath>packages\CreatioSDK.7.15.2.501\lib\net40\Terrasoft.GoogleServerConnector.dll</HintPath>
    </Reference>
    <Reference Include="Terrasoft.GoogleServices, Version=7.15.2.501, Culture=neutral, PublicKeyToken=edaadfbc3b0bb879, processorArchitecture=MSIL">
      <HintPath>packages\CreatioSDK.7.15.2.501\lib\net40\Terrasoft.GoogleServices.dll</HintPath>
    </Reference>
    <Reference Include="Terrasoft.IO, Version=7.15.2.501, Culture=neutral, PublicKeyToken=edaadfbc3b0bb879, processorArchitecture=MSIL">
      <HintPath>packages\CreatioSDK.7.15.2.501\lib\net40\Terrasoft.IO.dll</HintPath>
    </Reference>
    <Reference Include="Terrasoft.Messaging.Common, Version=7.15.2.501, Culture=neutral, PublicKeyToken=edaadfbc3b0bb879, processorArchitecture=MSIL">
      <HintPath>packages\CreatioSDK.7.15.2.501\lib\net40\Terrasoft.Messaging.Common.dll</HintPath>
    </Reference>
    <Reference Include="Terrasoft.Mobile, Version=7.15.2.501, Culture=neutral, PublicKeyToken=edaadfbc3b0bb879, processorArchitecture=MSIL">
      <HintPath>packages\CreatioSDK.7.15.2.501\lib\net40\Terrasoft.Mobile.dll</HintPath>
    </Reference>
    <Reference Include="Terrasoft.Monitoring, Version=7.15.2.501, Culture=neutral, PublicKeyToken=edaadfbc3b0bb879, processorArchitecture=MSIL">
      <HintPath>packages\CreatioSDK.7.15.2.501\lib\net40\Terrasoft.Monitoring.dll</HintPath>
    </Reference>
    <Reference Include="Terrasoft.Nui, Version=7.15.2.501, Culture=neutral, PublicKeyToken=edaadfbc3b0bb879, processorArchitecture=MSIL">
      <HintPath>packages\CreatioSDK.7.15.2.501\lib\net40\Terrasoft.Nui.dll</HintPath>
    </Reference>
    <Reference Include="Terrasoft.Nui.ServiceModel, Version=7.15.2.501, Culture=neutral, PublicKeyToken=edaadfbc3b0bb879, processorArchitecture=MSIL">
      <HintPath>packages\CreatioSDK.7.15.2.501\lib\net40\Terrasoft.Nui.ServiceModel.dll</HintPath>
    </Reference>
    <Reference Include="Terrasoft.Reports, Version=7.15.2.501, Culture=neutral, PublicKeyToken=edaadfbc3b0bb879, processorArchitecture=MSIL">
      <HintPath>packages\CreatioSDK.7.15.2.501\lib\net40\Terrasoft.Reports.dll</HintPath>
    </Reference>
    <Reference Include="Terrasoft.Services, Version=7.15.2.501, Culture=neutral, PublicKeyToken=edaadfbc3b0bb879, processorArchitecture=MSIL">
      <HintPath>packages\CreatioSDK.7.15.2.501\lib\net40\Terrasoft.Services.dll</HintPath>
    </Reference>
    <Reference Include="Terrasoft.Social, Version=7.15.2.501, Culture=neutral, PublicKeyToken=edaadfbc3b0bb879, processorArchitecture=MSIL">
      <HintPath>packages\CreatioSDK.7.15.2.501\lib\net40\Terrasoft.Social.dll</HintPath>
    </Reference>
    <Reference Include="Terrasoft.Sync, Version=7.15.2.501, Culture=neutral, PublicKeyToken=edaadfbc3b0bb879, processorArchitecture=MSIL">
      <HintPath>packages\CreatioSDK.7.15.2.501\lib\net40\Terrasoft.Sync.dll</HintPath>
    </Reference>
    <Reference Include="Terrasoft.UI.WebControls, Version=7.15.2.501, Culture=neutral, PublicKeyToken=edaadfbc3b0bb879, processorArchitecture=MSIL">
      <HintPath>packages\CreatioSDK.7.15.2.501\lib\net40\Terrasoft.UI.WebControls.dll</HintPath>
    </Reference>
    <Reference Include="Terrasoft.Web.Common, Version=7.15.2.501, Culture=neutral, PublicKeyToken=edaadfbc3b0bb879, processorArchitecture=MSIL">
      <HintPath>packages\CreatioSDK.7.15.2.501\lib\net40\Terrasoft.Web.Common.dll</HintPath>
    </Reference>
    <Reference Include="Terrasoft.Web.Http.Abstractions, Version=7.15.2.501, Culture=neutral, PublicKeyToken=edaadfbc3b0bb879, processorArchitecture=MSIL">
      <HintPath>packages\CreatioSDK.7.15.2.501\lib\net40\Terrasoft.Web.Http.Abstractions.dll</HintPath>
    </Reference>
  </ItemGroup>
  <ItemGroup />
  <ItemGroup>
    $files$
    <Compile Include="Properties\AssemblyInfo.cs" />
  </ItemGroup>
  <!-- ##BpmonlineSDKMarker## -->
  <ItemGroup>
    <Compile Include="Files\cs\*.cs" />
    <Content Include="Assemblies\**\*.*" />
    <Content Include="Data\**\*.*" />
    <Content Include="Resources\**\*.*" />
    <Content Include="Schemas\**\*.*" />
    <Content Include="SqlScripts\**\*.*" />
  </ItemGroup>
  <!-- ##BpmonlineSDKMarker## -->
  <ItemGroup>
    <None Include="descriptor.json" />
    <None Include="packages.config" />
  </ItemGroup>
  <ItemGroup>
$projects$
  </ItemGroup>
  <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
  <Import Project="$(TemplateBuilderTargets)" Condition="Exists('$(TemplateBuilderTargets)')" Label="TemplateBuilder" />
  <PropertyGroup>
    <PreBuildEvent></PreBuildEvent>
  </PropertyGroup>
</Project>