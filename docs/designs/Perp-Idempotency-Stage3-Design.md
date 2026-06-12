# Stage ③ perp idempotency 設計(自動移轉的真錢安全核心)

> 2026-06-12。自動移轉 roadmap 階段③。最關鍵最危險的一塊(紀律:真錢冪等做錯比停機慘)。

## Survey 現況
- **perp 主下單路徑全裸**:`TradingPerpetualHandler.PlaceOrder` 的 `OrderId = perp-{Guid}`(隨機、非冪等)、不讀 `client_order_id`;`BingxPerpetualClient.PlaceOrderAsync` 的 qs **沒有 clientOrderID**。
- **既有 prot- clientOrderId**(`AutoTraderService` ~1351)= **timestamp、非冪等**,且 handler/BingX 沒接 = **死碼**。
- **spot 有 dedup**(`client_order_id` → `_db.GetOrder` 查重),但 **perp orders 根本不入庫**(`TradingDbStorage` 只有 spot `SaveOrder`/`GetOrder` + `SaveTrade`;perp 只有 realized PnL 進 trades)→ **不能直接抄 spot**。

## 關鍵結論:robust 冪等「必須交易所當仲裁」
純本機 dedup 有解不掉的 **send/record race**:
- 存 key → 未送就 crash → 新主見 key → 跳過 → **漏單**(真錢該下沒下)。
- 送 BingX → 未存就 crash → 新主沒見 key → **重送**(雙下真錢單)。

→ 本機 store 解不掉。**robust = deterministic clientOrderID → BingX 對重複拒單(交易所是 order 的真相 + 仲裁者)+ failover 後查交易所對賬。**

## 設計(三層 + 對賬)
1. **deterministic key**(AutoTrader 生、含足夠唯一性避免碰撞):
   - 開倉:`op-` + short_hash(`owner|exchange|symbol|side|strategy|barCloseTime`)
   - 平倉:`cl-` + short_hash(`owner|exchange|symbol|positionOpenTime|reason`)
   - 截到 ≤36 字元(BingX clientOrderID 上限)。同信號/同平倉意圖 → 同 key(failover 重評也同 key)。
2. **BingX clientOrderID**:`PlaceOrderAsync` 送 `qs["clientOrderID"] = order.OrderId` → BingX 對重複 clientOrderID 拒單 = 主要冪等機制。
3. **(階段④)failover 對賬**:新主接手 → 對近期 in-flight key 查交易所 `GetOrderStatus`/positions → 已成交標 done、未成交才補。

## 安全實作計畫(真錢紀律)
- BingX clientOrderID **送出 gate 在 env flag `PERP_CLIENT_ORDER_ID`(預設 off)** → 啟用前對現行真錢單**零影響**。
- deterministic key + threading 先做(只設本機 OrderId label、無行為改變)。
- 啟用後 **人工小額真錢驗證**:同一 clientOrderID 送兩次 → 確認 BingX 第二次**拒單**(驗 param 名正確 + dedup 行為)。**這步只能真錢測**(shadow 不送真單)。
- 驗過 → 正式啟用 → 接階段④對賬 → 才到階段⑤暖備。

## 開放問題(實作/驗證時解)
1. **BingX swap v2 `trade/order` 的 clientOrderID 確切 param 名 + dedup 窗口長度**(成交後多久 key 可重用?)→ 真錢小額測。
2. **平倉 key 的 `positionOpenTime` 穩定來源**:要從 watch/position state 取「該倉的穩定身份」(不能用當下時間,否則 failover 不一致)。
3. 碰撞檢查:`barCloseTime` 同一根 K 棒同信號 → 同 key(要),但「同根 K 棒兩個不同意圖」要確保 key 不撞(side/strategy 已區分)。

## 與 roadmap 關係
- 本設計 = 階段③。**③ 機制(key+threading+BingX param、gated)可現在實作 + build/unit 驗**;**③ 行為驗證(BingX dedup)需小額真錢**;之後接 ④ 對賬 → ⑤ 暖備見證 → ⑥ 演練。
- 不改 Benson 核心:全在作者的交易延伸(AutoTrader / TradingPerpetualHandler / BingxPerpetualClient)。
