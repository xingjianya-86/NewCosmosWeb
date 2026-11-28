# 帝皇权杖δ-me13 社会救助管理系统（NewCosmos Web）

NewCosmos锛堝師 .NET MAUI 姘戞斂绀句細鏁戝姪绯荤粺锛夌殑 Web 鍏ㄥ姛鑳借縼绉诲伐绋嬨€?
## 鐩爣褰㈡€?
- **Web**锛歏ue 3 + Element Plus锛屾闈㈡祻瑙堝櫒 + 鎵嬫満娴忚鍣?- **鏈嶅姟绔?*锛欰SP.NET Core锛圠inux锛夛紝閮ㄧ讲鍦?`10.110.0.2`
- **杈圭紭**锛歚10.110.0.1` 鍙彂 `index.html` + 鍙嶄唬 `/api/auth/*`
- **鏁版嵁**锛歅ostgreSQL锛坄10.110.0.2`锛?- **Office/鎵撳嵃**锛歐indows 鍥哄畾 PC 涓婄殑 `NewCosmos.Agent`锛屾寜璐︽埛缁戝畾鎵撳嵃鏈?- **褰╃エ**锛歚10.110.0.2` 涓婄殑 Python Worker

## 鐩綍

```text
legacy/NewCosmos.Desktop/   鍘?MAUI 婧愮爜锛堝彧璇诲弬鑰冿級
src/NewCosmos.Core/         棰嗗煙鏍稿績 net10.0锛堝凡鍙紪璇戯級
src/NewCosmos.Server/       ASP.NET Core API锛圥0 寤鸿涓級
src/NewCosmos.Office/       Office/Word/Excel 寮曟搸锛圵indows锛?src/NewCosmos.Agent/        Windows 鎵樼洏浠ｇ悊锛圤ffice/鎵撳嵃锛?src/NewCosmos.Shell/        Windows WebView2 澹筹紙鍚屼竴濂?Vue 椤甸潰锛?src/NewCosmos.LotteryWorker/ Python/TensorFlow Worker
web/                        Vue 3 + Element Plus
deploy/                     .1/.2/agent/lottery 閮ㄧ讲閰嶇疆
docs/                       鏋舵瀯銆丄PI 濂戠害銆佽縼绉昏鍒?```

## 鏋勫缓

```powershell
dotnet build src\NewCosmos.Core\NewCosmos.Core.csproj
dotnet build src\NewCosmos.Server\NewCosmos.Server.csproj
cd web; npm install; npm run dev
```

## 鏂囨。

- `docs/ARCHITECTURE.md`
- `docs/API_CONTRACT.md`
- `docs/MIGRATION_PLAN.md`
- `docs/WEBVIEW2_SHELL.md`
- `docs/PRINTER_BINDING.md`
- `docs/KNOWN_ISSUES.md`

## 开源许可

[GPL-3.0](LICENSE)。

第三方依赖许可：`PDFsharp` 6.5.0 起为 **AGPL-3.0 / 商业双许可**，本项目按 AGPL 使用
（服务端 / Agent / 测试启动时由 `PdfSharpLicenseBootstrap.EnsureLoaded()` 显式加载）；
GPLv3 与 AGPLv3 兼容，组合分发按 GPLv3。其余依赖（EPPlus、NPOI、SkiaSharp、Npgsql 等）
许可见各包自带 LICENSE。

