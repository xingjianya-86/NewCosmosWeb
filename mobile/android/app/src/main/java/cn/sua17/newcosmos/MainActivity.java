package cn.sua17.newcosmos;

import android.Manifest;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.view.ViewGroup;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.widget.Toast;
import androidx.core.content.ContextCompat;
import androidx.webkit.WebViewCompat;
import androidx.webkit.WebViewFeature;
import com.getcapacitor.Bridge;
import com.getcapacitor.BridgeActivity;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;

/**
 * Android 壳主 Activity。
 *
 * 业务 UI 仍由 https://cosmos.sua17.cn 提供；这里只补齐原生能力：
 * 下载、返回键、外链、错误页、文件选择/拍照、状态栏安全区、客户端版本头与应用内更新。
 */
public class MainActivity extends BridgeActivity {

    private static final int REQ_SHELL_PERMISSIONS = 4711;
    private static final int REQ_STORAGE_PERMISSION = 4712;

    private final Handler mainHandler = new Handler(Looper.getMainLooper());

    private String serverUrl = NcConfig.DEFAULT_SERVER_URL;
    private String serverHost = NcConfig.PROD_HOST;
    private String injectedScript = "";

    private NcErrorView errorView;
    private NcDownloader downloader;
    private NcUpdateManager updateManager;

    private volatile boolean trustedPage;
    private volatile boolean pageLoaded;
    private boolean pageError;
    private boolean rendererGone;
    private Runnable watchdog;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        Bridge bridge = getBridge();
        if (bridge == null) return;
        setupShell(bridge);
    }

    private void setupShell(Bridge bridge) {
        if (bridge.getServerUrl() != null && !bridge.getServerUrl().isEmpty()) {
            serverUrl = bridge.getServerUrl();
        }
        String host = NcConfig.hostOf(serverUrl);
        serverHost = host == null ? NcConfig.PROD_HOST : host;
        // 首屏由 Capacitor 在 onCreate 中先行 loadUrl，onPageStarted 可能早于本回调；
        // 启动地址本身是可信业务域，先置为 true，后续页面跳转再按实际 URL 校正。
        trustedPage = NcConfig.isTrustedUri(parse(serverUrl), serverHost);

        WebView webView = bridge.getWebView();
        if (webView == null) return;

        WebSettings settings = webView.getSettings();
        settings.setSupportMultipleWindows(true);
        String userAgent = settings.getUserAgentString();
        if (userAgent != null && !userAgent.contains("NewCosmosAndroid/")) {
            settings.setUserAgentString(userAgent + " NewCosmosAndroid/" + NcConfig.VERSION_NAME);
        }

        errorView = new NcErrorView(this, new NcErrorView.Listener() {
            @Override
            public void onRetry() {
                retryLoad();
            }

            @Override
            public void onOpenBrowser() {
                openExternal(Uri.parse(serverUrl));
            }
        });
        addContentView(errorView, new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        updateManager = new NcUpdateManager(this);
        downloader = new NcDownloader(this, webView);
        NcJsBridge jsBridge = new NcJsBridge(this, downloader);

        webView.setWebChromeClient(new NcWebChromeClient(this, bridge, serverHost));
        bridge.setWebViewClient(new NcWebViewClient(this, bridge, serverHost));
        webView.setDownloadListener(downloader);
        webView.addJavascriptInterface(jsBridge, "NcShellNative");

        injectedScript = NcInjector.script(this);
        registerDocumentStartScript(webView);
        injectShellScript(webView);

        mainHandler.postDelayed(() -> {
            if (updateManager != null && !isFinishing() && !isDestroyed()) {
                updateManager.check(false);
            }
        }, 4000);

        requestStartupPermissions();
    }

    private void registerDocumentStartScript(WebView webView) {
        if (injectedScript.isEmpty() || !WebViewFeature.isFeatureSupported(WebViewFeature.DOCUMENT_START_SCRIPT)) {
            return;
        }
        Set<String> origins = new HashSet<>();
        String origin = NcConfig.originOf(serverUrl);
        if (origin != null) origins.add(origin);
        origins.add("https://" + NcConfig.STATIC_HOST);
        try {
            WebViewCompat.addDocumentStartJavaScript(webView, injectedScript, origins);
        } catch (Exception ignored) {
            // 旧 WebView 走 onPageStarted 注入兜底
        }
    }

    void injectShellScript(WebView webView) {
        if (webView == null || injectedScript.isEmpty()) return;
        try {
            webView.evaluateJavascript(injectedScript, null);
        } catch (Exception ignored) {
            // 页面销毁过程中注入失败可忽略
        }
    }

    // ------------------------------------------------------------------
    // WebView 回调（由 NcWebViewClient 调用）
    // ------------------------------------------------------------------

    void onPageStarted(String url) {
        pageLoaded = false;
        pageError = false;
        rendererGone = false;
        trustedPage = NcConfig.isTrustedUri(parse(url), serverHost);
        startWatchdog();
    }

    void onPageReady(String url) {
        pageLoaded = true;
        cancelWatchdog();
        if (!pageError) hideError();
    }

    void onMainFrameError(String detail) {
        if (isFinishing() || isDestroyed()) return;
        pageLoaded = false;
        pageError = true;
        cancelWatchdog();
        if (errorView != null) {
            errorView.show(detail);
        }
    }

    void onRendererGone() {
        rendererGone = true;
        onMainFrameError(getString(R.string.nc_error_renderer));
    }

    private void retryLoad() {
        hideError();
        pageError = false;
        if (rendererGone) {
            recreate();
            return;
        }
        Bridge bridge = getBridge();
        if (bridge == null || bridge.getWebView() == null) return;
        pageLoaded = false;
        startWatchdog();
        bridge.getWebView().loadUrl(serverUrl);
    }

    private void startWatchdog() {
        cancelWatchdog();
        watchdog = () -> {
            if (!pageLoaded && !isFinishing() && !isDestroyed()) {
                onMainFrameError(getString(R.string.nc_error_timeout));
            }
        };
        mainHandler.postDelayed(watchdog, 25000);
    }

    private void cancelWatchdog() {
        if (watchdog != null) {
            mainHandler.removeCallbacks(watchdog);
            watchdog = null;
        }
    }

    private void hideError() {
        if (errorView != null) errorView.hide();
    }

    private static Uri parse(String url) {
        try {
            return Uri.parse(url);
        } catch (Exception e) {
            return Uri.EMPTY;
        }
    }

    // ------------------------------------------------------------------
    // 外链
    // ------------------------------------------------------------------

    boolean openExternal(Uri uri) {
        if (uri == null) return false;
        String scheme = uri.getScheme();
        if (scheme == null) return false;
        scheme = scheme.toLowerCase(Locale.ROOT);
        if (scheme.equals("javascript") || scheme.equals("file") || scheme.equals("content")) {
            return false;
        }

        Intent intent = null;
        try {
            if (scheme.equals("intent")) {
                intent = Intent.parseUri(uri.toString(), Intent.URI_INTENT_SCHEME);
                intent.addCategory(Intent.CATEGORY_BROWSABLE);
                intent.setComponent(null);
            } else {
                intent = new Intent(Intent.ACTION_VIEW, uri);
            }
            intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            startActivity(intent);
            return true;
        } catch (Exception e) {
            String fallback = intent == null ? null : intent.getStringExtra("browser_fallback_url");
            if (fallback != null && !fallback.isEmpty()) {
                try {
                    startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse(fallback)).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
                    return true;
                } catch (Exception ignored) {
                    // fall through
                }
            }
            Toast.makeText(this, R.string.nc_external_unavailable, Toast.LENGTH_SHORT).show();
            return false;
        }
    }

    // ------------------------------------------------------------------
    // 返回键
    // ------------------------------------------------------------------

    @SuppressWarnings("deprecation")
    @Override
    public void onBackPressed() {
        if (rendererGone) {
            finish();
            return;
        }
        Bridge bridge = getBridge();
        WebView webView = bridge == null ? null : bridge.getWebView();
        if (webView != null && webView.canGoBack()) {
            webView.goBack();
            return;
        }
        super.onBackPressed();
    }

    // ------------------------------------------------------------------
    // 权限：相机用于拍照上传；存储仅 Android 9 及以下下载需要；通知用于下载完成提示
    // ------------------------------------------------------------------

    private void requestStartupPermissions() {
        List<String> missing = new ArrayList<>();
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU && !isGranted(Manifest.permission.POST_NOTIFICATIONS)) {
            missing.add(Manifest.permission.POST_NOTIFICATIONS);
        }
        if (!isGranted(Manifest.permission.CAMERA)) {
            missing.add(Manifest.permission.CAMERA);
        }
        if (Build.VERSION.SDK_INT <= Build.VERSION_CODES.P && !isGranted(Manifest.permission.WRITE_EXTERNAL_STORAGE)) {
            missing.add(Manifest.permission.WRITE_EXTERNAL_STORAGE);
        }
        if (!missing.isEmpty()) {
            requestPermissions(missing.toArray(new String[0]), REQ_SHELL_PERMISSIONS);
        }
    }

    private boolean isGranted(String permission) {
        return ContextCompat.checkSelfPermission(this, permission) == PackageManager.PERMISSION_GRANTED;
    }

    boolean hasStoragePermission() {
        return Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q || isGranted(Manifest.permission.WRITE_EXTERNAL_STORAGE);
    }

    void requestStoragePermission() {
        if (Build.VERSION.SDK_INT <= Build.VERSION_CODES.P) {
            requestPermissions(new String[] { Manifest.permission.WRITE_EXTERNAL_STORAGE }, REQ_STORAGE_PERMISSION);
        }
    }

    @Override
    public void onRequestPermissionsResult(int requestCode, String[] permissions, int[] grantResults) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == REQ_STORAGE_PERMISSION && grantResults.length > 0 && grantResults[0] != PackageManager.PERMISSION_GRANTED) {
            Toast.makeText(this, R.string.nc_storage_denied, Toast.LENGTH_SHORT).show();
        }
    }

    // ------------------------------------------------------------------
    // 生命周期
    // ------------------------------------------------------------------

    @Override
    public void onResume() {
        super.onResume();
        if (updateManager != null) updateManager.onResume();
    }

    @Override
    public void onDestroy() {
        mainHandler.removeCallbacksAndMessages(null);
        cancelWatchdog();
        if (downloader != null) downloader.destroy();
        if (updateManager != null) updateManager.destroy();
        super.onDestroy();
    }

    // ------------------------------------------------------------------
    // 供 NcJsBridge 使用
    // ------------------------------------------------------------------

    String getServerUrl() {
        return serverUrl;
    }

    NcUpdateManager getUpdateManager() {
        return updateManager;
    }

    boolean isTrustedPage() {
        return trustedPage;
    }
}
