# 按鍵輔助設定檢查與詞綴篩選開發計畫

> 實作交接採 `superpowers:executing-plans` 或使用者指定的 `superpowers:subagent-driven-development`。核准本計畫後才執行核取項目。

**目標**　在同一個 Windows 桌面視窗完成設定檢查與裝備製作詞綴編輯，並用同一組規則比對 PoE 1 國際服繁體中文物品資料。

**架構**　工作區現有 v5 互動預覽及舊工具。本計畫在新 App 內建立最小可運作的設定與裝備製作頁。設定檢查、詞綴索引、物品文字解析和條件評估可獨立測試，桌面介面繫結同一份設定草稿。

**技術**　沿用 v5 的 C#／WPF 提案，採 .NET 10 SDK、WPF `net10.0-windows`、核心 `net10.0`、System.Text.Json、MSTest。此工作區可見 .NET 6／8 桌面執行階段，`dotnet --list-sdks` 目前沒有列出 SDK。實作前需備妥 .NET 10 SDK 及測試還原來源。 [Microsoft WPF 目標框架](https://learn.microsoft.com/en-us/dotnet/standard/frameworks/)

**依據**　[v6 已審閱畫面與需求](../../brainstorm/desktop-app-b/v6/PLAN.md)、[v5 功能盤點](../../brainstorm/desktop-app-b/v5/FEATURES.md)、[v5 製作功能與舊語法](../../brainstorm/desktop-app-b/v5/CRAFTING.md)。使用者在 v6 交付後回覆「OK 請繼續整理開發」，本文件是下一階段待審閱的計畫。

## 範圍與驗收前提

- R16 首頁以功能必要的按鍵、座標、偵測點顯示紅框／綠框，缺漏可追到原設定。R17 把詞綴卡片放在改造石卡片正上方。R18 PoEDB 繁中詞綴可模糊搜尋，選定後可填最小及最大值。R19 支援必須命中、排除、至少命中 N 條、加權總和。
- 本次開發以可儲存的設定與可離線驗證的比對為完成邊界。製作規則提供給改造石、混沌石及重鑄＋點金的後續執行層。舊工具移植、連接遊戲、全域送鍵、擷取畫面、使用通貨與其餘 v5 執行引擎各自另有開發範圍。桌面介面清楚區分「已設定」與可執行狀態。
- P16–P20 細節列為本計畫採用的提案，供本次審閱。儲存及校準是支援這一階段的最小資料契約。舊 EXE、INI、Log 及歷史預覽維持原狀。v5 原有五種語法的完整移植另行處理，若使用者已提供既有條件，原文能保留供確認。
- PoEDB 的範圍以繁中詞綴頁、All 及其相關詞綴分類為準，逐類回報覆蓋。網站抓取、儲存或發佈的條件在建立資料前核對。首份資料及 PoE 1 繁中一般／詳細複製文字範例是驗收依賴，缺資料的部分須明確標記。 [PoEDB 詞綴索引](https://poedb.tw/tw/Modifiers) · [PoEDB All 清單](https://poedb.tw/tw/Modifiers_list)

v8 補充 R20、R21。F20 只編輯背包左上角及右下角。F03 直接編輯卷軸 X、Y，首頁從缺漏卡跳到該欄。F26 只顯示快捷鍵一覽，顯示主視窗快捷鍵不再列入本版設定。\n\n## 交付結構

| 位置 | 責任 |
| --- | --- |
| `src/KeyAssist.Core/` | 設定、狀態檢查、詞綴索引、物品文字解析及比對 |
| `src/KeyAssist.Desktop/` | WPF 主視窗、設定檢查、定位設定與詞綴編輯 |
| `tools/KeyAssist.Catalog/` | PoEDB 資料解析、建立詞綴快照及覆蓋報告 |
| `tests/KeyAssist.Core.Tests/` | 可離線測試的核心行為與固定輸入 |
| `tests/KeyAssist.Desktop.Tests/` | 桌面模型、編輯和驗收流程測試 |

任務 1 → 2 完成設定檢查。任務 3 → 4 完成詞綴資料及比對。任務 5 使用兩條路線的結果完成裝備製作頁。各任務先跑指定案例確認失敗原因，再完成最小實作並重跑。目前工作區無 Git metadata，逐項以檔案差異及測試輸出記錄驗收，若交接到已有 Git 的專案才依其規則提交。

## 優先驗證的情境

- 選到已刪除的偵測點時顯示紅框，尚存的點位排序不改變其引用。任務 1。
- 未校準的舊座標與合法的內容區原點分開判斷。任務 1。
- 資料來源中斷或網頁結構變動時，上一份有效詞綴快照仍可搜尋，覆蓋報告顯示缺口。任務 3。
- 同名固定及顯性詞綴、多數值和讀取不明不造成誤達標。任務 4。
- 重新開啟 App 後，群組權重、舊條件原文及目前編輯選擇仍可檢視，所選詞綴消失時顯示待修。任務 5。

## 任務 1　設定資料與紅綠判斷（R16）

**檔案**　`src/KeyAssist.Core/KeyAssist.Core.csproj`、`Settings/SettingsSnapshot.cs`、`Settings/SettingsStore.cs`、`Readiness/FeatureRequirements.cs`、`Readiness/ReadinessEvaluator.cs`、`tests/KeyAssist.Core.Tests/ReadinessTests.cs`、`SettingsStoreTests.cs`。

**介面**　`ReadinessEvaluator.Evaluate(SettingsSnapshot settings) → IReadOnlyList<ReadinessCard>`。卡片有 `FeatureId`、`SlotId`、`IsConfigured`、`Issues`、`TargetFieldId`、`IsEnabled`。設定保存用 `SettingsStore.Load()` 與 `Save(SettingsSnapshot snapshot)`，回傳載入／寫入結果。草稿立即反映到畫面，正式保存時驗證並一次替換可用檔案。

- [ ] 寫 `ScrollNeedsBothCoordinates`，令 F03 已有按鍵且只有 X，檢查卡片紅且缺 Y。補有效 Y 後轉綠。寫 `ClickNeedsNoPosition`，驗證 F05 只需其按鍵。
- [ ] 寫 `PointIdentitySurvivesReorderAndReportsDeletion`，令 F38 槽指向有座標和顏色的點，重排仍綠，移除或清空色碼即紅。寫 `CalibratedOriginDiffersFromPlaceholder`，驗證已確認的內容區 `(0,0)` 可用，只有舊預設值或座標基準不合則須確認。
- [ ] 寫 `OnlySelectedRecipePositionsAreRequired`，比較 F31 混沌石與重鑄＋點金，驗證切換時改列對應通貨點。寫 `FailedSaveKeepsLastSnapshot`，模擬寫入失敗後可重載上一份成功資料。
- [ ] 執行 `dotnet test tests/KeyAssist.Core.Tests --filter 'FullyQualifiedName~ReadinessTests|FullyQualifiedName~SettingsStoreTests'`，確認新案例在未有實作時失敗。
- [ ] 建立實際設定欄位與依賴表。以 v5 `pages.json` 對照保留項目，狀態對應真正可編輯的功能及子項。依目前模式和觸發路線檢查按鍵及座標。點位以穩定 ID 引用，需顏色的點驗證色碼，座標檢查內容區邊界與校準尺寸／DPI。藥劑未啟用補喝時，依其本次用途判定輸出鍵是否必填。裝備製作按模式及勾選的材料分支列必要點位。對座標用途不明或所選定位方式尚未驗證的項目，列出原因。
- [ ] 使用暫存檔完成設定驗證後替換。錯誤或舊格式中未辨識的設定可查核，留空設定能保存並於首頁回報。重跑本任務測試，預期全部通過。

## 任務 2　桌面首頁和補設定入口（R16、R20、R21）

**檔案**　`src/KeyAssist.Desktop/KeyAssist.Desktop.csproj`、`MainWindow.xaml`、`Overview/OverviewView.xaml`、`Overview/OverviewViewModel.cs`、`Settings/SettingsView.xaml`、`Settings/SettingsViewModel.cs`、`tests/KeyAssist.Desktop.Tests/OverviewTests.cs`。

**介面**　`OverviewViewModel.Refresh(SettingsSnapshot draft)` 更新 `Cards`。卡片 `OpenSetting` 依 `TargetFieldId` 導航至設定欄，並保留當前設定草稿。

- [ ] 寫 `HomeOrdersMissingAndShowsFieldReason` 和 `OpeningCardTargetsMissingField`，驗證紅框置前、有「未設定」與「缺少 Y」，點卡片定位滑鼠與按鍵的 F03 卷軸 Y，補齊即更新為綠框。加 `DisabledButConfiguredKeepsConfiguredLabel`，驗證停用狀態另顯示。
- [ ] 寫 `PortalCoordinatesLiveWithPortalAction` 及 `ShortcutSummaryOmitsShowWindow`，驗證 F20 只有兩個背包角、F03 保有卷軸 X、Y，總覽快捷鍵清單沒有顯示主視窗。
- [ ] 執行 `dotnet test tests/KeyAssist.Desktop.Tests --filter FullyQualifiedName~OverviewTests`，確認新增案例失敗。
- [ ] 製作可用 WPF 首頁和必要欄位。滑鼠與按鍵、藥劑、技能、背包、製作和座標保留對應入口。支援手動填寫座標基準、遊戲內容區寬高及 DPI 以驗收校準狀態。既有全域控制在引擎未接入時清楚顯示狀態。用紅／綠框及狀態文字表示設定進度。卡片導航用穩定欄位 ID，欄位變更、點位刪除及本次模式切換後重算。
- [ ] 重跑測試。以 `dotnet run --project src/KeyAssist.Desktop` 在 Windows 驗收 A16-1、A16-2 及窄視窗，確認可用鍵盤操作補設定。

## 任務 3　PoEDB 詞綴資料與本機搜尋（R18）

**檔案**　`tools/KeyAssist.Catalog/KeyAssist.Catalog.csproj`、`PoedbImport.cs`、`Program.cs`、`src/KeyAssist.Core/Catalog/CatalogSnapshot.cs`、`CatalogStore.cs`、`AffixIndex.cs`、`tests/KeyAssist.Core.Tests/AffixCatalogTests.cs`、`Fixtures/Poedb/`。

**介面**　`AffixIndex.Search(string query, int limit) → IReadOnlyList<AffixEntry>`。`AffixEntry` 有穩定 `AffixId`、繁中範本、`SourceKind`、`Metrics`、`CanEvaluate` 和來源明細。`MetricId` 定位可比較的單一數字。來源、統計語意及數值欄位語意共同決定 ID。來源快照版本與階級資訊可更新。

- [ ] 寫 `FireQueryFindsResistanceAndKeepsSourceDistinct`，驗證 `火焰` 找到顯性火抗、固定火抗及穿透火抗，顯性火抗的多階只出現一個候選。驗證「火焰　抗性」忽略空白差異，查無詞綴顯示空清單。
- [ ] 寫 `IncompleteUpdateKeepsPreviousCatalog` 和 `ImportReportsUnparsedRows`，驗證來源缺頁、HTTP 拒絕或表格結構改變時有原因與覆蓋數，上一版仍可搜尋，樣本結果不標作完整。
- [ ] 執行 `dotnet test tests/KeyAssist.Core.Tests --filter FullyQualifiedName~AffixCatalogTests`，確認新案例失敗。
- [ ] 先列來源清冊，查核允許的取得方式，從少量樣本確認 All 與相關分類的資料欄位。以已取得的 HTML 建立可重跑的匯入和去重流程，保存擷取時間、來源 URL、原始可用 ID、分類與階級、每類納入與失敗計數。抓取模式依來源允許方式執行並限制頻率。若遇來源限制，改用允許的本機資料，回報實際缺少的分類。索引使用本機快照，以詞句及分詞比對，完整關鍵詞優先。查詢時零網路請求，替換前驗證新快照完整度。
- [ ] 重跑測試。用 `dotnet run --project tools/KeyAssist.Catalog -- import --input <資料目錄> --output <輸出目錄>` 生成快照及覆蓋清單。人工對照 All、戒指、工藝與固定詞綴分類的樣本。A18-3 需附實際覆蓋清單與缺口，無法取得的資料分列。

## 任務 4　物品文字解析與群組評估（R18、R19）

**檔案**　`src/KeyAssist.Core/Affixes/RuleSet.cs`、`ItemAffixReader.cs`、`RuleEvaluator.cs`、`tests/KeyAssist.Core.Tests/ItemAffixReaderTests.cs`、`RuleEvaluatorTests.cs`、`Fixtures/Items/`。

**介面**　`ItemAffixReader.Read(string clipboardText, CatalogSnapshot catalog) → ItemReadResult`。`RuleEvaluator.Evaluate(RuleSet rules, ItemReadResult item, CatalogSnapshot catalog) → RuleResult`，回傳 `Passed`、`Failed`、`Indeterminate` 或 `Invalid`，以及逐列判定、命中數、加權總分。`RuleSet` 是多個啟用或停用的 `RuleGroup`。`RuleRow` 持有 `AffixId`、可選 `MetricId`、`decimal? Min`／`Max`、加權時的 `decimal? Weight`。

- [ ] 寫 `InclusiveAndIndependentBounds`，以 29、30、45、46 測 min 30、max 45 及兩欄組合。驗證兩欄留空時比對存在、輸入 46～45 判為 `Invalid`，保留 0、負數及小數。
- [ ] 寫 `FourGroupsCompose`，火抗 35、生命 80、無魔力、至少命中生命或能量護盾 1 條，應為 `Passed`。加上符合排除的魔力時變 `Failed`。權重 1、0.5 得分 75，門檻 70 通過、76 不通過。加入重複詞綴欄位的測例，避免將同一條件重複計數。
- [ ] 寫 `SameTextDifferentSourceAndMultipleNumbers`，固定火抗即使 35 也不命中顯性條件。附加傷害用所選數值欄位比較，無數值詞綴只能檢查有無。寫 `AbsentDiffersFromUnknown`，權重條件已確定不存在時以 0 計，缺來源或不可辨識時為 `Indeterminate`。
- [ ] 執行 `dotnet test tests/KeyAssist.Core.Tests --filter 'FullyQualifiedName~ItemAffixReaderTests|FullyQualifiedName~RuleEvaluatorTests'`，確認新增案例失敗。
- [ ] 以實際繁中物品複製文字樣本定義解析，辨識固定、顯性等來源、帶正負號的值和有多個數字的詞綴。用正規化後的選定詞綴識別與來源比對，維持資料缺失和已確定不存在的差異。比對支援來自文字的證據，索引搜尋命中不直接用作物品符合條件的依據。
- [ ] 實作四種群組。必須命中要求全部，排除有任一命中即不通過，至少命中比對唯一條件數，權重以每列通過範圍的詞綴值乘權重後加總，與群組分數上下限比較。分數需至少設定一側，判定數值使用 `decimal`。加權列未符合列範圍或已確認缺少時計 0。啟用群組皆須通過。啟用空群組、全停用、重複條件、不可用的詞綴或比較欄位顯示明確錯誤。來源不明、資料截斷或無法辨識的相關數值不得視為達標。
- [ ] 重跑測試，並用實際取得的繁中物品文字核對固定／顯性、單值／多值、讀取不完整。合成樣本用於開發測試，正式驗收標示實際範例來源與所覆蓋種類。

## 任務 5　裝備製作頁與整體驗收（R16–R21）

**檔案**　`src/KeyAssist.Desktop/Crafting/CraftingView.xaml`、`CraftingViewModel.cs`、`RuleEditorView.xaml`、`RuleEditorViewModel.cs`、`src/KeyAssist.Core/Settings/SettingsSnapshot.cs`、`tests/KeyAssist.Desktop.Tests/CraftingViewTests.cs`。

**介面**　`RuleEditorViewModel.Search(string query)` 更新 `Candidates`，`AddRow(Guid groupId, AffixId affixId)` 建立條件，`BuildRuleSet()` 回傳共用的規則和輸入驗證。`CraftingViewModel.EvaluateItemText(string text)` 呼叫任務 4，供本機驗收與後續製作邏輯引用。每個製作分頁引用同一個 `RuleSet`。

- [ ] 寫 `RulesPrecedeAlteration`，以 WPF 控制項順序檢查 F36 在 F30 前且相鄰。寫 `FireFilterAndGroupsRoundTrip`，選顯性火抗後可輸入 30、45，新增四種群組，儲存重開保留順序、條件與權重。用火抗 35／生命 80 的樣本驗證 75 分。
- [ ] 寫 `PreservesLegacyTextAndReportsUnavailableEntry`，驗證不明舊語法原文仍可讀取，索引更新後已保存 ID 若缺失，畫面標示待修且該規則不會被靜默略過。只有語意可證相等且使用者確認的轉換才寫入結構化條件。
- [ ] 執行 `dotnet test tests/KeyAssist.Desktop.Tests --filter FullyQualifiedName~CraftingViewTests`，確認新案例失敗。
- [ ] 製作條件選單及四種群組編輯。在 F36 卡片內可增刪、切換群組種類和欄位。候選顯示來源，同名不同類型可分辨，支援上下鍵選擇及 Enter 確認。根據詞綴型態啟用上下限、數字欄位或存在條件。把 F36 放在 F30 正上方，F30／F31 共用規則。畫面提供無法判定的原因和逐組分數，測試可輸入已取得的物品文字。
- [ ] 重跑兩個測試專案及 `dotnet build src/KeyAssist.Desktop`。以 `dotnet run --project src/KeyAssist.Desktop` 開啟成品，執行 A16-1、A16-2、A17、A18-1～3、A19-1～2、A20、A21。將 Windows 畫面、測試結果、詞綴覆蓋報告、實際物品樣本缺口列在交付驗收紀錄中。
