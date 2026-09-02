# ActionGameWithCSharp

MonoGame와 `CSharpServer.Networking`으로 만든 2인용 TCP 네트워크 액션 게임의 첫 번째
수직 슬라이스입니다. 클라이언트는 방향과 액션 입력만 보내며, 서버가 플레이어의 이동,
점프와 공격 상태를 계산해 모든 클라이언트에 같은 월드 스냅숏을 전달합니다.

## 현재 범위

- loopback TCP 서버와 최대 두 클라이언트
- 플레이어 입장, 정원 초과 거절, 정상 및 비정상 퇴장 처리
- 화살표 이동 입력과 서버 권위 2.5D 위치 계산
- X 공격, C 점프, 서버 판정 HP/피해/사망 및 5초 후 직접 부활 처리
- 향후 동작을 위한 Z 입력 예약
- 두 클라이언트의 플레이어 위치 동기화
- 깊이 정렬, 지면 그림자와 LPC 애니메이션, HP 바 및 화살 투사체 렌더링

각 캐릭터는 HP 100으로 시작합니다. 검 공격은 20의 피해를 주고, 활 공격은 15의 피해를
주는 화살을 발사합니다. 화살은 최대 360픽셀을 이동하며 상대에게 명중하거나 최대 거리에
도달하면 사라집니다. HP가 0이 되면 이동, 점프, 공격 입력은 무시됩니다. 사망 후 5초가
지나면 X를 새로 눌러 서버의 승인을 받은 뒤 HP 100으로 부활할 수 있습니다. 클라이언트
예측과 보간은 아직 포함하지 않습니다.

## 요구 사항

- .NET 10 SDK 10.0.200 이상
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

저장소 루트의 배치 파일을 실행하면 Debug 빌드 후 서버 1개와 클라이언트 2개가
자동으로 시작됩니다.

```powershell
.\run-game.bat
```

기본 포트는 `7777`입니다. 다른 포트를 사용하려면 첫 번째 인자로 전달합니다.

```powershell
.\run-game.bat 7788
```

배치 파일을 사용하지 않고 각각 실행하려면 다음 명령을 사용합니다.

먼저 서버를 실행합니다. 서버는 외부 네트워크에 노출되지 않도록 loopback 주소에만
바인딩됩니다.

```powershell
dotnet run --project .\src\ActionGame.Server\ActionGame.Server.csproj
```

별도 터미널 두 개에서 클라이언트를 각각 실행합니다.

```powershell
dotnet run --project .\src\ActionGame.Client\ActionGame.Client.csproj
```

- 화살표: 좌우 및 깊이 이동
- X: 생존 중 공격, 사망 중 5초 후 부활 요청
- C: 점프
- Z: 예약 입력이며 아직 동작 없음
- Esc: 클라이언트 종료
- 플레이어 1: 강철 갑옷 전사
- 플레이어 2: 숲색 가죽 갑옷 궁수
- 발밑의 초록색 표시: 자신

서버 포트를 변경하려면 서버의 첫 번째 인자로 포트를 전달하고, 클라이언트에는 호스트와
포트를 순서대로 전달합니다.

```powershell
dotnet run --project .\src\ActionGame.Server\ActionGame.Server.csproj -- 7788
dotnet run --project .\src\ActionGame.Client\ActionGame.Client.csproj -- 127.0.0.1 7788
```

이미 사용 중인 서버 포트로 다시 실행하면 서버는 Windows 오류 대화상자를 표시하지 않고
해당 포트가 사용 중이라는 메시지와 종료 코드 `2`를 반환합니다.

## 구조와 동시성 경계

- `ActionGame.Contracts`: 게임 패킷 종류, 데이터와 바이너리 코덱
- `ActionGame.Server`: TCP 호스트, 단일 소유권 게임 월드와 20Hz 시뮬레이션
- `ActionGame.Client`: MonoGame 렌더 루프와 지속 연결 네트워크 클라이언트
- `ActionGame.Tests`: 코덱, 게임 월드와 실제 loopback TCP 통합 테스트

네트워크 핸들러는 게임 월드나 렌더 상태를 직접 수정하지 않습니다. 서버에서는 bounded
이벤트 채널을 통해 단일 시뮬레이션 루프로 전달하고, 클라이언트에서는 MonoGame
`Update`가 수신 이벤트를 적용합니다. 연결별 송신도 하나의 bounded 큐와 단일 송신
루프로 직렬화합니다.

## 2.5D 좌표계

- `X`: 화면의 좌우 지면 위치
- `Y`: 바닥의 앞뒤 깊이 위치
- `Z`: 지면으로부터의 점프 높이
- 렌더 좌표: `screenX = X`, `screenY = Y - Z`

캐릭터 그림자는 항상 지면 `(X, Y)`에 남고, 캐릭터는 `Z`만큼 위에 표시됩니다. 캐릭터는
지면 깊이 `Y` 순으로 그려 앞쪽 캐릭터가 뒤쪽 캐릭터 위에 보입니다. 점프, 중력, 착지와
공격 지속시간과 피해는 모두 서버가 결정합니다. 검 공격은 바라보는 방향의 X 거리와
Y축 깊이 차이, 점프 높이 차이를 검사합니다. 화살은 서버에서 매 틱 이동하며 이전
좌표부터 새 좌표까지의 구간으로 충돌을 검사해 빠르게 움직여도 캐릭터를 통과하지
않습니다. 클라이언트는 서버 스냅숏의 플레이어와 화살 좌표를 렌더링합니다.

## 제한 사항

- 학습용 로컬 프로토타입으로 인증과 TLS가 없습니다.
- TCP 지연과 20Hz 스냅숏이 그대로 보이며 예측이나 보간을 하지 않습니다.
- 부활 대기 시간은 서버 시간을 기준으로 하며 자동 부활하지 않습니다.
- Z 키는 프로토콜 입력 비트만 예약되어 있고 게임 상태를 변경하지 않습니다.
- 로컬 패키지는 커밋하지 않으므로 새 개발 환경과 CI에서는 패키지를 별도로 준비해야
  합니다.
