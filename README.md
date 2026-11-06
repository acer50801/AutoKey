# 按鍵輔助 v8

Windows x64 桌面工具，供 PoE 1 國際服繁體中文介面使用。首頁會指出缺少的按鍵、座標與偵測點。裝備製作頁可搜尋本機詞綴、組合比對條件，並貼上物品文字驗證結果。

## 建置與啟動

安裝 .NET 10 SDK 後，在專案根目錄執行下列 PowerShell 指令。 [Microsoft .NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

```powershell
dotnet run --project src/KeyAssist.Desktop
```

如需可在 Windows x64 直接開啟的發行版，執行下列指令。搬移時請保留 EXE 和同目錄的 `Data`。

```powershell
dotnet publish src/KeyAssist.Desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/KeyAssist-v8
& .\dist\KeyAssist-v8\KeyAssist.Desktop.exe
```

## 測試

```powershell
dotnet test tests/KeyAssist.Core.Tests
dotnet test tests/KeyAssist.Desktop.Tests
```

本階段提供設定與離線物品文字比對。遊戲擷取、全域送鍵及使用通貨屬後續開發。附帶的繁中詞綴快照涵蓋 PoEDB 的 All 前後綴、戒指一般詞綴、工藝及部分固定詞綴。 [操作與索引更新](src/KeyAssist.Desktop/README.md) · [v8 互動預覽](docs/brainstorm/desktop-app-b/v8/interactive-preview.html) · [驗收紀錄](docs/brainstorm/desktop-app-b/v8/QA.md)
