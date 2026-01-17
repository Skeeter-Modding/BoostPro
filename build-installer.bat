@echo off
echo ========================================
echo Building BoostPro Installer
echo ========================================
echo.

REM Change to script directory
cd /d "%~dp0"

REM Clean previous builds
echo [1/4] Cleaning previous builds...
if exist "publish" rmdir /s /q "publish"
if exist "installer_output" rmdir /s /q "installer_output"

REM Build self-contained single-file executable
echo [2/4] Building self-contained executable...
dotnet publish BoostProUI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish

if errorlevel 1 (
    echo ERROR: Build failed!
    pause
    exit /b 1
)

echo.
echo [3/4] Build complete! Files in 'publish' folder:
dir /b publish
echo.

REM Check if Inno Setup is installed
set INNO_PATH=
if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" (
    set "INNO_PATH=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
) else if exist "C:\Program Files\Inno Setup 6\ISCC.exe" (
    set "INNO_PATH=C:\Program Files\Inno Setup 6\ISCC.exe"
)

if defined INNO_PATH (
    echo [4/4] Building installer with Inno Setup...
    "%INNO_PATH%" installer.iss
    if errorlevel 1 (
        echo ERROR: Installer build failed!
        pause
        exit /b 1
    )
    echo.
    echo ========================================
    echo SUCCESS! Installer created at:
    echo installer_output\BoostPro_Setup.exe
    echo ========================================
) else (
    echo [4/4] Inno Setup not found!
    echo.
    echo To create an installer, download Inno Setup from:
    echo https://jrsoftware.org/isdl.php
    echo.
    echo Then run this script again, OR manually compile installer.iss
    echo.
    echo For now, you can distribute the files in the 'publish' folder directly.
    echo The main executable is: publish\BoostProUI.exe
)

echo.
pause
