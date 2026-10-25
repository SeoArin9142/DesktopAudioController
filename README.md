# DesktopAudioController

**한국어** | [English](README.en.md)

> 🌐 **English speakers:** please read the [English README](README.en.md). The app UI also supports English (switch with the `English` button on the main window).

DesktopAudioController는 Windows에서 여러 출력 장치를 자주 바꾸거나, 프로그램별 볼륨과 출력 장치를 빠르게 제어해야 할 때 쓰는 데스크톱 유틸리티입니다.

기본 Windows 볼륨 믹서보다 좁은 목적에 맞춰 만들어졌습니다. 사용자가 고른 출력 장치만 메인 화면에 표시하고, 각 장치에서 실제로 소리를 내는 프로그램 세션을 장치 카드 안에서 바로 조절합니다.

- 최신 버전: `v0.20.0`
- 배포 형태: `win-x64` portable zip
- 실행 환경: Windows 10/11 x64
- 릴리즈 페이지: <https://github.com/SeoArin9142/DesktopAudioController/releases>

## 주요 기능

```text
- 표시할 출력 장치 직접 선택
- 장치별 마스터 볼륨 / 음소거 / 기본 출력 장치 전환
- 프로그램별 볼륨 / 음소거 / 사용자 지정 이름 저장
- 프로그램별 출력 장치 변경
- 프로필 저장 / 적용 / 삭제
  (기본 출력·입력(마이크) 장치, 대체 장치 순서, 앱별 출력 장치, 장치 볼륨·음소거 포함)
- 메인 화면 프로필 버튼, 트레이 메뉴, 전역 단축키로 한 번에 전환
- 명령으로 프로필 적용 (스트림덱 "열기" 동작 등)
- 다른 PC로 옮긴 프로필의 장치를 이름으로 다시 찾기
- 마스킹된 진단 패키지 내보내기 및 이슈 작성 도우미
- 한국어 / 영어 화면 (메인 화면 버튼으로 다시 켜지 않고 바로 전환)
- 트레이 상주, 현재 프로필 표시, 빠른 프로필 적용, 로그 폴더 열기
- 앱 내 자동 업데이트와 sha256 검증
- 필요할 때만 켜는 디버그 로그
```

## 빠른 시작

1. [릴리즈 페이지](https://github.com/SeoArin9142/DesktopAudioController/releases)에서 `DesktopAudioController-v0.20.0-win-x64.zip`을 다운로드합니다.
2. 원하는 위치에 압축을 풉니다.

```text
D:\DesktopAudioController\
  DesktopAudioController.exe
  DesktopAudioController.Updater.exe
```

3. `DesktopAudioController.exe`를 실행합니다.
4. 처음 실행하면 설정창이 열립니다.
5. 메인 화면에 표시할 출력 장치를 선택하고 저장합니다.
6. 필요하면 `Windows 시작 시 자동 실행`, `트레이로 최소화`, `연결된 장치만 표시`를 켭니다.

설정과 로그는 실행 폴더가 아니라 `%LocalAppData%\DesktopAudioController`에 저장됩니다. 따라서 앱 폴더를 새 버전으로 덮어써도 기존 설정은 유지됩니다.

릴리즈에는 zip과 함께 `.sha256` 파일이 올라갑니다. 수동 다운로드 후 무결성을 확인하려면 같은 폴더에서 다음처럼 확인할 수 있습니다.

```bash
sha256sum -c DesktopAudioController-v0.20.0-win-x64.zip.sha256
```

## 화면 구성

### 메인 창

메인 창에는 설정에서 선택한 출력 장치만 표시됩니다. 각 장치는 하나의 카드로 보이며, 카드 안에서 장치 볼륨과 프로그램 세션을 제어합니다. 지금 Windows 기본 출력 장치 카드가 항상 맨 위에 옵니다.

장치 카드 위의 `프로필` 줄에는 저장한 프로필이 번호와 함께 버튼으로 나옵니다(이름순). 버튼을 누르면 바로 그 프로필로 바뀌고, 지금 적용 중인 프로필은 파란색으로 표시됩니다. 이 번호는 트레이 메뉴와 `--profile-index` 명령의 번호와 같습니다.

장치 카드에서 할 수 있는 일:

```text
- 마스터 볼륨 변경
- 장치 음소거 전환
- 기본 출력 장치로 설정
- 프로그램별 볼륨 패널 열기 / 닫기
```

### 화면 언어

화면은 한국어와 영어를 지원합니다. 처음에는 Windows 표시 언어를 따르고(한국어 Windows면 한국어), 메인 화면 위쪽 `English` / `한국어` 버튼을 누르면 앱을 다시 켜지 않고 바로 바뀝니다. 설정창 맨 위 `언어 / Language`에서 자동 / 한국어 / English 를 고를 수도 있습니다. 트레이 메뉴와 알림, 안내 창도 같은 언어로 나옵니다.

장치 이름(예: `스피커(2- Sound BlasterX G5)`)은 Windows가 정한 이름이라 앱 언어와 상관없이 그대로 나옵니다. 바꾸려면 Windows 설정 > 시스템 > 소리에서 장치 이름을 바꾸면 됩니다.

### 프로그램별 볼륨 패널

`프로그램별 볼륨 열기`를 누르면 해당 출력 장치에서 감지된 프로그램 세션이 표시됩니다. 패널이 열린 동안에만 세션 목록이 주기적으로 갱신됩니다.

세션 카드에서 할 수 있는 일:

```text
- 프로그램 볼륨 변경
- 프로그램 음소거 전환
- 표시 이름 변경
- 출력 장치 변경
```

출력 변경 메뉴에서는 현재 장치와 기본 장치를 구분해 표시합니다. 출력 장치를 바꾼 뒤 바로 이동하지 않으면 해당 프로그램의 재생을 다시 시작하거나 앱을 다시 실행해 주세요.

Store 앱이 패키지 리소스 아이콘을 제공하는 경우에는 설치된 패키지의 PNG 아이콘을 우선 사용합니다. 패키지 아이콘을 찾지 못하면 기존처럼 실행 파일 아이콘으로 폴백합니다.

장치 전체 재열거는 무거운 작업이므로, 프로그램 패널을 열어둔 상태에서는 세션 목록만 가볍게 갱신합니다.

### 설정창

설정창에서는 메인 화면에 표시할 장치와 앱 동작 방식을 정합니다.

```text
- 표시할 출력 장치
- 시작 시 최소화
- Windows 시작 시 자동 실행
- 트레이로 최소화
- 연결된 장치만 표시
- 재생 중인 앱만 보기
- 시스템 사운드 세션 표시
- 프로필 만들기 / 적용 / 삭제
- 프로필별 기본 출력·입력 장치 / 대체 장치 / 앱별 출력 장치 / 장치 볼륨·음소거 / 단축키
- 프로필 단축키 사용, 단축키·명령 전환 알림
- 프리릴리즈 업데이트 포함
- 디버그 로그 기록
- 설정 내보내기 / 가져오기 / 초기화
- 진단 패키지 내보내기 / 이슈 작성 도우미
```

### 트레이 메뉴

트레이 아이콘에서 메인 창을 다시 열거나, 설정창을 열고, 장치 상태를 빠르게 바꿀 수 있습니다.

```text
- 창 열기
- 설정 열기
- 새로고침
- 현재 기본 출력 장치 볼륨을 10% 단위로 바로 선택 (0~100%)
- 현재 프로필 표시 (아이콘에 마우스를 올리면 툴팁에도 표시)
- 프로필 목록 (번호·단축키와 함께 바로 눌러 전환)
- 로그 폴더 열기
- 기본 장치 빠른 전환
- 장치 음소거 토글
- 종료
```

## 사용 안내

### 표시할 출력 장치 고르기

1. 메인 창 오른쪽 위의 설정 버튼을 누릅니다.
2. 사용할 출력 장치를 선택합니다.
3. 자주 쓰지 않는 장치는 선택 해제합니다.
4. 저장하면 메인 창에 선택한 장치만 표시됩니다.

`연결된 장치만 표시`를 켜면 현재 연결된 장치만 메인 화면에 남습니다. USB 오디오 장치나 가상 오디오 장치를 자주 바꾸는 환경에서 유용합니다.

### 장치 볼륨 조절

장치 카드의 슬라이더는 Windows 장치 마스터 볼륨과 연결됩니다. 슬라이더를 움직이면 짧은 지연 뒤 실제 장치 볼륨에 반영됩니다.

음소거 버튼은 즉시 반영됩니다. 기본 장치 버튼을 누르면 해당 장치가 Windows 기본 출력 장치가 됩니다.

### 프로그램별 볼륨 조절

1. 조절할 출력 장치 카드에서 `프로그램별 볼륨 열기`를 누릅니다.
2. 목록에서 프로그램을 찾습니다.
3. 슬라이더로 볼륨을 바꾸거나 음소거 버튼을 누릅니다.

프로그램별 볼륨과 음소거 상태는 저장됩니다. 같은 프로그램 세션이 다시 나타나면 저장된 값이 자동으로 적용됩니다.

### 프로그램 이름 바꾸기

프로그램 세션 카드에서 `이름 변경`을 누르면 화면에 표시될 이름을 직접 정할 수 있습니다.

예:

```text
msedge.exe -> Edge
Strinova-Win64-Shipping.exe -> Strinova
```

사용자 지정 이름은 설정 파일에 저장됩니다. 나중에 같은 프로그램이 다시 나타나면 바꾼 이름으로 표시됩니다.

### 수동 프로필

설정창의 `수동 프로필`에서 현재 장치 표시 설정과 프로그램별 볼륨, 음소거, 사용자 지정 이름 저장값을 하나의 프로필로 저장할 수 있습니다.

예:

```text
- 게임
- 작업
- 방송
```

프로필을 고르면 설정창 아래쪽에 `프로필을 적용할 때` 편집 영역이 열립니다. 여기서 프로필마다 다음을 더 지정할 수 있습니다.

```text
- 기본 출력 장치: Windows 의 '기본 장치'와 '기본 통신 장치'를 함께 바꿉니다. "(바꾸지 않음)"이면 그대로 둡니다.
  - 없으면 대신 1 / 2: 헤드셋을 꺼 뒀거나 동글이 빠져 있을 때 차례로 쓸 장치입니다. 대신 썼으면 알림이 뜹니다.
- 기본 입력(마이크): 기본 장치·기본 통신 장치를 함께 바꿔 디스코드처럼 통신 장치를 쓰는 앱도 따라옵니다.
  - 없으면 대신: 마이크가 없을 때 쓸 장치입니다.
- 앱별 출력 장치: 실행 파일 이름(예: TslGame.exe, chrome.exe, Discord.exe)별로 보낼 장치를 정합니다.
  "Windows 기본 장치 따름"을 고르면 앱별 지정을 지웁니다. 대상 장치가 없으면 출력 대체 순서를 씁니다.
- 장치 볼륨·음소거: 예) 이어폰 20%로 시작(귀 보호), 이어폰 프로필에서 스피커 음소거.
  볼륨 칸을 비우면 볼륨은 그대로, "음소거 그대로"면 음소거 상태도 그대로 둡니다.
- 단축키: 예) Ctrl+Alt+1. 어디서든 누르면 이 프로필이 적용됩니다.
```

편집 영역에서 바꾼 값은 설정창의 `저장`을 눌러야 프로필에 저장됩니다.

프로필은 다음 방법으로 적용합니다.

```text
- 메인 화면의 프로필 버튼
- 트레이 메뉴의 프로필 목록
- 설정창의 `프로필 적용`
- 프로필 단축키 (설정창에서 `프로필 단축키 사용`을 켠 경우)
- 명령 (아래 "명령으로 프로필 적용" 참고)
```

프로필을 적용하면 프로그램별 볼륨 / 음소거 저장값도 프로필에 저장된 값으로 바뀝니다. 앱별 출력은 그 순간 실행 중인 프로그램에만 적용됩니다. 게임처럼 시작할 때 출력 장치를 정하는 프로그램은, 게임을 켜기 전에 프로필을 바꿔 두는 쪽이 가장 확실합니다.

예시 구성 (스피커 MR3, 이어폰 마기반트(USB DAC), 무선 헤드셋 G Pro X·G733을 모두 꽂아 둔 경우):

```text
1. 스피커      기본 출력 = MR3, 마이크는 바꾸지 않음
2. 이어폰      기본 출력 = 마기반트(없으면 MR3), 장치 볼륨: 마기반트 20%, MR3 음소거
3. 헤드셋      기본 출력 = G Pro X(없으면 마기반트 → MR3), 기본 입력 = G Pro X 마이크
3-1. 헤드셋 G733  기본 출력 = G733(없으면 마기반트 → MR3), 마이크는 바꾸지 않음
```

앱별 지정이 없는 프로그램은 기본 출력 장치를 따르므로, "전부 이 장치로"는 기본 출력 장치만 정하면 됩니다. 예전에 특정 프로그램을 다른 장치로 보내 둔 적이 있다면 그 프로그램을 앱별 출력에 넣고 "Windows 기본 장치 따름"으로 두면 지정이 풀립니다.

### 프로필 단축키

설정창에서 `프로필 단축키 사용`을 켜고, 프로필 편집 영역의 `단축키` 칸에 `Ctrl+Alt+1` 처럼 적습니다.

```text
- 보조 키(Ctrl, Alt, Shift, Win)를 하나 이상 함께 써야 합니다. F13~F24는 보조 키 없이도 됩니다.
- 쓸 수 있는 키: A~Z, 0~9, F1~F24, Num0~Num9, 방향키, Space, Insert, Delete, Home, End, PageUp, PageDown, Pause
- 두 프로필에 같은 단축키를 지정하면 저장할 때 알려 줍니다.
- 게임 오버레이, 녹화 프로그램 등 다른 프로그램이 이미 쓰는 조합이면 등록하지 못했다고 안내합니다.
```

### 명령으로 프로필 적용 (스트림덱 등)

실행 파일에 인수를 붙여 실행하면 프로필을 바로 적용합니다. 앱이 이미 실행 중이면 새 창을 띄우지 않고 실행 중인 앱에 명령만 넘긴 뒤 바로 끝나고, 실행 중이 아니면 트레이로 조용히 시작한 뒤 적용합니다.

```text
DesktopAudioController.exe --profile "이어폰"        프로필 이름으로 적용 (대소문자·띄어쓰기 무시, 이름 일부만 써도 하나로 정해지면 적용)
DesktopAudioController.exe --profile-index 2         메인 화면·트레이에 보이는 번호로 적용
DesktopAudioController.exe --toggle-mute "MR3"       이름이 맞는 출력 장치의 음소거를 켜고 끔
```

엘가토 스트림덱의 `시스템 > 열기(Open)` 동작에 넣는 예:

```text
"D:\DesktopAudioController\DesktopAudioController.exe" --profile "헤드셋"
```

- 단축키는 키 입력이라 관리자 권한으로 실행된 게임(원신 등)이 앞에 있으면 스트림덱의 키 입력이 막힐 수 있습니다. 명령은 키 입력이 아니라 프로그램 실행과 이름 있는 파이프로 전달되므로 이 제한을 받지 않습니다.
- 적용 결과는 트레이 알림(`단축키·명령으로 바뀔 때 알림 표시`로 끌 수 있음)과 로그에 남습니다. 없는 프로필 이름, 여러 개에 걸리는 이름, 대체 장치 사용은 설정과 상관없이 알림으로 알려 줍니다.

### 다른 PC로 프로필 옮기기

같은 이름의 장치라도 PC마다 장치 ID가 다릅니다. 프로필을 저장할 때 장치 이름도 함께 기록해 두므로, `설정 내보내기`로 만든 파일을 다른 PC에서 `설정 가져오기`하면 ID가 맞지 않는 장치는 이름으로 다시 찾아 적용합니다. 가져온 뒤 설정창에서 프로필을 한 번 열어 `저장`하면 새 PC의 장치 ID로 바뀌어 저장됩니다.

### 프로그램 출력 장치 변경

프로그램 세션 카드의 `출력 변경` 버튼으로 해당 프로그램의 출력 장치를 바꿀 수 있습니다.

사용 순서:

```text
1. 프로그램이 소리를 내는 상태에서 프로그램별 볼륨 패널을 엽니다.
2. 대상 프로그램 카드의 `출력 변경`을 누릅니다.
3. 이동할 출력 장치를 선택합니다.
4. 앱이 "변경 요청을 보냈습니다"라고 표시하면 Windows 정책에 요청이 전달된 상태입니다.
```

이 기능은 Windows의 앱별 출력 장치 정책을 사용합니다. 일반 데스크톱 앱은 즉시 이동되는 경우가 많지만, 일부 앱은 재생을 다시 시작하거나 프로그램을 다시 실행해야 새 출력 장치를 사용할 수 있습니다.

프로필의 앱별 출력도 같은 정책을 씁니다. 이미 소리를 내고 있는 프로그램이 그대로라면 그 프로그램을 다시 시작해 주세요.

### 자동 업데이트

앱 시작 시 새 버전이 있으면 상단에 업데이트 안내가 표시됩니다. `업데이트` 버튼을 누르면 다음 순서로 진행됩니다.

```text
1. 최신 zip 다운로드
2. sha256 체크섬 다운로드
3. 업데이트 파일 무결성 확인
4. DesktopAudioController.Updater.exe 실행
5. 본 앱 종료 대기
6. 기존 실행 폴더 백업
7. 기존 실행 폴더에 새 파일 덮어쓰기
8. 실패 시 백업본으로 복구 시도
9. 새 버전 앱 재실행 시도
```

`v0.13.10` 이전 버전에는 updater가 없으므로 한 번은 수동으로 zip을 덮어써야 합니다. `v0.13.10` 이후 버전부터는 앱 안의 업데이트 버튼을 사용할 수 있습니다.

업데이트 적용 과정은 updater 전용 로그에도 기록됩니다. 파일 교체 중 실패하면 가능한 범위에서 기존 파일을 백업본으로 되돌린 뒤 앱을 다시 실행합니다.

기본 업데이트 채널은 stable only입니다. 설정에서 `프리릴리즈 업데이트 포함`을 켠 경우에만 GitHub prerelease도 업데이트 후보로 안내합니다.

### 수동 업데이트

자동 업데이트가 실패했거나 직접 교체하고 싶으면 다음 순서로 진행합니다.

```text
1. 최신 zip 다운로드
2. 임시 폴더에 압축 해제
3. DesktopAudioController 종료
4. 작업 관리자에 DesktopAudioController.exe가 남아 있지 않은지 확인
5. 압축 해제한 DesktopAudioController 폴더 내용을 기존 실행 폴더에 덮어쓰기
6. DesktopAudioController.exe 실행
```

실행 중인 `DesktopAudioController.exe`를 직접 덮어쓰지 마십시오. 앱을 종료한 뒤 교체해야 합니다.

### SmartScreen 안내

현재 배포 zip은 코드 서명되지 않은 portable 배포본입니다. Windows 설정이나 PC 정책에 따라 SmartScreen 또는 "알 수 없는 게시자" 경고가 표시될 수 있습니다.

브라우저로 직접 받은 zip은 인터넷에서 받은 파일로 표시될 가능성이 높습니다. 앱 내부 자동 업데이트는 환경에 따라 경고가 보이지 않을 수 있지만, 이를 보장하지는 않습니다.

### 디버그 로그

평상시에는 `디버그 로그 기록`을 꺼두는 것을 권장합니다. 문제가 있을 때만 켜면 로그에 더 자세한 장치와 세션 처리 흐름이 기록됩니다.

디버그 로그를 `OFF`에서 `ON`으로 바꾸고 저장하면 앱이 자동으로 재시작됩니다. 시작 단계부터 로그를 남기기 위한 동작입니다.

### 설정 백업과 복구

설정창의 `설정 관리` 영역에서 설정을 JSON 파일로 내보내거나 가져올 수 있습니다.

```text
- 설정 내보내기: 현재 설정창의 장치 선택과 옵션을 파일로 저장
- 설정 가져오기: 내보낸 JSON 파일을 현재 설정으로 즉시 적용
- 설정 초기화: 모든 설정을 기본값으로 되돌림
- 프로그램 저장값 초기화: 프로그램별 볼륨, 음소거, 이름 변경 저장값만 삭제
```

가져오기와 초기화 작업은 즉시 저장되고 메인 화면을 다시 불러옵니다.

## 저장 파일과 로그

설정 파일:

```text
%LocalAppData%\DesktopAudioController\settings.json
%LocalAppData%\DesktopAudioController\settings.json.bak
```

로그 파일:

```text
%LocalAppData%\DesktopAudioController\logs\DesktopAudioController-YYYYMMDD.log
%LocalAppData%\DesktopAudioController\logs\DesktopAudioController-Updater-YYYYMMDDHHMMSS.log
```

로그 보관 방식:

```text
- 날짜별 로그 파일 1개 생성
- 7일이 지난 로그 삭제
- 최근 30개까지만 유지
- 전체 로그 용량 100MB 상한 유지
```

로그에는 문제 파악에 필요한 정보가 기록됩니다. 공개 이슈에 로그를 올리기 전에는 내용을 한 번 읽어보는 것을 권장합니다.

### 진단 패키지

설정창의 `진단 패키지 내보내기`를 사용하면 공개 이슈에 첨부하기 쉬운 zip을 만들 수 있습니다. `진단 패키지 및 이슈 작성`을 사용하면 zip을 만든 뒤 GitHub 이슈 작성 화면을 열고, 짧은 redacted 진단 요약을 제목/본문에 미리 채우며, 같은 본문을 클립보드에도 복사합니다.

포함되는 정보:

```text
- 최근 로그
- settings.json
- audio-device-startup-snapshot.json
- run-state.json
- 버전 / 런타임 / 캐시 파일 정보
```

진단 패키지와 이슈 요약은 경로, 장치 ID, 세션 ID, 프로필 ID, MatchKey 같은 식별자를 마스킹합니다. 앱이 이슈를 자동 제출하거나 zip을 자동 업로드하지는 않습니다. 앱 표시명, 실행 파일명, 장치 표시명, 사용자 지정 별칭 같은 값은 남을 수 있으므로 공개 이슈에 올리기 전에는 zip 내용과 이슈 본문을 한 번 확인하는 것을 권장합니다.

## 문제 해결

### 장치가 보이지 않을 때

```text
1. 설정창을 열어 표시할 장치가 선택되어 있는지 확인합니다.
2. `연결된 장치만 표시`가 켜져 있으면 장치 연결 상태를 확인합니다.
3. Windows 사운드 설정에서 장치가 사용 가능 상태인지 확인합니다.
4. 앱을 종료한 뒤 다시 실행합니다.
```

### 프로그램이 목록에 없을 때

```text
1. 해당 프로그램에서 실제로 소리를 재생합니다.
2. 프로그램별 볼륨 패널을 닫았다가 다시 엽니다.
3. `재생 중인 앱만 보기`가 켜져 있으면 무음 상태 세션은 숨겨질 수 있습니다.
4. 시스템 사운드 세션은 설정에 따라 표시되지 않을 수 있습니다.
```

### 출력 변경 후 소리가 그대로일 때

```text
1. 대상 프로그램에서 재생을 멈췄다가 다시 시작합니다.
2. 그래도 그대로면 프로그램을 다시 실행합니다.
3. 일부 앱은 Windows 정책 변경을 즉시 반영하지 않습니다.
4. 게임이나 브라우저처럼 멀티프로세스 앱은 여러 세션이 생길 수 있습니다.
```

### 업데이트가 실패할 때

```text
1. 인터넷 연결을 확인합니다.
2. 실행 폴더에 쓰기 권한이 있는지 확인합니다.
3. 바이러스 백신이나 회사/학교 정책이 파일 교체를 막는지 확인합니다.
4. 자동 업데이트가 계속 실패하면 수동 업데이트 순서를 사용합니다.
```

### 설정을 초기화하고 싶을 때

설정창에서 `설정 초기화`를 누르면 기본 설정으로 되돌릴 수 있습니다. 프로그램별 볼륨, 음소거, 이름 변경 저장값만 지우려면 `프로그램 저장값 초기화`를 사용합니다.

## 알려진 제한

```text
- macOS / Linux는 지원하지 않습니다.
- Windows ARM64 네이티브 배포본은 제공하지 않습니다.
- 일부 UWP / Store 앱은 고유 타일 아이콘 대신 실행 파일 아이콘으로 표시될 수 있습니다.
- 프로그램 출력 변경은 Windows 내부 정책 API에 의존합니다.
- 일부 앱은 출력 변경 후 재생 재시작이나 앱 재실행이 필요할 수 있습니다.
- 프로필의 앱별 출력은 실행 파일 이름으로 구분합니다. 같은 이름의 다른 프로그램도 함께 바뀝니다.
- 관리자 권한으로 실행된 프로그램은 앱별 출력 변경이 거부될 수 있습니다.
- 코드 서명이 없어서 SmartScreen 경고가 나타날 수 있습니다.
- Windows 실제 장치/세션 동작은 오디오 드라이버와 앱 구현에 따라 달라질 수 있습니다.
```

## 개발자 정보

이 아래 내용은 직접 빌드하거나 소스 구조를 파악해야 하는 개발자를 위한 정보입니다. 일반 사용자는 위의 사용 안내만 보면 됩니다.

### 기술 스택

```text
- C#
- .NET 8
- WPF
- Windows Forms NotifyIcon
- Windows Core Audio API + NAudio
- Windows Media internal audio policy interop
```

### 개발 / 빌드 환경

아래 사양은 개발자가 실제 개발과 배포 작업에 사용한 기준 시스템입니다. 최소 사양이나 필수 사양은 아닙니다.

#### 환경 A - 데스크톱

| 항목 | 사양 |
| --- | --- |
| OS | Windows 11 Pro 64-bit, Build 26200 |
| 개발 도구 | Visual Studio 2026 Community |
| 대상 런타임 | .NET 8 / WPF / Windows Forms |
| CPU | AMD Ryzen 7 7800X3D, 8코어 16스레드 |
| 메모리 | DDR5 96GB |
| GPU | NVIDIA GeForce RTX 4080 SUPER, VRAM 16GB |
| DirectX | DirectX 12 |
| 주 모니터 | 2560 x 1440, 180Hz |
| 보조 모니터 | 1920 x 1080, 240Hz |

#### 환경 B - 노트북

| 항목 | 사양 |
| --- | --- |
| 모델 | ASUS TUF Gaming A18 FA808UP |
| OS | Windows 11 Pro 64-bit, Build 26200 |
| 개발 도구 | Visual Studio 2026 Community |
| 대상 런타임 | .NET 8 / WPF / Windows Forms |
| CPU | AMD Ryzen 7 260 w/ Radeon 780M Graphics, 8코어 16스레드 |
| 메모리 | 32GB RAM |
| dGPU | NVIDIA GeForce RTX 5070 Laptop GPU, VRAM 8GB |
| iGPU | AMD Radeon 780M Graphics |
| DirectX | DirectX 12 |
| 내장 디스플레이 | 1920 x 1200, 144Hz |
| 외부 디스플레이 | 1920 x 1080, 60Hz |

### 최소 사양 / 권장 사양

```text
최소 사양
- OS: Windows 10 x64
- CPU: 2코어 이상
- 메모리: 4GB RAM
- 저장공간: 200MB 이상 여유 공간
- 기타: 사용 가능한 출력 장치 1개 이상

권장 사양
- OS: Windows 11 x64 또는 Windows 10 22H2 이상
- CPU: 4코어 이상
- 메모리: 8GB RAM 이상
- 저장공간: SSD 기준 500MB 이상 여유 공간
- 기타: 다중 출력 장치 또는 USB 오디오 장치 사용 환경
```

### 프로젝트 구조

```text
DesktopAudioController/
  README.md
  LICENSE
  docs/
    design.md
    roadmap.md
    regression-checklist.md
    release-notes-template.md
    v1-release-criteria.md
    images/
      main-window-example.png
  scripts/
    publish-win-x64.sh
  src/
    DesktopAudioController/
      App.xaml
      App.xaml.cs
      Infrastructure/
      Models/
      Services/
      ViewModels/
      Views/
    DesktopAudioController.Updater/
      Program.cs
```

### 주요 소스 역할

```text
App.xaml.cs
- 앱 시작, 일반 실행과 워커 실행 분기, 종료 처리

MainViewModel.cs
- 메인 화면 데이터 로드, 장치/세션 갱신, 설정 복원

NativeAudioDeviceCatalogService.cs
- Core Audio 기반 출력 장치 열거, 장치 볼륨, 기본 장치 변경

NativeAudioSessionService.cs
- 장치별 프로그램 세션 열거, 세션 볼륨/음소거, 출력 변경 요청

WorkerBackedAudioDeviceCatalogService.cs
- 장치 열거를 별도 워커 프로세스로 격리

WorkerBackedAudioSessionService.cs
- 앱별 출력 변경을 별도 워커 프로세스로 격리

ApplicationAudioOutputPolicy.cs
- Windows 앱별 출력 정책 내부 API 호출

AudioPolicyEndpointId.cs
- 정책 API가 요구하는 packed render endpoint ID 생성

AutomaticUpdateService.cs
- zip 다운로드, sha256 확인, updater 실행

TrayMenuService.cs
- 트레이 아이콘, 빠른 메뉴 구성, 메뉴 갱신 시그니처, 트레이 알림

MainWindow.AutoProfile.cs
- 프로필 적용(기본 출력·입력, 대체 장치, 장치 볼륨, 앱별 출력), 메인 화면 프로필 버튼, 단축키·명령 연결

ProfileCommandLine.cs
- --profile / --profile-index / --toggle-mute 인수 해석과 파이프 전달 형식

ProfileDeviceResolver.cs
- 대체 장치 순서 선택, 다른 PC에서 장치 이름으로 다시 찾기

AutoProfileSwitchEngine.cs / ProcessWatchService.cs
- 실행 프로그램 기준 자동 전환(현재 화면에서 숨김, 기본 꺼짐)

GlobalHotkeyService.cs / HotkeyGesture.cs
- 프로필 전역 단축키 등록과 "Ctrl+Alt+1" 형식 해석

AudioProcessOutputChangeCommand.cs
- 실행 파일 이름 기준 앱별 출력 변경 워커 인수 규약

DesktopAudioController.Updater/Program.cs
- 본 앱 종료 대기, 파일 교체, 새 버전 재실행
```

### 로컬 빌드

Windows 또는 Windows 타겟팅이 가능한 환경에서 실행합니다.

```bash
dotnet restore src/DesktopAudioController/DesktopAudioController.csproj -p:EnableWindowsTargeting=true
dotnet build src/DesktopAudioController/DesktopAudioController.csproj -p:EnableWindowsTargeting=true
dotnet build src/DesktopAudioController.Updater/DesktopAudioController.Updater.csproj -c Release -r win-x64 -p:EnableWindowsTargeting=true
```

### 배포 zip 생성

```bash
bash scripts/publish-win-x64.sh
```

생성되는 파일:

```text
artifacts/release/packages/DesktopAudioController-<version>-win-x64.zip
artifacts/release/packages/DesktopAudioController-<version>-win-x64.zip.sha256
```

CI의 테스트 아티팩트도 같은 `scripts/publish-win-x64.sh` 경로로 zip과 sha256을 생성합니다.

sha256 검증:

```bash
(cd artifacts/release/packages && sha256sum -c <생성된 zip>.sha256)
```

zip 내부 구조:

```text
DesktopAudioController/
  DesktopAudioController.exe
  DesktopAudioController.Updater.exe
```

## 문서와 릴리즈

- 설계 문서: [docs/design.md](docs/design.md)
- 로드맵: [docs/roadmap.md](docs/roadmap.md)
- 회귀 검증 체크리스트: [docs/regression-checklist.md](docs/regression-checklist.md)
- v1.0.0 출고 기준: [docs/v1-release-criteria.md](docs/v1-release-criteria.md)
- 릴리즈 노트 템플릿: [docs/release-notes-template.md](docs/release-notes-template.md)
- GitHub Releases: <https://github.com/SeoArin9142/DesktopAudioController/releases>

## 라이선스

```text
MIT License
```

자세한 내용은 [LICENSE](LICENSE)를 따릅니다.
 
