$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$venvPython = Join-Path $projectRoot '.venv\Scripts\python.exe'

if (-not (Test-Path -LiteralPath $venvPython)) {
    throw 'El proyecto aun no esta configurado. Ejecute CONFIGURAR.bat primero.'
}

Push-Location $projectRoot
try {
    & $venvPython manage.py check
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $venvPython manage.py makemigrations --check --dry-run
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $venvPython manage.py test
    exit $LASTEXITCODE
} finally {
    Pop-Location
}
