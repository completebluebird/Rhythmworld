# RhythmWorld 작업 규칙
- 답변은 한국어로.
- Unity 6 URP 3D 리듬게임. 리듬세상 스타일 도장찍기.
- 박자 값은 절대 바꾸지 말 것: bpm 99.97, firstBeatOffset 0.14초, songEndTime 74초, 첫 서류 9박(First Paper Beat 9).
- 판정은 오디오 DSP 시간 기준(Conductor.SongTimeNow). Time.time 기준으로 바꾸지 말 것.
- 연결된 Unity 에디터가 있으면 `unity status`로 확인하고 에디터를 직접 조작할 것. 씬/프리팹 YAML을 손으로 고치지 말 것.
- 작업 시작: main을 최신으로 받고 새 브랜치를 만든다(이름은 작업 내용으로).
- 작업 끝: 컴파일 에러 확인 → Play 모드로 런타임 에러 확인 → Play 끄기 → 씬 저장 → 커밋. 푸시는 내가 말할 때만.
- 끝나면 바뀐 것, 내가 직접 확인할 것, 남은 질문을 짧게 정리해서 알려줄 것.

## 게임 구조(확정)
- 서류 한 장 = 4박. 1박 예고음+인턴이 건넴(입력 없음), 2박 왼손 도장, 3박 오른손 사인, 4박 인턴 회수+success/fail.
- 서류 3종: 일반(도장 1번, 사인), SECRET(도장 2번: 2박·2.5박, 사인), 함정=부장님 사진(도장 X, 사인 O).
- 키: 도장 F/Space/왼쪽 클릭, 사인 J/오른쪽 클릭. 1~5 채보 전환, R 다시, G 가이드 틱, [ ] 입력 보정.
- 채보: 한 글자 = 서류 한 장. n 일반, s SECRET, t 함정, . 4박 쉼.
- 효과음: Assets/Audio/soundeffect (normal_start, secret_start, Trap_start, stamp_sound, sign_sound, success, fail).
- 화면: 내 머리 뒤 위에서 책상을 내려다봄. 가운데 서류, 왼쪽 도장, 오른쪽 펜, 책상 건너편 미니어처 인턴. 플레이 중 UI 최소.
