# EscapeUnity Demo

`EscapeUnity`의 포트폴리오 제출용 공개 데모 저장소입니다. 원본 게임 전체가 아니라 타이틀에서 시작해 인트로와 침실 퍼즐을 체험하는 짧은 수직 슬라이스만 포함합니다.

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
- 런타임 칩튠 BGM/SFX 재생 시스템

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

## Repository scope

This public repository is a portfolio-focused vertical slice of `EscapeUnity`. It contains only the title, intro, bedroom puzzle, handcuff lock-picking sequence, and the demo game-over transition. Later rooms, minigames, endings, and their assets are intentionally omitted.

## 저작권 및 제3자 고지

이 저장소에는 별도의 오픈소스 라이선스를 부여하지 않습니다. 제3자 코드·폰트·에셋의 라이선스와 출처는 [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt)에서 확인할 수 있습니다.
