@echo off
setlocal enabledelayedexpansion

set "SCRIPT_DIR=%~dp0"
set "DLL_PATH="

if exist "%SCRIPT_DIR%Packages\com.exoforge.sdk\Editor\Cli~\exo.dll" (
    set "DLL_PATH=%SCRIPT_DIR%Packages\com.exoforge.sdk\Editor\Cli~\exo.dll"
) else if exist "%SCRIPT_DIR%..\Packages\com.exoforge.sdk\Editor\Cli~\exo.dll" (
    set "DLL_PATH=%SCRIPT_DIR%..\Packages\com.exoforge.sdk\Editor\Cli~\exo.dll"
) else (
    for /d %%D in ("%SCRIPT_DIR%Library\PackageCache\com.exoforge.sdk@*") do (
        if exist "%%D\Editor\Cli~\exo.dll" set "DLL_PATH=%%D\Editor\Cli~\exo.dll"
    )
    if "!DLL_PATH!"=="" (
        for /d %%D in ("%SCRIPT_DIR%..\Library\PackageCache\com.exoforge.sdk@*") do (
            if exist "%%D\Editor\Cli~\exo.dll" set "DLL_PATH=%%D\Editor\Cli~\exo.dll"
        )
    )
)

if "%DLL_PATH%"=="" (
    if exist "%SCRIPT_DIR%Editor\Cli~\exo.dll" set "DLL_PATH=%SCRIPT_DIR%Editor\Cli~\exo.dll"
)

if "%DLL_PATH%"=="" (
    echo Error: Exoforge CLI ^(exo.dll^) not found in Packages or Library\PackageCache. >&2
    exit /b 1
)

set "HAS_DIR="
for %%A in (%*) do (
    if "%%~A"=="--dir" set "HAS_DIR=1"
)

if defined HAS_DIR (
    dotnet "%DLL_PATH%" %*
) else (
    set "WORKSPACE_DIR=%SCRIPT_DIR%"
    if exist "%SCRIPT_DIR%Exoforge" set "WORKSPACE_DIR=%SCRIPT_DIR%Exoforge"
    dotnet "%DLL_PATH%" %* --dir "!WORKSPACE_DIR!"
)