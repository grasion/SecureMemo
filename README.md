# SecureMemo

로컬 전용 암호화 메모 애플리케이션

## 주요 기능

- 🔒 **완전한 로컬 저장**: 모든 데이터는 로컬에만 저장되며 온라인 전송 없음
- 🔐 **AES-256 암호화**: 모든 메모는 강력한 암호화로 보호
- 🎤 **음성 녹음**: 메모와 함께 음성 녹음 및 재생
- 🤖 **AI 통합**: Gemini API를 통한 음성→텍스트 변환 및 요약
- 📄 **Word 내보내기**: 메모를 Word 문서로 내보내기
- 🔑 **비밀번호 보호**: 선택적 비밀번호 보호 기능
- 🔄 **자동 업데이트**: GitHub 릴리즈를 통한 자동 업데이트
- 🌙 **다크 테마**: 눈에 편안한 다크 모드 UI

## 다운로드

[최신 릴리즈 다운로드](https://github.com/grasion/SecureMemo/releases/latest)

### 설치 방법

1. **설치 파일 (권장)**
   - `SecureMemo-Setup-vX.X.X.exe` 다운로드
   - 실행하여 설치
   - 시작 메뉴 및 바탕화면 바로가기 생성

2. **포터블 버전**
   - `SecureMemo-Portable-vX.X.X.zip` 다운로드
   - 압축 해제 후 `SecureMemo.exe` 실행
   - 설치 없이 USB 등에서 실행 가능

## 시스템 요구사항

- Windows 10/11 (64-bit)
- .NET 10.0 Runtime (자동 포함)

## 사용 방법

### 기본 사용

1. 프로그램 실행
2. "새 노트" 버튼으로 메모 생성
3. 제목과 내용 입력 (자동 저장)
4. 메모 목록에서 선택하여 편집

### 비밀번호 설정

1. 설정 버튼 클릭
2. "비밀번호 사용" 체크
3. 비밀번호 입력 및 확인
4. 다음 실행 시 비밀번호 입력 필요

### 음성 녹음

1. 메모 선택
2. 🎤 녹음 버튼 클릭
3. 녹음 후 ⏹ 중지 버튼 클릭
4. ▶ 재생 버튼으로 녹음 재생

### AI 기능 (선택사항)

1. [Google AI Studio](https://makersuite.google.com/app/apikey)에서 Gemini API 키 발급
2. 설정에서 API 키 입력
3. "음성→텍스트" 버튼으로 녹음을 텍스트로 변환
4. "요약" 버튼으로 메모 요약

## 데이터 저장 위치

- Windows: `%APPDATA%\SecureMemo\`
  - 메모: `memos\`
  - 음성: `audio\`
  - 설정: `api.enc`, `pwd.hash`

## 보안

- 모든 메모는 AES-256-CBC로 암호화
- 비밀번호는 SHA-256 해시로 저장
- API 키는 암호화되어 로컬에만 저장
- 온라인 전송 없음 (업데이트 확인 제외)

## 개발자 지원

- 블로그: [https://1st-life-2nd.tistory.com/](https://1st-life-2nd.tistory.com/)
- 후원: 앱 내 "커피 사주기" 버튼

## 라이선스

이 프로젝트는 상업적 사용이 가능한 오픈소스 라이브러리만 사용합니다:
- NAudio (MIT License)
- DocumentFormat.OpenXml (MIT License)
- Newtonsoft.Json (MIT License)
- QRCoder (MIT License)

## 변경 로그

[CHANGELOG.md](CHANGELOG.md) 참조
