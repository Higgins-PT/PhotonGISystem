
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class StopwatchTimer
{
    /// <summary>
    /// ������Ϊ Key ���� Stopwatch��
    /// </summary>
    private static Dictionary<string, Stopwatch> _timers = new Dictionary<string, Stopwatch>();

    /// <summary>
    /// ��¼����һ�� Stop ʱ�õ��ĺ�ʱ����λ�����롣
    /// </summary>
    private static Dictionary<string, long> _results = new Dictionary<string, long>();

    /// <summary>
    /// ��ʼ��ʱ�����ͬ����ʱ���Ѵ��ڣ������ò����¿�ʼ��������������½�����
    /// ����ǰ���� Stop ������ᱻ���ǻ������
    /// </summary>
    /// <param name="timerName">��ʱ������</param>
    public static void StartTimer(string timerName)
    {
        if (_timers.TryGetValue(timerName, out Stopwatch sw))
        {
            // �Ѵ��� => ���ò���ʼ
            sw.Reset();
            sw.Start();
        }
        else
        {
            // �½�һ��
            sw = new Stopwatch();
            sw.Start();
            _timers[timerName] = sw;
        }

        // ��֮ǰ�Ѿ��н����Ҳ���������������ʾ�����ǡ�
        if (_results.ContainsKey(timerName))
        {
            _results.Remove(timerName);
        }
    }

    /// <summary>
    /// ֹͣͬ����ʱ����������ʱ��¼�� _results �У�����δ start���򲻻��¼��
    /// </summary>
    /// <param name="timerName">��ʱ������</param>
    public static void StopTimer(string timerName)
    {
        if (_timers.TryGetValue(timerName, out Stopwatch sw))
        {
            sw.Stop();
            _results[timerName] = sw.ElapsedMilliseconds;
        }
        else
        {
            // ���û�ж�Ӧ�� stopwatch������������ѡ�� Log �����
            Debug.LogWarning($"[StopwatchTimer] StopTimer: No active stopwatch for '{timerName}'.");
        }
    }

    /// <summary>
    /// ��ÿ֡ĩβ������ʱ�����ã�����������еļ�ʱ������������ _results��
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