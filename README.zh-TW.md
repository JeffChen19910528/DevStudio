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

## 操作方式

1. **開啟工作區**——選單「File → Open Folder」，選擇一個包含 .NET 專案或方案的資料夾。
2. **瀏覽與編輯**——用左側的 Explorer 面板開啟檔案；已修改的檔案會顯示修改標記，可用
   「File → Save」（或 Ctrl+S）儲存。
3. **建置**——在工具列選擇組態（Debug/Release）後點擊 Build，或使用 Build 選單。錯誤與警告會
   顯示在 Problems 面板中，點擊項目可跳至對應的原始碼位置。
4. **執行**——在工具列的下拉選單選擇要執行的目標後點擊 Run。輸出會顯示在 Output 面板中，可用
   Stop/Restart 控制執行中的程序。
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
