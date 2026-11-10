# PitchSimulator — 프로젝트 규약

포트폴리오 3호작. 11대11 축구 경기 시뮬레이터 — 킥오프 전에 선수를 세팅(빌드·지시·자리)하고
**개입 없이 관전**해 이긴다. 매니지먼트 없음. Google Play 출시 목표.

**유저가 모든 코드를 면접에서 설명할 수 있어야 하므로, 유저의 이해가 결과물의 일부다.**
혼자 달리지 말 것 — 승인 게이트 밖에서 코드 파일을 수정하지 않는다.

작업 리듬·승인 게이트·설계 원칙의 SSOT = `~/.claude/rules/`(자동 로드). 운영 절차 = `/dev-cycle` 스킬.
이 파일엔 **규칙과 포인터만** 둔다. 이력·세부 목록·근거는 Vault `설계문서/신규게임-축구시뮬/CLAUDE-상세.md`(2026-11-09 다이어트 때 옮긴 전문).
새 내용은 반복되는 실수를 막는 규칙일 때만 여기 추가하고, 경위·수치는 설계 문서나 상세판에 쓴다.

## 기준 문서

- **스펙 SSOT**: `~/Documents/Obsidian Vault/설계문서/신규게임-축구시뮬/스펙.md`. 근거 자료 `레퍼런스-조사.md`. 어긋나면 스펙
- **장부**: 같은 폴더 `구현플랜/열린-태스크.md`. 설계 판단 문서는 `실험/`
- **관전 장면 대조표**: `실험/2026-11-09-관전-장면-대조표.md`(FM·FC 대비 ○△✗)

## 이 게임의 축 (설계 판단이 갈릴 때의 기준)

- **레버는 전부 킥오프 전에 있다.** 경기 중 개입은 없다. 막혔을 때의 답은 "선수 세팅을 다시 짠다"
- **목표는 "유저가 준 규칙을 엔진이 일관되게 해석하고 그 차이가 결과에 보이는 것"이다.** 자동 대전으로 실증한다
- **전 선수가 같은 트리를 쓴다.** 판정값(지시 다이얼)만 다르다. 역할은 다이얼 프리셋
- **막히면 강화가 아니라 재세팅.** 스탯 총점 고정·성장·랜덤 없음
- **공 하나를 22명이 다툰다.** 소유 판정은 "공에 가장 가까운 선수"

## 코드의 출신

- **템플릿 출신**(`com.pmj.template.mobilegame` 09-15판): `Scripts/Core`·`Scripts/OutGame`·`Scripts/UISystem`·`Scripts/Data`·`Editor/`. 고치면 템플릿 역이식 여부를 판단한다("프로젝트가 달라도 하는 일이 같은가")
- **WarTableSimulator에서 옮긴 것**: BT 엔진·`TargetSelector`·`FieldBounds`·`StageComposition`·매니저 골격·CSV 관례. 이름은 이 게임 말로(Unit→Player, Battle→Match)
- **게임 코드**(역이식 안 함): `Scripts/Game/Core`(경기 규칙 전부), 경기장·관중 에디터 빌더와 셰이더, `Scripts/OutGame/Screens/`의 로비·매치데이·스카우트 화면. 파일 목록은 상세판
- 제거한 모듈: 스태미나·가챠·스킨·상점. 코인은 남김(용처 미정)

## 검증

**Unity CLI**(Homebrew `unity-cli`). 에디터가 열려 있으면:

```bash
unity command editor_status --result-only          # playMode == stopped 먼저(Play 중 재컴파일·테스트 금지)
unity recompile --json                             # data.failed == false, errors 0
unity command run_tests --mode EditMode --timeout 1500 --result-only   # 전체. 하나만이면 --filter <클래스명>
unity command console_status --result-only
```

- `unity command` 기본 제한 30초. 긴 명령은 `--timeout`. 결과는 `~/Library/Application Support/DefaultCompany/PitchSimulator/TestResults.xml` 시각으로도 확인
- **코드 최적화는 Release로 둔다.** Debug면 교착 테스트가 180초를 넘는다(`CompilationPipeline.codeOptimization`, eval로 바꿀 수 있다)
- 에디터가 뒤에 있거나 닫혀 있으면 dotnet 스크래치(메모리 `unity-background-dotnet-runner`)
- **동작 검증은 유저 Play.** 바꾼 것 체크리스트 + 머지 게이트 훅이 보여 주는 **Play 고정 7장면**(`~/.claude/hooks/pitch-play-fixed.md`)
- 빌드 중(`Tools/Build/Android APK (로컬 배포)`)에는 리포 코드를 고치지 않는다(재컴파일이 끼어든다)

## 회귀 대시보드 — 경기 규칙·상수·CSV를 바꿨으면 먼저 돌린다

규칙 하나를 바꾸면 다른 지표가 한꺼번에 움직인다. 설계 단계에서 **"이 값을 읽는 곳" 표와 구멍 점검**을 먼저 내고, 구현 뒤엔 지표를 전부 한 번에 잰다.

```bash
cd "$HOME/Documents/Obsidian Vault/설계문서/신규게임-축구시뮬/실험/러너" && ./dashboard.sh develop quick   # 중간 확인 약 1분
cd "$HOME/Documents/Obsidian Vault/설계문서/신규게임-축구시뮬/실험/러너" && ./dashboard.sh develop         # 머지 전 full 약 4분
```

- 실제 90분·결정성·95% 신뢰 구간·실제 축구 기준(StatsBomb) 판정. 지표 목록·probe 방법은 러너 `README.md`
- **머지 게이트 훅**(`~/.claude/hooks/pitch-merge-gate.py`): develop 머지 때 엔진을 바꾼 브랜치면 full 대시보드가 최신이고 현실감 지표(`현실.*`)가 있어야 통과
- 무거운 측정은 동시에 돌리지 않는다. 러너 probe는 develop과 작업 트리 둘 다에서 빌드되게 쓴다
- **엔진 설계·구현 검증은 포메이션 6개 전부로 잰다**(3-4-3·3-5-2·4-2-3-1·4-3-3·4-4-2·5-3-2, 대칭). 판정은 포메이션마다 develop 대비 회귀, 가드 지표 = 슛/판·받을 때 라인 뒤·체력 표·형태(2m 쌍·깊이 폭)·압박 왕복. 설계 원칙은 `~/.claude/rules/design.md`. 대시보드에 포메이션 묶음이 들어가기 전까지는 빌드 복사본 패치(러너 `tools/patch-formation.py`, 워크트리 빌드는 `tools/syncwt.sh`)로 잰다

## 정적 검사 (경고 0 정책)

- `Assets/csc.rsp` `-warnaserror+`(Obsolete 612·618만 경고). asmdef 옆 `csc.rsp`는 이걸 **대체**하므로 같은 줄을 적는다. 새 asmdef면 `Scripts/Game/Core/csc.rsp`를 복사
- 분석기 Microsoft.Unity.Analyzers. 컴파일 심각도 SSOT = `Assets/Default.ruleset`(Unity는 `.editorconfig`를 안 읽음). 서드파티 asmdef는 옆 `ThirdParty.ruleset`
- `-nullable:enable`은 `Game.Core`에만. 문체는 Unity C# 스타일 가이드(private camelCase, 한 줄 문장도 중괄호)
- 심각도 기준 3층·audit 사용법은 상세판

## 프로젝트 관례

- 매니저: `InGameManager` 상속 + 태그 `"Manager"` + `GameManager` 등록 3종 세트. 매니저 간 참조는 `GameManager.{Manager}` 경유. 프리팹 인스턴스는 매니저를 모르고 값만 받는다
- **국면은 `GameManager.GameState`가 유일한 진실.** 매니저는 진입/퇴장 훅을 구독하고 상태 플래그를 따로 들지 않는다. 상태→UI는 `GameUIManager` 한 곳
- **경기 판정은 역할 이름(`RoleId`)을 보지 않는다(GK 제외).** 역할 차이는 다이얼과 `DefenseRoles.csv` 열로
- 오브젝트 생성은 ObjectPool. `FindObjectOfType`/`GameObject.Find` 금지
- **경기 AI·이동·공은 고정 스텝(`FixedUpdate`)·결정적.** 공은 Rigidbody 없이 순수 계산. 화면 전용 연출만 예외
- **내 경기 시뮬은 `MyMatchAssembler.Create` 한 곳에서 만든다.** 선수 뷰는 시뮬 `Players`에 몸만 붙인다
- 선수 세팅은 StageComposition과 같은 데이터 형식으로 저장
- **결과물(초상·클립·프리팹 배선·베이크 데이터)을 만든 에디터 스크립트는 1회용이어도 `Assets/Editor/`에 커밋한다.** 다시 만들 일이 생기면 그대로 다시 돌릴 수 있어야 한다(11-10: 초상 120장 렌더 스크립트를 10-21에 "1회성"으로 안 남겨 모델 교체 때 새로 짜야 함)
- **데이터는 CSV + CsvHelper `ClassMap`(자작 파서 금지).** CSV별 열 설명은 상세판. 바꾸면 그 CSV를 잠그는 테스트와 대시보드를 같이 돌린다(`StaminaLoadTable.csv`는 러너 `loadtable`로 재생성, `ThreatTable.csv`는 `xtgen`)
- **코드 문체 앵커 = AnimalBreakOut**: 이른 반환, 여러 줄 `&&` 지양, 메서드는 중괄호 블록, 람다는 외부 API가 요구할 때만(콜백은 이름 붙은 private 메서드), `out var` 압축 지양, **한 줄 중괄호 블록은 문장 하나까지**. 리뷰 기준 원본은 `~/unity/WarTableSimulator/CLAUDE.md` 75~79행
- 커밋: 한국어 + why + 논리 단위, pangyo-tone 신호 점검. 순서 = 구현 → 검증 → 리뷰 → 유저 Play → 커밋
- **브랜치**: 태스크 하나 = `feature/*` 하나 → `develop`. `master`는 AAB 빌드 때만. 첫 수정 전에 브랜치부터

## 테스트 가능 경계

MonoBehaviour 의존 없는 판정은 순수 C#(`Game.Core`, `noEngineReferences`)으로 빼서 EditMode로 잠근다. 노드 안 판단은 순수 함수로 빼고 트리 노드는 얇은 어댑터. 트리 흐름·카메라·화면은 PlayMode·유저 Play. 잠근 규칙의 전체 목록은 상세판.

## 빌드·출시

- `Tools/Build/Android APK (로컬 배포)` · `Tools/Build/Android AAB (스토어 제출)`. 패키지명·제품명·가로 고정은 `Assets/Editor/BuildScript.cs`가 강제
- Play Console 개인 계정은 프로덕션 전 테스터 12명·14일 연속. AdMob은 이 게임용 앱을 따로 등록

## 레퍼런스 스킬

플랜 `superpowers:writing-plans`, 완료 선언 전 `superpowers:verification-before-completion`, 구현 후 `superpowers:requesting-code-review`.
