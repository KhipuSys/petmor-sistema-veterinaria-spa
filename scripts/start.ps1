$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$venvPython = Join-Path $projectRoot '.venv\Scripts\python.exe'

if (-not (Test-Path -LiteralPath $venvPython)) {
    throw 'El proyecto aun no esta configurado. Ejecute CONFIGURAR.bat primero.'
}

Push-Location $projectRoot
try {
    & $venvPython manage.py migrate --check
    if ($LASTEXITCODE -ne 0) { throw 'Hay migraciones pendientes. Ejecute CONFIGURAR.bat.' }
    Start-Process 'http://127.0.0.1:8000/login/'
    & $venvPython manage.py runserver 127.0.0.1:8000
} finally {
    Pop-Location
}
