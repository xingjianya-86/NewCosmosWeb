# WebView2 离线安装包缓存

Shell 安装包会检测目标机是否已安装 Microsoft Edge WebView2 Runtime。

- 如果本目录存在 `MicrosoftEdgeWebview2Setup.exe`，`build-installers.ps1` 会让 Shell 安装包
  在运行时按需解压并静默执行 `/silent /install`。
- 如果不存在，安装包仍可编译；安装时会提示用户下载：
  https://go.microsoft.com/fwlink/p/?LinkId=2124703

下载 Evergreen Bootstrapper 后放到：

```text
deploy\installer\cache\MicrosoftEdgeWebview2Setup.exe
```

该 exe 体积较大，已在 `deploy\.gitignore` 中忽略，不建议提交到仓库。
