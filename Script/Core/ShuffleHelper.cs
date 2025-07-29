using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class ShuffleHelper
{
    /// <summary>
    /// �� <paramref name="source"/> �б�ִ�� Fisher-Yates ϴ�ƺ󣬷���ȡǰ <paramref name="sampleCount"/> ��Ԫ�صĽ����
    /// ��� <paramref name="sampleCount"/> �����б��С���򷵻�ȫ��Ԫ�ء�
    /// </summary>
    /// <typeparam name="T">�б�Ԫ������</typeparam>
    /// <param name="source">ԭ�б�</param>
    /// <param name="sampleCount">��Ҫ��ȡ������</param>
    /// <returns>ϴ�ƺ�ȡǰ sampleCount ��Ԫ�ع��ɵ����б�</returns>
    public static List<T> ShuffleTake<T>(List<T> source, int sampleCount)
    {
        if (source == null || source.Count == 0)
        {
            return new List<T>();
        }

        // ��� sampleCount >= source.Count���������ϸ���ֱ��ȡȫ��
        if (sampleCount >= source.Count)
        {
            // ����ֱ��ϴ�ƺ󷵻أ�Ҳ��ֻ���� clone
            List<T> allCopy = new List<T>(source);
            ShuffleInPlace(allCopy);
            return allCopy;
        }

        // 1) ����Դ�б������޸�ԭ����
        List<T> copy = new List<T>(source);

        // 2) Fisher�CYates ϴ��
        ShuffleInPlace(copy);

        // 3) ȡǰ sampleCount ��
        return copy.Take(sampleCount).ToList();
    }

    /// <summary>
    /// �� <paramref name="list"/> ���� Fisher�CYates ϴ��(�͵��޸�)��
    /// ʹ�� UnityEngine.Random.Range �����ѡ�񽻻�������
    /// </summary>
    private static void ShuffleInPlace<T>(List<T> list)
    {
        // �Ӻ���ǰ����
        for (int i = list.Count - 1; i > 0; i--)
        {
            // �� [0..i] ��Χ��ѡһ���������
            int swapIndex = Random.Range(0, i + 1);
            // ����
            T temp = list[i];
            list[i] = list[swapIndex];
            list[swapIndex] = temp;
        }
    }
}
