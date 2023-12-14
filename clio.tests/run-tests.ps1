# Install report generater
# dotnet tool install --global dotnet-reportgenerator-globaltool --version 5.2.0

dotnet test clio.tests.csproj /p:CollectCoverage=true /p:CoverletOutputFormat=opencover /p:CoverletOutput=TestResults\clio-tests.opencover.xml
reportgenerator -reports:TestResults\clio-tests.opencover.xml -targetdir:TestResults\Html -historydir:TestResults\History


# To view results in browser install LiveReloadServer
# dotnet tool install -g LiveReloadServer
LiveReloadServer "TestResults\Html"--LiveReloadEnabled True --OpenBrowser