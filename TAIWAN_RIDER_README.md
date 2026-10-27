# 公館機車試騎

Unity 6000.4.10f1、URP 17.4 的 Windows 機車原型，第一／第三人稱均可手動騎乘或自動避閃。場景以汀州路三段與羅斯福路四段 90 巷的 OpenStreetMap 中心線為基礎，實際可騎路段約 152 m，前後另有延伸景物。

## 開始騎乘

開啟 `Assets/TaiwanRider/Scenes/GongguanRide.unity` 並按 Play。勾選「自動避閃」，選汽車切入或巷口機車，再按 Enter 或「開始自動避閃示範」；預設第一人稱，以 40 km/h 自動巡航。取消勾選即可手動騎乘，視角保持不變。Windows 0.0.1 發行版存放於工作區 release/GongguanRider-0.0.1-win-x64，完整 ZIP 檔在同一個 release 資料夾。請先解壓縮，再執行 GongguanRider.exe。

| 操作 | 按鍵 |
|---|---|
| 油門與煞車 | W / S |
| 轉向 | A / D |
| 觀察與回正 | 滑鼠 / Space |
| 重試 | R |
| 暫停與繼續 | Esc |
| 選擇情境 | 主選單 1 / 2 / 3 |
| 自動避閃／手動接管 | F2 |
| 第一／第三人稱 | V |
| 顯示／隱藏雷達射線 | G |

主選單與 Esc 暫停選單提供獨立的自動避閃開關、第一／第三人稱按鈕、雷達射線勾選框，以及 FOV 與鏡頭晃動選項。視角和射線選擇會保留至重試與模式切換後。第一人稱自動避閃時也可用滑鼠環視、Space 回正；Esc 釋放游標調整設定。結果顯示反應時間、最近間距與碰撞結果；JSON 存於 Unity `Application.persistentDataPath/Runs`，不會上傳。

## 自動避閃 demo

第三人稱跟拍會顯示完整機車與騎士。68 條 Physics.Raycast 射線涵蓋車輛周圍，較原先 113 條減少約 40%；44 條環繞射線加上 24 條前方加密射線，前方最遠 32 m、側方 16 m、後方 10 m。兩種視角皆可按 G 顯示／隱藏射線；隱藏只影響可視化，自動模式的偵測與避閃仍持續運作。青色表示無命中，橙色表示障礙物，紅色表示碰撞風險；地面線顯示目標路徑，右上角顯示巡航、避讓、煞車、回正以及視角／射線開關狀態。

控制器依實際射線命中資料及連續觀測估算的相對速度判斷風險，不讀取 HazardActor 的種類、active 狀態或事件觸發計時。遇到切入或前方障礙時，選擇已觀測到的可用側方空間避讓；高速橫越物體則優先煞車讓行，通過後回到原車道。控制輸出仍經過機車的油門、煞車與轉向模型，不會瞬移車輛或關閉碰撞。

這是遊戲內的射線感測模型，尚未模擬雷達電波、多徑、雜訊及真實感測誤差。手動模式最高速仍為 100 km/h；自動 demo 預設巡航 40 km/h。

## 真實程度

目前專案已加入路口、店面、玩家與交通車輛精修；`release` 內的 0.0.1 仍是先前發行版，尚未包含這些美術更新。路口附近 9 棟建物補上小磁磚立面、局部水痕、各有用途的開放店面、櫃台、商品架、半開捲門和騎樓吊招，鄰棟立面色調也做銜接。第一／第三人稱使用 TAA 抗鋸齒，減少遠處鐵窗細線的斷裂；重試與切換視角會清除影像歷史。新增建築細節不帶碰撞器，原道路與危險事件使用既有碰撞模型。這些細節依附近照片推估，並非現地逐棟還原。

玩家採用獨立的 `Scooter125_Hero.fbx`，新增車殼接縫、胎紋、煞車及避震零件、燈具、座墊縫線與儀表殼。軸距維持 1.275 m、輪胎半徑 0.217 m；儀表指針對應 0–100 km/h 的刻度，雙側後照鏡維持即時畫面。42 台路邊機車及闖出機車採用由 Hero 製作的三級 `Scooter125_Fleet_LOD0/1/2`；3 台路邊汽車與闖出汽車改用原創四門房車 `CompactSedan_Detailed_LOD0/1/2`，新增車窗、燈具、格柵、輪圈與車身接縫。模型共用材質並依距離降低細節；只有玩家後照鏡有即時攝影機。闖出車輪依實際移動量轉動，原碰撞尺寸、觸發條件與軌跡保留。路邊機車分組停放，修掉原本與汽車重疊的位置，停車格也跟著調整。舊模型保留為不啟用的備份。

道路轉折與路口位置來自公開地圖；路寬 12.4 m、樓高、店面排列、標線和交通設施是推估重建。建築參考公館附近公開照片，包含騎樓、磁磚立面、鐵窗、冷氣、遮雨棚、管線及屋頂水塔；店招為虛構名稱。這一版仍是區域風格原型，並非公館街道的測繪或攝影測量複製。

玩家精修機車採約 1.775 m 車長、1.275 m 軸距、0.217 m 輪胎半徑的原創模型，尺寸參考 Yamaha 勁豪 125 公開資料。騎乘使用地面接觸剛體、輔助平衡與簡化單軌轉向，遊戲最高速為 100 km/h；不是經實車量測驗證的機車動力學模型。引擎音效為合成音。汽車與巷口機車分別在預估抵達衝突點前 1.1 / 1.0 秒開始動作；汽車於 0.7 秒內切入，機車橫越速度為 11.5 m/s。碰撞會讓機車失去平衡，騎士與第一人稱鏡頭隨獨立剛體拋飛、翻滾，2.6 秒後顯示結果；隨時可按 R 重試。兩種危險情境各有固定軌跡與固定觸發條件，每次重試重設。

## 重建與驗證

1. `Tools/prepare_assets.py` 準備授權材質與原創招牌；已有來源快取時可重用。
2. Blender 5.1：`blender --background --factory-startup --python Tools/model_assets.py`。只重建車輛可加 `-- --vehicles-only`。
3. `Tools/derive_route.py` 從保存的 OSM 資料產生公尺座標。
4. Unity CLI `command eval` 執行 `TaiwanRider.Editor.RiderSceneBuilder.PrepareImports()` 與 `BuildScene()`。不要在有未儲存場景修改時重建。
5. 若重建基礎場景，先執行 `TaiwanRider.Editor.DemoAssetSetup.Prepare()`，再依序執行 `TaiwanRider.Editor.IntersectionDetailSetup.Apply()`、`ScooterHeroSetup.Apply()`、`StreetLifeSetup.Apply()`、`TrafficFleetSetup.Apply()`（皆在 `TaiwanRider.Editor`）補回精修並儲存。精修後未新增光照烘焙；目前使用既有即時燈光設定。
6. CLI `run_tests --mode editor --filter TaiwanRider` 驗證道路、尺度、材質與元件連接。
7. 執行 `BuildWindows()` 打包；執行檔加 `-rider-smoke -rider-output <資料夾>` 跑行為測試，或 `-rider-benchmark -duration 600 -rider-output <資料夾>` 跑效能測試。

基準測試排除前 30 秒暖機，1% low 為最慢 1% 畫面時間平均值的倒數。自動 demo 在選單與 F2 切換；驗證腳本的 InputOverride 優先於 demo 控制輸出。實際通過狀態與效能數據以 `Validation` 下的 JSON 為準。

新增 demo 素材後執行 `TaiwanRider.Editor.DemoAssetSetup.Prepare()` 建立 Resources 雷達材質。Play mode 下呼叫 `DemoAcceptance.StartChecks(true)` 可驗證 68 條射線下的 40／50 km/h 巡航、兩種闖出事件、第一／第三人稱、顯示／隱藏射線、無障礙巡航，以及暫停、重試與手動／自動切換時的設定保留。報告存於 `Application.persistentDataPath/Validation/RadarControls`；前一版 demo 報告保留在 `AutoAvoidDemo`。

精修模型來源在 `Sources/Blender/Scooter125_Hero.blend`，重建腳本為 `Tools/refine_scooter.py`；材質的原始 linear RGBA、metallic、roughness 在 `Sources/Blender/Scooter125_Hero-materials.json`。以 `RealismCapture.CaptureAsync(label)` 產生固定視角畫面、`RealismValidation.SaveReport()` 檢查模型、尺度、後照鏡與道路引用；結果放在 `Validation/Realism`。`IntersectionDetailSetup.Remove()` 可恢復原建物視覺。

交通模型重建腳本為 `Tools/build_scooter_fleet.py`、`Tools/refine_car.py`；原稿與材質 JSON 在 `Sources/Blender`。`TrafficFleetValidation.Save()` 檢查全部 47 輛車的匯入尺度、LOD、材質、碰撞尺寸、前進方向、輪子引用與停車重疊。這輪驗證與遊戲實拍保留於 `Validation/StreetFleet`、`Validation/Realism/street-fleet-final`。店面材質與參考依據記錄於 `Sources/streetlife-provenance.json`；店家名稱與陳列為原創虛構設計。

2026-10-11 驗證：車輛整合／存檔重開 336 項、玩家與道路 39 項、自動避閃 32 項、遊玩與碰撞 30 項皆通過。另在 RTX 3060 Laptop GPU 的 Editor（1334×750）跑 60 秒短測，排除前 30 秒後得到平均 24.6 FPS、1% low 9.2 FPS，無執行期錯誤；此為編輯器中的初步樣本，不能代表獨立發行版效能。原本長時 benchmark 的 1000 取樣幀門檻在這次短測未通過（738 幀），因此不列為效能驗收通過。尚需後續效能優化及獨立 build 測試。

## 資料來源

`Sources/asset-manifest.json` 記錄素材來源與授權，`Sources/asset-sha256.json` 記錄內容雜湊。Blender 原始檔在 `Sources/Blender`；OSM 原始資料及衍生座標保留於專案。照片作者與授權詳見 `Sources/THIRD_PARTY_NOTICES.txt` 和 `Sources/References/credits.json`。


