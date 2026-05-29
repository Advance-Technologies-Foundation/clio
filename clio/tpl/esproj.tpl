<Project Sdk="Microsoft.VisualStudio.JavaScript.Sdk">

  <!--
    Wraps the Angular "<%projectName%>" project so that building MainSolution.slnx (dotnet/msbuild,
    or Visual Studio) also produces the client bundle. The JavaScript SDK runs `npm install`
    (only when package.json / package-lock.json change) and then the build command below.

    `ng build` emits straight into the Creatio package — see angular.json `outputPath` — so there
    is no separate dist to copy. The SDK version is pinned centrally in the repo-root global.json,
    so the Sdk attribute above is intentionally version-less.
  -->
  <PropertyGroup>
    <!--
      Declare the same build configurations the solution defines so the .slnx auto-maps and builds
      this project under each of them. `ng build` is configuration-agnostic, so dev-n8 / dev-nf
      behave the same as Debug here. NOTE: this alone is not enough for a custom solution config to
      select the project — the empty <Build /> element in MainSolution.slnx is what forces it.
    -->
    <Configurations>Debug;Release;dev-n8;dev-nf</Configurations>

    <BuildCommand>npm run build</BuildCommand>
    <StartupCommand>npm start</StartupCommand>
    <ShouldRunBuildScript>true</ShouldRunBuildScript>

    <!--
      Where `ng build` writes the bundle (angular.json outputPath). angular.json leaves
      deleteOutputPath at its default (true), so every build recreates this folder. `dotnet clean`
      runs the npm `clean` script below to remove the bundle; the next build regenerates it.
    -->
    <BuildOutputFolder>$(MSBuildProjectDirectory)\<%distPath%></BuildOutputFolder>
    <CleanCommand>npm run clean</CleanCommand>

    <!-- Unit tests run through Jest (see jest.config.ts / `npm test`). -->
    <JavaScriptTestRoot>src\</JavaScriptTestRoot>
    <JavaScriptTestFramework>Jest</JavaScriptTestFramework>
  </PropertyGroup>

</Project>
