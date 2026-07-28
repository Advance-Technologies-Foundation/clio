<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>{{targetFramework}}</TargetFramework>
    <LangVersion>latest</LangVersion>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <RootNamespace>{{packageName}}.IntegrationTests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Allure.NUnit" Version="2.15.0" />
    <PackageReference Include="ATF.Repository" Version="2.0.3.5" />
    <PackageReference Include="FluentAssertions" Version="7.2.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.0.1" />
    <PackageReference Include="NUnit" Version="4.4.0" />
    <PackageReference Include="NUnit3TestAdapter" Version="6.1.0" />
  </ItemGroup>
  <ItemGroup>
    <None Update="allureConfig.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
