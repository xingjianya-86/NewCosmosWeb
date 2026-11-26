package cn.sua17.newcosmos;

import android.net.Uri;
import android.os.Handler;
import android.os.Looper;
import android.os.Message;
import android.webkit.WebResourceRequest;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import com.getcapacitor.Bridge;
import com.getcapacitor.BridgeWebChromeClient;

/**
 * 在 Capacitor 默认 WebChromeClient 之上接管 window.open / target="_blank"：
 * 业务域在当前 WebView 打开，其余地址交给系统浏览器。
 * 文件选择、拍照、JS 对话框等仍沿用 Capacitor 的实现。
 */
final class NcWebChromeClient extends BridgeWebChromeClient {

    private final MainActivity activity;
    private final String serverHost;
    private final Handler main = new Handler(Looper.getMainLooper());

    NcWebChromeClient(MainActivity activity, Bridge bridge, String serverHost) {
        super(bridge);
        this.activity = activity;
        this.serverHost = serverHost;
    }

    @Override
    public boolean onCreateWindow(WebView view, boolean isDialog, boolean isUserGesture, Message resultMsg) {
        WebView.HitTestResult hit = view.getHitTestResult();
        String url = hit == null ? null : hit.getExtra();
        if (url != null && !url.isEmpty()) {
            openTarget(view, url);
            return true;
        }

        // window.open() 无命中 URL 时，用临时 WebView 捕获首个导航地址。
        WebView probe = new WebView(view.getContext());
        probe.getSettings().setJavaScriptEnabled(false);
        probe.setWebViewClient(new WebViewClient() {
            @Override
            public boolean shouldOverrideUrlLoading(WebView probeView, WebResourceRequest request) {
                openTarget(view, request.getUrl().toString());
                destroyLater(probeView);
                return true;
            }

            @SuppressWarnings("deprecation")
            @Override
            public boolean shouldOverrideUrlLoading(WebView probeView, String targetUrl) {
                openTarget(view, targetUrl);
                destroyLater(probeView);
                return true;
            }
        });

        if (resultMsg != null && resultMsg.obj instanceof WebView.WebViewTransport) {
            WebView.WebViewTransport transport = (WebView.WebViewTransport) resultMsg.obj;
            transport.setWebView(probe);
            resultMsg.sendToTarget();
            return true;
        }
        probe.destroy();
        return false;
    }

    private void destroyLater(WebView webView) {
        main.postDelayed(() -> {
            try {
                webView.destroy();
            } catch (Exception ignored) {
                // ignore
            }
        }, 300);
    }

    private void openTarget(WebView view, String url) {
        try {
            Uri uri = Uri.parse(url);
            if (NcConfig.isTrustedUri(uri, serverHost)) {
                view.loadUrl(url);
            } else {
                activity.openExternal(uri);
            }
        } catch (Exception ignored) {
            // 无法解析的地址直接忽略
        }
    }
}
