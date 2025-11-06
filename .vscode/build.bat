@echo off
setlocal

rem Map the UNC path (if any) to a temporary drive letter so tooling works.
pushd "%~dp0.."
set "REPO_ROOT=%CD%"

set "PROJECT=%REPO_ROOT%\.vscode\mod.csproj"
set "MOD_NAME=Gravload"
set "MOD_DIR=%REPO_ROOT%\gravload"
set "OUTPUT_DIR=%MOD_DIR%\1.6\Assemblies"

for %%I in ("C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods") do set "RIMWORLD_MODS_DIR=%%~fI"
set "TARGET_DIR=%RIMWORLD_MODS_DIR%\%MOD_NAME%"
set "LOCK_PATH=%TARGET_DIR%\.gravload_lock"

if not exist "%MOD_DIR%\1.6" (
    mkdir "%MOD_DIR%\1.6"
)

if not exist "%OUTPUT_DIR%" (
    mkdir "%OUTPUT_DIR%"
)

del /q "%OUTPUT_DIR%\*" >nul 2>&1

dotnet build "%PROJECT%" -c Release
if errorlevel 1 (
    set "RC=%ERRORLEVEL%"
    popd
    exit /b %RC%
)

if not exist "%RIMWORLD_MODS_DIR%" goto :mods_missing

if exist "%LOCK_PATH%" goto :lock_found

robocopy "%MOD_DIR%" "%TARGET_DIR%" /MIR /NFL /NDL /NJH /NJS /NP >nul
set "RC=%ERRORLEVEL%"
if %RC% GEQ 8 (
    echo Robocopy failed with exit code %RC%.
    popd
    exit /b %RC%
)

echo Copied mod contents to %TARGET_DIR%
popd
exit /b 0

:mods_missing
echo RimWorld Mods directory not found: %RIMWORLD_MODS_DIR%
popd
exit /b 1

:lock_found
echo RimWorld mod folder appears in use (lock file found: %LOCK_PATH%).
echo Please exit RimWorld and remove the lock file if the game is closed.
popd
exit /b 2
