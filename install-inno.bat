@echo off
chcp 65001 >nul

echo ========================================
echo Inno Setup 설치
echo ========================================
echo.

REM 설치 확인
if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" goto :already_installed
if exist "C:\Program Files\Inno Setup 6\ISCC.exe" goto :already_installed
if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" goto :already_installed

echo Inno Setup이 설치되어 있지 않습니다.
echo.
echo 설치 방법을 선택하세요:
echo 1. winget으로 자동 설치 (권장)
echo 2. 수동 다운로드 및 설치
echo 3. 취소
echo.
set /p choice="선택 (1-3): "

if "%choice%"=="1" goto :install_winget
if "%choice%"=="2" goto :install_manual
goto :end

:install_winget
echo.
echo winget으로 설치 중...
winget install --id JRSoftware.InnoSetup --silent --accept-package-agreements --accept-source-agreements
if errorlevel 1 (
    echo.
    echo winget 설치 실패. 수동 설치를 시도하세요.
    goto :install_manual
)
echo.
echo 설치 완료!
goto :check_install

:install_manual
echo.
echo 브라우저에서 다운로드 페이지를 엽니다...
start https://jrsoftware.org/isdl.php
echo.
echo 다운로드 후 설치하고 아무 키나 누르세요...
pause
goto :check_install

:check_install
echo.
echo 설치 확인 중...
if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" goto :success
if exist "C:\Program Files\Inno Setup 6\ISCC.exe" goto :success
if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" goto :success
echo.
echo 설치를 확인할 수 없습니다.
echo 설치 후 다시 실행하세요.
goto :end

:already_installed
echo Inno Setup이 이미 설치되어 있습니다.
if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" echo 경로: C:\Program Files (x86)\Inno Setup 6\
if exist "C:\Program Files\Inno Setup 6\ISCC.exe" echo 경로: C:\Program Files\Inno Setup 6\
if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" echo 경로: %LOCALAPPDATA%\Programs\Inno Setup 6\
goto :end

:success
echo.
echo ========================================
echo 설치 완료!
echo ========================================
echo.
echo 이제 build-release.bat를 실행할 수 있습니다.

:end
echo.
pause
