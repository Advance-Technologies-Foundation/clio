$cliogate_Version="2.0.0.35"
$clioPath=".\clio\bin\Release\net8.0\clio.dll"

# Update _gateVersion in InfoCommand.cs
$infoCommandPath = ".\clio\Command\InfoCommand.cs"
$infoCommandContent = Get-Content $infoCommandPath -Raw
$updatedContent = $infoCommandContent -replace 'private const string _gateVersion = "[\d\.]+";', "private const string _gateVersion = `"$cliogate_Version`";"
Set-Content -Path $infoCommandPath -Value $updatedContent -NoNewline


dotnet build .\clio\clio.csproj -c Release --no-incremental

New-Item ".\cliogate\.temp" -ItemType Directory -Force
dotnet build .\cliogate\cliogate.csproj --output ".\cliogate\.temp" -p:CopyLocalLockFileAssemblies=true

dotnet restore .\cliogate\cliogate.csproj --force

dotnet build .\cliogate\cliogate.csproj -c Release -f net472 -p:TargetFrameworks=net472  --no-incremental  -p:AssemblyName=cliogate_netcore
dotnet build .\cliogate\cliogate.csproj -c Release -f netstandard2.0 -p:TargetFrameworks=netstandard2.0 --no-incremental -p:AssemblyName=cliogate_netcore
Copy-Item -Path ".\cliogate\.temp\ATF.Repository.dll"  -Destination ".\cliogate\Files\Bin" -Force
Copy-Item -Path ".\cliogate\.temp\ATF.Repository.dll"  -Destination ".\cliogate\Files\Bin\netstandard" -Force
dotnet $clioPath set-pkg-version ".\cliogate" --PackageVersion $cliogate_Version
(Get-Content ".\cliogate\descriptor.json") | Foreach-Object { $_ -replace '"Name": "cliogate",', '"Name": "cliogate_netcore",' } |  Out-File -encoding ASCII (".\cliogate\descriptor.json")
dotnet $clioPath compress .\cliogate -d .\clio\cliogate\cliogate_netcore.gz

dotnet build .\cliogate\cliogate.csproj -c Release -f net472 -p:TargetFrameworks=net472  --no-incremental  -p:AssemblyName=cliogate
dotnet build .\cliogate\cliogate.csproj -c Release -f netstandard2.0 -p:TargetFrameworks=netstandard2.0 --no-incremental -p:AssemblyName=cliogate
Copy-Item -Path ".\cliogate\.temp\ATF.Repository.dll"  -Destination ".\cliogate\Files\Bin" -Force
Copy-Item -Path ".\cliogate\.temp\ATF.Repository.dll"  -Destination ".\cliogate\Files\Bin\netstandard" -Force
dotnet $clioPath set-pkg-version ".\cliogate" --PackageVersion $cliogate_Version
(Get-Content ".\cliogate\descriptor.json") | Foreach-Object { $_ -replace '"Name": "cliogate_netcore",', '"Name": "cliogate",' } |  Out-File -encoding ASCII (".\cliogate\descriptor.json")
dotnet $clioPath compress .\cliogate -d .\clio\cliogate\cliogate.gz

Remove-Item -LiteralPath ".\cliogate\.temp" -Force -Recurse

Get-ChildItem .\cliogate\Files\Bin | Remove-Item -Force -Recurse

dotnet build .\clio\clio.csproj -c Release --no-incremental
dotnet build .\clio\clio.csproj -c Debug --no-incremental
