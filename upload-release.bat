@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

echo ========================================
echo SecureMemo GitHub 릴리즈 업로드
echo ========================================
echo.

REM GitHub CLI 확인
echo GitHub CLI 확인 중...
where gh >nul 2>nul
if errorlevel 1 (
    echo ❌ GitHub CLI가 설치되지 않았습니다.
    echo.
    echo 설치: winget install --id GitHub.cli
    echo.
    pause
    exit /b 1
)
echo ✅ GitHub CLI 설치 확인
echo.

REM GitHub 인증
echo GitHub 인증 확인 중...
gh auth status >nul 2>nul
if errorlevel 1 (
    echo GitHub 로그인이 필요합니다.
    pause
    gh auth login
    if errorlevel 1 (
        echo ❌ 로그인 실패
        pause
        exit /b 1
    )
)
echo ✅ 인증 완료
echo.

REM 저장소 확인
echo 저장소 확인 중...
gh repo view grasion/SecureMemo >nul 2>nul
if errorlevel 1 (
    echo ❌ 저장소를 찾을 수 없습니다.
    echo    https://github.com/grasion/SecureMemo
    pause
    exit /b 1
)
echo ✅ 저장소 확인 완료
echo.

REM README.md와 LICENSE.txt 확인 및 업로드
echo ========================================
echo 필수 파일 확인
echo ========================================
echo.

REM Git 초기화
if not exist ".git" (
    echo Git 초기화 중...
    git init
    git branch -M main
    git remote add origin https://github.com/grasion/SecureMemo.git
    echo ✅ Git 초기화 완료
    echo.
)

REM 원격 저장소 설정 확인
git remote get-url origin >nul 2>nul
if errorlevel 1 (
    git remote add origin https://github.com/grasion/SecureMemo.git
)

REM README.md 확인
echo README.md 확인 중...
gh api repos/grasion/SecureMemo/contents/README.md >nul 2>nul
if errorlevel 1 (
    echo ⚠️  README.md가 저장소에 없습니다. 업로드 중...
    if exist "README.md" (
        git add README.md
        git commit -m "Add README.md"
        git push origin main
        echo ✅ README.md 업로드 완료
    ) else (
        echo ❌ README.md 파일이 없습니다.
    )
) else (
    echo ✅ README.md 존재
)
echo.

REM LICENSE.txt 확인
echo LICENSE.txt 확인 중...
gh api repos/grasion/SecureMemo/contents/LICENSE.txt >nul 2>nul
if errorlevel 1 (
    echo ⚠️  LICENSE.txt가 저장소에 없습니다. 업로드 중...
    if exist "LICENSE.txt" (
        git add LICENSE.txt
        git commit -m "Add LICENSE.txt"
        git push origin main
        echo ✅ LICENSE.txt 업로드 완료
    ) else (
        echo ❌ LICENSE.txt 파일이 없습니다.
    )
) else (
    echo ✅ LICENSE.txt 존재
)
echo.

REM 릴리즈 패키지 폴더 확인
if not exist "release-package" (
    echo ❌ release-package 폴더가 없습니다.
    echo    먼저 build-release.bat을 실행하세요.
    pause
    exit /b 1
)

echo ========================================
echo 릴리즈 버전 선택
echo ========================================
echo.

REM 사용 가능한 버전 목록
echo 사용 가능한 버전:
echo.
set count=0
for /d %%d in (release-package\v*) do (
    set /a count+=1
    set "version[!count!]=%%~nxd"
    echo !count!. %%~nxd
)

if %count%==0 (
    echo ❌ 릴리즈 버전이 없습니다.
    echo    먼저 build-release.bat을 실행하세요.
    pause
    exit /b 1
)

echo.
set /p choice="업로드할 버전 번호 선택 (1-%count%): "

REM 선택 검증
if not defined version[%choice%] (
    echo ❌ 잘못된 선택입니다.
    pause
    exit /b 1
)

set SELECTED_VERSION=!version[%choice%]!
set VERSION_DIR=release-package\%SELECTED_VERSION%

echo.
echo 선택된 버전: %SELECTED_VERSION%
echo.

REM 파일 확인
echo 업로드할 파일:
echo.
set FILE_COUNT=0
if exist "%VERSION_DIR%\SecureMemo-Setup-%SELECTED_VERSION%.exe" (
    set /a FILE_COUNT+=1
    echo ✅ SecureMemo-Setup-%SELECTED_VERSION%.exe
    set SETUP_FILE=%VERSION_DIR%\SecureMemo-Setup-%SELECTED_VERSION%.exe
)
if exist "%VERSION_DIR%\SecureMemo-Portable-%SELECTED_VERSION%.zip" (
    set /a FILE_COUNT+=1
    echo ✅ SecureMemo-Portable-%SELECTED_VERSION%.zip
    set PORTABLE_FILE=%VERSION_DIR%\SecureMemo-Portable-%SELECTED_VERSION%.zip
)
if exist "%VERSION_DIR%\RELEASE_NOTES.txt" (
    echo ✅ RELEASE_NOTES.txt
    set NOTES_FILE=%VERSION_DIR%\RELEASE_NOTES.txt
)

if %FILE_COUNT%==0 (
    echo ❌ 업로드할 파일이 없습니다.
    pause
    exit /b 1
)

echo.
set /p confirm="업로드하시겠습니까? (Y/N): "
if /i not "%confirm%"=="Y" (
    echo 취소되었습니다.
    pause
    exit /b 0
)

echo.
echo ========================================
echo 릴리즈 업로드
echo ========================================
echo.

REM 릴리즈 노트 읽기
set RELEASE_NOTES=버그 수정 및 개선
if exist "%NOTES_FILE%" (
    set /p RELEASE_NOTES=<"%NOTES_FILE%"
)

REM 기존 릴리즈 확인
echo 기존 릴리즈 확인 중...
gh release view %SELECTED_VERSION% --repo grasion/SecureMemo >nul 2>nul
if not errorlevel 1 (
    echo ⚠️  릴리즈 %SELECTED_VERSION%가 이미 존재합니다.
    echo.
    set /p delete_confirm="기존 릴리즈를 삭제하고 다시 업로드하시겠습니까? (Y/N): "
    if /i "!delete_confirm!"=="Y" (
        echo 기존 릴리즈 삭제 중...
        gh release delete %SELECTED_VERSION% --repo grasion/SecureMemo --yes
        timeout /t 2 /nobreak >nul
        echo ✅ 삭제 완료
    ) else (
        echo 취소되었습니다.
        pause
        exit /b 0
    )
)
echo.

REM 릴리즈 생성
echo 릴리즈 생성 중...
echo 저장소: grasion/SecureMemo
echo 버전: %SELECTED_VERSION%
echo.

REM 업로드할 파일 목록 생성
set UPLOAD_FILES=
if defined SETUP_FILE set UPLOAD_FILES=%UPLOAD_FILES% "%SETUP_FILE%"
if defined PORTABLE_FILE set UPLOAD_FILES=%UPLOAD_FILES% "%PORTABLE_FILE%"

REM 릴리즈 노트 파일에서 내용 읽기
if exist "%NOTES_FILE%" (
    set "NOTES_CONTENT="
    for /f "usebackq delims=" %%a in ("%NOTES_FILE%") do (
        if defined NOTES_CONTENT (
            set "NOTES_CONTENT=!NOTES_CONTENT!%%0A%%a"
        ) else (
            set "NOTES_CONTENT=%%a"
        )
    )
) else (
    set "NOTES_CONTENT=SecureMemo %SELECTED_VERSION%%%0A%%0A변경 사항:%%0A- 버그 수정 및 개선"
)

REM 릴리즈 생성 및 파일 업로드
gh release create %SELECTED_VERSION% ^
    --repo grasion/SecureMemo ^
    --title "SecureMemo %SELECTED_VERSION%" ^
    --notes "!NOTES_CONTENT!" ^
    %UPLOAD_FILES%

if errorlevel 1 (
    echo.
    echo ❌ 릴리즈 업로드 실패!
    echo.
    echo 수동 업로드: https://github.com/grasion/SecureMemo/releases/new
    pause
    exit /b 1
)

echo.
echo ========================================
echo ✅ 업로드 완료!
echo ========================================
echo.
echo 버전: %SELECTED_VERSION%
echo 🌐 릴리즈: https://github.com/grasion/SecureMemo/releases/tag/%SELECTED_VERSION%
echo.
echo 📦 업로드된 파일:
if defined SETUP_FILE echo    - SecureMemo-Setup-%SELECTED_VERSION%.exe
if defined PORTABLE_FILE echo    - SecureMemo-Portable-%SELECTED_VERSION%.zip
echo.
echo 🎉 사용자들이 이제 다운로드 및 자동 업데이트를 받을 수 있습니다!
echo.

pause
