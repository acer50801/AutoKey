# 按鍵輔助 v8

## 開啟程式

從 `dist/KeyAssist-v8` 開啟 `KeyAssist.Desktop.exe`。發行包適用 Windows x64，包含 .NET 10 執行環境。若要搬移程式，請一併保留同目錄的 `Data` 資料夾。

## 設定與比對

1. 從總覽點選紅框卡片，跳到缺漏欄位。首次輸入座標前，在「座標與配置」填寫遊戲內容區寬、高和 DPI。
2. 在「滑鼠與按鍵」的傳送卷軸卡片填寫 X、Y。快捷鍵欄聚焦後可直接按鍵，Delete 可清除綁定。
3. 在「裝備製作」新增群組、搜尋並加入詞綴。數值上下限含邊界，兩欄留空可比對是否存在。加權群組至少填一側分數界限。
4. 貼上完整繁中物品文字後選「比對物品文字」。若普通複製內容可能把多條效果合在一條混合詞綴下，請貼上含詞綴標頭的詳細文字。未收錄的詞綴可能使結果顯示「無法判定」。
5. 點「儲存設定」。設定位置為 `%LOCALAPPDATA%\KeyAssist\v8\settings.json`。

本版提供設定和離線物品文字比對。遊戲連接、全域送鍵、畫面擷取和使用通貨屬後續開發。

## 詞綴快照

發行版附本機快照。詞綴規則卡的「來源覆蓋與缺口」和 `Data/catalog-coverage.json` 列出本次資料來源、可用列數及例外。快照使用 PoEDB 的 All 前後綴、戒指一般詞綴、工藝台與部分固定詞綴。其他分類、特殊生成方式及實際遊戲剪貼簿格式的全面驗收仍待補足。 [PoEDB 詞綴索引](https://poedb.tw/tw/Modifiers) · [All 前後綴](https://poedb.tw/tw/Modifiers_list) · [戒指](https://poedb.tw/tw/Rings#ModifiersCalc) · [工藝台](https://poedb.tw/tw/Crafting_Bench) · [紅玉戒指](https://poedb.tw/tw/Ruby_Ring)

要更新本機索引，將已取得的繁中 HTML 存到同一目錄，檔名為 `Modifiers.html`、`Modifiers_list.html`、`Rings.html`、`Crafting_Bench.html` 及 `Ruby_Ring.html`。也可加入索引中所列的其他物品分類 HTML。關閉 App 後在專案根目錄執行下列指令。

```powershell
$sourceDirectory = 'D:\PoEDB-HTML'
dotnet run --project tools/KeyAssist.Catalog -- import --input "$sourceDirectory" --output "$env:LOCALAPPDATA\KeyAssist\v8"
```

匯入工具會先輸出覆蓋報告。必要來源缺失、結構無法解析或既有覆蓋明顯減少時，會保留上一份快照。

## 建置與測試

```powershell
dotnet test tests/KeyAssist.Core.Tests
dotnet test tests/KeyAssist.Desktop.Tests
dotnet publish src/KeyAssist.Desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/KeyAssist-v8
```
