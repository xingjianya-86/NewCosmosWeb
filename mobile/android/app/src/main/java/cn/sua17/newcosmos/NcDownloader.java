package cn.sua17.newcosmos;

import android.app.DownloadManager;
import android.content.BroadcastReceiver;
import android.content.ContentResolver;
import android.content.ContentValues;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.database.Cursor;
import android.net.Uri;
import android.os.Build;
import android.os.Environment;
import android.os.Handler;
import android.os.Looper;
import android.provider.MediaStore;
import android.util.Base64;
import android.webkit.CookieManager;
import android.webkit.DownloadListener;
import android.webkit.MimeTypeMap;
import android.webkit.URLUtil;
import android.webkit.WebView;
import android.widget.Toast;
import androidx.core.app.NotificationChannelCompat;
import androidx.core.app.NotificationCompat;
import androidx.core.app.NotificationManagerCompat;
import androidx.core.content.ContextCompat;
import androidx.core.content.FileProvider;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.OutputStream;
import java.util.HashMap;
import java.util.Locale;
import java.util.Map;
import java.util.UUID;
import org.json.JSONObject;

/**
 * 下载支持：
 *  - http(s)：交给系统 DownloadManager，通知栏可见，文件保存到“下载/NewCosmos”；
 *  - blob:/data:：WebView 无法直接交给 DownloadManager，由注入脚本分段传到原生，
 *    写入 MediaStore（Android 10+）或公共下载目录（Android 9-），再发通知。
 */
final class NcDownloader implements DownloadListener {

    private static final String CHANNEL_ID = "nc_downloads";
    private static final String SUB_DIR = "NewCosmos";

    private final MainActivity activity;
    private final WebView webView;
    private final DownloadManager downloadManager;
    private final Handler main = new Handler(Looper.getMainLooper());
    private final Map<Long, String> activeDownloads = new HashMap<>();
    private final Map<String, BlobSession> blobs = new HashMap<>();
    private BroadcastReceiver receiver;

    NcDownloader(MainActivity activity, WebView webView) {
        this.activity = activity;
        this.webView = webView;
        this.downloadManager = (DownloadManager) activity.getSystemService(Context.DOWNLOAD_SERVICE);
        createChannel();
        registerReceiver();
    }

    private void createChannel() {
        NotificationChannelCompat channel = new NotificationChannelCompat.Builder(CHANNEL_ID, NotificationManagerCompat.IMPORTANCE_DEFAULT)
            .setName(activity.getString(R.string.nc_download_channel))
            .setDescription(activity.getString(R.string.nc_download_channel_desc))
            .build();
        NotificationManagerCompat.from(activity).createNotificationChannel(channel);
    }

    private void registerReceiver() {
        IntentFilter filter = new IntentFilter(DownloadManager.ACTION_DOWNLOAD_COMPLETE);
        receiver = new BroadcastReceiver() {
            @Override
            public void onReceive(Context context, Intent intent) {
                long id = intent.getLongExtra(DownloadManager.EXTRA_DOWNLOAD_ID, -1L);
                String name = id >= 0 ? activeDownloads.remove(id) : null;
                if (name == null) return;
                toast(isSuccessful(id)
                    ? activity.getString(R.string.nc_download_complete, name)
                    : activity.getString(R.string.nc_download_failed));
            }
        };
        ContextCompat.registerReceiver(activity, receiver, filter, ContextCompat.RECEIVER_NOT_EXPORTED);
    }

    private boolean isSuccessful(long id) {
        try (Cursor cursor = downloadManager.query(new DownloadManager.Query().setFilterById(id))) {
            if (cursor == null || !cursor.moveToFirst()) return false;
            int index = cursor.getColumnIndex(DownloadManager.COLUMN_STATUS);
            return index >= 0 && cursor.getInt(index) == DownloadManager.STATUS_SUCCESSFUL;
        } catch (Exception e) {
            return false;
        }
    }

    @Override
    public void onDownloadStart(String url, String userAgent, String contentDisposition, String mimetype, long contentLength) {
        if (url == null || url.isEmpty()) {
            toast(activity.getString(R.string.nc_download_empty));
            return;
        }

        String scheme;
        try {
            scheme = Uri.parse(url).getScheme();
        } catch (Exception e) {
            scheme = null;
        }
        scheme = scheme == null ? "" : scheme.toLowerCase(Locale.ROOT);

        if (scheme.equals("blob") || scheme.equals("data")) {
            String guessed = URLUtil.guessFileName(url, contentDisposition, mimetype);
            String js = "window.__ncShellSaveUrl(" + JSONObject.quote(url) + "," + JSONObject.quote(guessed) + ")";
            webView.evaluateJavascript(js, null);
            return;
        }

        if (!scheme.equals("http") && !scheme.equals("https")) {
            toast(activity.getString(R.string.nc_download_empty));
            return;
        }

        if (Build.VERSION.SDK_INT <= Build.VERSION_CODES.P && !activity.hasStoragePermission()) {
            toast(activity.getString(R.string.nc_storage_denied));
            activity.requestStoragePermission();
            return;
        }

        String name = uniqueName(sanitize(URLUtil.guessFileName(url, contentDisposition, mimetype)));
        try {
            DownloadManager.Request request = new DownloadManager.Request(Uri.parse(url));
            request.setMimeType(normalizeMime(mimetype, name));
            request.setTitle(name);
            request.setDescription(NcConfig.hostOf(url));
            request.setNotificationVisibility(DownloadManager.Request.VISIBILITY_VISIBLE_NOTIFY_COMPLETED);
            request.setAllowedOverMetered(true);
            request.setAllowedOverRoaming(true);
            request.addRequestHeader("X-NC-Client", NcConfig.CLIENT);
            request.addRequestHeader("X-NC-App-Version", NcConfig.VERSION_NAME);
            String cookie = CookieManager.getInstance().getCookie(url);
            if (cookie != null && !cookie.isEmpty()) {
                request.addRequestHeader("Cookie", cookie);
            }
            if (userAgent != null && !userAgent.isEmpty()) {
                request.addRequestHeader("User-Agent", userAgent);
            }
            String referer = webView.getUrl();
            if (referer != null && !referer.isEmpty()) {
                request.addRequestHeader("Referer", referer);
            }
            request.setDestinationInExternalPublicDir(Environment.DIRECTORY_DOWNLOADS, SUB_DIR + "/" + name);

            long id = downloadManager.enqueue(request);
            activeDownloads.put(id, name);
            toast(activity.getString(R.string.nc_download_started, name));
        } catch (Exception e) {
            toast(activity.getString(R.string.nc_download_failed));
        }
    }

    // ---------------------------------------------------------------------
    // blob 分段写入
    // ---------------------------------------------------------------------

    String beginBlob(String rawName, String rawMime) {
        String name = uniqueName(sanitize(rawName));
        String mime = normalizeMime(rawMime, name);
        String id = UUID.randomUUID().toString();
        BlobSession session = new BlobSession(name, mime);

        try {
            ContentResolver resolver = activity.getContentResolver();
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                ContentValues values = new ContentValues();
                values.put(MediaStore.Downloads.DISPLAY_NAME, name);
                values.put(MediaStore.Downloads.MIME_TYPE, mime);
                values.put(MediaStore.Downloads.RELATIVE_PATH, Environment.DIRECTORY_DOWNLOADS + "/" + SUB_DIR);
                values.put(MediaStore.Downloads.IS_PENDING, 1);
                session.uri = resolver.insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, values);
                if (session.uri == null) return "";
                session.out = resolver.openOutputStream(session.uri);
            } else {
                if (!activity.hasStoragePermission()) {
                    toast(activity.getString(R.string.nc_storage_denied));
                    activity.runOnUiThread(activity::requestStoragePermission);
                    return "";
                }
                File dir = new File(Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS), SUB_DIR);
                if (!dir.exists() && !dir.mkdirs()) return "";
                session.file = new File(dir, name);
                session.out = new FileOutputStream(session.file);
            }
            if (session.out == null) {
                discard(session);
                return "";
            }
            synchronized (blobs) {
                blobs.put(id, session);
            }
            return id;
        } catch (Exception e) {
            discard(session);
            return "";
        }
    }

    void appendBlob(String id, String base64) {
        BlobSession session;
        synchronized (blobs) {
            session = blobs.get(id);
        }
        if (session == null) return;

        try {
            byte[] data = Base64.decode(base64, Base64.DEFAULT);
            if (session.written + data.length > NcConfig.MAX_BLOB_BYTES) {
                abortBlob(id, R.string.nc_blob_too_large);
                return;
            }
            session.out.write(data);
            session.written += data.length;
        } catch (Exception e) {
            abortBlob(id, R.string.nc_blob_failed);
        }
    }

    void finishBlob(String id) {
        BlobSession session;
        synchronized (blobs) {
            session = blobs.remove(id);
        }
        if (session == null) return;

        try {
            session.out.flush();
            session.out.close();
            session.out = null;

            if (session.written <= 0) {
                discard(session);
                toast(activity.getString(R.string.nc_blob_failed));
                return;
            }

            Uri openUri = session.uri;
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q && session.uri != null) {
                ContentValues values = new ContentValues();
                values.put(MediaStore.Downloads.IS_PENDING, 0);
                activity.getContentResolver().update(session.uri, values, null, null);
            } else if (session.file != null) {
                openUri = FileProvider.getUriForFile(activity, activity.getPackageName() + ".fileprovider", session.file);
            }

            notifySaved(openUri, session.name, session.mime);
            toast(activity.getString(R.string.nc_blob_saved, session.name));
        } catch (Exception e) {
            discard(session);
            toast(activity.getString(R.string.nc_blob_failed));
        }
    }

    void abortBlob(String id) {
        abortBlob(id, R.string.nc_blob_failed);
    }

    private void abortBlob(String id, int message) {
        BlobSession session;
        synchronized (blobs) {
            session = blobs.remove(id);
        }
        if (session == null) return;
        discard(session);
        toast(activity.getString(message));
    }

    private void discard(BlobSession session) {
        try {
            if (session.out != null) session.out.close();
        } catch (IOException ignored) {
            // ignore
        }
        session.out = null;
        try {
            if (session.uri != null) {
                activity.getContentResolver().delete(session.uri, null, null);
            } else if (session.file != null && session.file.exists()) {
                //noinspection ResultOfMethodCallIgnored
                session.file.delete();
            }
        } catch (Exception ignored) {
            // ignore
        }
    }

    private void notifySaved(Uri uri, String name, String mime) {
        try {
            Intent view = new Intent(Intent.ACTION_VIEW);
            view.setDataAndType(uri, mime);
            view.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_ACTIVITY_NEW_TASK);
            int requestCode = (name + System.currentTimeMillis()).hashCode();
            android.app.PendingIntent pending = android.app.PendingIntent.getActivity(
                activity,
                requestCode,
                view,
                android.app.PendingIntent.FLAG_UPDATE_CURRENT | android.app.PendingIntent.FLAG_IMMUTABLE
            );
            NotificationCompat.Builder builder = new NotificationCompat.Builder(activity, CHANNEL_ID)
                .setSmallIcon(android.R.drawable.stat_sys_download_done)
                .setContentTitle(name)
                .setContentText(activity.getString(R.string.nc_blob_saved, name))
                .setContentIntent(pending)
                .setAutoCancel(true)
                .setOnlyAlertOnce(true);
            NotificationManagerCompat.from(activity).notify(requestCode, builder.build());
        } catch (Exception ignored) {
            // 通知失败不影响文件已保存
        }
    }

    // ---------------------------------------------------------------------
    // 工具
    // ---------------------------------------------------------------------

    private void toast(String message) {
        main.post(() -> {
            if (!activity.isFinishing() && !activity.isDestroyed()) {
                Toast.makeText(activity, message, Toast.LENGTH_SHORT).show();
            }
        });
    }

    private String sanitize(String raw) {
        String name = raw == null ? "" : raw;
        name = name.replaceAll("[\\\\/:*?\"<>|\\r\\n\\t]", "_").trim();
        if (name.isEmpty()) name = "download-" + System.currentTimeMillis();
        if (name.length() > 120) {
            String ext = "";
            int dot = name.lastIndexOf('.');
            if (dot > 0 && name.length() - dot <= 10) ext = name.substring(dot);
            name = name.substring(0, 110) + ext;
        }
        return name;
    }

    private String normalizeMime(String mime, String name) {
        if (mime != null) {
            String trimmed = mime.trim();
            if (!trimmed.isEmpty() && !trimmed.equalsIgnoreCase("application/octet-stream")) {
                int semicolon = trimmed.indexOf(';');
                return semicolon > 0 ? trimmed.substring(0, semicolon) : trimmed;
            }
        }
        int dot = name.lastIndexOf('.');
        if (dot > 0) {
            String guessed = MimeTypeMap.getSingleton().getMimeTypeFromExtension(name.substring(dot + 1).toLowerCase(Locale.ROOT));
            if (guessed != null) return guessed;
        }
        return "application/octet-stream";
    }

    private String uniqueName(String name) {
        String base = name;
        String ext = "";
        int dot = name.lastIndexOf('.');
        if (dot > 0 && name.length() - dot <= 10) {
            base = name.substring(0, dot);
            ext = name.substring(dot);
        }
        String candidate = name;
        for (int i = 1; i < 100; i++) {
            if (!downloadExists(candidate)) return candidate;
            candidate = base + " (" + i + ")" + ext;
        }
        return base + "-" + System.currentTimeMillis() + ext;
    }

    private boolean downloadExists(String name) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            String relativePath = Environment.DIRECTORY_DOWNLOADS + "/" + SUB_DIR + "%";
            try (
                Cursor cursor = activity.getContentResolver().query(
                    MediaStore.Downloads.EXTERNAL_CONTENT_URI,
                    new String[] { MediaStore.Downloads._ID },
                    MediaStore.Downloads.DISPLAY_NAME + "=? AND " + MediaStore.Downloads.RELATIVE_PATH + " LIKE ?",
                    new String[] { name, relativePath },
                    null
                )
            ) {
                return cursor != null && cursor.getCount() > 0;
            } catch (Exception e) {
                return false;
            }
        }
        File file = new File(new File(Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS), SUB_DIR), name);
        return file.exists();
    }

    void destroy() {
        try {
            if (receiver != null) activity.unregisterReceiver(receiver);
        } catch (Exception ignored) {
            // ignore
        }
        synchronized (blobs) {
            for (BlobSession session : blobs.values()) {
                discard(session);
            }
            blobs.clear();
        }
    }

    private static final class BlobSession {

        final String name;
        final String mime;
        Uri uri;
        File file;
        OutputStream out;
        long written;

        BlobSession(String name, String mime) {
            this.name = name;
            this.mime = mime;
        }
    }
}
