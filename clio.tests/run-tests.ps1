#Run tests onclio.tests.csproj

# Clean up TestResults folder except for History and Html
Get-ChildItem -Path .\TestResults -Exclude History, Html | Remove-Item -Recurse -Force

dotnet test clio.tests.csproj --collect:"XPlat Code Coverage";
$report = Get-ChildItem -Path .\TestResults -Recurse -Filter coverage.cobertura.xml;
Copy-Item $report.FullName -Destination .\TestResults\coverage.opencover.xml -Force;

#Publish UnitTest Results to TestResults folder (folder is ignored in .gitignore)
# Install report generater
# dotnet tool install --global dotnet-reportgenerator-globaltool

reportgenerator -reports:TestResults\coverage.opencover.xml `
          -targetdir:".\TestResults\Html" `
          -historydir:".\TestResults\History" `
          -assemblyfilters:"+Clio;-Terrasoft.*" `
          -title:"Clio Unit Tests" `
          -reporttypes:"Html;SonarQube;MarkdownSummaryGithub;Badges";

# To view results in browser install LiveReloadServer
# dotnet tool install -g LiveReloadServer
LiveReloadServer "TestResults\Html"--LiveReloadEnabled True --OpenBrowser