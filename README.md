# Token Monitor

Claude Code / Codex 사용량 한도(5시간·주간)의 사용률과 리셋까지 남은 시간을 보여주는 Windows 오버레이입니다.

## 기능
- 툴별(Claude / Codex) 5시간·주간 2줄 표시
- 미니바 색상: 60% 미만 녹색, 60~85% 주황, 85% 초과 빨강
- 출처(API / 로그 / CLI) 및 신선도(방금·n분 전·n시간 전 등) 표시
- 클릭 통과(오버레이 뒤 화면 조작 가능), 항상 위 표시
- 트레이 메뉴: 지금 새로고침 / 업데이트 주기 / 클릭 통과 / 항상 위 / Claude 표시 / Codex 표시 / Windows 시작 시 실행 / 종료
- Windows 시작 시 자동 실행

## 업데이트 주기
트레이 메뉴 "업데이트 주기"에서 3초 / 5초 / 10초 / 15초 / 30초 / 1분 / 3분(기본) / 5분 / 10분 / 15분 / 30분 중 선택할 수 있습니다. 선택 즉시 적용되며 설정 파일에 저장됩니다.
- Claude는 매 주기마다 API를 호출하므로 짧은 주기에서는 요청 제한(429)에 걸릴 수 있습니다. 이 경우 최소 60초부터 간격을 늘려 재시도합니다.
- Codex는 로컬 로그만 읽으므로 짧은 주기도 부담이 적습니다.
- CLI 폴백은 주기와 관계없이 성공 후에는 최소 10분, 실패 후에는 최소 2분 간격으로만 실행됩니다.

## 데이터 소스
- **Claude**: `~/.claude/.credentials.json`을 읽기 전용으로 읽어 Claude OAuth usage API를 설정한 업데이트 주기(기본 3분)마다 폴링합니다. 토큰을 갱신하거나 파일에 쓰지 않습니다. 실패 시 `claude` CLI를 실행해 `/usage` 화면을 스크래핑하는 방식으로 폴백합니다.
- **Codex**: `~/.codex/sessions` 아래 rollout 로그를 파싱합니다. 로그가 10분 넘게 오래되었거나 읽기에 실패하면 `codex` CLI를 실행해 `/status` 화면을 스크래핑하는 방식으로 폴백합니다(성공 후 최소 10분, 실패 후 최소 2분 간격).

## CLI 폴백 준비
- `%LOCALAPPDATA%\TokenMonitor\cli-workdir` 폴더에서 `claude`를 한 번 직접 실행해 trust 프롬프트를 승인해 두어야 합니다. 승인 전에는 폴백이 "CLI 폴백 설정 필요" 실패를 반환합니다.
- `codex` 실행 중 업데이트 안내 프롬프트가 뜨면 폴백은 조작하지 않고 즉시 중단합니다.

## 개인정보 / 보안
- `~/.claude`, `~/.codex`는 읽기 전용으로만 접근하며 수정하지 않습니다.
- 네트워크 호출은 `api.anthropic.com`으로만 나가며, 텔레메트리는 없습니다.
- 토큰을 저장하거나 로그·화면에 남기지 않습니다.
- Claude usage API와 Codex 로그 포맷은 비공식/내부 포맷이므로 CLI 업데이트에 따라 언제든 바뀔 수 있습니다.

## 설치
[Releases](https://github.com/name0825/Token-Monitor/releases)에서 `TokenMonitor.exe`를 내려받아 실행하세요. self-contained 빌드라 별도 .NET 설치가 필요 없습니다. Windows 10/11 x64 전용입니다.

## 빌드
요구사항: .NET 10 SDK

```
dotnet build
dotnet test
dotnet run --project src/TokenMonitor.App
dotnet publish src/TokenMonitor.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

## 설정 파일
오버레이 위치, 클릭 통과, 업데이트 주기 등 설정은 `%APPDATA%\TokenMonitor\settings.json`에 저장됩니다.

## 프로젝트 구조
```
TokenMonitor.sln
src/TokenMonitor.App/        WPF 오버레이, 트레이, 설정 UI
src/TokenMonitor.Core/       모델, IUsageProvider, 폴백 체이닝, 스케줄링
src/TokenMonitor.Providers/  ClaudeOAuthProvider, CodexLogProvider, CliScrapeProvider (ConPTY)
tests/TokenMonitor.Tests/    xUnit 테스트
```
