@echo off
setlocal
cd /d "%~dp0"

echo Cleaning generated build folders...
if exist bin rmdir /s /q bin
if exist obj rmdir /s /q obj
if exist Backend\CarScanner.Api\bin rmdir /s /q Backend\CarScanner.Api\bin
if exist Backend\CarScanner.Api\obj rmdir /s /q Backend\CarScanner.Api\obj
if exist Tests\CarScanner.Tests\bin rmdir /s /q Tests\CarScanner.Tests\bin
if exist Tests\CarScanner.Tests\obj rmdir /s /q Tests\CarScanner.Tests\obj

echo Restoring...
dotnet restore
if errorlevel 1 exit /b 1

echo Building backend...
dotnet build Backend\CarScanner.Api\CarScanner.Api.csproj --configuration Debug --no-restore
if errorlevel 1 exit /b 1

echo Building Android...
dotnet build CarScanner.csproj -f net10.0-android --configuration Debug --no-restore
if errorlevel 1 exit /b 1

echo BUILD SUCCESS
