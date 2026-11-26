package cn.sua17.newcosmos;

import android.app.AlertDialog;
import android.content.ActivityNotFoundException;
import android.content.Intent;
import android.content.pm.PackageInfo;
import android.net.Uri;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.provider.Settings;
import android.widget.ProgressBar;
import android.widget.Toast;
import androidx.core.content.FileProvider;
import java.io.BufferedInputStream;
import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.security.MessageDigest;
import java.util.Locale;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicBoolean;
import org.json.JSONObject;

/**
 * 启动更新检查：
 *   GET https://cosmos-static.sua17.cn:20000/downloads/android/latest.json
 *   { versionCode, versionName, url, sha256, notes }
 * 有新版本时提示，下载 APK 到应用缓存，校验 SHA256 与包名/versionCode 后经 FileProvider
 * 交给系统安装器（Android 8+ 需用户允许“安装未知应用”）。
 */
final class NcUpdateManager {

    private final MainActivity activity;
    private final Handler main = new Handler(Looper.getMainLooper());
    private final ExecutorService executor = Executors.newSingleThreadExecutor();

    private volatile boolean checking;
    private volatile boolean destroyed;
    private AlertDialog promptDialog;
    private AlertDialog progressDialog;
    private ProgressBar progressBar;
    private File pendingApk;
    private AtomicBoolean downloadCancelled;

    NcUpdateManager(MainActivity activity) {
        this.activity = activity;
    }

    void check(boolean manual) {
        if (destroyed) return;
        if (checking) {
            if (manual) toast(activity.getString(R.string.nc_update_manifest_failed));
            return;
        }
        checking = true;

        executor.execute(() -> {
            UpdateInfo info = null;
            try {
                info = fetchManifest();
            } catch (Exception e) {
                info = null;
            } finally {
                checking = false;
            }

            final UpdateInfo resolved = info;
            main.post(() -> {
                if (destroyed || activity.isFinishing() || activity.isDestroyed()) return;
                if (resolved == null) {
                    if (manual) toast(activity.getString(R.string.nc_update_manifest_failed));
                    return;
                }
                if (resolved.versionCode > NcConfig.VERSION_CODE) {
                    showPrompt(resolved);
                } else if (manual) {
                    toast(activity.getString(R.string.nc_update_already_latest));
                }
            });
        });
    }

    private UpdateInfo fetchManifest() throws Exception {
        HttpURLConnection connection = open(NcConfig.UPDATE_URL);
        try {
            int code = connection.getResponseCode();
            if (code < 200 || code >= 300) {
                throw new IllegalStateException("HTTP " + code);
            }
            try (InputStream in = new BufferedInputStream(connection.getInputStream())) {
                byte[] buffer = new byte[8192];
                java.io.ByteArrayOutputStream out = new java.io.ByteArrayOutputStream();
                int read;
                int total = 0;
                while ((read = in.read(buffer)) != -1) {
                    total += read;
                    if (total > 64 * 1024) throw new IllegalStateException("manifest too large");
                    out.write(buffer, 0, read);
                }
                JSONObject json = new JSONObject(stripBom(out.toString("UTF-8")));
                UpdateInfo info = new UpdateInfo();
                info.versionCode = json.optLong("versionCode", 0L);
                info.versionName = json.optString("versionName", "").trim();
                info.url = json.optString("url", "").trim();
                info.sha256 = json.optString("sha256", "").trim().toLowerCase(Locale.ROOT);
                info.notes = json.optString("notes", "").trim();
                if (info.versionCode <= 0 || info.url.isEmpty()) {
                    throw new IllegalStateException("manifest missing fields");
                }
                if (info.sha256.length() != 64) {
                    throw new IllegalStateException("manifest missing sha256");
                }
                return info;
            }
        } finally {
            connection.disconnect();
        }
    }

    private static String stripBom(String text) {
        return text != null && !text.isEmpty() && text.charAt(0) == '\uFEFF' ? text.substring(1) : text;
    }

    private HttpURLConnection open(String url) throws Exception {
        String current = url;
        for (int i = 0; i < 4; i++) {
            HttpURLConnection connection = (HttpURLConnection) new URL(current).openConnection();
            connection.setInstanceFollowRedirects(false);
            connection.setConnectTimeout(15000);
            connection.setReadTimeout(60000);
            connection.setRequestProperty("Accept", "application/json, application/octet-stream, */*");
            connection.setRequestProperty("X-NC-Client", NcConfig.CLIENT);
            connection.setRequestProperty("X-NC-App-Version", NcConfig.VERSION_NAME);
            connection.setRequestProperty("User-Agent", "NewCosmosAndroid/" + NcConfig.VERSION_NAME);
            int code;
            try {
                code = connection.getResponseCode();
            } catch (Exception e) {
                connection.disconnect();
                throw e;
            }
            if (code == 301 || code == 302 || code == 303 || code == 307 || code == 308) {
                String location = connection.getHeaderField("Location");
                connection.disconnect();
                if (location == null || location.isEmpty()) throw new IllegalStateException("redirect without location");
                current = new URL(new URL(current), location).toString();
                continue;
            }
            return connection;
        }
        throw new IllegalStateException("too many redirects");
    }

    private void showPrompt(UpdateInfo info) {
        if (promptDialog != null && promptDialog.isShowing()) return;
        if (activity.isFinishing() || activity.isDestroyed()) return;

        String displayName = info.versionName.isEmpty() ? String.valueOf(info.versionCode) : info.versionName;
        String notes = info.notes.isEmpty() ? "" : info.notes;
        String message = activity.getString(R.string.nc_update_message, NcConfig.VERSION_NAME, displayName, notes);
        promptDialog = new AlertDialog.Builder(activity)
            .setTitle(activity.getString(R.string.nc_update_title, displayName))
            .setMessage(message)
            .setPositiveButton(R.string.nc_update_now, (dialog, which) -> startDownload(info))
            .setNegativeButton(R.string.nc_update_later, null)
            .setOnDismissListener(dialog -> promptDialog = null)
            .create();
        promptDialog.show();
    }

    private void startDownload(UpdateInfo info) {
        if (destroyed) return;
        final AtomicBoolean cancelled = new AtomicBoolean(false);
        downloadCancelled = cancelled;
        showProgress();

        executor.execute(() -> {
            File apk = null;
            String error = null;
            try {
                apk = download(info, cancelled);
                if (apk == null) {
                    error = null; // 用户取消
                } else {
                    main.post(this::setVerifying);
                    verifySha256(apk, info.sha256);
                    verifyPackage(apk, info);
                }
            } catch (Exception e) {
                error = e.getMessage() == null ? e.getClass().getSimpleName() : e.getMessage();
                if (apk != null && apk.exists()) {
                    //noinspection ResultOfMethodCallIgnored
                    apk.delete();
                }
                apk = null;
            }

            final File result = apk;
            final String failure = error;
            main.post(() -> {
                dismissProgress();
                if (destroyed) return;
                if (result == null) {
                    if (failure != null && !failure.isEmpty()) {
                        toast(activity.getString(R.string.nc_update_download_failed, failure));
                    }
                    return;
                }
                install(result);
            });
        });
    }

    private File download(UpdateInfo info, AtomicBoolean cancelled) throws Exception {
        File dir = new File(activity.getExternalCacheDir() != null ? activity.getExternalCacheDir() : activity.getCacheDir(), "updates");
        if (!dir.exists() && !dir.mkdirs()) {
            throw new IllegalStateException("cache dir unavailable");
        }
        // 清理旧安装包
        File[] olds = dir.listFiles();
        if (olds != null) {
            for (File old : olds) {
                //noinspection ResultOfMethodCallIgnored
                old.delete();
            }
        }

        String safeName = info.versionName.replaceAll("[^0-9A-Za-z._-]", "_");
        if (safeName.isEmpty()) safeName = String.valueOf(info.versionCode);
        File apk = new File(dir, "newcosmos-" + safeName + ".apk");

        HttpURLConnection connection = open(info.url);
        try {
            connection.setConnectTimeout(15000);
            connection.setReadTimeout(60000);
            int code = connection.getResponseCode();
            if (code < 200 || code >= 300) {
                throw new IllegalStateException("HTTP " + code);
            }
            long total = connection.getContentLengthLong();
            try (InputStream in = new BufferedInputStream(connection.getInputStream()); FileOutputStream out = new FileOutputStream(apk)) {
                byte[] buffer = new byte[64 * 1024];
                long written = 0;
                int read;
                long lastUpdate = 0;
                while ((read = in.read(buffer)) != -1) {
                    if (cancelled.get()) {
                        //noinspection ResultOfMethodCallIgnored
                        apk.delete();
                        return null;
                    }
                    out.write(buffer, 0, read);
                    written += read;
                    long now = System.currentTimeMillis();
                    if (total > 0 && now - lastUpdate > 150) {
                        lastUpdate = now;
                        int percent = (int) Math.min(1000, written * 1000 / total);
                        main.post(() -> updateProgress(percent));
                    }
                }
                out.flush();
            }
        } finally {
            connection.disconnect();
        }
        return apk;
    }

    private void verifySha256(File apk, String expected) throws Exception {
        MessageDigest digest = MessageDigest.getInstance("SHA-256");
        try (InputStream in = new BufferedInputStream(new java.io.FileInputStream(apk))) {
            byte[] buffer = new byte[64 * 1024];
            int read;
            while ((read = in.read(buffer)) != -1) {
                digest.update(buffer, 0, read);
            }
        }
        StringBuilder hex = new StringBuilder();
        for (byte b : digest.digest()) {
            hex.append(String.format(Locale.ROOT, "%02x", b));
        }
        if (!hex.toString().equalsIgnoreCase(expected)) {
            throw new IllegalStateException(activity.getString(R.string.nc_update_verify_failed));
        }
    }

    @SuppressWarnings("deprecation")
    private void verifyPackage(File apk, UpdateInfo info) {
        PackageInfo packageInfo = activity.getPackageManager().getPackageArchiveInfo(apk.getAbsolutePath(), 0);
        if (packageInfo == null) {
            throw new IllegalStateException(activity.getString(R.string.nc_update_package_invalid));
        }
        if (!activity.getPackageName().equals(packageInfo.packageName)) {
            throw new IllegalStateException(activity.getString(R.string.nc_update_package_invalid));
        }
        long code = Build.VERSION.SDK_INT >= Build.VERSION_CODES.P ? packageInfo.getLongVersionCode() : packageInfo.versionCode;
        if (code != info.versionCode) {
            throw new IllegalStateException(activity.getString(R.string.nc_update_package_invalid));
        }
    }

    private void install(File apk) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O && !activity.getPackageManager().canRequestPackageInstalls()) {
            pendingApk = apk;
            new AlertDialog.Builder(activity)
                .setMessage(activity.getString(R.string.nc_update_install_blocked, activity.getString(R.string.app_name)))
                .setPositiveButton(R.string.nc_update_open_settings, (dialog, which) -> openInstallSettings())
                .setNegativeButton(android.R.string.cancel, null)
                .show();
            return;
        }
        launchInstaller(apk);
    }

    private void launchInstaller(File apk) {
        try {
            Uri uri = FileProvider.getUriForFile(activity, activity.getPackageName() + ".fileprovider", apk);
            Intent intent = new Intent(Intent.ACTION_VIEW);
            intent.setDataAndType(uri, "application/vnd.android.package-archive");
            intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_ACTIVITY_NEW_TASK);
            activity.startActivity(intent);
        } catch (Exception e) {
            toast(activity.getString(R.string.nc_update_install_failed));
        }
    }

    private void openInstallSettings() {
        try {
            Intent intent = new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:" + activity.getPackageName()));
            activity.startActivity(intent);
        } catch (ActivityNotFoundException e) {
            toast(activity.getString(R.string.nc_update_install_failed));
        }
    }

    void onResume() {
        if (pendingApk != null && Build.VERSION.SDK_INT >= Build.VERSION_CODES.O && activity.getPackageManager().canRequestPackageInstalls()) {
            File apk = pendingApk;
            pendingApk = null;
            launchInstaller(apk);
        }
    }

    private void showProgress() {
        main.post(() -> {
            if (destroyed || activity.isFinishing() || activity.isDestroyed()) return;
            dismissProgress();
            progressBar = new ProgressBar(activity, null, android.R.attr.progressBarStyleHorizontal);
            progressBar.setMax(1000);
            progressBar.setIndeterminate(true);
            progressBar.setPadding(48, 32, 48, 32);

            progressDialog = new AlertDialog.Builder(activity)
                .setTitle(R.string.nc_update_downloading)
                .setView(progressBar)
                .setCancelable(false)
                .setNegativeButton(android.R.string.cancel, (dialog, which) -> {
                    if (downloadCancelled != null) downloadCancelled.set(true);
                })
                .create();
            progressDialog.show();
        });
    }

    private void updateProgress(int percent) {
        if (progressDialog == null || !progressDialog.isShowing()) return;
        if (progressBar.isIndeterminate()) progressBar.setIndeterminate(false);
        progressBar.setProgress(percent);
    }

    private void setVerifying() {
        if (progressDialog != null && progressDialog.isShowing()) {
            progressDialog.setTitle(R.string.nc_update_verifying);
            if (progressBar != null) progressBar.setIndeterminate(true);
        }
    }

    private void dismissProgress() {
        try {
            if (progressDialog != null && progressDialog.isShowing()) progressDialog.dismiss();
        } catch (Exception ignored) {
            // ignore
        }
        progressDialog = null;
        progressBar = null;
    }

    private void toast(String message) {
        main.post(() -> {
            if (destroyed || activity.isFinishing() || activity.isDestroyed()) return;
            Toast.makeText(activity, message, Toast.LENGTH_LONG).show();
        });
    }

    void destroy() {
        destroyed = true;
        if (downloadCancelled != null) downloadCancelled.set(true);
        dismissProgress();
        try {
            if (promptDialog != null && promptDialog.isShowing()) promptDialog.dismiss();
        } catch (Exception ignored) {
            // ignore
        }
        promptDialog = null;
        executor.shutdownNow();
    }

    private static final class UpdateInfo {

        long versionCode;
        String versionName = "";
        String url = "";
        String sha256 = "";
        String notes = "";
    }
}
