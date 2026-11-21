param(
    [string]$InnoSetupCompiler
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$project = Join-Path $root "Flavirus.UI\Flavirus.UI.csproj"
$script = Join-Path $PSScriptRoot "LivGuard.iss"
$publishDir = Join-Path $root "artifacts\publish"
$installerDir = Join-Path $root "artifacts\installer"
[xml]$projectFile = Get-Content -Path $project -Raw
$version = [string]$projectFile.Project.PropertyGroup.Version | Select-Object -First 1
if (-not $version) {
    throw "A versão não foi definida em Flavirus.UI.csproj."
}

Push-Location $root
try {
    $localDotnetRoot = Join-Path $env:LOCALAPPDATA "dotnet"
    if (Test-Path (Join-Path $localDotnetRoot "sdk\11.0.100")) {
        $env:PATH = "$localDotnetRoot;$env:PATH"
    }

    if (Test-Path $publishDir) {
        Remove-Item -LiteralPath $publishDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

    Write-Host "Publicando LivGuard para Windows x64..." -ForegroundColor Cyan
    dotnet publish $project -c Release -r win-x64 --self-contained true `
        -p:Version=$version -p:DebugType=None -p:DebugSymbols=false `
        -o $publishDir
    if ($LASTEXITCODE -ne 0) {
        throw "A publicação falhou (código $LASTEXITCODE)."
    }

    if (-not $InnoSetupCompiler) {
        $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($command) {
            $InnoSetupCompiler = $command.Source
        }
        else {
            $candidates = @(
                "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
                "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
                "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
            )
            $InnoSetupCompiler = $candidates |
                Where-Object { Test-Path $_ } |
                Select-Object -First 1
        }
    }

    if (-not $InnoSetupCompiler -or -not (Test-Path $InnoSetupCompiler)) {
        throw "A publicação terminou, mas o compilador Inno Setup (ISCC.exe) não foi encontrado. Instale o Inno Setup 6 ou informe -InnoSetupCompiler."
    }

    New-Item -ItemType Directory -Path $installerDir -Force | Out-Null
    Write-Host "Compilando o instalador..." -ForegroundColor Cyan
    & $InnoSetupCompiler "/DMyAppVersion=$version" $script
    if ($LASTEXITCODE -ne 0) {
        throw "A compilação do instalador falhou (código $LASTEXITCODE)."
    }

    $setupPath = Join-Path $installerDir "LivGuard-Setup-$version-x64.exe"
    $hash = (Get-FileHash $setupPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -Path "$setupPath.sha256" -Value "$hash  $(Split-Path $setupPath -Leaf)" -Encoding Ascii
    Write-Host "Instalador criado: $setupPath" -ForegroundColor Green
    Write-Host "SHA-256: $hash"
}
finally {
    Pop-Location
}
