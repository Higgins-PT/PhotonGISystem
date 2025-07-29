using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class ShuffleHelper
{
    /// <summary>
    /// 对 <paramref name="source"/> 列表执行 Fisher-Yates 洗牌后，返回取前 <paramref name="sampleCount"/> 个元素的结果。
    /// 如果 <paramref name="sampleCount"/> 超过列表大小，则返回全部元素。
    /// </summary>
    /// <typeparam name="T">列表元素类型</typeparam>
    /// <param name="source">原列表</param>
    /// <param name="sampleCount">想要获取的数量</param>
    /// <returns>洗牌后取前 sampleCount 个元素构成的新列表</returns>
    public static List<T> ShuffleTake<T>(List<T> source, int sampleCount)
    {
        if (source == null || source.Count == 0)
        {
            return new List<T>();
        }

        // 如果 sampleCount >= source.Count，则无需严格处理，直接取全部
        if (sampleCount >= source.Count)
        {
            // 可以直接洗牌后返回，也可只返回 clone
            List<T> allCopy = new List<T>(source);
            ShuffleInPlace(allCopy);
            return allCopy;
        }

        // 1) 复制源列表，避免修改原数据
        List<T> copy = new List<T>(source);

        // 2) Fisher–Yates 洗牌
        ShuffleInPlace(copy);

        // 3) 取前 sampleCount 个
        return copy.Take(sampleCount).ToList();
    }

    /// <summary>
    /// 对 <paramref name="list"/> 进行 Fisher–Yates 洗牌(就地修改)。
    /// 使用 UnityEngine.Random.Range 来随机选择交换索引。
    /// </summary>
    private static void ShuffleInPlace<T>(List<T> list)
    {
        // 从后向前交换
        for (int i = list.Count - 1; i > 0; i--)
        {
            // 在 [0..i] 范围内选一个随机索引
            int swapIndex = Random.Range(0, i + 1);
            // 交换
            T temp = list[i];
            list[i] = list[swapIndex];
            list[swapIndex] = temp;
        }
    }
}
