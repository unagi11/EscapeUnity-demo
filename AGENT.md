# EscapeUnity 프로젝트 지침

## Unity 작업 절차

- `ProjectSettings/ProjectVersion.txt`를 먼저 확인하고, Unity 버전이 작업에 영향을 주면 답변에 명시한다.
- UI, 입력, 렌더링, 테스트, Timeline, 2D 패키지 API를 만질 때는 `Packages/manifest.json`을 확인한다.
- 관련 C# 타입, `.asmdef`, prefab, scene, ScriptableObject, serialized reference는 `rg`로 먼저 찾아보고 수정한다.
- 기존 네이밍, 생명주기 메서드, 이벤트 연결 방식, serialization 스타일을 따른다.
- 문서와 구현이 다르면 의도한 동작과 현재 구현을 구분해서 설명한다.
- MonoBehaviour 참조는 Inspector/씬 세팅으로 연결하는 것을 기본으로 하고, 누락된 참조를 런타임에 찾거나 동적으로 생성하지 않는다.

## Unity 에셋/직렬화 규칙

- 가능하면 `.unity`/`.prefab` YAML 직접 수정보다 C# 스크립트, 데이터 에셋, 에디터 스크립트 수정을 우선한다.
- `.meta` 파일과 GUID 안정성을 보존한다. 에셋 이동/이름 변경 시 `.meta`를 같이 다룬다.
- `Library/`, `Temp/`, `Obj/`, `Build/`, `Builds/`, `Logs/`, `UserSettings/`, package cache 폴더는 수정하지 않는다.
- scene, prefab, material, animator, importer YAML 직접 수정은 최소화하고, serialized format을 확실히 이해한 경우에만 한다.
- serialized field 이름은 Unity Inspector 데이터가 깨지지 않게 유지한다. 기존 데이터가 있을 수 있으면 additive field나 migration code를 우선한다.
- MonoBehaviour 생성자에 숨은 작업을 넣지 말고, Unity lifecycle method에서 명확히 처리한다.
- 씬에 필요한 오브젝트와 UI 참조는 씬 세팅으로 보장한다. 코드가 누락 참조를 자동 보정하려고 scene child 검색, `Find*`, 동적 `new GameObject` 생성을 추가하지 않는다.
- ScriptableObject와 asset은 생성 경로, menu name, Resources/addressable 의존성을 확인한다.

## 코딩 원칙

- 중복 코드 지양 — 기존 유틸리티 재사용, 새 헬퍼는 꼭 필요할 때만 추가
- 불필요한 코드 지양 — 나중에 쓸지도 모르는 추상화 미리 만들지 않기
- 간결하게 — 명확한 구조로 가독성 좋게 작성
- 모든 클래스와 메서드에는 의도를 설명하는 짧은 한글 주석을 둔다. 단, 줄마다 반복 설명하는 주석은 피한다.
- package API는 설치된 패키지 버전에 맞춰 사용한다.

## C# 파일/폴더 구조

- `Assets/Scripts`의 최상위 폴더는 `Audio`, `Data`, `Dialogue`, `MiniGame`, `Room`, `SceneFlow`, `UI`처럼 기능 또는 기술 영역을 나타낸다.
- 전역 상태와 서비스도 `Progress`, `Localization`, `Platform`, `Runtime`처럼 실제 책임을 나타내는 기능 폴더에 둔다.
- Unity Editor에서만 실행되는 코드는 `Editor` 폴더 아래에 둔다.
- MonoBehaviour, ScriptableObject 여부가 아니라 코드의 기능 책임을 기준으로 폴더를 선택한다.
- `MonoBehaviour`, `ScriptableObject`, 서비스, Handler처럼 독립 행동과 수명주기가 있는 주 타입은 기본적으로 파일을 분리하고 파일명을 주 타입명과 일치시킨다.
- 특정 기능에만 종속된 `enum`, 작은 `struct`/DTO, 보조 인터페이스는 소유 타입 파일이나 응집된 `*Types.cs` 파일에 함께 둘 수 있다.
- 타입 수가 아니라 변경 이유와 함께 읽히는지를 기준으로 분리하며, 한두 줄짜리 보조 타입을 기계적으로 별도 파일로 만들지 않는다.
- 클래스 내부에서만 쓰는 `nested`/`private` 구현 타입은 주 클래스 파일에 함께 둔다.
- `Core`, `Common`, `Utils`처럼 책임 범위가 불명확한 새 폴더는 만들지 않는다.
- 역할 접미사는 `Controller`, `Player`, `Presenter`, `Service`, `Factory`, `Registry`, `Persistence`, `Result`, `Handler`를 일관되게 사용하고 인터페이스에는 `I` 접두사를 붙인다.
- Unity 에셋을 폴더 간 이동하거나 이름을 바꿀 때는 `.meta`를 함께 이동해 GUID를 유지한다.

## Room 코드 구조

- `Assets/Scripts/Room` 루트에는 `Room`, `RoomController`, `RoomRegistry`처럼 씬 진입, 전체 흐름 조정, 공용 Room 등록을 담당하는 타입만 둔다.
- MonoBehaviour 여부와 관계없이 기능 코드는 아래 책임 폴더로 구분한다.
  - `Animations`: 룸 Sprite/Aseprite 애니메이션 재생
  - `Dialogues`: 룸 대사 생성, 재생, 결과와 대사 효과 계약
  - `Interactions`: 클릭 판정, 상호작용 규칙과 상호작용 타입
  - `Persistence`: 룸 오브젝트 상태 저장과 복원
  - `ScreenEffects`: 화면 전환, 패널 가시성, 포스트 이펙트와 시각 피드백
  - `SpecialActions`: 상호작용 특수 행동의 분배, 핸들러와 전용 씬 컴포넌트

## 검증

- 빠른 정적 검증, 기존 테스트, solution/project build가 있으면 먼저 실행한다.
- Unity Test Framework는 순수 로직은 EditMode, scene/input/frame 동작은 PlayMode 테스트를 우선한다.
- Unity batchmode는 Unity 실행 파일 경로가 확인될 때만 실행하고, project path를 명시해 로그를 남긴다.
- Unity Editor가 열려 있고 `com.unity.pipeline`이 설치되어 있으면 Unity CLI/Pipeline을 로컬 Editor 검증 루프로 활용한다.
  - 기본 확인: `unity pipeline list --format json`, `unity status --format json` 또는 `unity status --project-path <absolute-project-path> --format json`
  - 컴파일/콘솔 확인: `unity command recompile_status --project-path . --format json`, `unity command console --tail 50 --level warn --project-path . --format json`
  - scene, prefab, importer, asset, EditorWindow 변경 후에는 가능하면 Pipeline으로 Editor recompile idle, Console warn/error, 필요한 오브젝트/참조 상태를 확인한다.
  - Pipeline이 없거나 Editor가 꺼져 있으면 기존 batchmode/build 검증으로 진행하고, Unity Editor hands-on 검증 미완료를 답변에 명시한다.
- scene, prefab, importer, package 변경 후에는 Unity 재임포트 또는 Editor 열기 확인을 검증 계획에 포함한다.

## 응답

- 사용자가 한국어로 말하면 간결하고 바로 실행 가능한 한국어로 답한다.
- Unity asset/script 경로는 정확한 repo 상대 경로나 절대 경로로 언급한다.
- Unity serialization이나 Editor-only 제한이 있으면 위험성을 먼저 설명한다.
