@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

echo ========================================
echo SecureMemo 릴리즈 빌드
echo ========================================
echo.

REM 버전 읽기
for /f "tokens=2 delims=<>" %%a in ('findstr "<Version>" SecureMemo.csproj') do set CURRENT_VERSION=%%a
echo 현재 버전: %CURRENT_VERSION%
echo.

REM 새 버전 입력
set /p NEW_VERSION="새 버전 입력 (Enter=현재 버전): "
if "%NEW_VERSION%"=="" set NEW_VERSION=%CURRENT_VERSION%
echo 빌드 버전: %NEW_VERSION%
echo.

REM 버전 업데이트
if not "%NEW_VERSION%"=="%CURRENT_VERSION%" (
    echo 버전 업데이트 중...
    powershell -Command "(gc SecureMemo.csproj) -replace '<Version>.*</Version>', '<Version>%NEW_VERSION%</Version>' | Out-File -encoding UTF8 SecureMemo.csproj"
    powershell -Command "(gc SecureMemo.csproj) -replace '<AssemblyVersion>.*</AssemblyVersion>', '<AssemblyVersion>%NEW_VERSION%.0</AssemblyVersion>' | Out-File -encoding UTF8 SecureMemo.csproj"
    powershell -Command "(gc SecureMemo.csproj) -replace '<FileVersion>.*</FileVersion>', '<FileVersion>%NEW_VERSION%.0</FileVersion>' | Out-File -encoding UTF8 SecureMemo.csproj"
    echo ✅ 버전 업데이트 완료
    echo.
)

echo ========================================
echo 빌드 시작
echo ========================================
echo.

REM 기존 프로세스 종료
taskkill /F /IM SecureMemo.exe 2>nul
timeout /t 1 /nobreak >nul

REM 빌드 폴더 정리
echo 빌드 폴더 정리 중...
if exist "bin\Release" rmdir /s /q "bin\Release"
if exist "obj\Release" rmdir /s /q "obj\Release"
dotnet clean -c Release >nul 2>nul
echo ✅ 정리 완료
echo.

REM 빌드
echo 포터블 버전 빌드 중...
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

if errorlevel 1 (
    echo ❌ 빌드 실패!
    pause
    exit /b 1
)
echo ✅ 빌드 완료
echo.

REM 릴리즈 폴더 생성
set RELEASE_DIR=release-package\v%NEW_VERSION%
if not exist "%RELEASE_DIR%" mkdir "%RELEASE_DIR%"

REM 포터블 ZIP 생성
echo 포터블 버전 압축 중...
set ZIP_FILE=%RELEASE_DIR%\SecureMemo-Portable-v%NEW_VERSION%.zip
powershell -Command "Compress-Archive -Path 'bin\Release\net10.0-windows\win-x64\publish\*' -DestinationPath '%ZIP_FILE%' -Force"
echo ✅ 압축 완료
echo.

REM 설치 파일 생성
echo 설치 파일 생성 확인 중...
echo.

REM Inno Setup 확인 (여러 경로 확인)
set INNO_FOUND=0
set INNO_SETUP=""

REM 경로 1: Program Files (x86)
if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" (
    set "INNO_SETUP=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
    set INNO_FOUND=1
)

REM 경로 2: Program Files
if exist "C:\Program Files\Inno Setup 6\ISCC.exe" (
    set "INNO_SETUP=C:\Program Files\Inno Setup 6\ISCC.exe"
    set INNO_FOUND=1
)

REM 경로 3: 사용자 AppData (winget 설치 시)
if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" (
    set "INNO_SETUP=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
    set INNO_FOUND=1
)

if %INNO_FOUND%==1 (
    echo ✅ Inno Setup 발견
    echo    경로: %INNO_SETUP%
    echo 설치 파일 생성 중...
    
    REM installer.iss 버전 업데이트
    powershell -Command "(gc installer.iss) -replace '#define MyAppVersion \".*\"', '#define MyAppVersion \"%NEW_VERSION%\"' | Out-File -encoding UTF8 installer.iss"
    
    REM Inno Setup 실행
    "%INNO_SETUP%" installer.iss
    
    if exist "release-package\SecureMemo-Setup-v%NEW_VERSION%.exe" (
        move "release-package\SecureMemo-Setup-v%NEW_VERSION%.exe" "%RELEASE_DIR%\" >nul
        echo ✅ 설치 파일 생성 완료
    ) else (
        echo ⚠️  설치 파일 생성 실패
        echo    예상 위치: release-package\SecureMemo-Setup-v%NEW_VERSION%.exe
    )
) else (
    echo ⚠️  Inno Setup을 찾을 수 없습니다.
    echo.
    echo 설치 파일을 생성하려면:
    echo    install-inno.bat 실행
    echo.
)
echo.

REM 릴리즈 노트 생성
echo 릴리즈 노트 생성 중...
set NOTES_FILE=%RELEASE_DIR%\RELEASE_NOTES.txt
echo SecureMemo v%NEW_VERSION% > "%NOTES_FILE%"
echo. >> "%NOTES_FILE%"
echo 변경 사항: >> "%NOTES_FILE%"
echo - 버그 수정 및 개선 >> "%NOTES_FILE%"
echo. >> "%NOTES_FILE%"
echo 다운로드: >> "%NOTES_FILE%"
if exist "%RELEASE_DIR%\SecureMemo-Setup-v%NEW_VERSION%.exe" (
    echo - 설치 파일: SecureMemo-Setup-v%NEW_VERSION%.exe (권장) >> "%NOTES_FILE%"
)
echo - 포터블 버전: SecureMemo-Portable-v%NEW_VERSION%.zip >> "%NOTES_FILE%"
echo. >> "%NOTES_FILE%"
echo 시스템 요구사항: >> "%NOTES_FILE%"
echo - Windows 10/11 (64-bit) >> "%NOTES_FILE%"
echo - .NET 10.0 Runtime (자동 포함) >> "%NOTES_FILE%"
echo ✅ 릴리즈 노트 생성 완료
echo.

echo ========================================
echo ✅ 빌드 완료!
echo ========================================
echo.
echo 버전: v%NEW_VERSION%
echo 📁 릴리즈 폴더: %RELEASE_DIR%
echo.
echo 📦 생성된 파일:
if exist "%RELEASE_DIR%\SecureMemo-Setup-v%NEW_VERSION%.exe" (
    echo    ✅ SecureMemo-Setup-v%NEW_VERSION%.exe (설치 파일)
)
echo    ✅ SecureMemo-Portable-v%NEW_VERSION%.zip (포터블)
echo    ✅ RELEASE_NOTES.txt (릴리즈 노트)
echo.
echo 1. https://github.com/grasion/SecureMemo/releases/new 방문
echo 2. Tag version: v%NEW_VERSION% 입력
echo 3. Release title: SecureMemo v%NEW_VERSION% 입력
echo 4. 위 파일들을 드래그 앤 드롭
echo 5. RELEASE_NOTES.txt 내용을 복사해서 Description에 붙여넣기
echo 6. Publish release 클릭
echo.
echo 🌐 릴리즈 페이지: https://github.com/grasion/SecureMemo/releases
echo.

pause
