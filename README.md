# EscapeUnity Demo

`EscapeUnity`의 포트폴리오 제출용 공개 데모 저장소입니다. 원본 게임 전체가 아니라 타이틀에서 시작해 인트로와 침실 퍼즐을 체험하는 짧은 수직 슬라이스만 포함합니다.

## 웹 데모

[브라우저에서 EscapeUnity 데모 플레이](https://unagi11.github.io/EscapeUnity-demo/)

## 트레일러

[![EscapeUnity 트레일러](https://img.youtube.com/vi/Ct5GcwSQ2rg/maxresdefault.jpg)](https://youtu.be/Ct5GcwSQ2rg?si=m8yjc33vhWTHBqdl)

이미지를 클릭하면 YouTube에서 트레일러를 볼 수 있습니다.

## 데모 범위

1. 타이틀에서 새 게임 시작 및 플레이어 이름 입력
2. 다국어 인트로 대화 재생
3. 침실 조사와 아이템 획득·조합
4. 머리핀과 락픽 도구를 이용한 수갑 해제 미니게임
5. 침실을 나가면 게임오버 대화 재생 후 타이틀 복귀

거실·주방·현관·다용도실, 후반 미니게임, 엔딩 시나리오 및 관련 리소스는 공개 데모에서 제외했습니다.

## 구현 포인트

- Unity 직렬화 기반의 데이터 주도형 오브젝트 상호작용
- TSV 기반 한국어·영어·일본어 대화 및 UI 현지화
- UniTask 기반 비동기 대화·화면 전환 흐름
- 아이템 획득, 조합, 선택 상태와 세이브/로드
- 별도 씬으로 구성한 락픽 미니게임과 룸 상태 복귀
- UI `RawImage` 기반 커스텀 포스트 이펙트와 픽셀 해상도 전환
- 런타임 칩튠 BGM/SFX 재생 시스템

## 코드를 처음 보는 분께

이 저장소는 기능별 책임이 드러나도록 코드를 분리했습니다. 전체 코드를 순서대로 읽기보다 아래 진입점부터 확인하면 데모의 주요 흐름을 빠르게 파악할 수 있습니다.

| 영역 | 주요 코드 | 확인할 내용 |
| --- | --- | --- |
| TSV 데이터 | [`TsvDataLoader.cs`](Assets/Scripts/Data/TsvDataLoader.cs), [`Assets/Resources/Data`](Assets/Resources/Data) | 대사·아이템·UI 텍스트를 코드와 분리해 세 언어로 로드하는 구조 |
| 대사 연출 | [`DialoguePlayer.cs`](Assets/Scripts/Dialogues/DialoguePlayer.cs) | 비동기 타이핑, 선택지, 화면 효과와 스토리 상태 변경 흐름 |
| 방 상호작용 | [`InteractionRule.cs`](Assets/Scripts/Room/Interactions/InteractionRule.cs), [`RoomInteractor.cs`](Assets/Scripts/Room/Interactions/RoomInteractor.cs) | 직렬화된 조건·결과와 클릭 판정을 분리한 오브젝트 상호작용 |
| 아이템·진행 상태 | [`PlayerInventory.cs`](Assets/Scripts/Progress/PlayerInventory.cs), [`GameSession.cs`](Assets/Scripts/Progress/GameSession.cs) | 아이템 선택·조합과 현재 플레이 상태 관리 |
| 락픽 미니게임 | [`LockPickGameController.cs`](Assets/Scripts/MiniGames/LockPickGameController.cs) | 별도 씬 미니게임의 입력, 핀 판정과 룸 복귀 |
| 화면 연출 | [`Assets/Scripts/Room/ScreenEffects`](Assets/Scripts/Room/ScreenEffects), [`Assets/Shaders`](Assets/Shaders) | 상태 기반 포스트 이펙트와 화면 전환 |
| 런타임 QA | [`RuntimeQaExecutor.cs`](Assets/Scripts/QA/RuntimeQaExecutor.cs), [`demo.qa`](Assets/StreamingAssets/QA/Routes/demo.qa) | 실제 입력 흐름을 재생하는 QA 명령과 데모 경로 |

## 커스텀 쉐이더와 화면 연출

### 상태 기반 룸 포스트 이펙트

[`RoomRawImagePostEffect.shader`](Assets/Shaders/RoomRawImagePostEffect.shader)는 방의 렌더 텍스처를 표시하는 UI `RawImage`에 다음 효과를 한 패스에서 합성합니다.

- 일반·방향성 블러
- CRT 스캔라인, 화면 플리커와 노이즈
- 최대 8색 제한 팔레트와 디더링
- 화면 일부가 끊어지는 글리치, 색수차와 UV 왜곡
- 색상 회전, 비네트 펄스와 저해상도 리샘플링

효과 수치는 [`RoomPostEffectSettings.cs`](Assets/Scripts/Room/ScreenEffects/RoomPostEffectSettings.cs)의 `ScriptableObject` 프로필로 관리합니다. 공개 데모에는 `Default`, `Warn`, `Danger`, `Drunken` 프로필이 포함되어 있으며, [`RoomPostEffectController.cs`](Assets/Scripts/Room/ScreenEffects/RoomPostEffectController.cs)가 체력 상태 또는 대사 TSV의 `shader` 열에 맞춰 프로필과 BGM 피치를 함께 보간합니다.

씬이 참조하는 공유 머티리얼을 플레이 중 직접 수정하지 않도록 시작 시 전용 런타임 머티리얼을 복제하고, 종료 시 원본을 복구합니다. 따라서 상태 전환 값이 에디터 에셋에 남지 않습니다.

```text
체력 변화 또는 dialogue*.tsv의 shader 값
  → RoomPostEffectController
  → RoomPostEffectSettings 프로필 보간
  → RoomRawImagePostEffect.shader
  → RoomImage 출력
```

### 픽셀 해상도 전환

[`RoomResolutionFade.shader`](Assets/Shaders/RoomResolutionFade.shader)는 화면을 단순히 검게 덮지 않고 현재 방 프레임의 픽셀 해상도를 단계적으로 낮추거나 복원합니다. [`ResolutionFadeScreenEffectHandler.cs`](Assets/Scripts/Room/ScreenEffects/Handlers/ResolutionFadeScreenEffectHandler.cs)가 텍스처 높이를 2의 거듭제곱 단계로 변경하고, 쉐이더는 화면 비율에 맞춰 가로 셀 수를 계산해 픽셀 블록을 유지합니다.

전환 직전에는 [`RoomScreenEffectResources.cs`](Assets/Scripts/Room/ScreenEffects/RoomScreenEffectResources.cs)가 `materialForRendering`을 복제해 현재 포스트 이펙트까지 포함된 방 화면을 `RenderTexture`로 캡처합니다. UI 클리핑 범위는 캡처용으로 확장하고 출력 알파는 별도로 보존해, 화면과 전환 사이에서 색감이나 투명도가 달라지지 않도록 구성했습니다.

## 실행 환경

- Unity `6000.3.8f1`
- Git LFS
- 기본 입력: 마우스 또는 터치

## 실행 방법

```bash
git clone https://github.com/unagi11/EscapeUnity-demo.git
cd EscapeUnity-demo
git lfs pull
```

Unity Hub에서 저장소 폴더를 Unity `6000.3.8f1`로 연 뒤 `Assets/Scenes/0_TitleScene.unity`를 실행합니다. Build Settings에는 타이틀, 침실, 락픽 씬만 등록되어 있습니다.

WebGL 배포본을 갱신하려면 Web Build Support가 설치된 Unity에서 아래 명령을 실행한 뒤 생성된 `docs/`를 `main` 브랜치에 반영합니다. GitHub Actions가 해당 폴더를 GitHub Pages에 자동 배포합니다.

```bash
/Applications/Unity/Hub/Editor/6000.3.8f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -quit -projectPath "$PWD" \
  -buildTarget WebGL -executeMethod Escape.EditorTools.ProjectBuilder.BuildWebGl
```

## Repository scope

This public repository is a portfolio-focused vertical slice of `EscapeUnity`. It contains only the title, intro, bedroom puzzle, handcuff lock-picking sequence, and the demo game-over transition. Later rooms, minigames, endings, and their assets are intentionally omitted.

## 저작권 및 제3자 고지

이 저장소에는 별도의 오픈소스 라이선스를 부여하지 않습니다. 제3자 코드·폰트·에셋의 라이선스와 출처는 [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt)에서 확인할 수 있습니다.
