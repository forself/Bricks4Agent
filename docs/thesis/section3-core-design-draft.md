# §3 核心設計:受控自主 AI 系統(Benson 架構)— 草稿 scaffold

> 草稿。grounded on docs/designs/ControlledAutonomousAISystemTechnicalDesign.md(Benson 原設計)。
> 本章呈現「主體 = Benson 的受控自主架構」;§4-7 則是作者把這套原則落地成可運維/安全/被真實應用(見 3.5 銜接)。

## 3.1 設計哲學
> 核心目標**不是最大化 AI 自由,而是建立一個「可限制、可審核、可撤權、可重放、可替換」的受控自主系統**。

對控制平面而言,**人類與 AI 都是 `Principal`** —— 授權、審批、稽核、撤權、證據要求只看角色/能力/任務/信任錨,不看主體是不是人。

## 3.2 六大設計原則
1. **統一主體模型**:人 + AI 都是 Principal、同一套授權/審批/稽核/撤權。
2. **零信任主體**:不預設任何主體自我節制;只在「顯式授權的能力面」內動作。
3. **不可迴避節點**:所有任務提交/能力發放/執行請求/上下文寫入/升權/結果提交都**必須過單一控制平面**。
4. **狀態外部化**:工作節點不持有長期記憶;上下文/批准結果/工作記憶/稽核證據都屬系統。
5. **角色先於自由**:主體不能用自然語言自行詮釋權限;自由僅存在於角色允許的行為空間。
6. **能力目錄即憲法**:Capability Catalog 是唯一合法能力面;**未列入目錄的能力 = 不存在**。

## 3.3 七層邏輯架構
```
1 使用者/外部事件層
2 控制平面層(PEP)
3 政策與審批層(PDP)
4 共享上下文與狀態層
5 代理容器層(planner、role runtime、request-only、no direct tool)
6 執行配接層(doc/repo/build/deploy/MCP-compatible tools)
7 證據與觀測層(append-only events、replay artifacts、trace correlation)
```

## 3.4 治理模型(借用存取控制的 PEP/PDP 範式)
- **控制平面 = PEP**(Policy Enforcement Point):Task API、Session API、Routing、**Token Issuer、Revocation**。
- **政策審批 = PDP**(Policy Decision Point):PDP、**Risk Engine**、Trust Anchor。
- **代理容器**:planner + role-specific runtime,**只能 request、不能直接碰 tool/provider/部署/模型**。
- = 把成熟的「PEP/PDP/能力授權」存取控制範式,系統化套用到**自主 AI 主體**(把 AI 當受治理的 Principal)。

## 3.5 與作者貢獻的銜接(§3 是設計、§4-7 是落地)
| Benson 的原則(§3) | 作者如何落地(§4-7) |
|---|---|
| Token Issuer / Revocation(3.4) | **§5c 安全管線**:scoped-token + epoch 撤權在 wire 層強制執行 |
| 審批(PDP) | **§5-ext**:approval 裝飾鏈(template+time+multisig)擴充決策維度 |
| 不可迴避節點 + 狀態外部化 | **§6 運維韌性**:讓「單一控制平面」可監控/自癒/緊急撤權(可運維) |
| 能力目錄 + 執行配接 + request-only 容器 | **§4 worker 生態**:7 個 worker 都消費 ApprovedRequest、遵守能力契約 |
| 受治理 Principal 模型 | **§7 應用**:真錢多用戶交易驗證 principal/能力模型在最嚴苛場景有效 |

→ **本報告的論點**:Benson 設計了「可治理」的受控自主架構(§3);作者在其上建構了讓它「**可運維、決策更豐富、安全、且被真實多用戶應用**」所需的幾乎所有層(§4-7),把受治理原型推進為**生產級受治理 AI 操作平台**。

---
**待擴**:① 3.2 各原則補「為什麼」+ 與無治理 agent 的對比;② 3.4 補 PEP/PDP/PAP 學理(XACML)出處;③ 3.3 補各層的資料流範例。
