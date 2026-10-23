@echo off
setlocal

cd /d "%~dp0"
title Petmor Sistema Veterinaria Spa

where dotnet >nul 2>&1
if errorlevel 1 (
    echo .NET SDK no esta instalado o no esta disponible en PATH.
    echo Instala .NET 9 SDK desde https://dotnet.microsoft.com/download/dotnet/9.0
    pause
    exit /b 1
)

echo Restaurando dependencias...
dotnet restore Petmor.Web.csproj
if errorlevel 1 (
    echo No se pudieron restaurar las dependencias.
    pause
    exit /b 1
)

echo Iniciando Petmor...
start "" /B dotnet run --project Petmor.Web.csproj --urls http://localhost:5000

echo Esperando a que Petmor este disponible...
timeout /t 5 /nobreak >nul
start "" "http://localhost:5000"

echo Petmor esta disponible en http://localhost:5000
echo La aplicacion se esta ejecutando en segundo plano.
pause

endlocal
