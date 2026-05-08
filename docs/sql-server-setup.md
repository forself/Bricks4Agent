# SQL Server in Docker — 設定 + 連線指南

把 SQL Server Express edition 跑在 Docker 上、跟 broker / Prometheus / Grafana 共用同一個 `trading-net` 網路。

**現階段定位**：純 infrastructure。broker 還是用 SQLite。等你熟悉 SQL Server 之後再決定要不要把 broker 遷移過去（那是另一輪 ~4h 工作：BaseOrm dialect + 資料遷移腳本）。

## 1. 設 SA 密碼

SQL Server SA 帳號的密碼規則：**≥ 8 字元、需含大寫 + 小寫 + 數字 + 特殊字符**。

**選 a (推薦) — Docker secret**：

```bash
mkdir -p tools/secrets
notepad tools/secrets/mssql_sa_password.txt
# 貼一行密碼進去、存檔
# Windows 注意：notepad 會多一個 BOM、用 VSCode 開存成 UTF-8 without BOM 比較保險
```

或從 PowerShell 直接寫（會自動 trim trailing newline、沒問題）：

```powershell
echo "YourStr0ngPwd!" > tools/secrets/mssql_sa_password.txt
```

**選 b — Plain env**：

如果你不想用 secret、改在 `tools/.env.trading` 加：

```
MSSQL_SA_PASSWORD=YourStr0ngPwd!
```

然後編輯 `tools/compose.sqlserver.yml`、把 entrypoint 裡的「elif」拿掉、換成直接 `MSSQL_SA_PASSWORD: "${MSSQL_SA_PASSWORD:-ChangeMe_Insecure_123!}"` 在 environment block。

## 2. 啟動

```bash
docker compose --env-file tools/.env.trading \
  -f tools/compose.trading.yml \
  -f tools/compose.sqlserver.yml \
  up -d sqlserver
```

第一次啟動 SQL Server 慢、約 30-60 秒。

驗證起來了：

```bash
docker logs b4a-sqlserver | grep "SQL Server is now ready"
# 看到 "SQL Server is now ready for client connections" 就成功
```

`sqlserver-init` 一次性容器會在 sqlserver healthy 後 run、建好 `B4A` 資料庫：

```bash
docker logs b4a-sqlserver-init
# 看到 "B4A database created." 或 "B4A database already exists." 都 OK
```

## 3. 連線

### 從 host 連（推薦工具：Azure Data Studio）

| 欄位 | 值 |
|---|---|
| Server | `127.0.0.1,1433`（注意是逗號、不是冒號）|
| Authentication type | SQL Login |
| User name | `sa` |
| Password | （你設定的）|
| Database | `B4A`（先連 master 也行）|
| Encrypt | Optional（dev）/ Mandatory（prod）|
| Trust server certificate | true |

工具選擇：
- **Azure Data Studio**（微軟自家、跨平台、推薦）：https://azure.microsoft.com/en-us/products/data-studio
- **DBeaver**（萬用 DB 工具，用得到 PostgreSQL 之後也用同一套）
- **VS Code 「SQL Server」extension**（mssql 官方擴充）
- **SSMS**（Windows 限定、最完整、稍重）

### 從容器內連（之後 broker 遷移時用）

Connection string（C# / .NET 格式）：

```
Server=sqlserver,1433;Database=B4A;User Id=sa;Password=YourStr0ngPwd!;TrustServerCertificate=true;
```

注意：
- 從 broker 容器看 sqlserver 是 hostname `sqlserver`、port `1433`（容器內網路）
- 從 host 看是 `127.0.0.1,1433`（port mapping 出來）
- `TrustServerCertificate=true` 是 dev 環境必要、SQL Server image 沒裝 trusted cert
- prod 上要換 `Encrypt=true;TrustServerCertificate=false` + 自己 mount cert

### sqlcmd 直連測試

```bash
docker exec -it b4a-sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "YourStr0ngPwd!" -C \
  -Q "SELECT @@VERSION; SELECT name FROM sys.databases;"
```

預期看到：
- SQL Server 2022 版本字串
- 5 個資料庫：master, tempdb, model, msdb, **B4A**

## 4. 常用 maintenance 命令

```bash
# 進 sqlcmd interactive
docker exec -it b4a-sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "YourStr0ngPwd!" -C

# 備份 B4A db 到 host（先在 sqlserver 內 backup、再 cp 出來）
docker exec b4a-sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "YourStr0ngPwd!" -C \
  -Q "BACKUP DATABASE B4A TO DISK = '/var/opt/mssql/data/B4A.bak'"
docker cp b4a-sqlserver:/var/opt/mssql/data/B4A.bak ./B4A-backup-$(date +%Y%m%d).bak

# 看資源使用
docker stats b4a-sqlserver

# 完全清乾淨重來（會刪掉所有資料）
docker compose -f tools/compose.trading.yml -f tools/compose.sqlserver.yml down sqlserver sqlserver-init
docker volume rm b4a-trading_sqlserver-data
```

## 5. 容量規劃（Express edition 限制）

| 限制 | Express | Standard | Developer (dev only) |
|---|---|---|---|
| DB 大小 | **10 GB** | 524 PB | 524 PB |
| RAM | **1.4 GB** | 128 GB | 128 GB |
| CPU socket | **1** | 4 sockets | 限機器 |
| 商業使用 | ✓ 免費 | 要授權 | ✗ 只能 dev |

我們 PoC 階段（broker.db 約 50MB-200MB）Express 綽綽有餘。

**你帳戶 trade 一年下來大概**：
- orders 表：~10K 筆/月 × 12 = 120K → ~50MB
- trades 表：~30K 筆/月 × 12 = 360K → ~150MB
- equity_curve / metrics 紀錄：~1MB/天 × 365 = 365MB
- alert_events：~少量

合計第一年 < 1GB、Express 可以撐 10 年。

## 6. 下一步（broker 真要遷移時）

順序大概是：

1. **驗 BaseOrm 有沒有 SQL Server dialect**：grep `SqlServer` 在 BaseOrm csproj 看；如果沒、就要寫 ~3-4h adapter（或換 EF Core / Dapper）
2. **改 broker connection string**：`Database__Path` env 改成 `Database__ConnectionString` + 用 SQL Server 連線字串
3. **跑一次性 migration 腳本**：把 SQLite 所有表 dump 出來、用 BCP 或 sqlcmd 灌進 SQL Server B4A db
4. **雙寫過渡期（保險）**：1 週同時寫兩邊、check 數據一致
5. **cutover**：broker 只指 SQL Server、SQLite db 留著當 backup
6. **觀察 1 個月**：寫入併發 / connection pool / latency 指標

整套估 ~5-6h 工作 + 1 週觀察。**等你 SQL Server 用了一陣子、覺得真的有需要才動**。

## 7. 重要：這跟你的 BingX 實盤計畫沒關係

當前 broker.db 處理寫入併發 OK（SQLite WAL + ~< 100 writes/sec 的我們規模）。

**SQL Server 不是實盤的 prerequisite**——把實盤跑穩 + 補 walk-forward / alert / observability 那些事比 DB 升級重要太多。

這個 setup 主要功能是：
- 你練手 SQL Server 工具（dev 履歷加分）
- 為未來規模升級備好基礎建設
- 之後 partner repo 合併時、能直接 import 他用 SQL Server 的部分（如果他用）
