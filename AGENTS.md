# NewCosmos Web 寮€鍙戣鑼?
## 鏋舵瀯

```text
web (Vue 3 + Element Plus)
  鈹溾攢 椤甸潰锛?1 cosmos.sua17.cn
  鈹溾攢 涓氬姟 API锛?2 cosmos-static.sua17.cn:20000
  鈹斺攢 璁よ瘉 API锛?1 鍚屾簮 /api/auth

src/NewCosmos.Core     棰嗗煙/DB/涓氬姟鏈嶅姟锛坣et10.0锛屾棤 MAUI锛?src/NewCosmos.Server   ASP.NET Core锛堥儴缃?.2锛?src/NewCosmos.Office   Word/Excel/PDF 寮曟搸锛坣et10.0-windows锛?src/NewCosmos.Agent    Windows 鎵樼洏鎵撳嵃/鏂囦功浠ｇ悊
src/NewCosmos.Shell    Windows WebView2 澹筹紙鍚屼竴濂?Vue 椤甸潰锛?src/NewCosmos.LotteryWorker  Python/TensorFlow锛?2 瀹瑰櫒锛?legacy/                鍘?MAUI 婧愮爜锛屽彧璇诲弬鑰?```

## 閾佸緥

1. 涓氬姟鍏紡/鐘舵€佹満/鏍￠獙鍙湪 `NewCosmos.Core`锛沇eb/Agent/Server 涓嶉噸鎶勩€?2. 閲戦骞村€煎彛寰勩€佸垎绫诲垽瀹氥€佺姸鎬佹満浠?`legacy/NewCosmos.Desktop` 涓鸿涔夋潈濞併€?3. SQL 鍙敤 `$1..$n` 鍙傛暟锛涘姩鎬佽〃鍚嶅繀椤荤櫧鍚嶅崟銆?4. 鏈嶅姟绔己鍒舵潈闄?鏁版嵁鑼冨洿锛涗笉淇′换鍓嶇浼犳潵鐨?userId/orgId銆?5. Office/鎵撳嵃鍙湪 Windows Agent锛汱inux 鏈嶅姟绔笉璺?COM銆?6. 鎵撳嵃鏈烘寜璐︽埛缁戝畾锛歚user_id + device_id + printer_name`锛涗换鍔″浐鍖栫洰鏍囷紝鍙湁瀵瑰簲 Agent 鑳借棰嗐€?7. 涓嶆彁浜ゅ瘑鐮併€乼oken銆乣config/database.ini`銆乣*.local.*`銆乲eystore銆?8. `.1` 鍙彂 index 鍜?auth锛涗笟鍔″ぇ娴侀噺涓嶅緱缁忚繃 `.1`銆?9. 鏂伴〉闈㈠繀椤诲悓鏃惰€冭檻妗岄潰涓庢墜鏈猴紱鎵嬫満绔姛鑳藉绛夈€佷氦浜掗€傞厤銆?10. 鏀规暟鎹簱蹇呴』璧?`deploy/database` 杩佺Щ锛屼笉鐮村潖鏃ф闈㈢銆?
## 鏋勫缓

```powershell
dotnet build NewCosmosWeb.slnx
dotnet test tests\NewCosmos.Core.Tests\NewCosmos.Core.Tests.csproj
cd web; npm install; npm run build
```

## 鐩綍鍛藉悕

- C# 椤圭洰锛歚NewCosmos.*`
- C# 鍛藉悕绌洪棿锛歚NewCosmos.*`
- 鍓嶇鐩綍锛歚views` / `components` / `api` / `stores` / `layouts`
- 鏁版嵁搴撹〃锛氭部鐢?`nc_biz_` / `nc_sys_` / `nc_config_` / `nc_dict_` / `nc_perm_` / `nc_regions_`
- 鏂囨。锛歚docs/YYYYMMDD_涓婚.md` 鎴栧浐瀹氫富棰?`docs/ARCHITECTURE.md`

## 鎻愪氦

- 姣忔壒涓€涓彁浜わ紱鎻愪氦淇℃伅鍐欐竻鈥滄敼浜嗕粈涔?涓轰粈涔堚€濄€?- 鎻愪氦鍓嶈嚦灏戯細`dotnet build` + `npm run build` 閫氳繃锛涗笉鎻愪氦鍗婃垚鍝併€?
