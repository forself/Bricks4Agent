# 三賢者系統 (Three Sages System)

## 概述

三賢者系統是一個多層級審核機制，用於確保程式碼品質、安全性和架構一致性。
系統由三個獨立的 AI 審核者組成，各自負責不同的審核面向。

## 架構設計

```
┌─────────────────────────────────────────────────────────────────┐
│                        提交審核請求                              │
└─────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────┐
│                      審核調度器 (Scheduler)                      │
│  - 接收審核請求                                                  │
│  - 分配給三賢者                                                  │
│  - 收集審核結果                                                  │
│  - 產生最終報告                                                  │
└─────────────────────────────────────────────────────────────────┘
                                │
            ┌───────────────────┼───────────────────┐
            ▼                   ▼                   ▼
┌───────────────────┐ ┌───────────────────┐ ┌───────────────────┐
│   🛡️ 安全賢者     │ │   🏗️ 架構賢者     │ │   ✨ 品質賢者     │
│   (Security)      │ │   (Architecture)  │ │   (Quality)       │
│                   │ │                   │ │                   │
│ - 資安漏洞檢測    │ │ - 設計模式審查    │ │ - 程式碼風格      │
│ - OWASP Top 10    │ │ - SOLID 原則      │ │ - 測試覆蓋率      │
│ - 敏感資料處理    │ │ - 相依性管理      │ │ - 文件完整性      │
│ - 加密實作審查    │ │ - 效能考量        │ │ - 可維護性        │
│ - 輸入驗證        │ │ - 擴展性          │ │ - 命名規範        │
└───────────────────┘ └───────────────────┘ └───────────────────┘
            │                   │                   │
            └───────────────────┼───────────────────┘
                                ▼
┌─────────────────────────────────────────────────────────────────┐
│                      審核結果彙整                                │
│  - 問題嚴重度分類 (Critical/High/Medium/Low/Info)               │
│  - 修改建議                                                     │
│  - 通過/駁回決定                                                │
│  - 學習記錄                                                     │
└─────────────────────────────────────────────────────────────────┘
```

## 三賢者職責

### 🛡️ 安全賢者 (Security Sage)

專注於程式碼的安全性審查：

| 檢查項目 | 說明 |
|---------|------|
| SQL Injection | 檢查 SQL 查詢是否使用參數化 |
| XSS | 檢查輸出是否正確編碼 |
| CSRF | 檢查是否有 CSRF 保護 |
| 認證漏洞 | 密碼處理、Session 管理 |
| 授權漏洞 | 權限檢查、存取控制 |
| 敏感資料 | 加密、遮蔽、日誌安全 |
| 相依套件 | CVE 漏洞檢查 |
| 密碼學 | 正確使用加密演算法 |

### 🏗️ 架構賢者 (Architecture Sage)

專注於程式碼的架構設計：

| 檢查項目 | 說明 |
|---------|------|
| SOLID 原則 | 單一職責、開放封閉等 |
| 設計模式 | 適當使用設計模式 |
| 分層架構 | 正確的層級分離 |
| 相依性 | 依賴注入、循環依賴 |
| 介面設計 | API 一致性、版本相容 |
| 效能 | 演算法複雜度、資源使用 |
| 擴展性 | 水平/垂直擴展能力 |
| 可測試性 | 單元測試友善設計 |

### ✨ 品質賢者 (Quality Sage)

專注於程式碼的品質和可維護性：

| 檢查項目 | 說明 |
|---------|------|
| 命名規範 | 變數、函數、類別命名 |
| 程式碼風格 | 一致的格式和縮排 |
| 註解品質 | 有意義的註解、文件 |
| 複雜度 | 圈複雜度、認知複雜度 |
| 重複程式碼 | DRY 原則 |
| 錯誤處理 | 完整的例外處理 |
| 測試覆蓋 | 單元測試、整合測試 |
| 可讀性 | 程式碼清晰易懂 |

## 審核流程

### 1. 提交審核

```typescript
interface ReviewRequest {
  id: string;
  type: 'commit' | 'pr' | 'file' | 'module';
  language: string;
  files: FileChange[];
  context?: string;
  priority: 'normal' | 'urgent';
  requestedBy: string;
  timestamp: Date;
}

interface FileChange {
  path: string;
  content: string;
  diff?: string;
  language: string;
}
```

### 2. 分配審核

```typescript
interface ReviewAssignment {
  requestId: string;
  sage: 'security' | 'architecture' | 'quality';
  assignedAt: Date;
  deadline: Date;
  status: 'pending' | 'in_progress' | 'completed';
}
```

### 3. 審核結果

```typescript
interface ReviewResult {
  requestId: string;
  sage: string;
  status: 'approved' | 'rejected' | 'needs_changes';
  findings: Finding[];
  summary: string;
  score: number; // 0-100
  reviewedAt: Date;
}

interface Finding {
  id: string;
  severity: 'critical' | 'high' | 'medium' | 'low' | 'info';
  category: string;
  title: string;
  description: string;
  location: {
    file: string;
    line?: number;
    column?: number;
  };
  suggestion?: string;
  codeExample?: string;
  references?: string[];
}
```

### 4. 最終決定

```typescript
interface FinalVerdict {
  requestId: string;
  decision: 'approved' | 'rejected' | 'conditional';
  overallScore: number;
  sageResults: {
    security: ReviewResult;
    architecture: ReviewResult;
    quality: ReviewResult;
  };
  mustFix: Finding[];      // 必須修正
  shouldFix: Finding[];    // 建議修正
  mayFix: Finding[];       // 可選修正
  summary: string;
  decidedAt: Date;
}
```

## 評分機制

### 嚴重度權重

| 嚴重度 | 扣分 |
|--------|------|
| Critical | -25 |
| High | -15 |
| Medium | -8 |
| Low | -3 |
| Info | 0 |

### 通過標準

| 條件 | 要求 |
|------|------|
| 總分 | ≥ 70 分 |
| Critical 問題 | 0 個 |
| High 問題 | ≤ 2 個 |
| 安全賢者評分 | ≥ 60 分 |

### 條件通過

- 總分 60-69 分
- 無 Critical 問題
- High 問題 ≤ 5 個
- 需在指定時間內修正

## API 設計

### 提交審核

```
POST /api/review/submit
```

```json
{
  "type": "pr",
  "language": "typescript",
  "files": [
    {
      "path": "src/auth/login.ts",
      "content": "...",
      "diff": "..."
    }
  ],
  "context": "New login flow with MFA",
  "priority": "normal"
}
```

### 查詢審核狀態

```
GET /api/review/{requestId}/status
```

### 取得審核結果

```
GET /api/review/{requestId}/result
```

### 申請複審

```
POST /api/review/{requestId}/appeal
```

## 學習機制

系統會記錄所有審核結果，用於：

1. **模式識別**：常見問題類型統計
2. **團隊改進**：針對性培訓建議
3. **規則調整**：根據誤判率調整規則
4. **知識庫**：建立最佳實踐案例庫

## 整合方式

### Git Hook 整合

```bash
# pre-commit hook
three-sages review --type=commit --quick

# pre-push hook
three-sages review --type=pr --full
```

### CI/CD 整合

```yaml
# GitHub Actions
- name: Three Sages Review
  uses: component-library/three-sages-action@v1
  with:
    threshold: 70
    fail-on-critical: true
```

### IDE 整合

- VS Code Extension
- JetBrains Plugin
- 即時審核建議

## 配置選項

```yaml
# .three-sages.yml
version: 1

rules:
  security:
    enabled: true
    severity_threshold: medium
    ignore_patterns:
      - "**/test/**"
      - "**/*.test.ts"

  architecture:
    enabled: true
    max_complexity: 15
    max_file_lines: 500

  quality:
    enabled: true
    min_coverage: 80
    require_docs: true

scoring:
  pass_threshold: 70
  conditional_threshold: 60

notifications:
  slack: "https://hooks.slack.com/..."
  email: ["team@example.com"]
```

## 下一步實作

1. **核心引擎**：審核調度器和結果彙整
2. **三賢者 AI**：各領域專家 Prompt 設計
3. **API 服務**：RESTful API 實作
4. **Web 介面**：審核結果查看和管理
5. **整合工具**：Git Hook、CI/CD Action
