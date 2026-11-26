package cn.sua17.newcosmos;

import android.net.Uri;
import android.net.http.SslError;
import android.webkit.SslErrorHandler;
import android.webkit.WebResourceError;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.webkit.WebView;
import com.getcapacitor.Bridge;
import com.getcapacitor.BridgeWebViewClient;

/** 导航策略、错误页与注入时机。 */
final class NcWebViewClient extends BridgeWebViewClient {

    private final MainActivity activity;
    private final String serverHost;

    NcWebViewClient(MainActivity activity, Bridge bridge, String serverHost) {
        super(bridge);
        this.activity = activity;
        this.serverHost = serverHost;
    }

    @Override
    public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
        return handleUri(request.getUrl());
    }

    @SuppressWarnings("deprecation")
    @Override
    public boolean shouldOverrideUrlLoading(WebView view, String url) {
        return handleUri(Uri.parse(url));
    }

    private boolean handleUri(Uri uri) {
        if (uri == null) return false;
        String scheme = uri.getScheme();
        if (scheme == null) return false;
        scheme = scheme.toLowerCase(java.util.Locale.ROOT);

        if (scheme.equals("http") || scheme.equals("https")) {
            if (NcConfig.isTrustedUri(uri, serverHost)) {
                return false; // 业务域/静态域留在壳内
            }
            activity.openExternal(uri);
            return true;
        }

        if (scheme.equals("about") || scheme.equals("data") || scheme.equals("blob") || scheme.equals("file") || scheme.equals("content")) {
            return false;
        }
        if (scheme.equals("javascript")) {
            return true;
        }

        // tel: / mailto: / weixin: / intent: …交给系统
        activity.openExternal(uri);
        return true;
    }

    @Override
    public void onPageStarted(WebView view, String url, android.graphics.Bitmap favicon) {
        super.onPageStarted(view, url, favicon);
        activity.onPageStarted(url);
        activity.injectShellScript(view);
    }

    @Override
    public void onPageCommitVisible(WebView view, String url) {
        super.onPageCommitVisible(view, url);
        activity.onPageReady(url);
        activity.injectShellScript(view);
    }

    @Override
    public void onPageFinished(WebView view, String url) {
        super.onPageFinished(view, url);
        activity.onPageReady(url);
        activity.injectShellScript(view);
    }

    @Override
    public void onReceivedError(WebView view, WebResourceRequest request, WebResourceError error) {
        if (request != null && request.isForMainFrame()) {
            CharSequence description = error == null ? null : error.getDescription();
            activity.onMainFrameError(description == null ? "" : description.toString());
            return;
        }
        super.onReceivedError(view, request, error);
    }

    @Override
    public void onReceivedHttpError(WebView view, WebResourceRequest request, WebResourceResponse errorResponse) {
        if (request != null && request.isForMainFrame() && errorResponse != null && errorResponse.getStatusCode() >= 400) {
            activity.onMainFrameError("HTTP " + errorResponse.getStatusCode());
            return;
        }
        super.onReceivedHttpError(view, request, errorResponse);
    }

    @Override
    public void onReceivedSslError(WebView view, SslErrorHandler handler, SslError error) {
        // 生产壳永不忽略证书错误；证书异常按网络错误处理。
        handler.cancel();
        activity.onMainFrameError("SSL " + (error == null ? "" : error.getPrimaryError()));
    }

    @Override
    public boolean onRenderProcessGone(WebView view, android.webkit.RenderProcessGoneDetail detail) {
        activity.onRendererGone();
        return true;
    }
}
