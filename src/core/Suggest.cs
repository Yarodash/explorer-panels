namespace ExplorerPanel.Core;

/// <summary>Подсказка «возможно, вы имели в виду…» для опечаток в именах.</summary>
public static class Suggest
{
    /// <summary>Ближайшее к <paramref name="word"/> имя из списка, если оно отличается не больше чем на две правки.</summary>
    public static string? Nearest(string word, IEnumerable<string> candidates)
    {
        string? best = null;
        int bestDistance = 3;
        foreach (var candidate in candidates)
        {
            if (Math.Abs(candidate.Length - word.Length) >= bestDistance) continue;
            int distance = Distance(word, candidate, bestDistance);
            if (distance < bestDistance) { best = candidate; bestDistance = distance; }
        }
        return best;
    }

    /// <summary>Добавка к сообщению: <c> (did you mean "play"?)</c> или пустая строка.</summary>
    public static string Hint(string word, IEnumerable<string> candidates) =>
        Nearest(word, candidates) is { } nearest ? $" (did you mean \"{nearest}\"?)" : "";

    // расстояние Левенштейна; как только оно заведомо не меньше limit — возвращает limit
    static int Distance(string a, string b, int limit)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) previous[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            int rowMin = current[0];
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                rowMin = Math.Min(rowMin, current[j]);
            }
            if (rowMin >= limit) return limit;
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }
}
