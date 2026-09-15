setlocal
cd /d "%~dp0"

echo ============================================
echo  Building VariableFleaPrices for SPT 5.0
echo ============================================
echo.

dotnet build VariableFleaPrices.csproj -c Release
if errorlevel 1 (
    echo.
    echo BUILD FAILED.
    pause
    exit /b 1
)

echo.
echo BUILD SUCCEEDED.
echo Output: bin\Release\VariableFleaPrices\
echo.
pause