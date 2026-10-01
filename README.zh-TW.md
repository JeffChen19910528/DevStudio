# DevStudio

**語言切換：[English](README.md) | [繁體中文](README.zh-TW.md)**

DevStudio 是一套跨平台的 .NET 開發桌面 IDE。它不重新實作編譯器、偵錯工具、建置系統與語言伺服器，
而是整合你電腦上已經安裝好的真實工具（.NET SDK、`git`、Roslyn 語言伺服器、`netcoredbg`），
提供一致的操作介面。

## 這個工具能做什麼

- **開啟資料夾**時自動偵測其中的專案／方案（.NET 為主，並可唯讀偵測 CMake、Node、Python、
  Java、Rust、Go 專案）。
- **偵測你電腦上實際安裝的工具鏈**（SDK、編譯器、Git、Docker 等），並根據實際安裝情況顯示每個
  偵測到的專案真正可以做到哪些事。
- **編輯與儲存檔案**，重新啟動後會記住開啟過的分頁與最近使用的工作區，並內建整合式終端機。
- **建置、執行、偵錯真實的 .NET 專案**——真正呼叫 `dotnet build`、自動偵測可執行目標並啟動程序，
  並透過 Debug Adapter Protocol 提供真正的中斷點偵錯。
- **提供 C# 語言智慧功能**（自動完成、滑鼠停留提示、跳至定義、診斷訊息），皆由真實的 Roslyn
  語言伺服器提供。
- **執行 .NET 測試**並在測試總管中顯示結果。
- **內建原始碼控制面板**，直接驅動你電腦上真實安裝的 `git`。
- **內建套件管理員面板**，可檢視、搜尋、新增、移除、更新與還原相依套件——真實支援 NuGet
  (.NET)、pip (Python)、npm (Node)、Maven、Gradle、Cargo、Go Modules、vcpkg 與 Conan，任何會修改
  專案的操作都會受到工作區信任機制保護（部分工具如 Gradle 僅支援檢視，詳見 `CLAUDE.md` 已知限制）。
  pnpm、Yarn、Poetry、uv 尚未支援。
- **支援擴充功能**，採用簡單的資訊清單與命令貢獻模型。
- **支援兩種介面語言**——英文與繁體中文，可隨時在「設定」中切換，無需重新啟動應用程式。

其他專案生態系（CMake、Node、Python、Java、Rust、Go）目前僅會被偵測到，尚無法建置／執行／偵錯——
介面會誠實顯示尚未支援，而不會假裝可以使用。

## 開始使用

### 方式一：一鍵啟動（需先安裝 .NET 10 SDK）

若你的電腦已安裝 [.NET 10 SDK](https://dotnet.microsoft.com/download)：

- **Windows**：直接雙擊 `run.bat`
- **Linux / macOS**：在此資料夾開啟終端機，執行：
  ```bash
  chmod +x run.sh   # 第一次執行時才需要
  ./run.sh
  ```

執行後腳本會自動建置並啟動 DevStudio。

### 方式二：獨立執行檔（執行時不需要安裝 .NET SDK）

你可以為各平台建置獨立的單一執行檔。建置過程仍需要 .NET SDK，但建置完成後的檔案可以複製到
完全沒有安裝 .NET 的電腦上直接執行。

```bash
# Windows
scripts\publish.bat

# Linux / macOS
chmod +x scripts/publish.sh   # 第一次執行時才需要
./scripts/publish.sh
```

執行後會產生：

| 平台 | 執行檔位置 |
|---|---|
| Windows | `publish/win-x64/DevStudio.App.exe` |
| Linux | `publish/linux-x64/DevStudio.App` |
| macOS（Intel） | `publish/osx-x64/DevStudio.App` |
| macOS（Apple Silicon） | `publish/osx-arm64/DevStudio.App` |

將對應平台的執行檔複製到任何地方即可直接執行——不需要安裝步驟，之後也不需要開啟終端機。

## 操作介面

### 整體佈局

視窗分為四個主要區域：

```
┌─────────────────────────────────────────────────────────┐
│  選單列                                                  │
├─────────────────────────────────────────────────────────┤
│  工具列第一列 — 檔案 / 建置 / 執行控制                   │
│  工具列第二列 — 偵錯 / 語言 / 測試控制                   │
├──────────────┬──────────────────────────┬───────────────┤
│              │                          │               │
│   檔案總管    │      編輯器（分頁式）      │   屬性面板    │
│  （資料夾樹）  │                          │  （專案詳情）  │
│              │                          │               │
├──────────────┴──────────────────────────┴───────────────┤
│  下方面板（分頁式）                                       │
│  Problems · Output · Terminal · Toolchains · Call Stack  │
│  Threads · Locals · Breakpoints · Completion · Hover     │
│  Test Explorer · Source Control · Extensions · Packages  │
├─────────────────────────────────────────────────────────┤
│  狀態列                                                  │
└─────────────────────────────────────────────────────────┘
```

### 工具列

**第一列 — 檔案、建置與執行：**

| 控制項 | 說明 |
|---|---|
| Open Folder | 開啟工作區資料夾 |
| Save | 儲存目前使用中的檔案 |
| Terminal | 開啟新的整合式終端機分頁 |
| Build | 建置選取的專案／方案 |
| Cancel | 取消正在執行的建置 |
| 建置組態下拉選單 | 選擇主要建置目標的 Debug 或 Release 組態 |
| 相依套件組態下拉選單 | 選擇相依專案的 Debug 或 Release 組態 |
| 執行組態下拉選單 | 選擇要執行的專案或目標 |
| Run | 建置後啟動（或直接啟動）所選目標 |
| Stop | 停止正在執行的程序 |
| Restart | 停止後立即重新啟動程序 |

**第二列 — 偵錯與語言：**

| 控制項 | 說明 |
|---|---|
| Start Debugging | 在偵錯工具下啟動所選目標 |
| Continue | 從中斷點繼續執行 |
| Pause | 暫停正在執行的偵錯工作階段 |
| Step Over | 執行目前行，停在下一行 |
| Step Into | 進入被呼叫的方法 |
| Step Out | 執行至目前方法返回 |
| Stop Debugging | 終止偵錯工作階段 |
| Completion | 在游標位置要求程式碼完成（.cs 檔案） |
| Hover | 顯示游標處符號的型別／說明文件 |
| Go To Definition | 跳至游標處符號的定義 |
| Restart LSP | 重新啟動 Roslyn 語言伺服器 |

### 各面板說明

**左側 — 檔案總管（Explorer）：** 顯示開啟工作區的資料夾樹。雙擊檔案可在編輯器中開啟。對專案節點按右鍵可使用情境選單（例如**設為啟動專案**）。

**中央 — 編輯器（Editor）：** 分頁式程式碼編輯器。已修改的檔案分頁名稱旁會顯示 `*` 標記。尋找／取代列（Edit → Find）在啟用時會出現在編輯器上方，支援區分大小寫搜尋與全部取代。

當開啟中的檔案被外部工具修改時，畫面上方會出現黃色提示列，提供「從磁碟重新載入」選項。

**右側 — 屬性面板（Properties）：** 顯示檔案總管中所選項目的中繼資料：專案類型、檔案路徑、偵測可信度、是否可執行，以及根據目前安裝的工具鏈所判定的可用能力（Build、Run、Debug 等）。

**下方面板分頁：**

| 分頁 | 說明 |
|---|---|
| Problems | 顯示最近一次建置或語言伺服器的錯誤與警告。點擊任一項目可跳至對應的原始碼行。 |
| Output | 即時顯示建置與執行操作的標準輸出及錯誤輸出。 |
| Terminal | 整合式命令列工作階段。使用 New / Restart / Close 管理分頁，在每個分頁底部的輸入框中輸入指令。 |
| Toolchains | 列出所有偵測到的工具鏈（SDK、編譯器、Git、Docker 等）及其版本與路徑，並顯示已安裝的 Visual Studio 執行個體。可使用 Refresh 重新掃描。 |
| Call Stack | 偵錯工作階段中的執行堆疊框架。點擊框架可跳至對應的原始碼位置。 |
| Threads | 被偵錯程序的執行緒清單。 |
| Locals | 目前中斷點範圍內的變數，依範圍分組顯示。 |
| Breakpoints | 目前設定的所有中斷點，包含檔案、行號與啟用狀態。 |
| Completion | 最近一次 Completion 要求的結果。雙擊項目可將其插入編輯器。 |
| Hover | 最近一次 Hover 要求的說明文件或型別資訊。 |
| Test Explorer | 探索並執行 .NET 測試。支援 Refresh Tests、Run All、Run Selected、Stop。雙擊結果可跳至對應的測試程式碼。 |
| Source Control | 完整的 Git 操作介面：檢視已暫存／未暫存的變更及差異、暫存／取消暫存／捨棄、提交、管理分支、瀏覽歷史記錄。 |
| Extensions | 列出已偵測到的擴充功能。選取一個可查看詳情、驗證錯誤及其貢獻的命令。使用 Enable/Disable 切換啟用狀態，使用 Invoke 手動執行擴充命令。 |
| Package Manager | 以專案為單位管理相依套件。選擇專案與套件管理員後，使用 Installed / Browse / Updates / Dependencies 分頁進行操作。 |

**狀態列：** 顯示工作區名稱、目前檔案名稱、專案情境、游標行／欄位、檔案編碼、換行符號格式、修改標記、最近建置狀態、執行狀態，以及語言伺服器狀態。

---

### 選單說明

#### File（檔案）

| 項目 | 動作 |
|---|---|
| New File | 建立新的未命名檔案 |
| Open File | 在編輯器中開啟單一檔案 |
| Open Folder | 開啟資料夾作為工作區 |
| Recent Workspaces | 最近開啟的資料夾子選單 |
| Reopen Last Workspace on Startup | 切換：下次啟動時自動還原上次的工作區 |
| Save | 儲存目前使用中的檔案（Ctrl+S） |
| Save As | 將目前檔案儲存至新路徑 |
| Close | 關閉目前的編輯器分頁 |
| Exit | 退出 DevStudio |

#### Edit（編輯）

| 項目 | 動作 |
|---|---|
| Undo | 復原上一次編輯 |
| Redo | 取消復原上一次的復原操作 |
| Cut | 剪下選取內容 |
| Copy | 複製選取內容 |
| Paste | 貼上剪貼簿內容 |
| Find | 開啟尋找／取代列 |
| Replace | 開啟尋找／取代列（取代模式） |
| Go To Line | 跳至指定行號 |

#### View（檢視）

| 項目 | 動作 |
|---|---|
| Terminal | 開啟新的終端機分頁（與工具列按鈕相同） |
| Toggle Theme | 切換淺色與深色主題 |

#### Project（專案）

| 項目 | 動作 |
|---|---|
| Trust / Untrust Workspace | 切換目前資料夾的工作區信任狀態。受信任的工作區才能解鎖 Build、Run、Debug、測試，以及會修改儲存庫狀態的 Git 操作。 |

#### Build（建置）

| 項目 | 動作 |
|---|---|
| Build | 建置目前工作區 |
| Rebuild | 清除後重新建置 |
| Clean | 刪除建置產出物 |
| Restore | 還原 NuGet 套件（`dotnet restore`） |
| Cancel | 取消正在執行的建置 |

#### Run（執行）

| 項目 | 動作 |
|---|---|
| Run | 建置後啟動所選執行目標 |
| Run Without Building | 直接啟動上次建置的輸出檔 |
| Stop | 停止執行中的程序 |
| Restart | 停止後立即重新啟動 |

#### Debug（偵錯）

| 項目 | 動作 |
|---|---|
| Start Debugging | 在偵錯工具下啟動 |
| Continue | 從中斷點繼續執行 |
| Pause | 暫停執行 |
| Step Over | 逐行執行（不進入呼叫） |
| Step Into | 進入被呼叫的方法 |
| Step Out | 執行至目前方法返回 |
| Stop | 終止偵錯工作階段 |
| Toggle Breakpoint at Line | 在游標行新增或移除中斷點 |

#### Language（語言）

| 項目 | 動作 |
|---|---|
| Completion | 在游標位置要求程式碼完成 |
| Hover | 在游標位置要求懸停資訊 |
| Go To Definition | 跳至符號定義 |
| Restart Language Server | 重新啟動 Roslyn 語言伺服器 |

#### Tools（工具）

| 項目 | 動作 |
|---|---|
| Refresh Toolchains | 重新掃描已安裝的 SDK、編譯器與工具 |
| Settings | 開啟設定視窗（主題、介面語言、啟動行為） |

#### Extensions（擴充功能）

| 項目 | 動作 |
|---|---|
| Refresh | 重新掃描擴充功能資料夾，偵測新增或變更的擴充功能 |

---

## 操作方式

1. **開啟工作區**——選單「File → Open Folder」，選擇一個包含 .NET 專案或方案的資料夾。
2. **瀏覽與編輯**——用左側的 Explorer 面板開啟檔案；已修改的檔案會顯示修改標記，可用
   「File → Save」（或 Ctrl+S）儲存。
3. **建置**——在工具列選擇組態（Debug/Release）後點擊 Build，或使用 Build 選單。錯誤與警告會
   顯示在 Problems 面板中，點擊項目可跳至對應的原始碼位置。
4. **執行**——在工具列的下拉選單選擇要執行的目標後點擊 Run。輸出會顯示在 Output 面板中，可用
   Stop/Restart 控制執行中的程序。若要將某個專案設為啟動目標（包含未被自動偵測為可執行的專案，
   例如舊式 ASP.NET Web 專案），可在檔案總管中對該專案按右鍵，選擇**設為啟動專案**。
5. **偵錯**——用「Debug → Toggle Breakpoint at Line」設定中斷點，再點擊 Start Debugging。程式
   停在中斷點時，可使用 Call Stack、Threads、Locals、Breakpoints 面板檢視狀態。
6. **取得程式碼輔助**——在 `.cs` 檔案中輸入時，可使用 Completion / Hover 按鈕或 Go To
   Definition 向語言伺服器查詢；診斷訊息會自動顯示。
7. **執行測試**——開啟 Tests 面板，使用 Refresh Tests / Run All / Run Selected。
8. **使用原始碼控制**——開啟 Source Control 面板，檢視變更、暫存／取消暫存／提交，並管理真實
   Git 儲存庫的分支。
9. **管理擴充功能**——開啟 Extensions 面板，檢視已偵測到的擴充功能並啟用／停用。
10. **切換介面語言**——開啟「Tools → Settings」，在 Language 下拉選單中選擇 English 或
    繁體中文，介面會立即套用變更。

部分操作（Build、Run、Debug、啟動語言伺服器、執行測試，以及會變更儲存庫狀態的 Git 操作）第一次
執行時會要求你確認信任此工作區，因為這些操作會實際執行你所開啟專案中的程式碼。

## 系統需求

- **.NET 10 SDK**——僅在「從原始碼建置／執行」或「建置獨立執行檔」時才需要。透過
  `scripts/publish.*` 建置出的獨立執行檔，在執行時**不需要**該電腦安裝 .NET。
- **git**——使用原始碼控制面板時需要。
- **netcoredbg**——偵錯功能需要（Windows 可透過
  `winget install Samsung.NetCoreDbg` 安裝，其他平台請使用對應的套件管理工具）。
