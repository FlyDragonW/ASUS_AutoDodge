# v2：固定觸發時序與動態避閃比較

2026-10-14。**兩組都使用相同 v2 模型與新固定時序**，只交換避閃控制器。原控制器取自本次修改前的工作目錄；不是拿前一輪不同觸發方式的數字直接比較。

## 主測結果

48 個相同條件：6 情境 × 20／40 km/h × 晴天／濃霧 × 2 變體。下列依序為 **完成／安全停下／碰撞／超時**。

| 情境 | 原控制器 | 新控制器 |
|---|---:|---:|
| 切入 | 2 / 0 / 6 / 0 | 4 / 0 / 4 / 0 |
| 穿越 | 4 / 0 / 4 / 0 | 8 / 0 / 0 / 0 |
| 車道內違停 | 8 / 0 / 0 / 0 | 8 / 0 / 0 / 0 |
| 兩側受阻 | 0 / 8 / 0 / 0 | 0 / 8 / 0 / 0 |
| 空路 | 8 / 0 / 0 / 0 | 8 / 0 / 0 / 0 |
| 路旁車輛 | 8 / 0 / 0 / 0 | 8 / 0 / 0 / 0 |
| **合計** | **30 / 8 / 10 / 0** | **36 / 8 / 4 / 0** |

空路與路旁車的誤介入：**0 / 16 → 0 / 16**。靜態違停與兩側受阻的通過／停車均單獨列出，沒有併入「完成」膨脹通過率。

### 主測結果改變的條件

| 條件 | 原控制器 | 新控制器 |
|---|---|---|
| crossing-20-clear-v0 | collision | completed |
| crossing-20-fog-v0 | collision | completed |
| crossing-40-clear-v0 | collision | completed |
| crossing-40-fog-v0 | collision | completed |
| cut_in-40-clear-v0 | collision | completed |
| cut_in-40-fog-v0 | collision | completed |

ID 中的 v0／v1 是情境變體，所有列使用的辨識模型均為 **v2**。

### 仍然碰撞的配對案例

| 條件 | 自車碰撞前速度，km/h | 相對碰撞速度，km/h |
|---|---:|---:|
| cut_in-20-clear-v0 | 17.14 → 15.73 | 17.88 → 16.78 |
| cut_in-20-clear-v1 | 15.13 → 12.99 | 18.37 → 18.08 |
| cut_in-20-fog-v0 | 17.14 → 15.73 | 17.88 → 16.78 |
| cut_in-20-fog-v1 | 15.13 → 13.49 | 18.37 → 20.65 |

列出兩版都碰撞的同一條件，避免拿不同失敗組合的平均速度宣稱改善。尚未證明這些碰撞不可避免。

需要注意的退步：cut_in-20-fog-v1 的相對碰撞速度 18.37 → 20.65 km/h。自車速度下降不等於相對撞擊速度必然下降。

![兩個 40 km/h 主測案例的速度與路線偏移](dynamic-trajectories.png)

圖中只選取主測的兩個案例，用於說明煞車與側向避讓差異；完整結果與失敗案例以上表與逐案資料為準。

## 重跑與保留情境

| 組別 | 原控制器：完成／停下／碰撞／超時 | 新控制器：完成／停下／碰撞／超時 |
|---|---:|---:|
| 主測中的 16 個動態條件 | 6 / 0 / 10 / 0 | 12 / 0 / 4 / 0 |
| 相同 16 條件重跑 | 6 / 0 / 10 / 0 | 10 / 0 / 6 / 0 |
| 未用來調參的 16 個動態條件 | 14 / 0 / 2 / 0 | 14 / 0 / 2 / 0 |

相同條件跨輪的結果差異：

- Candidate: crossing-20-clear-v0，主測 `completed` → 重跑 `collision`。
- Candidate: crossing-20-fog-v0，主測 `completed` → 重跑 `collision`。

保留組在選擇控制器前就固定：30／50 km/h、衝突位置 0／4 m、觸發 TTA 0.95／1.45 s，晴天／濃霧，共 16 案。控制器凍結後才執行，沒有依保留組結果再調整。這是相同遊戲世界的額外條件，不是獨立真實道路測試；48 案主測則沿用已知開發條件。

<details><summary>保留情境逐案結果</summary>

| 條件 | 原控制器 | 新控制器 |
|---|---|---|
| cut_in-50-clear-h1 | completed | completed |
| cut_in-50-fog-h1 | completed | completed |
| crossing-30-clear-h0 | completed | completed |
| crossing-30-clear-h1 | completed | completed |
| crossing-30-fog-h0 | completed | completed |
| crossing-30-fog-h1 | completed | completed |
| crossing-50-clear-h0 | completed | completed |
| crossing-50-clear-h1 | completed | completed |
| crossing-50-fog-h0 | completed | completed |
| crossing-50-fog-h1 | completed | completed |
| cut_in-30-clear-h0 | collision | collision |
| cut_in-30-clear-h1 | completed | completed |
| cut_in-30-fog-h0 | collision | collision |
| cut_in-30-fog-h1 | completed | completed |
| cut_in-50-clear-h0 | completed | completed |
| cut_in-50-fog-h0 | completed | completed |

</details>

### 兩側受阻保持／移除後恢復

| 控制器 | 通過 | 停穩至移除前最高速度 |
|---|---:|---:|
| Baseline | 4 / 4 | 0.0000 km/h |
| Candidate | 4 / 4 | 0.0000 km/h |

20／40 km/h 各測保持受阻至少連續停穩 6 s，以及 12 s 移除三車後恢復並完成。判定包含結果、保持時間與停穩後速度，沒有只檢查第一次停車。

## 改了什麼

1. **固定時序**：情境計時／觸發放在 FixedUpdate，明確順序為情境 → 障礙物移動 → Raycast → 避閃 → 自車積分。自動輸入的平滑也放到同一物理步。benchmark 關閉 Rigidbody interpolation，記錄觸發 tick 與位置。
2. **動態預測**：從 Raycast 連續觀測估計速度變化，有限度外推正在加速靠近的橫向運動。沒有讀取 scenario ID、HazardActor 的未來路徑或真實速度。
3. **短期路徑比較**：以現有車輛軸距、制動力、油門／煞車／轉向變化率做約 2 s 以上的 bicycle rollout，比較原路煞停與左右煞車避讓。使用自車實際 chassis 外形，加 15 cm 規劃餘裕；物理碰撞盒不變。
4. **持續重算**：危險持續時約每 40 ms 重算；檢查 0.75／1.0／1.25 倍預估障礙速度。若煞車路徑皆受阻，只有完整預測路徑無重疊時才考慮維持巡航速度；否則全煞車，選預測碰撞較晚／間距較大的路徑。已開始的側向避讓有短暫穩定機制，減少目標來回切換。
5. **靜態策略保留**：靜態側向走廊檢查、兩側受阻時停穩保持，以及前方清空後恢復，仍走原有流程。

v2 權重、相機前處理、信心 0.60、配對門檻與 350 ms 時效未變。視覺仍只提供類別／框與有限預測時距（Raycast 1.8 s；新鮮車／機車證據 2.2／2.4 s）。這次改善屬於**控制與時序修正**，不是重新訓練模型，也沒有證明每個避閃成功都依賴視覺。

## 時序驗證與煞停校準

Raycast-only 的 8 個晴天動態案例分別在 30／90 FPS 下執行：觸發 tick、結果分類全部相同，觸發位置最大差異 **0.00003939 m**。先前探索使用 0.00001 m 浮點容許值未過，原紀錄也保留；採 0.001 m 的位置誤差檢查通過，沒有宣稱逐位元確定性。

| 配對組 | 觸發 tick 不同的案例數 | 最大觸發位置差 |
|---|---:|---:|
| FinalPilot | 0 | 0.00002802 m |
| CandidateMain | 0 | 0.00002825 m |
| CandidateRepeat | 0 | 0.00002825 m |
| CandidateHold | 0 | 0.00000000 m |
| CandidateHeldout | 0 | 0.00000000 m |

固定的是觸發物理時序，並非整個 Unity 模擬逐位元一致。物理／Raycast 的邊界觀測、浮點與非同步視覺仍可能讓後續介入相差一個物理步；開發重跑曾出現低速穿越在碰撞與完成間變動，詳見所有保留的 pilot。

| 初速 | 全煞車停止位移 | 停止時間 |
|---|---:|---:|
| 20 km/h | 2.492 m | 0.860 s |
| 40 km/h | 9.293 m | 1.640 s |

位移是起終點距離，這個校準使用直線與全煞車；未增加車輛制動力或轉向極限。

## 即時效能與重要限制

| 主測指標 | 原控制器 | 新控制器 |
|---|---:|---:|
| 平均擷取至結果延遲（ms） | 37.2 | 40.8 |
| 每案推論 Hz 中位數（牆鐘） | 8.93 | 8.91 |
| 每案渲染 FPS 中位數（牆鐘） | 59.6 | 59.3 |
| 推論完成影格 | 3046 | 3435 |
| 新鮮配對影格 | 757 | 882 |
| 讀回時已超過 350 ms 的影格 | 0 | 4 |
| 視覺延長預測時距的介入次數 | 8 | 10 |

成功動態案例中的最高橫向加速度：**13.63 → 23.73 m/s²**。新控制器縮短動態轉向的追蹤距離，可能以較激烈的轉向換取遊戲內避讓；目前 bicycle model 沒有輪胎摩擦、傾角與騎士穩定性約束。這項結果不能直接用於真實車輛控制。

Unity 6000.4.10f1、RTX 3060 Laptop GPU、GPUCompute、50 Hz 物理、60 FPS 渲染目標、timeScale=1。延遲包含擷取、渲染、排程與讀回；每案 Hz 使用牆鐘時間。連續執行的溫度／時脈見 [gpu.txt](gpu.txt)，不是受控硬體速度測試或 UGen300 測量。

路線與 collider 幾何來自模擬器，Raycast 不受濃霧影響。短期速度外推、理想距離資訊與簡化車輛模型仍有限制；幾十個固定案例不是道路事故率估計。

## 可重現資料

- [主測逐案 CSV](CandidateMain-paired.csv)、[彙總與配對 JSON](summary.json)。每組目錄的 report.json 含完整設定、逐案事件與 telemetry；provenance.json 含控制器、模型與設定 SHA。
- [凍結候選記錄](frozen-candidate.json)、[共用來源 SHA](shared-manifest.json)、[受保護輸入 SHA](before-hashes.json)、[完成後完整性檢查](final-integrity.json)。
- [原控制器](controllers/baseline.cs)／[新控制器](controllers/improved.cs)，以及 shared 目錄中的實際共用程式快照。v2 SHA：`d0cd520e2b52d2518735eb3d5a04939a27c0777788bae7d8ad2ea9b37b70d219`。
- [測試計畫](PROTOCOL.md)；完整比較共 168 次（96 主測、32 動態重跑、8 保持／恢復、32 保留條件），另有時序／制動校準與開發 pilot。正式比較採用預先指定的完整組數，保留失敗結果。

一次基準執行因終端工作階段消失且 Unity 程序已不在，於 44／48 案中斷；[不完整原始報告](BaselineMainInterrupted/report.json) 與 [中斷記錄](interruption.json) 保留，未計入上表，隨後重新執行完整 48 案。並非依測試分數捨棄結果。

關閉已開啟的 Unity Editor 後，在儲存庫根目錄執行；將路徑換成自己的安裝與 checkout。腳本驗證共用程式、模型／場景 SHA，使用已存在的 v2，結束後還原原先控制器。

```powershell
& ./Validation/DynamicAvoidance/reproduce.ps1 `
  -Unity '<Unity Editor>/Unity.exe' -ProjectPath '<checkout>' `
  -OutputDirectory '<new output directory>' -Controller baseline -Suite main
# 以另一個新的 OutputDirectory 重跑 -Controller improved。
# Suite 亦支援 dynamic、hold、heldout、timing30、timing90、brake。
```

<details><summary>全部開發 pilot（完成／停下／碰撞／超時）</summary>

只用既有 8 個晴天動態條件調整，未接觸保留組的執行結果。

| 開發版本 | 結果 |
|---|---:|
| BaselinePilot | 3 / 0 / 5 / 0 |
| Candidate01Pilot | 3 / 0 / 5 / 0 |
| Candidate02Pilot | 4 / 0 / 4 / 0 |
| Candidate03Pilot | 5 / 0 / 3 / 0 |
| Candidate04Pilot | 5 / 0 / 3 / 0 |
| Candidate05Pilot | 6 / 0 / 2 / 0 |
| FinalPilot | 5 / 0 / 3 / 0 |

candidate05 選定後僅清理命名、未使用欄位、診斷輸出與不可達分支，凍結為 improved，隨後重跑 FinalPilot 與完整比較。FinalPilot 及後續波動均列入報告，未挑選最好的一輪。

</details>
