# ActionGameWithCSharp

MonoGame와 `CSharpServer.Networking`으로 만든 2인용 TCP 네트워크 액션 게임의 첫 번째
수직 슬라이스입니다. 클라이언트는 이동 입력만 보내며, 서버가 플레이어 위치를 계산해
모든 클라이언트에 같은 월드 스냅숏을 전달합니다.

## 현재 범위

- loopback TCP 서버와 최대 두 클라이언트
- 플레이어 입장, 정원 초과 거절, 정상 및 비정상 퇴장 처리
- WASD 이동 입력과 서버 권위 위치 계산
- 두 클라이언트의 플레이어 위치 동기화
- MonoGame DesktopGL 사각형 렌더링

공격, 체력, 사망, 재생성, 클라이언트 예측과 보간은 아직 포함하지 않습니다.

## 요구 사항

- .NET SDK 10.0.200 이상 호환 패치 버전
- `CSharpServer.Networking` 0.1.0 로컬 NuGet 패키지

## 로컬 NuGet 패키지 준비

로컬 패키지 폴더는 Git에 포함하지 않습니다. CSharpServer 저장소가 이 저장소와 같은
상위 폴더에 있을 때 다음 명령으로 준비할 수 있습니다.

```powershell
New-Item -ItemType Directory -Path .local-packages -Force
Copy-Item ..\CSharpServer\artifacts\packages\CSharpServer.Networking.0.1.0.nupkg .local-packages\
```

네트워크 라이브러리를 변경할 때는 기존 패키지 파일을 같은 버전으로 덮어쓰지 않습니다.
CSharpServer 패키지 버전을 먼저 올리고 새 `.nupkg`를 복사한 뒤
`Directory.Packages.props`의 `CSharpServer.Networking` 버전을 함께 갱신합니다. 이 규칙은
NuGet 전역 캐시에 남은 이전 DLL이 사용되는 문제를 방지합니다.

## 빌드와 테스트

```powershell
dotnet restore .\ActionGame.slnx
dotnet build .\ActionGame.slnx --configuration Debug
dotnet test .\tests\ActionGame.Tests\ActionGame.Tests.csproj --configuration Debug
```

## 실행

먼저 서버를 실행합니다. 서버는 외부 네트워크에 노출되지 않도록 loopback 주소에만
바인딩됩니다.

```powershell
dotnet run --project .\src\ActionGame.Server\ActionGame.Server.csproj
```

별도 터미널 두 개에서 클라이언트를 각각 실행합니다.

```powershell
dotnet run --project .\src\ActionGame.Client\ActionGame.Client.csproj
```

- WASD: 이동
- Esc: 클라이언트 종료
- 초록색: 자신
- 주황색: 다른 플레이어

서버 포트를 변경하려면 서버의 첫 번째 인자로 포트를 전달하고, 클라이언트에는 호스트와
포트를 순서대로 전달합니다.

```powershell
dotnet run --project .\src\ActionGame.Server\ActionGame.Server.csproj -- 7788
dotnet run --project .\src\ActionGame.Client\ActionGame.Client.csproj -- 127.0.0.1 7788
```

## 구조와 동시성 경계

- `ActionGame.Contracts`: 게임 패킷 종류, 데이터와 바이너리 코덱
- `ActionGame.Server`: TCP 호스트, 단일 소유권 게임 월드와 20Hz 시뮬레이션
- `ActionGame.Client`: MonoGame 렌더 루프와 지속 연결 네트워크 클라이언트
- `ActionGame.Tests`: 코덱, 게임 월드와 실제 loopback TCP 통합 테스트

네트워크 핸들러는 게임 월드나 렌더 상태를 직접 수정하지 않습니다. 서버에서는 bounded
이벤트 채널을 통해 단일 시뮬레이션 루프로 전달하고, 클라이언트에서는 MonoGame
`Update`가 수신 이벤트를 적용합니다. 연결별 송신도 하나의 bounded 큐와 단일 송신
루프로 직렬화합니다.

## 제한 사항

- 학습용 로컬 프로토타입으로 인증과 TLS가 없습니다.
- TCP 지연과 20Hz 스냅숏이 그대로 보이며 예측이나 보간을 하지 않습니다.
- 로컬 패키지는 커밋하지 않으므로 새 개발 환경과 CI에서는 패키지를 별도로 준비해야
  합니다.
