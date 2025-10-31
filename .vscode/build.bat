@echo off
setlocal

set "PROJECT=%~dp0mod.csproj"

for %%I in ("%~dp0..") do set "TEMPLATE_ROOT=%%~fI"
set "MOD_ROOT=%TEMPLATE_ROOT%\placeholder"
set "OUTPUT_DIR=%MOD_ROOT%\1.6\Assemblies"

if not exist "%MOD_ROOT%\1.6" (
    mkdir "%MOD_ROOT%\1.6"
)

if not exist "%OUTPUT_DIR%" (
    mkdir "%OUTPUT_DIR%"
)

echo Building placeholder mod assembly into %OUTPUT_DIR%
dotnet build "%PROJECT%" -c Release
if errorlevel 1 (
    exit /b %errorlevel%
)

echo.
echo Build complete. Sync this template with a real RimWorld mod folder when ready.
exit /b 0
