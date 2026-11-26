package cn.sua17.newcosmos;

import android.net.Uri;
import java.util.Locale;

/**
 * 壳层常量与域名判定。
 *
 * 业务域（cosmos.sua17.cn / 测试服务器）与静态资源域（cosmos-static.sua17.cn）留在
 * WebView 内；其余 http(s) 链接一律交给系统浏览器。
 */
final class NcConfig {

    static final String CLIENT = BuildConfig.NC_CLIENT;
    static final String VERSION_NAME = BuildConfig.VERSION_NAME;
    static final int VERSION_CODE = BuildConfig.VERSION_CODE;
    static final String UPDATE_URL = BuildConfig.NC_UPDATE_URL;
    static final String DEFAULT_SERVER_URL = BuildConfig.NC_SERVER_URL;

    /** blob 下载的单个文件上限，避免 WebView JS 桥一次性占用过多内存。 */
    static final long MAX_BLOB_BYTES = 96L * 1024L * 1024L;

    static final String PROD_HOST = "cosmos.sua17.cn";
    static final String STATIC_HOST = "cosmos-static.sua17.cn";

    private NcConfig() {}

    static String hostOf(String url) {
        if (url == null) return null;
        try {
            String host = Uri.parse(url).getHost();
            return host == null ? null : host.toLowerCase(Locale.ROOT);
        } catch (Exception e) {
            return null;
        }
    }

    static boolean isTrustedHost(String host, String serverHost) {
        if (host == null) return false;
        String normalized = host.toLowerCase(Locale.ROOT);
        if (serverHost != null && normalized.equals(serverHost.toLowerCase(Locale.ROOT))) return true;
        return normalized.equals(PROD_HOST) || normalized.equals(STATIC_HOST);
    }

    static boolean isTrustedUri(Uri uri, String serverHost) {
        if (uri == null) return false;
        String scheme = uri.getScheme();
        if (scheme == null) return false;
        scheme = scheme.toLowerCase(Locale.ROOT);
        if (!scheme.equals("http") && !scheme.equals("https")) return false;
        return isTrustedHost(uri.getHost(), serverHost);
    }

    /** 生成 scheme://host[:port]，用于 WebView document-start 脚本的 origin 规则。 */
    static String originOf(String url) {
        try {
            Uri uri = Uri.parse(url);
            String scheme = uri.getScheme();
            String host = uri.getHost();
            if (scheme == null || host == null) return null;
            int port = uri.getPort();
            return port > 0 ? scheme + "://" + host + ":" + port : scheme + "://" + host;
        } catch (Exception e) {
            return null;
        }
    }
}
