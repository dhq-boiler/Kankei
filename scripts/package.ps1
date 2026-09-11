param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    $publishPath = Join-Path (Get-Location) "artifacts/publish/$Version"
    $releasePath = Join-Path (Get-Location) "artifacts/releases/$Version"
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'Tool restore failed.' }
    dotnet publish src/Kankei.Desktop/Kankei.Desktop.csproj -c Release -r win-x64 --self-contained true -p:Version=$Version -o $publishPath
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    dotnet tool run vpk -- pack --packId KankeiApp --packVersion $Version --packDir $publishPath --mainExe Kankei.Desktop.exe --packTitle Kankei --packAuthors dhq-boiler --icon assets/icons/kankei.ico --channel win --runtime win-x64 --delta None --outputDir $releasePath
    if ($LASTEXITCODE -ne 0) { throw 'Installer packaging failed.' }
    $installer = Get-ChildItem -LiteralPath $releasePath -Filter '*Setup.exe'
    if (!$installer) { throw 'Setup.exe was not generated.' }
    if (!(Test-Path (Join-Path $releasePath 'releases.win.json'))) { throw 'Update feed was not generated.' }
    Get-ChildItem -LiteralPath $releasePath -File | Select-Object Name,Length
} finally { Pop-Location }

