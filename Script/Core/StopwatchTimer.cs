
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class StopwatchTimer
{
    /// <summary>
    /// 以名字为 Key 存多个 Stopwatch。
    /// </summary>
    private static Dictionary<string, Stopwatch> _timers = new Dictionary<string, Stopwatch>();

    /// <summary>
    /// 记录最新一次 Stop 时得到的耗时，单位：毫秒。
    /// </summary>
    private static Dictionary<string, long> _results = new Dictionary<string, long>();

    /// <summary>
    /// 开始计时（如果同名计时器已存在，则重置并重新开始；如果不存在则新建）。
    /// 若此前已有 Stop 结果，会被覆盖或清除。
    /// </summary>
    /// <param name="timerName">计时器名称</param>
    public static void StartTimer(string timerName)
    {
        if (_timers.TryGetValue(timerName, out Stopwatch sw))
        {
            // 已存在 => 重置并开始
            sw.Reset();
            sw.Start();
        }
        else
        {
            // 新建一个
            sw = new Stopwatch();
            sw.Start();
            _timers[timerName] = sw;
        }

        // 若之前已经有结果，也可在这里清除，表示“覆盖”
        if (_results.ContainsKey(timerName))
        {
            _results.Remove(timerName);
        }
    }

    /// <summary>
    /// 停止同名计时器，并将耗时记录到 _results 中；若尚未 start，则不会记录。
    /// </summary>
    /// <param name="timerName">计时器名称</param>
    public static void StopTimer(string timerName)
    {
        if (_timers.TryGetValue(timerName, out Stopwatch sw))
        {
            sw.Stop();
            _results[timerName] = sw.ElapsedMilliseconds;
        }
        else
        {
            // 如果没有对应的 stopwatch，可以视需求选择 Log 或忽略
            Debug.LogWarning($"[StopwatchTimer] StopTimer: No active stopwatch for '{timerName}'.");
        }
    }

    /// <summary>
    /// 在每帧末尾或任意时机调用，输出所有已有的计时器结果，并清空 _results。
    /// </summary>
    public static void PrintAndClearResults()
    {
        if (_results.Count == 0) return;

        foreach (var kvp in _results)
        {
            string timerName = kvp.Key;
            long elapsedMs = kvp.Value;
            Debug.Log($"[StopwatchTimer] Timer '{timerName}' => {elapsedMs} ms");
        }

        _results.Clear();
    }
}