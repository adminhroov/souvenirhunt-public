package com.hroov.souvenirhunt.launcher;
import android.app.Activity;
import android.os.Bundle;
import android.content.Intent;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.TextView;
import android.view.Gravity;

public class LauncherActivity extends Activity {
    // 11-10 언어 선택(사용자 결정: 별도 영어 APK 대신 실행 앱 화면에서 한국어/English) — 고른 언어를 브리지에 extra "lang"(ko|en)으로 넘기고,
    // 브리지는 그 값을 게임(NRXRActivity) 인텐트에 그대로 넘긴다(게임 쪽 Loc가 읽음). 예전엔 열리자마자 브리지를 실행했다.
    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        LinearLayout column = new LinearLayout(this);
        column.setOrientation(LinearLayout.VERTICAL);
        column.setGravity(Gravity.CENTER);
        column.setPadding(dp(28), dp(48), dp(28), dp(48));
        column.setBackgroundColor(0xff101b30);
        TextView title = new TextView(this);
        title.setText("SouvenirHunt");
        title.setTextSize(32); title.setTextColor(0xffffffff); title.setGravity(Gravity.CENTER);
        column.addView(title);
        TextView sub = new TextView(this);
        sub.setText("언어를 선택하세요 · Select language");
        sub.setTextSize(18); sub.setTextColor(0xff74d9dd); sub.setGravity(Gravity.CENTER);
        LinearLayout.LayoutParams subParams = new LinearLayout.LayoutParams(-1, -2);
        subParams.topMargin = dp(8); subParams.bottomMargin = dp(36);
        column.addView(sub, subParams);
        column.addView(languageButton("한국어", "ko"), buttonParams(0));
        column.addView(languageButton("English", "en"), buttonParams(dp(16)));
        setContentView(column);
    }

    private Button languageButton(String label, final String lang) {
        Button b = new Button(this);
        b.setText(label); b.setAllCaps(false); b.setTextSize(26);
        b.setMinHeight(dp(96));
        b.setOnClickListener(v -> launch(lang));
        return b;
    }

    private LinearLayout.LayoutParams buttonParams(int top) {
        LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(-1, dp(110));
        p.topMargin = top;
        return p;
    }

    private int dp(int value) { return Math.round(value * getResources().getDisplayMetrics().density); }

    private void launch(String lang) {
        try {
            startActivity(new Intent().setClassName("com.hroov.souvenirhunt", "com.hroov.souvenirhunt.bridge.BeamEntryActivity")
                .putExtra("lang", lang)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
            finish();
        } catch (Exception e) {
            // 11-10 게임 미설치 안내 — 한/영 두 줄
            TextView message = new TextView(this);
            message.setText("SouvenirHunt 최신 게임 APK를 먼저 설치해 주세요.\nPlease install the latest SouvenirHunt game APK first.");
            message.setTextSize(20); message.setGravity(Gravity.CENTER); message.setPadding(40,40,40,40);
            message.setTextColor(0xffffffff); message.setBackgroundColor(0xff101b30);
            setContentView(message);
        }
    }
}
