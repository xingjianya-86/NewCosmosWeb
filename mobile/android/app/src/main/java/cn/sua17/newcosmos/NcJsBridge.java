package cn.sua17.newcosmos;

import android.net.Uri;
import android.webkit.JavascriptInterface;
import java.util.Locale;

/**
 * 暴露给页面的原生能力（window.NcShellNative）。
 * 所有方法都校验当前页面是否属于业务域，避免外链页面调用。
 */
public final class NcJsBridge {

    private final MainActivity activity;
    private final NcDownloader downloader;

    NcJsBridge(MainActivity activity, NcDownloader downloader) {
        this.activity = activity;
        this.downloader = downloader;
    }

    @JavascriptInterface
    public String getClient() {
        return NcConfig.CLIENT;
    }

    @JavascriptInterface
    public String getVersion() {
        return NcConfig.VERSION_NAME;
    }

    @JavascriptInterface
    public int getVersionCode() {
        return NcConfig.VERSION_CODE;
    }

    @JavascriptInterface
    public String getServerUrl() {
        return activity.getServerUrl();
    }

    @JavascriptInterface
    public String beginBlob(String name, String mime) {
        if (!activity.isTrustedPage()) return "";
        return downloader.beginBlob(name, mime);
    }

    @JavascriptInterface
    public void appendBlob(String id, String base64) {
        if (!activity.isTrustedPage()) return;
        downloader.appendBlob(id, base64);
    }

    @JavascriptInterface
    public void finishBlob(String id) {
        if (!activity.isTrustedPage()) return;
        downloader.finishBlob(id);
    }

    @JavascriptInterface
    public void abortBlob(String id) {
        if (!activity.isTrustedPage()) return;
        downloader.abortBlob(id);
    }

    @JavascriptInterface
    public void checkUpdate() {
        if (!activity.isTrustedPage()) return;
        activity.runOnUiThread(() -> activity.getUpdateManager().check(true));
    }

    @JavascriptInterface
    public void openExternal(String url) {
        if (!activity.isTrustedPage() || url == null) return;
        Uri uri;
        try {
            uri = Uri.parse(url);
        } catch (Exception e) {
            return;
        }
        String scheme = uri.getScheme();
        if (scheme == null) return;
        scheme = scheme.toLowerCase(Locale.ROOT);
        if (!scheme.equals("http") && !scheme.equals("https") && !scheme.equals("mailto") && !scheme.equals("tel")) {
            return;
        }
        final Uri target = uri;
        activity.runOnUiThread(() -> activity.openExternal(target));
    }
}
