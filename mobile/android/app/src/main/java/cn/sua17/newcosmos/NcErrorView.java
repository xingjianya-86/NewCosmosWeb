package cn.sua17.newcosmos;

import android.content.Context;
import android.content.res.ColorStateList;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.TextView;

/**
 * 原生网络错误覆盖层：WebView 加载失败时盖在页面上，提供“重试”和“用浏览器打开”。
 * 页面恢复可见时自动隐藏，不依赖 Web 侧错误页。
 */
final class NcErrorView extends LinearLayout {

    interface Listener {
        void onRetry();

        void onOpenBrowser();
    }

    private final TextView messageView;

    NcErrorView(Context context, Listener listener) {
        super(context);
        setOrientation(VERTICAL);
        setGravity(Gravity.CENTER);
        setBackgroundColor(Color.parseColor("#F4F6FA"));
        setClickable(true);
        setFocusable(true);
        setPadding(dp(28), dp(28), dp(28), dp(28));
        setVisibility(GONE);

        TextView mark = new TextView(context);
        mark.setText("δ");
        mark.setTextSize(TypedValue.COMPLEX_UNIT_SP, 30);
        mark.setTextColor(Color.parseColor("#D9B45B"));
        mark.setGravity(Gravity.CENTER);
        GradientDrawable markBackground = new GradientDrawable();
        markBackground.setColor(Color.parseColor("#0B1220"));
        markBackground.setCornerRadius(dp(18));
        mark.setBackground(markBackground);
        LayoutParams markParams = new LayoutParams(dp(64), dp(64));
        markParams.bottomMargin = dp(20);
        addView(mark, markParams);

        TextView title = new TextView(context);
        title.setText(R.string.nc_error_title);
        title.setTextColor(Color.parseColor("#16233C"));
        title.setTextSize(TypedValue.COMPLEX_UNIT_SP, 20);
        title.setTypeface(Typeface.DEFAULT_BOLD);
        title.setGravity(Gravity.CENTER);
        addView(title);

        messageView = new TextView(context);
        messageView.setTextColor(Color.parseColor("#5C6B82"));
        messageView.setTextSize(TypedValue.COMPLEX_UNIT_SP, 14);
        messageView.setGravity(Gravity.CENTER);
        messageView.setLineSpacing(dp(4), 1f);
        LayoutParams messageParams = new LayoutParams(LayoutParams.MATCH_PARENT, LayoutParams.WRAP_CONTENT);
        messageParams.topMargin = dp(12);
        addView(messageView, messageParams);

        Button retry = new Button(context);
        retry.setText(R.string.nc_error_retry);
        retry.setTextColor(Color.WHITE);
        retry.setTextSize(TypedValue.COMPLEX_UNIT_SP, 15);
        retry.setAllCaps(false);
        retry.setBackgroundTintList(ColorStateList.valueOf(Color.parseColor("#AB3227")));
        retry.setOnClickListener(v -> listener.onRetry());
        LayoutParams retryParams = new LayoutParams(LayoutParams.MATCH_PARENT, dp(48));
        retryParams.topMargin = dp(28);
        addView(retry, retryParams);

        TextView browser = new TextView(context);
        browser.setText(R.string.nc_error_browser);
        browser.setTextColor(Color.parseColor("#AB3227"));
        browser.setTextSize(TypedValue.COMPLEX_UNIT_SP, 14);
        browser.setGravity(Gravity.CENTER);
        browser.setPadding(dp(8), dp(14), dp(8), dp(8));
        browser.setOnClickListener(v -> listener.onOpenBrowser());
        addView(browser);

        setOnClickListener(v -> {
            // 覆盖层吞掉点击，避免穿透到 WebView。
        });
    }

    void show(String detail) {
        messageView.setText(getContext().getString(R.string.nc_error_message, detail == null ? "" : detail));
        setVisibility(VISIBLE);
        bringToFront();
    }

    void hide() {
        if (getVisibility() != GONE) setVisibility(GONE);
    }

    private int dp(int value) {
        return Math.round(value * getResources().getDisplayMetrics().density);
    }
}
