$cliogate_Version="2.0.0.14"

dotnet build .\clio\clio.csproj -c Release --no-incremental

New-Item ".\cliogate\.temp" -ItemType Directory -Force
dotnet build .\cliogate\cliogate.csproj --output ".\cliogate\.temp" -p:CopyLocalLockFileAssemblies=true

dotnet restore .\cliogate\cliogate.csproj --force

dotnet build .\cliogate\cliogate.csproj -c Release -f net472 -p:TargetFrameworks=net472  --no-incremental  -p:AssemblyName=cliogate_netcore
dotnet build .\cliogate\cliogate.csproj -c Release -f netstandard2.0 -p:TargetFrameworks=netstandard2.0 --no-incremental -p:AssemblyName=cliogate_netcore
Copy-Item -Path ".\cliogate\.temp\ATF.Repository.dll"  -Destination ".\cliogate\Files\Bin" -Force
Copy-Item -Path ".\cliogate\.temp\ATF.Repository.dll"  -Destination ".\cliogate\Files\Bin\netstandard" -Force
dotnet .\clio\bin\Release\netcoreapp3.1\clio.dll set-pkg-version ".\cliogate" --PackageVersion $cliogate_Version
(Get-Content ".\cliogate\descriptor.json") | Foreach-Object { $_ -replace '"Name": "cliogate",', '"Name": "cliogate_netcore",' } |  Out-File -encoding ASCII (".\cliogate\descriptor.json")
dotnet .\clio\bin\Release\netcoreapp3.1\clio.dll compress .\cliogate -d .\clio\cliogate\cliogate_netcore.gz

dotnet build .\cliogate\cliogate.csproj -c Release -f net472 -p:TargetFrameworks=net472  --no-incremental  -p:AssemblyName=cliogate
dotnet build .\cliogate\cliogate.csproj -c Release -f netstandard2.0 -p:TargetFrameworks=netstandard2.0 --no-incremental -p:AssemblyName=cliogate
Copy-Item -Path ".\cliogate\.temp\ATF.Repository.dll"  -Destination ".\cliogate\Files\Bin" -Force
Copy-Item -Path ".\cliogate\.temp\ATF.Repository.dll"  -Destination ".\cliogate\Files\Bin\netstandard" -Force
dotnet .\clio\bin\Release\netcoreapp3.1\clio.dll set-pkg-version ".\cliogate" --PackageVersion $cliogate_Version
(Get-Content ".\cliogate\descriptor.json") | Foreach-Object { $_ -replace '"Name": "cliogate_netcore",', '"Name": "cliogate",' } |  Out-File -encoding ASCII (".\cliogate\descriptor.json")
dotnet .\clio\bin\Release\netcoreapp3.1\clio.dll compress .\cliogate -d .\clio\cliogate\cliogate.gz

Remove-Item -LiteralPath ".\cliogate\.temp" -Force -Recurse

Get-ChildItem .\cliogate\Files\Bin | Remove-Item -Force -Recurse

dotnet build .\clio\clio.csproj -c Release --no-incremental
dotnet build .\clio\clio.csproj -c Debug --no-incremental
