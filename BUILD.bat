@echo off
echo ================================================
echo   Moore Threads UpScaler 1.0 - Build Script
echo ================================================
echo.

:: Check for .NET 8 SDK
dotnet --version >nul 2>&1
if errorlevel 1 (
    echo ERROR: .NET SDK not found.
    echo Install .NET 8.0 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

echo .NET SDK version:
dotnet --version
echo.

:: Restore NuGet packages
echo [1/3] Restoring packages...
dotnet restore MooreThreads.sln
if errorlevel 1 ( echo ERROR: Restore failed & pause & exit /b 1 )

:: Build
echo.
echo [2/3] Building Release...
dotnet build MooreThreads.sln -c Release --no-restore
if errorlevel 1 ( echo ERROR: Build failed & pause & exit /b 1 )

:: Publish self-contained single-file exe into .\publish\
echo.
echo [3/3] Publishing self-contained executable...
dotnet publish src\MooreThreads.App\MooreThreads.App.csproj ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:EnableCompressionInSingleFile=true ^
    --no-build ^
    -o publish

if errorlevel 1 ( echo ERROR: Publish failed & pause & exit /b 1 )

echo.
echo ================================================
echo   Build Complete!
echo ================================================
echo.
echo   Executable: %~dp0publish\MooreThreadsUpScaler.exe
echo.
pause
