using System.IO;
using UnityEditor;
using UnityEngine;

/// Records Play Mode frames at a fixed game-time step (Time.captureFramerate) to PNGs, so a sequence plays back
/// smoothly at that rate however fast the editor actually renders (slow-motion via timeScale included).
/// Driven from eval scripts (unity/transition_test.sh): Start(dir, frames, fps), poll Busy, frames land in dir/f_0000.png...
public static class PlayFrameRecorder
{
    static string dir;
    static int left, lastFrame = -1, index;

    public static bool Busy => left > 0;
    public static int Written => index;

    public static void Start(string outDir, int frames, int fps)
    {
        dir = outDir; Directory.CreateDirectory(dir);
        foreach (var f in Directory.GetFiles(dir, "f_*.png")) File.Delete(f);
        left = frames; index = 0; lastFrame = -1;
        Time.captureFramerate = fps;
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) { Stop(); return; }
        if (Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;
        ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"f_{index++:D4}.png"));
        if (--left <= 0) Stop();
    }

    public static void Stop()
    {
        EditorApplication.update -= Tick;
        left = 0; Time.captureFramerate = 0;
    }
}
