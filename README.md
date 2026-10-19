# KamiToolKit

由 MidoriKami 開發的 C# 包裝函式庫，用來建立與操作 FFXIV 原生 UI（AddonToolKit）節點，
讓插件可以自製看起來像遊戲原生介面的視窗與元件。

## 台服 fork 的目的

跟隨艦隊釘在 API13 世代的 commit（上游已進行大規模重構並上到 API15，兩邊結構差異很大，
無法直接合併），並補上原生 addon 路徑上六處解參防護：

- `NativeMemoryHelper.UiAlloc`/`Create`：配置失敗或 `GetUISpace()` 回 `null` 時原本會對
  位址 0 解參，改成先判空。
- `NativeAddon.Open`：`InitializeAddon()` 會經由虛擬表重入回本類別，回來後不能沿用舊前提，
  補一次重讀與判空，不成立就記錄錯誤並放棄開窗。
- `NativeAddon.GetPosition`：`Open()` 之前或釋放之後讀取會直接崩潰（`AccessViolationException`
  連 `try/catch` 都攔不住），改為回傳尚未套用的預定位置。
- `NativeAddon.IsOpen`/`AddonId`：判空與解參各讀一次欄位，兩次讀取之間欄位可能已被遊戲的
  釋放路徑設回 `null`；改成先抄進區域變數再判斷。

以上六處失敗形式從「AccessViolation 直接崩潰」降級為「可處理的錯誤」；`NativeAddon.Open`/
`Close` 的 `RunOnFrameworkThread` 委派經逐指令查證後判定本身安全（未變動）。

## 與上游的差異

- 上述六處解參防護。
- **遊戲改版後的容錯**：函式庫裡的特徵碼標成可失敗，對不上時用到它的插件照常載入（原本整個插件載入失敗）。
- **背景執行緒不碰原生記憶體**：GIF 動畫節點改在框架執行緒上組裝；圖片節點碰原生記憶體前再判一次框架是否卸載中。
- **style 檔讀過就快取**，每次登入不再重讀數十個 JSON。

因 pin 在 API13 世代、上游已大幅重構到 API15，整體差異未逐一比對。

## 誰在用它

目前 3 個插件 repo 以子模組使用：`DailyDuty`、`EzWondrousTails`（插件 `WondrousTailsSolver`）、`SortaKinda`。

---

上游原始碼：<https://github.com/MidoriKami/KamiToolKit>
