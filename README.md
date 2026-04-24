# Samurai (Unity 2D 프로토타입)

이 저장소는 사무라이 캐릭터의 등장 연출, 공격 전환, 슬래시 FX 타이밍을 중심으로 만든 Unity 2D 프로토타입 프로젝트입니다.

## 1. 프로젝트 개요

- 프로젝트 이름: samurai
- 엔진: Unity 6
- Unity 버전: 6000.4.3f1
- 유형: 2D 프로토타입 / 애니메이션 상태 전환 테스트 씬
- 주요 목표: Idle -> Run(등장 이동) -> Attack 흐름이 안정적으로 동작하고, 공격 전환 시점에 슬래시 FX가 정확히 실행되는지 검증

## 2. 핵심 기능

- 공격 입력 허용 전에 캐릭터 등장 이동 연출 수행
- 공격 버튼 기반 상태 전환
- Animator 상태 이름 해석 및 폴백 동작 처리
- 이벤트/델리게이트 기반 공격 전환 알림
- FX 생성 요청 및 런타임 수명 관리
- 상태 및 입력 디버깅을 위한 런타임 로그 기록

## 3. 씬 및 실행 진입점

- 메인 씬: Assets/Scenes/SampleScene.unity
- 템플릿 씬(URP): Assets/Settings/Scenes/URP2DSceneTemplate.unity

현재 동작을 확인하려면 SampleScene을 열고 Play를 실행하면 됩니다.

## 4. 조작 방법

- UI 공격 버튼: 공격 시퀀스 시작 (등장 연출 완료 후)
- 선택 기능인 키보드 폴백: Space 키 (Inspector의 enableKeyboardFallback 활성화 시)

## 5. 주요 스크립트

### Assets/gamemain.cs

메인 흐름 제어 스크립트입니다.

역할:
- 참조 유효성 검사 및 캐시 처리
- idle/run/attack 애니메이션 상태 해석
- 공격 버튼 클릭 처리 및 공격 루틴 타이밍 제어
- FX 스폰 위치와 함께 공격 전환 이벤트 호출
- 애니메이션/입력 연결 문제 확인용 런타임 로그 기록

### Assets/FxManager.cs

FX 생명주기 관리 스크립트입니다.

역할:
- Singleton 방식 전역 접근(FxManager.Instance)
- 요청 위치에 슬래시 FX 오브젝트 생성
- FX 렌더러 강제 표시 및 애니메이션 즉시 재생
- 애니메이션/파티클 재생 시간 기준으로 자동 제거

## 6. 주요 패키지

Packages/manifest.json 기준:

- Universal Render Pipeline: com.unity.render-pipelines.universal
- Input System: com.unity.inputsystem
- 2D 애니메이션/스프라이트/타일맵 관련 패키지

## 7. 로그

런타임에서 공격/디버그 로그를 아래 경로에 기록합니다.

- 영구 데이터 경로 로그: samurai_attack_log.txt
- 프로젝트 루트 런타임 로그: samurai_attack_log_runtime.txt

이 로그는 버튼 입력, 상태 전환, FX 요청이 정상적으로 발생하는지 점검할 때 유용합니다.

## 8. 저장소 관리 메모

- Unity 생성 폴더는 .gitignore로 제외했습니다.
- 이 저장소는 재실행/개발 이어서 진행에 필요한 소스 에셋, 스크립트, 패키지 설정, 프로젝트 설정을 추적합니다.

## 9. 평가용 체크리스트 (선생님 확인용)

- 프로젝트가 패키지 오류 없이 열리는지
- SampleScene 실행 시 등장 이동이 정상 재생되는지
- 등장 완료 후 공격 버튼으로 공격 상태가 전환되는지
- 공격 전환 시점에 슬래시 FX가 표시되는지
- 설정된 공격 시간 이후 idle로 복귀하는지
- 동작 중 로그가 정상 기록되는지

## 10. 다음 개선 계획

- HP/적 상호작용 및 피격 판정 추가
- 콤보 공격 확장 및 상태머신 가독성 개선
- UI 피드백 및 쿨다운 표시 추가
- 공격 전환 타이밍 Play Mode 테스트 자동화
