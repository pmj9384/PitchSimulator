# PitchSimulator — 프로젝트 규약

포트폴리오 3호작. 11대11 축구 경기 시뮬레이터 — 킥오프 전에 선수를 세팅(빌드·지시·자리)하고
**개입 없이 관전**해 이긴다. 매니지먼트 없음. 4주 개발 + Google Play 출시.

**유저가 모든 코드를 면접에서 설명할 수 있어야 하므로, 유저의 이해가 결과물의 일부다.**
혼자 달리지 말 것 — 승인 게이트 밖에서 코드 파일을 수정하지 않는다.

작업 리듬·승인 게이트·설계 원칙의 SSOT = `~/.claude/rules/` (유저 스코프, 자동 로드).
운영 절차의 SSOT = `/dev-cycle` 스킬(검증 루프 4.5절 포함). 이 파일엔 **프로젝트 특수사항만** 남긴다.

## 구현의 기준 문서

**`~/Documents/Obsidian Vault/설계문서/신규게임-축구시뮬/스펙.md`가 SSOT다.**
필드 치수·선수 3겹(빌드·지시·자리 2쌍)·공 모델·행동 트리·경기 규칙·배치 조작·스테이지·데이터·스코프 밖이 전부 여기 있다.
`레퍼런스-조사.md`(유사 게임 8·Simple Soccer AI 모델·xG식)는 근거 자료다. 스펙과 어긋나면 스펙을 따른다.
장부 = 같은 폴더 `구현플랜/열린-태스크.md`.

## 이 게임의 축 (설계 판단이 갈릴 때의 기준)

- **레버는 전부 킥오프 전에 있다.** 경기 중 개입은 없다. 막혔을 때의 답은 "선수 세팅을 다시 짠다"
- **"유저가 원하는 대로"가 아니라 "유저가 준 규칙을 엔진이 일관되게 해석하고 그 차이가 결과에 보이는 것"이 목표다.**
  자동 대전으로 "세팅을 바꾸면 결과가 바뀐다"를 실증한다
- **전 선수가 같은 트리를 쓴다.** 판정값(지시 다이얼)만 다르다. 역할은 다이얼 프리셋이지 별도 트리가 아니다
- **막히면 강화가 아니라 재세팅.** 스탯 총점 고정·성장·랜덤 없음
- **공 하나를 22명이 다툰다.** 유닛 시뮬의 "각자 가장 가까운 적"과 갈리는 유일한 코어. 소유 판정은 "공에 가장 가까운 선수"

## 코드의 출신

**커스텀 템플릿**(`com.pmj.template.mobilegame`, 2026-09-15 재패킹본 = ResumeFromPause·RestartGame(bool)·배너·SafeArea Clamp 포함)에서 생성.

- **템플릿 출신** — `Scripts/Core`(매니저 허브·세이브·풀·로딩), `Scripts/OutGame`, `Scripts/UISystem`, `Scripts/Data`, `Editor/`.
  고치면 **템플릿에도 역이식할지** 판단한다(기준: "프로젝트가 달라도 하는 일이 같은가")
- **WarTableSimulator에서 옮겨 올 것**(장르 무관, 스펙 12절 표) — BT 엔진(`BehaviorNode`·`CompositeNodes`·`IUnitContext`), `TargetSelector`,
  `PlacementRules`·`FieldBounds`·`PlacementManager`·`PlacementInput`·`PlacementPanel`, `StageComposition`/`StageTable` 파서·리포지토리·`StageManager`,
  `UnitManager`(명부·풀)·`BattleManager` 틱 구조·`HudManager` 골격, CSV 관례·테스트 틀. 옮길 때 이름을 이 게임 말로 바꾼다(Unit→Player, Battle→Match)
- **게임 코드** — 공·소유·패스·슛(`MatchRules`), 선수 트리, 스코어·시간 UI, 선수 세팅 화면. 템플릿에 안 올라간다

**제거한 모듈**: 스태미나·가챠·스킨·상점 (2026-09-15 세팅 때 뗌). **코인은 남겼다** — 용처는 3주차에 결정, 없으면 그때 제거.

## 검증 — Unity CLI (MCP 서버는 CLI가 호스팅)

에디터가 열려 있으면 파이프라인 서버(`com.unity.pipeline`)로 붙는다. `unity status --json`에 ready면:

```bash
unity command run_tests --mode EditMode --json     # 동기, data.result.Summary.Failed == 0
unity command recompile && unity command recompile_status
unity command console_status --json           # compilationFailed·경고 수. 본문은 console --level warning --tail 30
unity command get_scene_hierarchy --json           # 씬 읽기
unity command screenshot --view game --json        # Play 중 화면
```

- 씬 배선은 `create_gameobject`·`attach_script`·`set_serialized_field`·`set_parent`·`save_scene`으로 직접 한다. YAML 편집·1회성 에디터 도구 불필요
- 결과 `data.result`가 문자열로 오는 명령이 있다(`recompile_status`) — 문자열이면 한 번 더 파싱
- 에디터가 닫혀 있으면 배치: `unity projects verify .` · `unity run .` · `unity test . --mode EditMode`
- **동작 검증은 유저 Play Mode.** 체크리스트를 제시한다. `editor_play`는 에디터가 앞에 있어야 프레임이 돈다(뒤에 있으면 frameCount 1에서 멈춤) — AI는 콘솔 오류 확인까지만

## 정적 검사 (2026-09-15 세팅, 경고 0 정책)

컴파일러·분석기가 먼저 잡고 사람은 남은 것만 본다. 이식 전에 깔아 옮기는 코드가 첫 컴파일부터 검사된다.

- `Assets/csc.rsp`: `-warnaserror+`(경고 = 컴파일 실패) · `-warnaserror-:612,618`(Obsolete 2종만 경고로 남김. Unity 패키지 업그레이드가 우리 손 밖이라. Unity 자체 리포 ml-agents·ECS 샘플과 같은 설정)
- `Assets/Analyzers/Microsoft.Unity.Analyzers.dll`(1.27.0, 라벨 `RoslynAnalyzer`, 모든 플랫폼 끔): **Microsoft 제작** Unity 전용 규칙(UNTxxxx). VS·VS Code Unity 확장에 기본 탑재라 사실상 표준이고, Unity Technologies 공식은 아니다. 분석기 오류는 `recompile_status`의 errors[]에 CS 오류와 같이 나오므로 검증 루프가 그대로 잡는다
- **심각도 기준 3층**(근거를 물으면 이 순서로 답한다): ① Unity "모바일 게임 성능 최적화" 가이드가 명시한 관행(빈 Update·CompareTag·WaitForSeconds 캐시·StringToHash·PropertyToID·매 프레임 문자열) → 오류 ② Microsoft 카테고리 Correctness·TypeSafety(fake null 비교 등, Unity 레퍼런스 `Object.operator==`가 근거) → 오류 ③ 그 외 Performance·Readability → Info. 성능 규칙은 "쓰지 마"가 아니라 "이 형태로"라서 읽기 편한 쪽으로 쓰고 프로파일러에 잡힌 곳만 규칙 형태로 바꾼다
- **컴파일 시 심각도의 SSOT = `Assets/Default.ruleset`.** Unity 컴파일은 `.editorconfig`를 읽지 않는다(Bee rsp에 `-analyzerconfig` 없음, 09-15 실측). `.editorconfig`는 IDE 표시·문체용이고 UNT 값은 ruleset과 같게 유지한다
- 서드파티 asmdef(`Plugins/SerializedCollections` Runtime·Editor)는 asmdef마다 옆의 `ThirdParty.ruleset`으로 UNT 규칙을 끈다. 남의 코드는 고치지 않는다. 새 플러그인을 넣으면 같은 파일을 복사한다
- `UNT0021`(메시지를 protected로)은 끔. 분석기 기본값도 꺼져 있고 이 프로젝트 관례는 `private void Awake()`다
- 문체는 Unity C# 스타일 가이드(Unity 6판)와 맞춘다: private 필드 camelCase 접두 없음(가이드의 첫 권장, m_는 선택), 공개 멤버 PascalCase, **한 줄 문장도 중괄호**(`if (x) { return; }`, 가이드 "don't omit braces"). `.editorconfig`가 IDE에서 표시한다
- `-nullable:enable`은 게임 코드 어셈블리(`Game.Core`)의 asmdef 옆 `csc.rsp`에만 켠다. 템플릿 코드 전체에 켜면 경고가 쏟아진다. **asmdef 옆 csc.rsp는 `Assets/csc.rsp`를 대체한다**(합쳐지지 않음, 09-16 Bee rsp로 확인). 그래서 `-warnaserror+`·`-warnaserror-:612,618`을 거기에도 적는다. 새 asmdef를 만들면 `Scripts/Game/Core/csc.rsp`를 복사한다
- `unity command audit --output <csv>` → `audit_status`(completed) → CSV. 4,800건 중 4,700건이 Packages라 **`RelativePath`가 `Assets/`(Plugins 제외)·`ProjectSettings`인 행만** 본다. 커밋 전 1회면 충분하다(구현 단위마다 안 돌린다)

## 프로젝트 관례

- 매니저: `InGameManager` 상속 + 태그 `"Manager"` + `GameManager` 프로퍼티·등록 — 3종 세트. 아웃게임은 `OutGameManager`가 따로
- **국면은 `GameManager.GameState`가 유일한 진실.** 각 매니저는 Initialize에서 자기 상태의 진입/퇴장 훅을 구독하고 `running`·`IsActive` 같은 플래그를 따로 들지 않는다.
  상태→UI는 `GameUIManager` 한 곳. 일시정지 복귀는 `ResumeFromPause()`(진입 훅 재실행 없음). GameReady = 선수 세팅 국면, GamePlay = 경기, GameClear/GameOver = 결과
- 매니저 간 참조는 `GameManager.{Manager}` 경유. 프리팹 인스턴스는 매니저를 모르고 값만 받는다
- 오브젝트 생성은 ObjectPool. `FindObjectOfType`/`GameObject.Find` 금지
- **경기 AI·이동·공은 고정 스텝(`FixedUpdate`)으로 돈다.** 공은 Rigidbody를 쓰지 않고 순수 계산으로 옮긴다(결정성). 기기가 달라도 결과가 같아야 한다
- **선수 세팅은 StageComposition과 같은 데이터 형식으로 저장한다.** 재도전·상대 팀 데이터·멀티가 같은 것을 읽는다
- 데이터는 CSV: `PlayerTable.csv` · `StageTable.csv` · `StageComposition.csv`. 파싱은 CsvHelper + `ClassMap`, 자작 파서 금지
- **코드 문체 앵커 = AnimalBreakOut 코드베이스.** 이른 반환, 메서드는 중괄호 블록, 람다는 외부 API가 함수를 요구할 때만, `out var` 지양
- 커밋: 한국어 + why + 논리 단위. 쓰기 전 pangyo-tone 신호 14개 자체 점검. 구현 → 검증 루프(4.5절) → 유저 Play → 커밋
- **브랜치**: `feature/*` → `develop`. `master`는 빌드 검증(AAB) 시점에만. 첫 수정 전에 브랜치부터

## 빌드·출시

- `Tools/Build/Android APK` · `Tools/Build/Android AAB`. 패키지명 `com.pmj.pitchsimulator`·제품명·가로 고정은 `Assets/Editor/BuildScript.cs` 상수가 빌드마다 강제
- **Play Console 개인 계정은 프로덕션 전 테스터 12명·14일 연속.** 3주차 초에 돌아가는 빌드가 나오면 **즉시** 비공개 테스트에 올린다
- AdMob은 이 게임용 앱을 따로 등록해야 한다(WarTableSimulator 앱 ID를 쓰지 않는다). 절차는 세팅 스킬 "광고 붙이기"

## 테스트 가능 경계

MonoBehaviour 의존 없는 판정은 순수 C#(`Game.Core` asmdef, `noEngineReferences`)으로 뺀다.

- **EditMode 대상**: 공 소유 판정(가장 가까운 선수·타이브레이크), 패스 가로채기, 슛 성공 확률(거리·각도), 세팅 배치 판정(11명·GK 1·간격), 파서 전건 대조
- **PlayMode·수동**: 트리 흐름, 이동, 카메라. 노드 안 판단은 `MatchRules` 순수 함수로 빼고 노드는 얇은 어댑터

## 레퍼런스 스킬

플랜 문서 `superpowers:writing-plans`, 완료 선언 전 `superpowers:verification-before-completion`, 구현 후 `superpowers:requesting-code-review`.
세팅은 `unity-project-setup` 스킬로 했다(2026-09-15).
