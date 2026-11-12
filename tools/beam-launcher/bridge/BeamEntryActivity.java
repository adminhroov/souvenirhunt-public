package com.hroov.souvenirhunt.bridge;

import android.app.Activity;
import android.app.ActivityManager;
import android.content.Context;
import android.content.Intent;
import android.hardware.display.DisplayManager;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.os.Process;
import android.os.SystemClock;
import android.util.AtomicFile;
import android.util.Log;
import android.view.Display;
import android.view.Gravity;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.TextView;
import org.json.JSONObject;
import java.io.File;
import java.io.FileOutputStream;
import java.nio.charset.StandardCharsets;

/** Phone entry runs independently of Unity, including when the XR process is stuck. */
public class BeamEntryActivity extends Activity {
    private static final String TAG = "SouvenirEntry";
    private final Handler handler = new Handler(Looper.getMainLooper());
    private TextView status, subtitle, help;
    private Button start, restart;
    private boolean recovering;
    private String notice = "";
    // 10-29 검은 화면 대응: 안경 디스플레이가 3D(3840×1080)로 이만큼 연속 유지돼야 시작 가능(연결 직후 2D→3D 전환 중 시작 막기)
    private static final long STABLE_3D_MS = 3000;
    // 안전판: 안경이 2D로만 이만큼 계속 보이면(시스템이 3D로 안 바꿔 주는 경우) 시작은 허용 — 버튼이 영영 꺼지지 않게
    private static final long FALLBACK_2D_MS = 15000;
    private android.content.res.ColorStateList restartColors; // 10-29 강조 해제 때 되돌릴 기본 글자색
    private int stableId = -1, seenId = -1;
    private long stableSince, seenSince;
    private boolean lastReady;
    // 11-10 한/영: 실행 앱이 고른 언어(extra "lang" = ko|en). 이 화면 문구에 쓰고, 게임(NRXRActivity) 인텐트에 그대로 넘긴다(없으면 안 넣음 — 게임은 config/한국어)
    private String lang;
    private boolean en() { return "en".equals(lang); }
    private String t(String ko, String en) { return en() ? en : ko; }
    private static String readLang(Intent intent) {
        String v = intent != null ? intent.getStringExtra("lang") : null;
        if (v == null) return null;
        v = v.trim().toLowerCase(java.util.Locale.ROOT);
        return v.equals("en") || v.equals("ko") ? v : null;
    }
    private final Runnable refresh = new Runnable() {
        public void run() { updateStatus(); handler.postDelayed(this, 1000); }
    };

    private static AtomicFile stateFile(Context context) {
        return new AtomicFile(new File(context.getFilesDir(), "souvenir-xr-state.json"));
    }

    // 10-29 시작 화면 자기 기록(마지막으로 게임을 띄운 안경 displayId) — Unity가 1초마다 덮어쓰는 상태 파일과 따로
    private static AtomicFile entryFile(Context context) {
        return new AtomicFile(new File(context.getFilesDir(), "souvenir-entry.json"));
    }

    // Called from Unity. No participant identifiers, tokens or server payloads are stored.
    public static void publish(Context context, String state, boolean prizeSending, String diagnostics) {
        publish(context, state, prizeSending, diagnostics, false);
    }

    /** 10-29 linkLost = 게임이 자기 로그에서 안경 링크 끊김 서명을 봤다(시작 화면이 [처음부터 다시 실행] 강조) */
    public static void publish(Context context, String state, boolean prizeSending, String diagnostics, boolean linkLost) {
        FileOutputStream stream = null;
        AtomicFile file = stateFile(context);
        try {
            JSONObject json = new JSONObject();
            json.put("pid", Process.myPid());
            json.put("state", state);
            json.put("prizeSending", prizeSending);
            json.put("updated", System.currentTimeMillis());
            json.put("diagnostics", diagnostics);
            json.put("linkLost", linkLost);
            stream = file.startWrite();
            stream.write(json.toString().getBytes(StandardCharsets.UTF_8));
            file.finishWrite(stream);
        } catch (Exception e) {
            if (stream != null) file.failWrite(stream);
            Log.w(TAG, "Status write failed", e);
        }
    }

    private JSONObject readState() {
        try { return new JSONObject(new String(stateFile(this).readFully(), StandardCharsets.UTF_8)); }
        catch (Exception e) { return new JSONObject(); }
    }

    private int lastDisplayId() {
        try { return new JSONObject(new String(entryFile(this).readFully(), StandardCharsets.UTF_8)).optInt("displayId", -1); }
        catch (Exception e) { return -1; }
    }

    private void recordDisplayId(int id) {
        FileOutputStream stream = null;
        AtomicFile file = entryFile(this);
        try {
            JSONObject json = new JSONObject();
            json.put("displayId", id);
            json.put("updated", System.currentTimeMillis());
            stream = file.startWrite();
            stream.write(json.toString().getBytes(StandardCharsets.UTF_8));
            file.finishWrite(stream);
            Log.i(TAG, "displayId recorded " + id);
        } catch (Exception e) {
            if (stream != null) file.failWrite(stream);
            Log.w(TAG, "Entry state write failed", e);
        }
    }

    /** 10-29 게임이 안경 링크 끊김 서명을 알렸나(지금 살아 있는 게임 pid의 기록만) */
    private boolean linkLost(int pid) {
        JSONObject state = readState();
        return pid > 0 && state.optInt("pid", -1) == pid && state.optBoolean("linkLost");
    }

    /** 10-29 에뮬레이터 검증용: 보조 디스플레이(overlay_display_devices)를 안경으로 본다. 판정은 Build.HARDWARE ranchu/goldfish만(감사 — FINGERPRINT 부분 문자열은 실기기 오판 여지) */
    private static boolean isEmulator() {
        return "ranchu".equals(Build.HARDWARE) || "goldfish".equals(Build.HARDWARE);
    }

    private static boolean is3d(Display d) {
        Display.Mode m = d.getMode();
        int w = Math.max(m.getPhysicalWidth(), m.getPhysicalHeight()), h = Math.min(m.getPhysicalWidth(), m.getPhysicalHeight());
        return w == 3840 && h == 1080;
    }

    /**
     * 10-29 검은 화면 대응: 시작해도 되는 안경 디스플레이 — 3D(3840×1080)가 STABLE_3D_MS 이상 같은 displayId로 연속 유지될 때만.
     * 2D(1920×1080)이거나 방금 바뀐 디스플레이면 null(시작 화면 "글래스 준비 중…"). 단 2D로만 FALLBACK_2D_MS 넘게 계속이면 허용(버튼이 영영 꺼지지 않게)
     */
    private Display readyGlassesDisplay() {
        Display d = glassesDisplay();
        long now = SystemClock.uptimeMillis();
        if (d == null) { stableId = -1; seenId = -1; return null; }
        if (d.getDisplayId() != seenId) { seenId = d.getDisplayId(); seenSince = now; }
        if (!is3d(d)) { stableId = -1; return now - seenSince >= FALLBACK_2D_MS ? d : null; }
        if (d.getDisplayId() != stableId) { stableId = d.getDisplayId(); stableSince = now; }
        return now - stableSince >= STABLE_3D_MS ? d : null;
    }

    private int gamePid() {
        ActivityManager manager = (ActivityManager)getSystemService(ACTIVITY_SERVICE);
        java.util.List<ActivityManager.RunningAppProcessInfo> processes = manager.getRunningAppProcesses();
        if (processes != null) for (ActivityManager.RunningAppProcessInfo p : processes)
            if (p.uid == Process.myUid() && p.processName.equals(getPackageName())) return p.pid;
        return -1;
    }

    private boolean prizeBusy(int pid) {
        JSONObject state = readState();
        return pid > 0 && state.optInt("pid", -1) == pid && state.optBoolean("prizeSending");
    }

    private Display glassesDisplay() {
        DisplayManager dm = (DisplayManager)getSystemService(DISPLAY_SERVICE);
        for (Display d : dm.getDisplays()) {
            if (d.getDisplayId() == Display.DEFAULT_DISPLAY) continue;
            if (isEmulator()) return d; // 10-29 에뮬레이터 검증 — 보조 디스플레이 = 안경
            try {
                // Use the same display identification as SDK 3.1.0, excluding its virtual display.
                Object match = Class.forName("ai.nreal.activitylife.NRXRApp")
                    .getMethod("isNrealDisplay", Display.class, boolean.class).invoke(null, d, false);
                if (Boolean.TRUE.equals(match)) return d;
            } catch (Exception e) { Log.w(TAG, "Glasses display lookup failed", e); }
        }
        return null;
    }

    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        lang = readLang(getIntent());
        LinearLayout column = new LinearLayout(this);
        column.setOrientation(LinearLayout.VERTICAL);
        column.setPadding(dp(28), dp(64), dp(28), dp(28));
        column.setGravity(Gravity.CENTER_VERTICAL);
        column.setBackgroundColor(0xff101b30);
        TextView title = text("SouvenirHunt", 32, 0xffffffff);
        column.addView(title);
        subtitle = text("", 18, 0xff74d9dd);
        column.addView(subtitle);
        status = text(t("연결 확인 중…", "Checking connection…"), 17, 0xffd4ddeb);
        LinearLayout.LayoutParams info = new LinearLayout.LayoutParams(-1, -2);
        info.topMargin = dp(32); info.bottomMargin = dp(32); column.addView(status, info);
        start = button(""); column.addView(start);
        start.setOnClickListener(v -> launchGame());
        restart = button("");
        LinearLayout.LayoutParams gap = new LinearLayout.LayoutParams(-1, dp(58)); gap.topMargin = dp(12);
        column.addView(restart, gap);
        restart.setOnClickListener(v -> recoverGame());
        restartColors = restart.getTextColors();
        help = text("", 14, 0xffaebbd0);
        LinearLayout.LayoutParams footer = new LinearLayout.LayoutParams(-1,-2); footer.topMargin=dp(28);
        column.addView(help, footer);
        setContentView(column);
        applyLabels();
        Log.i(TAG, "Phone entry opened (independent of XR), lang=" + (lang == null ? "none" : lang));
    }

    /** 11-10 고정 문구를 지금 언어로(상태 줄·[처음부터 다시 실행] 강조는 updateStatus) */
    private void applyLabels() {
        subtitle.setText(t("XR 인형뽑기", "XR Claw Machine"));
        start.setText(t("콘텐츠 시작 / 이어하기", "Start / Continue"));
        restart.setText(t("처음부터 다시 실행", "Restart from scratch"));
        help.setText(t("글래스를 벗어도 이 화면은 열 수 있습니다.\n\n화면이 나오지 않으면 ‘처음부터 다시 실행’을 누르세요. 진행 중인 게임은 초기화됩니다.",
                       "You can open this screen without the glasses.\n\nIf nothing shows in the glasses, press ‘Restart from scratch’. The current game will be reset."));
    }

    /**
     * 11-10 singleTask — 이미 열린 시작 화면을 실행 앱이 다른 언어로 다시 열면 그 언어로 문구만 다시 쓴다.
     * 11-10(2) QA: recreate()는 [처음부터 다시 실행] 대기 작업(최대 8초, handler)을 지워 게임이 다시 안 떴다 → 화면을 다시 만들지 않고 문구만.
     * lang 없는 인텐트(다른 경로로 열림)는 setIntent 하지 않는다(지금 언어 유지)
     */
    @Override protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        String next = readLang(intent);
        if (next == null) return;
        setIntent(intent);
        if (next.equals(lang)) return;
        Log.i(TAG, "Language changed " + lang + " -> " + next + (recovering ? " (restart pending — relabel only)" : ""));
        lang = next;
        notice = recovering ? t("콘텐츠를 새로 시작하고 있어요…", "Restarting the content…") : ""; // 이전 언어 안내는 지움
        applyLabels();
        updateStatus();
    }

    private int dp(int value) { return Math.round(value * getResources().getDisplayMetrics().density); }
    private TextView text(String value, int size, int color) {
        TextView view = new TextView(this); view.setText(value); view.setTextSize(size); view.setTextColor(color);
        view.setPadding(0,dp(5),0,dp(5)); return view;
    }
    private Button button(String label) {
        Button b = new Button(this); b.setText(label); b.setAllCaps(false); b.setTextSize(17);
        b.setMinHeight(dp(58)); return b;
    }

    private void updateStatus() {
        int pid = gamePid(); Display display = glassesDisplay(); boolean busy = prizeBusy(pid);
        Display ready = readyGlassesDisplay(); boolean lost = linkLost(pid);
        if ((ready != null) != lastReady) {
            lastReady = ready != null;
            Log.i(TAG, "glasses " + (display == null ? "none" : display.getDisplayId() + " " + display.getMode().getPhysicalWidth() + "x" + display.getMode().getPhysicalHeight())
                + (lastReady ? " ready" : " not ready"));
        }
        status.setText((display == null ? t("글래스 연결을 기다리고 있어요.\n연결 후 아래 버튼을 눌러 주세요.", "Waiting for the glasses.\nConnect them, then press the button below.")
                : ready == null ? t("글래스 준비 중… 잠시 뒤 시작할 수 있어요.", "Glasses getting ready… you can start in a moment.") : t("글래스 연결됨", "Glasses connected"))
            + "\n\n" + (pid > 0 ? t("콘텐츠 실행 중", "Content running") : t("콘텐츠 시작 대기", "Ready to start"))
            + (busy ? t("\n경품 요청 중 — 응답을 기다려 주세요.", "\nRequesting prize — please wait for the response.") : "")
            + (lost ? t("\n\n안경 화면 연결이 끊어졌어요 — 아래 ‘처음부터 다시 실행’을 눌러 주세요.", "\n\nGlasses display link lost — press ‘Restart from scratch’ below.") : "")
            + (notice.isEmpty() ? "" : "\n\n" + notice));
        start.setEnabled(!recovering && ready != null);
        restart.setEnabled(!recovering && !busy && pid > 0);
        // 10-29 게임이 안경 링크 끊김을 알리면 [처음부터 다시 실행]을 강조
        restart.setText(lost ? t("▶ 처음부터 다시 실행 (권장)", "▶ Restart from scratch (recommended)") : t("처음부터 다시 실행", "Restart from scratch"));
        if (lost) restart.setTextColor(0xffffd27a); else if (restartColors != null) restart.setTextColor(restartColors);
    }

    private void launchGame() {
        Display display = readyGlassesDisplay();
        if (display == null) { notice = glassesDisplay() == null ? t("글래스 연결을 확인해 주세요.", "Please check the glasses connection.") : t("글래스 준비 중… 잠시 뒤 다시 눌러 주세요.", "Glasses getting ready… please press again in a moment."); updateStatus(); return; }
        // 10-29 안경 displayId가 지난번 시작 때와 다르면(다시 연결됨) 살아 있는 게임은 옛 디스플레이에 묶여 있을 수 있다 → 끄고 새로 시작
        int last = lastDisplayId(), id = display.getDisplayId(), pid = gamePid();
        recordDisplayId(id);
        if (last >= 0 && last != id && pid > 0 && !recovering) {
            Log.w(TAG, "Glasses display changed " + last + " -> " + id + ", fresh start of pid " + pid);
            recoverGame();
            return;
        }
        // 10-29 감사 I1: 상대 변형(A↔B) 종료(killBackgroundProcesses)는 Android 14부터 자기 앱만 대상이라 무효 → 제거. 운영: 한쪽 실행 전 다른 쪽 force-stop/최근 앱에서 닫기
        try {
            Intent intent = new Intent().setClassName(getPackageName(), "ai.nreal.activitylife.NRXRActivity");
            intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            if (lang != null) intent.putExtra("lang", lang); // 11-10 게임 화면 언어(Loc) — 없으면 안 넣음
            startActivity(intent);
            notice=t("글래스 화면을 확인해 주세요.", "Please check the glasses screen.");
            Log.i(TAG, "Start/resume via SDK entry, lang=" + (lang == null ? "none" : lang));
        } catch (Exception e) { notice=t("실행하지 못했습니다. 다시 시도해 주세요.", "Could not start. Please try again."); Log.e(TAG,"Launch failed",e); }
        updateStatus();
    }

    private void recoverGame() {
        int pid=gamePid();
        if (prizeBusy(pid)) { notice=t("경품 요청이 끝난 뒤 다시 실행해 주세요.", "Please restart after the prize request finishes."); updateStatus(); return; }
        if (pid <= 0) { launchGame(); return; }
        recovering=true; notice=t("콘텐츠를 새로 시작하고 있어요…", "Restarting the content…"); updateStatus();
        // Only our own Unity process. The entry process and saved config remain intact.
        Log.w(TAG,"User requested fresh XR process, oldPid="+pid);
        Process.killProcess(pid);
        final long deadline=android.os.SystemClock.uptimeMillis()+8000;
        handler.postDelayed(new Runnable() {
            public void run() {
                if (gamePid()>0 && android.os.SystemClock.uptimeMillis()<deadline) {
                    handler.postDelayed(this,200); return;
                }
                recovering=false;
                if (gamePid()>0) notice=t("종료되지 않았습니다. 잠시 후 다시 시도해 주세요.", "It did not close. Please try again shortly.");
                else if (glassesDisplay()==null) notice=t("초기화 완료. 글래스를 연결한 후 시작해 주세요.", "Reset done. Connect the glasses, then start.");
                else { notice=""; launchGame(); }
                updateStatus();
            }
        },1000);
    }

    @Override protected void onResume() { super.onResume(); handler.removeCallbacks(refresh); refresh.run(); }
    @Override protected void onPause() { super.onPause(); handler.removeCallbacks(refresh); }
    @Override protected void onDestroy() { handler.removeCallbacksAndMessages(null); super.onDestroy(); }
}
