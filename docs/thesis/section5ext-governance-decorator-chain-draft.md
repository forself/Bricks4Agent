# §5-ext 治理擴充:可組合的審批裝飾鏈(作者貢獻)— 草稿 scaffold

> 草稿。作者貢獻章節,grounded on Program.cs 225-241 + git blame(3 decorators + IApprovalService 皆 AnthonyLee)。

## 動機
- Benson 的核心提供「基礎審批」(approve / reject + 持久化)。
- 但生產級治理需要更細的決策維度:
  - 不同 capability 需要不同審批規則(**template**)
  - 權限/審批應可限定時間窗(**time-windowed**)
  - 高風險操作需多人簽核(**multi-sig**)
- 作者用 **decorator pattern** 在不改 Benson 核心的前提下,把這三個維度**可組合地**疊上審批流。

## 設計:四層 decorator chain(Program.cs 225-241)
```
MultiSig → Time → Template → 原 ApprovalService(base)
```
- **base `ApprovalService`**:基礎 approve/reject + BrokerDb 持久化。
- **+ `TemplateAwareApprovalService`**:依 `ApprovalTemplate` 匹配規則(不同操作不同審批要求);注入 `ApprovalTemplateMatcher` + `IAuditService`。
- **+ `TimeAwareApprovalService`**:時間窗 ACL(`TimeAclService`)— 權限/審批限定時段。
- **+ `MultiSigApprovalService`**:高風險需 N 簽;注入 `BrokerDb` + `IAuditService`。
- 註冊在 `AddSingleton<IApprovalService>(sp => …)` 內由內而外組裝;對呼叫端透明(仍只看到 `IApprovalService`)。

## 對應專題工作原則
- **擴充點順勢加 > 推倒重來**:decorator 疊在 `IApprovalService` 介面上、**完全不改 Benson 的 base ApprovalService**。
- **可組合**:每層獨立、可加可減、順序明確(MultiSig 最外、最後把關)。
- **可審核**:每層注入 `IAuditService` → 每個治理決策都留證據(呼應核心「可審核」原則)。
- **可解釋**:每層只負責一個治理維度(單一職責),整條鏈一眼看懂「審批要過哪幾關」。

## 貢獻定位
- 作者擴充的是**「治理邏輯本身」**(增強審批的決策維度),層次比 §6(觀測治理狀態)更深。
- 與 §6 並列為作者的兩大平台級貢獻:**§5-ext = 強化治理「決策」、§6 = 讓系統「可運維」**。
- git blame:`MultiSigApprovalService` / `TemplateAwareApprovalService` / `TimeAwareApprovalService` / `IApprovalService` 皆 AnthonyLee。

---
**待擴**:① 各 decorator 內部邏輯片段(template 怎麼 match、time 窗判定、multi-sig 計數);② 與相關工作對比(RBAC/ABAC、policy engine 如 OPA);③ 一個請求穿過四層的 sequence diagram。
