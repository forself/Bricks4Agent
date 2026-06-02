# Edge 經濟學:為什麼這個 edge 存在 —— 框架 + funding carry 完整拆解

> 2026-06-02。把「會跑回測的工程師」升級成「量化」的那條線:對每條策略,
> 你要答得出「誰在另一邊、為什麼這個錯誤定價存在、誰在付錢給你、為什麼他們不停、
> 容量多大、什麼條件下它死」。回測告訴你 edge **有沒有**;edge 經濟學告訴你它**為什麼有、會不會持續**。

---

## 可重用框架(對每條策略問這 6 題)

1. **機制 / 誰在另一邊**:你賺的錢從誰口袋出來?那個對手方是誰、為什麼願意當對手?
2. **錯誤定價的來源**:三選一(常常混合)——
   - **風險溢酬(risk premium)**:你被付錢去承擔別人不想要的風險 → **durable**(承擔風險的補償不會消失)。
   - **行為異常(behavioral)**:某些人系統性地非理性 → 會隨市場成熟/學習衰減。
   - **結構/流動(structural/flow)**:有人被迫交易(法規、贖回、再平衡、清算)→ 看 flow 持續性。
3. **為什麼沒被套利掉**:套利者的障礙是什麼(資本、風險、複雜度、限制、timing 不確定)?
4. **容量天花板**:多少資本後你會推動價格 / 把溢酬吃光?
5. **什麼殺死它**:regime、**策略自身擁擠**(太多人做同一招)、結構改變。
6. **分類定論**:它主要是哪一種(1 的三選一)?→ 這決定你該給它多少信任 + 多長壽命。

**鐵律**:看起來像「無風險現金流」的東西,如果你**不承擔某個相關風險就拿不到它**,那它就是**風險溢酬、不是免費午餐**。

---

## 範例拆解:橫截面 funding carry

策略:每日 long 最低 funding 幣 / short 最高 funding 幣(dollar-neutral)。拆成 carry 腿 + price 腿。

### Carry 腿(回測看起來 Sharpe ~10 的那塊)

- **機制**:永續合約 funding 是每 8h 多空之間的轉帳,用來把永續價格錨回現貨。永續高於現貨(多頭激進)→ funding 正 → **多頭付錢給空頭**。
- **誰付錢給你**:你 short 高 funding 幣 → 收**擁擠多頭**付的 funding。那些多頭是誰?**想要槓桿多頭曝險的散戶/投機客**,願意付溢酬(funding)來持有槓桿長倉、不用一直 roll。你 long 負 funding 幣 → 收**擁擠空頭**付的錢。
- **為什麼有溢酬**:這是**庫存/風險溢酬 + 槓桿需求溢酬**。總得有人接擁擠多頭的反面——做市商/套利者 short 永續(收 funding)+ 買現貨對沖(cash-and-carry),他們承擔 basis 風險、佔用資本、軋空爆掉的風險。funding **就是他們的補償**。你當 carry harvester = 被付錢去提供這個沒人要的反面。
- **🔑 最重要的一課(直接打臉「Sharpe 10」)**:carry 腿單獨看 Sharpe 10 是**測量假象**——你**不持倉就收不到 funding**,持倉就吃 price 腿的軋空風險。carry 正是承擔那個風險的補償。**把一個現金流跟它分不開的風險拆開算,才得出假的 Sharpe 10。誠實的 Sharpe 是合併的 ~1.0。** 這就是框架鐵律的活教材:不承擔風險拿不到 → 是風險溢酬。

### Price 腿(擁擠回歸,Sharpe ~0.85)

- **機制**:高 funding = 擁擠多頭。擁擠的槓桿倉很脆——一個小跌觸發清算 → 瀑布 → 該幣跑輸。所以高 funding 幣有負的預期前向報酬(人群被軋出)。
- **誰在另一邊**:**人群自己**(過槓桿的散戶多頭,被清算)。你賺的是他們**被迫去槓桿**的錢。
- **為什麼沒被套利掉**:(a) **timing 不確定**——擁擠幣可以續漲一陣才崩,短期會把你輾過(這就是你量到的 maxDD 高);(b) 要承擔軋空風險。混合**行為(FOMO 擁擠反覆出現)+ 結構(槓桿/清算機制放大)**。

### 為什麼它能持續、什麼殺死它

- **持續**:只要 crypto 有結構性的散戶/槓桿**做多需求**(牛市/正常時持續),funding 就正、就有人付。
- **🔑 edge 經濟學「預測」了你量到的 regime 依賴**:熊市散戶槓桿需求萎縮 → funding 壓縮 → 溢酬縮水甚至倒貼。**這就是為什麼你回測到 2022 −45%。** 你不是「發現一個 bug」,是這條 edge 的經濟本質決定它偏多頭 regime。能事先講出這點 = 量化成熟度。
- **殺死它**:① 熊/低槓桿 regime(已量到);② **策略自身擁擠**——太多基金做橫截面 funding → 溢酬被吃光;③ 結構改變(交易所改 funding 機制、crypto 機構化散戶槓桿變少、法規限槓桿);④ 尾部:被 short 的高 funding 幣突然軋空、回歸前先吃大虧。

### 分類定論

**主要是風險溢酬(carry = 被付錢承擔庫存/軋空風險 + 提供沒人要的反面)+ 部分行為/結構異常(price 腿 = 人群清算瀑布)。** 風險溢酬那塊 durable;異常那塊隨擁擠/成熟衰減。→ 所以它**配當小權重 diversifier**(賺風險溢酬 + 一點行為 alpha)、**不配當主引擎**(異常會衰減、且偏多頭 regime)。

### 容量

edge 在大 10 流動幣(你量到)。BTC/ETH 永續深(數十億 ADV),個人跑到數百萬美元不會推動 funding;機構級資本越多、溢酬越壓縮。→ **個人容量充足、機構級會壓縮**。

---

## 整本 live 書的 edge 經濟學(以程式碼實際 Category 為準)

> live 清單(strat-validate liveStrats):`rsi_stoch, mfi, donchian_fade_ls, dual_mom_ls, ts_momentum, ma_regime_trend, decorr4_ls`。
> 攤開後分兩大家族 + 一個組合。**設計上很去相關(趨勢 + MR + fade),但各條 edge 的「硬度」差很多。**

| 策略 | 實際分類 | 誰付錢給你 | 來源 | 什麼殺死它 | edge 硬度 |
|---|---|---|---|---|---|
| **ts_momentum** | Trend(vol-gated) | 對新資訊反應不足者 / 末段 FOMO 追高者 | 行為 underreaction + **動量崩盤尾部風險溢酬** | 急反轉(momentum crash)、橫盤 | **最硬**(跨資產百年最穩異常 + vol 閘門防崩) |
| **ma_regime_trend** | Trend(long-biased) | 同上;緩漲段留得住 | 同上 | 頂部急轉(早離場緩解)、假突破洗 | 硬(long-biased 不吃做空逆風) |
| **dual_mom_ls** | Momentum(多空) | 同上 | 同上 | 同上 + **做空腿吃 crypto 上行逆風** | 中(多空在 crypto 比 long-biased 弱) |
| **donchian_fade_ls** | MeanReversion(ADX 閘) | 在區間邊緣被假突破洗的突破客 | 行為(假突破)+ 區間內多數突破會失敗 | **趨勢市**(ADX 閘已擋掉一部分) | 中(ADX 閘是它比裸 MR 強的關鍵) |
| **mfi** | MeanReversion(量價) | 恐慌/被迫賣方(量能確認衰竭) | 行為過度反應 + 流動性提供 | **崩盤接刀**、擁擠 | 弱-中(量價比純價 RSI 稍好) |
| **rsi_stoch** | MeanReversion | 恐慌/被迫賣方 | 行為過度反應 + 流動性提供 | **崩盤接刀(你自己的「裸 MR 爆倉」鐵律)** | **最弱**(最多人用的指標、被高度競爭) |
| decorr4_ls | 組合(VPS 配、非 repo) | = 成員加權 | = 成員 | = 最弱成員 + 相關性上升 | 看成員(去相關是 portfolio 級好處) |

**讀法**:
- **趨勢家族(ts_momentum / ma_regime / dual_mom)edge 最硬**——有人**結構性反應不足** + 你被付錢承擔**動量崩盤**尾部風險。這是百年跨資產最穩的異常。long-biased(ts/ma)比多空(dual)在 crypto 強。
- **MR 家族(rsi_stoch / mfi / donchian_fade)edge 最脆**——賺的是恐慌者的短期錯誤 + 接刀補償,**沒有人結構性付你錢**。`donchian_fade` 靠 ADX 閘避開趨勢、最防得住;`rsi_stoch`/`mfi` 用最多人看的指標、最該被懷疑。
- 你的 bear audit 量到的(oi_contrarian / 某 tsmom 變體熊市失敗)跟這張表一致:**MR 與多空在熊/急轉 regime 最先死。**

### rsi_stoch 真錢前必確認清單(它 edge 最弱、最該嚴控)

- [ ] **long-only?**(sell = 出場、**不是放空**)。放空 crypto 超買 rip = 爆倉路徑;long-only buy-the-dip 才活。**確認 live watch mode。**
- [ ] **硬止損?** MR 失敗模式 = 跌了再跌;沒止損的 buy-the-dip 崩盤歸零。確認 `_protectionConfig.InitialSlPct` / bracket SL 有開。
- [ ] **bear/急跌 gate?** 它的死法就在崩盤接刀。考慮 BTC regime 閘(同 funding carry 的 200d-SMA 外生閘),熊市縮倉/關。
- [ ] **小倉**:edge 最弱 → 權重該最小、別當主引擎。
- [ ] **擁擠監測**:RSI/Stoch 全世界在用,定期看 live vs backtest 勝率偏離(shadow 週報)——衰減先兆。

→ 文獻:Jegadeesh-Titman(1993, 動量)、AQR carry/value/momentum 跨資產、López de Prado《Advances in Financial ML》(回測嚴謹度、你已在用他的方法)。**知道 edge 在文獻哪裡 = 答辯站得住。**

---

## 給使用者的下一步練習

對你**每一條真錢/shadow 策略**,寫一頁這個 6 題。寫不出「誰付錢 + 為什麼不停」的,就**別放真錢**——那條很可能是過擬合或借來的 beta。這比再跑一條新策略更補你的量化缺口。

---

## 附錄:live 配重診斷 + 按 edge 硬度建議重排(2026-06-02,只給數字、未動 live)

查 VPS live watchlist(shadow=0 active=1)實際 budget,對照 edge 硬度,發現**表面權重藏了錯配**。

**`decorr4_ls`(budget 60、全書最大注)拆解 = NetWeightedEnsemble**:
`dual_mom_ls 0.38 / dual_thrust 0.32 / bb_revert_ls 0.19 / fib_retrace_ls 0.10`。

**真實單策略曝險(獨立 watch + decorr4 內含)**:

| 策略 | 獨立 | decorr4 內含 | 真實合計 | edge 硬度 |
|---|---|---|---|---|
| dual_mom_ls | 42 | 60×0.38≈23 | **~65** ⬆全書最大 | 中(多空逆風) |
| dual_thrust | 33 | 60×0.32≈19 | ~52 | 硬-ish |
| ma_regime_trend | 50 | — | 50 | 硬 |
| ts_momentum | 40 | — | 40 | **最硬** |
| mfi | 25 | — | 25 | 弱 |
| bb_revert_ls | 0 | 60×0.19≈11 | ~11 | **弱(沒過 pool t-stat)** |
| fib_retrace_ls | 0 | 60×0.10≈6 | ~6 | 弱(輸 fib_confluence) |

**兩個錯配**:① decorr4 不是新去相關 edge、是把已超配的 dual_mom/dual_thrust **再加碼** → 真實最大注是「中」硬度的 dual_mom、最硬的 ts_momentum 只第 4。② decorr4 約 30%(bb_revert+fib_retrace,~17 budget)押在沒紮實 edge 故事的腿上(bb_revert 還是你自己「t-stat 不顯著紀律救命案例」)。

**建議重排(gross 同等或更低、按硬度、砍無 edge 腿;數字僅供參考)**:

| 策略 | 現真實 | 建議 | 理由 |
|---|---|---|---|
| ts_momentum | 40 | **55** | 最硬 + vol 閘存活 → 該最大 |
| ma_regime_trend | 50 | 50 | 硬 long-bias、已合適 |
| dual_thrust | ~52 | 45 | 硬-ish、略降避免過度集中 |
| dual_mom_ls | ~65 | 40 | 中(多空逆風)、不該全書最大 |
| mfi | 25 | 25 | 弱→小 + long_only(去相關值、留) |
| bb_revert_ls | ~11 | **0** | 沒過 t-stat、砍 |
| fib_retrace_ls | ~6 | **0** | 輸 fib_confluence、砍 |
| **gross** | ~249 | **~215** | 更保守、砍無 edge 腿 |

**實作選項**:(a) **拆掉 decorr4** → 4 條改成 hardness-weighted 獨立 watch(最透明、消除重複計算,代價=失去 ensemble 的訊號 netting/降 churn);或 (b) 保留 ensemble 但**只放非重複的硬腿**(拿掉 bb_revert/fib_retrace)+ 調獨立 budget 對到真實目標。

**紀律**:這是「**降風險 + 提升配置品質**」(gross 沒升、砍掉無 edge 腿 = de-risk),不是加碼;但動 live 真錢配重仍**留使用者手按**([[feedback_derisk_now_addrisk_waits]])。
