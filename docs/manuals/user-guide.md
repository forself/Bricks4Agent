# Bricks4Agent 使用手冊

> 本文件已由目前版手冊取代。

請改讀：

- [目前版本完整使用手冊](current-user-manual.zh-TW.md)

- [目前版本完整技術手冊](current-technical-manual.zh-TW.md)

## 為什麼保留此檔案

舊版連結仍可能指向 `docs/manuals/user-guide.md`。為避免讀者開到過期或亂碼內容，本檔保留作為相容入口，並將目前狀態集中到 `current-user-manual.zh-TW.md`。

目前 canonical 使用路徑是：

```text
LINE webhook -> public tunnel -> line-worker -> broker /api/v1/high-level/line/process
```

`agent --line-listen` 僅為 legacy/development-only。
