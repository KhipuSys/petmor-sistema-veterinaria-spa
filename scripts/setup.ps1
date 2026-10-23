$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$venvPython = Join-Path $projectRoot '.venv\Scripts\python.exe'

Push-Location $projectRoot
try {
    if (-not (Test-Path -LiteralPath $venvPython)) {
        $launcher = Get-Command py -ErrorAction SilentlyContinue
        if ($launcher) {
            & py -3 -m venv .venv
        } else {
            $python = Get-Command python -ErrorAction SilentlyContinue
            if (-not $python) { throw 'Python 3.10 o superior no esta instalado.' }
            & python -m venv .venv
        }
        if ($LASTEXITCODE -ne 0) { throw 'No se pudo crear el entorno virtual.' }
    }

    & $venvPython -c "import sys; raise SystemExit(0 if sys.version_info >= (3, 10) else 1)"
    if ($LASTEXITCODE -ne 0) { throw 'Petmor requiere Python 3.10 o superior.' }

    & $venvPython -m pip install --upgrade pip
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo actualizar pip.' }
    & $venvPython -m pip install -r requirements.txt
    if ($LASTEXITCODE -ne 0) { throw 'No se pudieron instalar las dependencias.' }
    & $venvPython manage.py migrate
    if ($LASTEXITCODE -ne 0) { throw 'No se pudieron aplicar las migraciones.' }
    & $venvPython manage.py seed_demo
    if ($LASTEXITCODE -ne 0) { throw 'No se pudieron crear los datos de demostracion.' }
    & $venvPython manage.py check
    if ($LASTEXITCODE -ne 0) { throw 'Django detecto errores de configuracion.' }

    Write-Host ''
    Write-Host 'Petmor quedo configurado correctamente.' -ForegroundColor Green
    Write-Host 'Usuario: admin_demo'
    if ($env:PETMOR_DEMO_PASSWORD) {
        Write-Host 'Contrasena: valor configurado en PETMOR_DEMO_PASSWORD'
    } else {
        Write-Host 'Contrasena: PetmorDemo2026!'
    }
    Write-Host 'Ejecute INICIAR.bat para abrir el sistema.'
} finally {
    Pop-Location
}
