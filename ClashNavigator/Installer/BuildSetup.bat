@echo off
rem ---------------------------------------------------------------------------
rem  Builds Clash Navigator for Revit 2022 - 2026 and packs one setup file.
rem
rem  Everything is quoted because the project path contains an "&" character,
rem  which otherwise breaks the command line.
rem
rem  Needs: .NET SDK 8 (comes with Visual Studio 2022) and Inno Setup 6.
rem ---------------------------------------------------------------------------

setlocal
cd /d "%~dp0"

set "PROJECT=..\ClashNavigator.csproj"

echo.
echo ============================================
echo   Building Clash Navigator - 5 Revit builds
echo ============================================
echo.

for %%V in (R22 R23 R24 R25 R26) do (
    echo --- %%V ---
    dotnet build "%PROJECT%" -c %%V -v minimal --nologo || goto :buildfailed
    echo.
)

echo ============================================
echo   Packing the installer
echo ============================================
echo.

set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"

if not exist "%ISCC%" (
    echo Inno Setup 6 was not found.
    echo Install it from https://jrsoftware.org/isdl.php and run this file again.
    goto :end
)

"%ISCC%" "Setup.iss" || goto :packfailed

echo.
echo Done. The setup file is in the Output folder next to this script.
goto :end

:buildfailed
echo.
echo BUILD FAILED. Scroll up for the first error.
goto :end

:packfailed
echo.
echo THE INSTALLER FAILED TO BUILD. Scroll up for the error.

:end
echo.
pause
endlocal
