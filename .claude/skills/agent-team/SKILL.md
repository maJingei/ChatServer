---
name: agent-team
description: >
  복잡한 작업을 자동으로 하위 태스크로 분해하고, 여러 Claude 에이전트를 병렬로 스폰하여 동시에 처리하는 오케스트레이션 스킬.
  이 스킬은 사용자가 다음과 같은 요청을 할 때 반드시 트리거되어야 한다:
  "여러 파일 동시에 만들어줘", "병렬로 처리해줘", "에이전트 팀으로 작업해줘",
  "동시에 여러 모듈 개발해줘", "subagent로 나눠서 해줘", "팀으로 분업해줘",
  "parallel agents", "concurrent tasks", "spawn agents" 등.
  또한 명시적 요청이 없더라도 작업이 3개 이상의 독립적인 파일/모듈/컴포넌트로
  자연스럽게 분해 가능한 경우에도 이 스킬을 적극적으로 사용할 것.
  코드 작업(여러 파일/모듈 동시 개발)에 특히 최적화되어 있으며,
  Claude Code CLI (`claude -p`)와 Task 기반 실행을 모두 지원한다.
---

# Agent Team — 병렬 에이전트 오케스트레이션

## 개요

이 스킬은 **Orchestrator → Workers → Merger** 패턴을 따른다.

```
사용자 요청
    │
    ▼
┌─────────────────┐
│  Orchestrator    │  ← 작업 분석 & 분해
│  (현재 세션)      │
└────┬───┬───┬────┘
     │   │   │
     ▼   ▼   ▼       ← 병렬 스폰
   [A1] [A2] [A3]     Worker 에이전트들
     │   │   │
     ▼   ▼   ▼
┌─────────────────┐
│    Merger        │  ← 결과 수집 & 통합
│  (현재 세션)      │
└─────────────────┘
     │
     ▼
  최종 결과물
```

핵심 원칙:
- 각 Worker는 **완전히 독립적**으로 실행된다 (공유 상태 없음)
- Orchestrator가 충분한 컨텍스트를 각 Worker에게 전달해야 한다
- 결과 병합 시 충돌 감지 및 해결 로직을 포함한다

---

## Phase 1: 작업 분석 & 분해

사용자의 요청을 받으면, 먼저 병렬화 적합성을 판단한다.

### 병렬화 판단 기준

**병렬 처리에 적합한 경우:**
- 서로 다른 파일/모듈을 각각 생성하는 작업
- 독립적인 컴포넌트/서비스 개발 (예: API 라우트 여러 개)
- 서로 다른 테스트 파일 작성
- 독립된 유틸리티 함수 모듈들
- 마이크로서비스의 개별 서비스 구현

**순차 처리가 나은 경우:**
- 파일 간 강한 의존성이 있는 경우 (B가 A의 출력을 필요로 함)
- 단일 파일 내 복잡한 로직 구현
- 공유 상태를 변경하는 작업

### 분해 템플릿

작업을 분해할 때 다음 구조를 따른다:

```
## 작업 분해 결과

전체 목표: [사용자의 원래 요청 요약]

### Worker 1: [태스크명]
- 담당: [구체적 파일/모듈명]
- 출력 경로: [파일 경로]
- 컨텍스트: [이 Worker가 알아야 할 정보]

### Worker 2: [태스크명]
- 담당: [구체적 파일/모듈명]  
- 출력 경로: [파일 경로]
- 컨텍스트: [이 Worker가 알아야 할 정보]

### 공유 규약
- 네이밍 컨벤션: [함수명, 변수명 규칙]
- 인터페이스 계약: [모듈 간 공유 타입/인터페이스 정의]
- 디렉토리 구조: [프로젝트 레이아웃]
```

**중요**: 분해 결과를 사용자에게 보여주고 확인받은 후 실행한다.
"이렇게 N개의 에이전트로 나눠서 병렬 처리하려고 합니다. 진행할까요?"

---

## Phase 2: 병렬 실행

두 가지 실행 모드를 지원한다. 환경에 따라 자동 선택하거나 사용자가 지정할 수 있다.

### Mode A: `claude -p` Subprocess 병렬 스폰 (Claude Code CLI)

Claude Code 환경에서 가장 강력한 방식. 각 Worker를 독립 프로세스로 스폰한다.

#### 실행 스크립트

각 Worker에 대해 다음과 같이 백그라운드로 스폰한다:

```bash
# 작업 디렉토리 생성
WORK_DIR="$(pwd)/.agent-team/session-$(date +%s)"
mkdir -p "$WORK_DIR"/{tasks,outputs,logs}

# 공유 컨텍스트 파일 생성 (모든 Worker가 참조)
cat > "$WORK_DIR/shared-context.md" << 'SHARED_CTX'
# 공유 컨텍스트
## 프로젝트 구조
[여기에 프로젝트 구조 기술]

## 네이밍 컨벤션
[여기에 네이밍 규칙]

## 공유 인터페이스/타입
[여기에 인터페이스 정의]
SHARED_CTX

# Worker 1 스폰
cat > "$WORK_DIR/tasks/worker-1.md" << 'TASK1'
당신은 병렬 에이전트 팀의 Worker 1입니다.

## 공유 컨텍스트
[shared-context.md 내용 인라인]

## 당신의 태스크
[구체적 작업 내용]

## 출력 요구사항
- 파일을 지정된 경로에 생성하세요
- 완료 후 작업 요약을 출력하세요
TASK1

claude -p "$WORK_DIR/tasks/worker-1.md" \
  --output-format text \
  > "$WORK_DIR/outputs/worker-1-result.txt" \
  2> "$WORK_DIR/logs/worker-1.log" &
PID1=$!

# Worker 2 스폰 (동일 패턴)
claude -p "$WORK_DIR/tasks/worker-2.md" \
  --output-format text \
  > "$WORK_DIR/outputs/worker-2-result.txt" \
  2> "$WORK_DIR/logs/worker-2.log" &
PID2=$!

# Worker 3 스폰 (필요한 만큼 반복)
claude -p "$WORK_DIR/tasks/worker-3.md" \
  --output-format text \
  > "$WORK_DIR/outputs/worker-3-result.txt" \
  2> "$WORK_DIR/logs/worker-3.log" &
PID3=$!

echo "🚀 에이전트 팀 가동: Worker 1(PID:$PID1), Worker 2(PID:$PID2), Worker 3(PID:$PID3)"

# 모든 Worker 완료 대기
wait $PID1 $PID2 $PID3
echo "✅ 모든 Worker 완료"
```

#### 진행 상황 모니터링

Worker들이 실행되는 동안 주기적으로 상태를 확인한다:

```bash
# 로그 테일링으로 진행 상황 확인
for log in "$WORK_DIR/logs/"*.log; do
  echo "=== $(basename $log) ==="
  tail -5 "$log"
  echo ""
done

# 프로세스 생존 확인
for pid in $PID1 $PID2 $PID3; do
  if kill -0 $pid 2>/dev/null; then
    echo "PID $pid: 실행 중..."
  else
    wait $pid
    echo "PID $pid: 완료 (exit code: $?)"
  fi
done
```

#### 에러 처리

Worker가 실패하면:
1. 로그 파일에서 에러 원인 확인
2. 사용자에게 보고: "Worker N이 실패했습니다. [에러 요약]. 재시도할까요?"
3. 재시도 시 해당 Worker만 다시 스폰 (다른 Worker 결과는 유지)

```bash
# 개별 Worker 재시도
wait $PID1
if [ $? -ne 0 ]; then
  echo "⚠️ Worker 1 실패. 재시도 중..."
  claude -p "$WORK_DIR/tasks/worker-1.md" \
    --output-format text \
    > "$WORK_DIR/outputs/worker-1-result.txt" \
    2> "$WORK_DIR/logs/worker-1-retry.log" &
  PID1_RETRY=$!
  wait $PID1_RETRY
fi
```

### Mode B: Task 기반 순차 실행 (claude.ai / Fallback)

`claude -p`를 사용할 수 없는 환경에서는 Task 리스트 기반으로 순차 처리한다.
병렬성은 없지만, 동일한 분해 & 병합 로직을 적용하여 구조적 이점을 유지한다.

```
## Task Queue

### ✅ Task 1: [파일/모듈명]
- Status: 완료
- Output: [경로]

### 🔄 Task 2: [파일/모듈명]  
- Status: 진행 중
- Output: [경로]

### ⏳ Task 3: [파일/모듈명]
- Status: 대기
```

각 Task 완료 후 결과를 기록하고, 다음 Task로 넘어간다.
모든 Task 완료 후 Phase 3(병합)으로 진행한다.

---

## Phase 3: 결과 수집 & 병합

모든 Worker가 완료되면 결과를 수집하고 통합한다.

### 3-1. 결과 검증

```bash
# 모든 출력 파일 존재 확인
echo "📋 결과 검증:"
for output in "$WORK_DIR/outputs/"*.txt; do
  WORKER=$(basename "$output" .txt)
  if [ -s "$output" ]; then
    echo "  ✅ $WORKER: $(wc -l < "$output") lines"
  else
    echo "  ❌ $WORKER: 출력 없음"
  fi
done
```

### 3-2. 충돌 감지

병렬로 생성된 코드 간 잠재적 충돌을 확인한다:

- **파일 경로 충돌**: 두 Worker가 같은 파일을 생성/수정했는지 확인
- **네이밍 충돌**: 동일한 함수명/클래스명/변수명 사용 여부
- **import 정합성**: 모듈 간 import 경로가 올바른지 확인
- **타입 호환성**: 공유 인터페이스를 올바르게 구현했는지 확인

```bash
# 생성된 파일 목록 취합
find . -name "*.ts" -o -name "*.tsx" -o -name "*.py" -o -name "*.js" -o -name "*.jsx" \
  -newer "$WORK_DIR" | sort > "$WORK_DIR/created-files.txt"

# 중복 파일 경로 검출
sort "$WORK_DIR/created-files.txt" | uniq -d > "$WORK_DIR/conflicts.txt"
if [ -s "$WORK_DIR/conflicts.txt" ]; then
  echo "⚠️ 파일 충돌 감지:"
  cat "$WORK_DIR/conflicts.txt"
fi
```

### 3-3. 통합 작업

Orchestrator(현재 세션)가 수행하는 최종 통합:

1. **Index/Barrel 파일 생성**: 모듈을 한데 묶는 index 파일
2. **Import 연결**: 모듈 간 import문 추가
3. **공유 타입 확인**: interface/type이 일관되는지 검증
4. **통합 테스트**: 전체가 함께 동작하는지 기본 검증

```
## 통합 체크리스트
- [ ] 모든 Worker 출력 파일 존재 확인
- [ ] 파일 경로 충돌 없음
- [ ] 모듈 간 import 경로 정확
- [ ] 공유 인터페이스 구현 일치
- [ ] index/barrel 파일 생성 완료
- [ ] 기본 빌드/린트 통과
```

---

## 공유 컨텍스트 작성 가이드

Worker에게 전달하는 컨텍스트의 품질이 결과를 좌우한다.
반드시 포함해야 하는 정보:

### 필수 컨텍스트

```markdown
# 공유 컨텍스트

## 1. 프로젝트 개요
- 기술 스택: [언어, 프레임워크, 주요 라이브러리]
- 대상 런타임: [Node 버전, Python 버전 등]

## 2. 디렉토리 구조
```
src/
├── components/   ← Worker 1 담당
├── services/     ← Worker 2 담당
├── utils/        ← Worker 3 담당
└── types/        ← 공유 타입 (Orchestrator가 미리 생성)
```

## 3. 공유 인터페이스
[모듈 간 계약이 되는 타입/인터페이스 정의]

## 4. 네이밍 규칙
- 파일명: kebab-case
- 함수명: camelCase
- 타입/인터페이스: PascalCase
- 상수: UPPER_SNAKE_CASE

## 5. 코딩 스타일
[린터 설정, 포매터 설정, 또는 주요 스타일 규칙]
```

### 팁: 공유 타입을 먼저 만들어라

Worker를 스폰하기 **전에**, Orchestrator가 공유 타입/인터페이스 파일을 먼저 생성하면
Worker 간 호환성이 크게 향상된다.

```
[순서]
1. Orchestrator: types/ 디렉토리에 공유 타입 정의 생성
2. Orchestrator: 공유 컨텍스트에 타입 파일 내용 포함
3. Workers: 해당 타입을 import하여 사용
4. Merger: import 경로 및 타입 일치 확인
```

---

## 실전 예시: React 앱 컴포넌트 병렬 개발

사용자 요청: "대시보드 페이지를 만들어줘. 헤더, 사이드바, 차트 위젯, 테이블 위젯이 필요해."

### Step 1 — 분해

```
전체 목표: 대시보드 페이지 (4개 컴포넌트)

Orchestrator 선행 작업:
  - src/types/dashboard.ts (공유 타입)
  - src/types/api.ts (API 응답 타입)

Worker 1: Header 컴포넌트
  - 출력: src/components/Header.tsx
  - 컨텍스트: 네비게이션 구조, 사용자 정보 타입

Worker 2: Sidebar 컴포넌트
  - 출력: src/components/Sidebar.tsx
  - 컨텍스트: 메뉴 구조, 라우트 정보

Worker 3: ChartWidget 컴포넌트
  - 출력: src/components/ChartWidget.tsx
  - 컨텍스트: 차트 데이터 타입, recharts 사용

Worker 4: TableWidget 컴포넌트
  - 출력: src/components/TableWidget.tsx
  - 컨텍스트: 테이블 데이터 타입, 정렬/필터 요구사항
```

### Step 2 — 실행

```bash
# Orchestrator: 공유 타입 먼저 생성
mkdir -p src/types
# [타입 파일 생성 코드]

# 4개 Worker 동시 스폰
for i in 1 2 3 4; do
  claude -p ".agent-team/tasks/worker-$i.md" \
    --output-format text \
    > ".agent-team/outputs/worker-$i-result.txt" \
    2> ".agent-team/logs/worker-$i.log" &
done
wait
```

### Step 3 — 병합

```
Orchestrator 후처리:
  - src/components/index.ts (barrel export)
  - src/pages/Dashboard.tsx (모든 컴포넌트 조합)
  - import 경로 검증
  - 빌드 테스트
```

---

## 설정 & 커스터마이징

### 동시 Worker 수 제한

시스템 리소스에 따라 동시 실행 Worker 수를 조절한다:

```bash
MAX_WORKERS=4  # 기본값. 리소스에 따라 조절

# GNU parallel 사용 가능 시
find "$WORK_DIR/tasks/" -name "*.md" | \
  parallel -j $MAX_WORKERS 'claude -p {} --output-format text > {.}-result.txt 2> {.}.log'
```

### 타임아웃 설정

Worker가 무한히 실행되는 것을 방지:

```bash
TIMEOUT=300  # 5분 타임아웃

timeout $TIMEOUT claude -p "$WORK_DIR/tasks/worker-1.md" \
  --output-format text \
  > "$WORK_DIR/outputs/worker-1-result.txt" 2>&1 &
```

---

## 향후 확장: DAG 기반 의존성 실행 (v2 로드맵)

현재 버전은 완전 독립 병렬만 지원한다.
향후 DAG(Directed Acyclic Graph) 기반 의존성 실행을 추가할 예정이다:

```
# DAG 정의 예시 (향후 지원)
tasks:
  - id: types
    cmd: "공유 타입 생성"
    depends_on: []

  - id: api-service
    cmd: "API 서비스 구현"
    depends_on: [types]

  - id: ui-components
    cmd: "UI 컴포넌트 구현"
    depends_on: [types]

  - id: integration
    cmd: "통합 및 페이지 조합"
    depends_on: [api-service, ui-components]
```

DAG 실행 시 `depends_on`이 빈 태스크부터 시작하여,
의존성이 충족된 태스크를 순차적으로 스폰한다.
같은 레벨의 태스크는 병렬로 실행된다.

---

## 트러블슈팅

| 증상 | 원인 | 해결 |
|------|------|------|
| Worker가 잘못된 파일을 생성 | 컨텍스트 부족 | 공유 컨텍스트에 디렉토리 구조와 파일 경로 명시 |
| 모듈 간 타입 불일치 | 공유 타입 미정의 | Phase 2 전에 공유 타입 파일을 먼저 생성 |
| Worker가 다른 Worker 파일을 덮어씀 | 출력 경로 충돌 | 각 Worker의 출력 경로를 명확히 분리 |
| `claude -p` 명령어 없음 | Claude Code CLI 미설치 | Mode B (Task 기반)로 전환 |
| Worker 일부만 실패 | 네트워크/리소스 이슈 | 실패한 Worker만 재시도 |
