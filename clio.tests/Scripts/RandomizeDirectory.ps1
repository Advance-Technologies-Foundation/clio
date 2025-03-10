param(
    [string]$Path = "C:\Projects\ATF\clio\clio.tests\Examples\WebFarm\Node2-Incorrect",
    [int]$NumberOfOperations = 10
)

# Check if the directory exists
if (-not (Test-Path -Path $Path)) {
    Write-Error "Path $Path does not exist."
    exit
}

# Function to get a random file or folder
function Get-RandomFileOrFolder {
    param([string]$RootPath)
    $items = Get-ChildItem -Path $RootPath -Recurse -File
    if ($items.Count -eq 0) {
        return $null
    }
    return $items | Get-Random
}

# Function to create a new file
function Create-RandomFile {
    param([string]$RootPath)
    $randomFileName = [System.IO.Path]::GetRandomFileName()
    $newFilePath = Join-Path -Path $RootPath -ChildPath $randomFileName
    Set-Content -Path $newFilePath -Value "Random content $(Get-Random)"
    Write-Host "Created file: $newFilePath"
}

# Function to modify the content of a file
function Modify-RandomFile {
    param([string]$FilePath)
    $newContent = "Modified content $(Get-Random)"
    Set-Content -Path $FilePath -Value $newContent
    Write-Host "Modified file: $FilePath"
}

# Function to delete a file
function Delete-RandomFile {
    param([string]$FilePath)
    Remove-Item -Path $FilePath -Force
    Write-Host "Deleted file: $FilePath"
}

# Perform operations
for ($i = 1; $i -le $NumberOfOperations; $i++) {
    $operation = Get-Random -InputObject @("Create", "Modify", "Delete")
    switch ($operation) {
        "Create" {
            Create-RandomFile -RootPath $Path
        }
        "Modify" {
            $randomFile = Get-RandomFileOrFolder -RootPath $Path
            if ($null -ne $randomFile) {
                Modify-RandomFile -FilePath $randomFile.FullName
            }
        }
        "Delete" {
            $randomFile = Get-RandomFileOrFolder -RootPath $Path
            if ($null -ne $randomFile) {
                Delete-RandomFile -FilePath $randomFile.FullName
            }
        }
    }
}
