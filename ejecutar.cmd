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
dotnet restore MiniMarket.Web.csproj
if errorlevel 1 (
    echo No se pudieron restaurar las dependencias.
    pause
    exit /b 1
)

echo Iniciando Petmor...
dotnet run --project MiniMarket.Web.csproj

if errorlevel 1 (
    echo La aplicacion termino con un error.
    pause
)

endlocal
