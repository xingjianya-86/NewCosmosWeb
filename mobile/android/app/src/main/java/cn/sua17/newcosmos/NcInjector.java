package cn.sua17.newcosmos;

import android.content.Context;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;

/** 读取 assets/nc-shell/header-inject.js，注入版本号后交给 WebView。 */
final class NcInjector {

    private static final String ASSET_PATH = "nc-shell/header-inject.js";
    private static String cached;

    private NcInjector() {}

    static synchronized String script(Context context) {
        if (cached != null) return cached;
        try (InputStream in = context.getAssets().open(ASSET_PATH)) {
            ByteArrayOutputStream out = new ByteArrayOutputStream();
            byte[] buffer = new byte[8192];
            int read;
            while ((read = in.read(buffer)) != -1) {
                out.write(buffer, 0, read);
            }
            String raw = out.toString(StandardCharsets.UTF_8.name());
            cached = raw.replace("__NC_VERSION__", NcConfig.VERSION_NAME);
        } catch (IOException e) {
            // 注入脚本缺失不应阻塞页面；原生 UA 仍会带上客户端版本。
            cached = "";
        }
        return cached;
    }
}
