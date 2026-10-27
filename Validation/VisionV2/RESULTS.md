# v2 接入 Unity：與 v1 的閉迴路比較

## 執行版本

Unity 預設模型已更新為 **v2**，v1 保留作為比較。兩版皆使用同一份已修好的 Raycast／避閃控制器；本次沒有調整控制門檻或路線。

- Unity 6000.4.10f1、Inference Engine 2.6.1 GPUCompute、RTX 3060 Laptop GPU。
- v2 ONNX：`d0cd520e2b52d2518735eb3d5a04939a27c0777788bae7d8ad2ea9b37b70d219`。
- v1 ONNX：`989305d3698e592074fb86c1100f2dbbcaf9283509f516e2fb6bf78c3fe53cd3`。
- 修正後控制器 SHA-256：`7dd9cf1d64bf3e1bd5a76b3e6e9973a6b02606c680bebac4ad2a30bedcda9e8d`。
- 模型介面維持 RGB `[1,3,640,640]` → `[1,6,8400]`；0=car、1=motorcycle。
- 信心門檻 0.60、證據時效 350 ms、Raycast 基礎預測 1.8 s；新鮮配對的汽車／機車分類可延長至 2.2／2.4 s。

## 同條件結果

每版 48 次：6 情境 × 2 速度（20／40 km/h）× 2 天候（晴／濃霧）× 2 設計變體；共 96 次，全部有有效推論、無執行錯誤。採滾動起步，`timeScale=1`、`captureFramerate=0`、50 Hz 物理、目標渲染 60 FPS。

兩版依同一案例順序先跑 v1、再跑 v2，只有 `-visionModel` 不同。沿用先前 benchmark 的場景與 48 個融合條件，隱藏相同 45 個裝飾停車物件。這次重新跑 v1，沒有拿尚未修正控制器的舊結果來比較。

| 模型 | 完成通過 | 安全煞停 | 碰撞 | 超時 | 空路／路旁車輛不必要介入 |
|---|---:|---:|---:|---:|---:|
| v1 + 修正後 Raycast | 30 | 8 | 10 | 0 | 0／16 |
| v2 + 修正後 Raycast | 30 | 8 | 10 | 0 | 0／16 |

「安全煞停」只代表本次觀察時間內未碰撞且停穩，不等於已完成路程。額外保持／恢復測試見下節。

| 情境（每版各 8 次） | v1 完成／停下／碰撞／超時 | v2 完成／停下／碰撞／超時 |
|---|---|---|
| 空路 | 8／0／0／0 | 8／0／0／0 |
| 路旁車輛 | 8／0／0／0 | 8／0／0／0 |
| 車道違停 | 8／0／0／0 | 8／0／0／0 |
| 汽車切入 | 2／0／6／0 | 2／0／6／0 |
| 機車橫向穿越 | 4／0／4／0 | 4／0／4／0 |
| 兩側受阻 | 0／8／0／0 | 0／8／0／0 |

### 逐案結果變動

48 個成對情境的完成／停下／碰撞／超時分類完全相同。

![v1 與 v2 結果](comparison.png)

## 視覺實際參與與效能

| 指標 | v1 | v2 |
|---|---:|---:|
| 超過 Raycast 基礎門檻的視覺提早介入次數 | 8 | 8 |
| 完成推論影格 | 3032 | 3038 |
| 有成功配對 Raycast 物件的影格 | 693 | 752 |
| 有 ≥0.60 辨識框的影格 | 763 | 852 |
| 依推論影格加權的擷取至結果平均延遲 | 38.5 ms | 40.8 ms |
| 各案例 P95 延遲的中位數 | 50.4 ms | 50.7 ms |
| 各案例實際推論頻率的中位數 | 8.92 Hz | 8.94 Hz |

成功配對的物件可以是任何 Raycast contact，不能將此影格數當作危險物體召回率。每段行駛時間可能不同，原始影格數也不是精度分數。P95 欄為每案 P95 的中位數，並非所有推論合併後的 P95。延遲包含攝影機渲染、GPU 排程及讀回；不是模型純運算時間，也不是 UGen300 測量值。

濃霧沿用密度 0.45、攝影機 far clip 8 m；Unity physics rays 不受視覺霧影響。濃霧下成功主要可由 Raycast fallback 完成，不能視為視覺已能看穿濃霧。

## 兩側受阻：保持煞停與恢復

另測 8 次：20／40 km/h × Raycast／v2 融合 × 持續受阻／12 s 移除前方三車。全部通過：保持組停穩至少 6 s；移除組持續等待，通道清空後恢復並完成。停穩至移除前最高速度 0.0000 km/h。

| 案例 | 結果 | 連續停穩秒數 | 停穩後／移除前最高 km/h |
|---|---|---:|---:|
| blocked_sides-20-clear-v0-raycast_1_8-hold | safe_stop | 6.02 | 0.0000 |
| blocked_sides-20-clear-v0-raycast_1_8-release | completed | 7.32 | 0.0000 |
| blocked_sides-20-clear-v0-vision_fusion-hold | safe_stop | 6.02 | 0.0000 |
| blocked_sides-20-clear-v0-vision_fusion-release | completed | 7.71 | 0.0000 |
| blocked_sides-40-clear-v0-raycast_1_8-hold | safe_stop | 6.00 | 0.0000 |
| blocked_sides-40-clear-v0-raycast_1_8-release | completed | 9.32 | 0.0000 |
| blocked_sides-40-clear-v0-vision_fusion-hold | safe_stop | 6.00 | 0.0000 |
| blocked_sides-40-clear-v0-vision_fusion-release | completed | 9.72 | 0.0000 |

## Unity 數值一致性

預設 v2 先通過巡航 smoke，153 個推論影格、無不必要介入。另將 Unity 的同一張 NCHW 輸入交給 CPU ONNX Runtime：最大類別分數差 0.000001669，≥0.25 候選框的最大座標差 0.000061035 像素，輸入 tensor 與存圖差值為 0；通過預設容許值。這是一張影像的整合驗證，非整個資料集精度測試。

## 如何解讀

v2 的離線辨識進步（同一 500 張合成集 mAP50：66.14% → 76.43%；ACDC：6.81% → 22.76%）與閉迴路控制結果是不同指標。固定信心門檻下，合成集 FP 441 → 9，但 FN 422 → 505；誤判減少也伴隨漏判增加。

目前視覺只提供類別／信心與有限的預測時距延長；路徑幾何、煞停和轉向仍由規則控制。改善辨識不保證能處理近距離切入或橫向穿越。此測試每條件只跑一次，且即時渲染／物理觸發並非完全確定性，不能將少數變動直接當作模型因果效果或真實道路碰撞率。

### v2 碰撞案例的第一次介入

| 情境 | 控制狀態 | 當時 TTC（s） | 決策來源 |
|---|---|---:|---|
| crossing-20-clear-v0 | Brake | 0.74 | Raycast |
| crossing-20-fog-v0 | Brake | 0.74 | Raycast |
| crossing-40-clear-v0 | Brake | 0.79 | Raycast + vision confirmation |
| crossing-40-fog-v0 | Brake | 0.79 | Raycast |
| cut_in-20-clear-v0 | EvadeLeft | 1.02 | Raycast |
| cut_in-20-clear-v1 | EvadeLeft | 1.32 | Raycast |
| cut_in-20-fog-v0 | EvadeLeft | 1.02 | Raycast |
| cut_in-20-fog-v1 | EvadeLeft | 1.32 | Raycast |
| cut_in-40-clear-v0 | EvadeLeft | 0.74 | Raycast + vision confirmation |
| cut_in-40-fog-v0 | EvadeLeft | 0.74 | Raycast |

這些是控制器第一次介入時記錄的預測 TTC，並非實際碰撞倒數。不能只靠換分類模型，便假設既有動態避讓策略會自動改善。

下一步應針對仍然碰撞的動態情境改善追蹤、相對速度／TTC 與煞停規劃，再用固定條件比較；不宜僅為本測試調低辨識門檻。此輪未訓練 v3、未測實體雷達或 UGen300。

## 原始證據與重現

- [v1 原始 48 案報告](V1/report.json)、[v2 原始 48 案報告](V2/report.json)、[逐案比較 CSV](paired-results.csv)。
- [保持／恢復報告](HoldReleaseV2/report.json)、[推論一致性](SmokeV2/parity-check.json)。
- [既有離線辨識評估](offline-evaluation.json)（本輪沿用，未重新訓練）。
- [相同案例設定](paired-config.json)、[保持／恢復設定](hold-release-config.json)、[來源雜湊](provenance.json)、[執行後完整性](final-integrity.json)。

從 Unity Editor 命令列執行（需圖形支援，不加 `-nographics`）；兩版僅替換 `-visionModel` 與輸出資料夾：

```powershell
& '<Unity Editor>/Unity.exe' -batchmode -projectPath '<checkout>' `
  -executeMethod TaiwanRider.Editor.AvoidanceBenchmark.Begin `
  -visionModel v2 -benchmarkConfig '<absolute path>/paired-config.json' `
  -benchmarkOutput '<new output directory>' -logFile '<log path>'
```

正常開啟專案省略 `-visionModel` 即使用 v2；傳入 `-visionModel v1` 可復現 v1。報告記錄實際載入的 `modelResourcePath`。v1／v2 資產均保留，既有影片及截圖仍為 v1 錄製，尚未重新錄影。
