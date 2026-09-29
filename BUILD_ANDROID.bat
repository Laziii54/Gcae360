@echo off
setlocal
cd /d "%~dp0"

echo [1/2] Restoring packages...
dotnet restore CarScanner.csproj

echo [2/2] Publishing Signed Android Bundle...
dotnet publish CarScanner.csproj -c Release -f net10.0-android -p:AndroidKeyStore=true -p:AndroidSigningKeyAlias=carscanner -p:AndroidSigningKeystore=StoreRelease\carscanner.keystore -p:AndroidSigningKeyPass=123456 -p:AndroidSigningStorePass=123456

if errorlevel 1 goto :fail
echo.
echo BUILD SUCCEEDED - Android Release
exit /b 0

:fail
echo.
echo BUILD FAILED - see the error above.
exit /b 1