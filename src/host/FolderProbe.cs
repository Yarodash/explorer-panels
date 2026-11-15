using System.Collections.Concurrent;

namespace ExplorerPanel.Host;

/// <summary>
/// Сведения о папках, ради которых приходится обращаться к диску: есть ли у папки панель, существует ли она.
///
/// Обращение к диску может длиться долго — зависшая сетевая папка отвечает десятки секунд. Если бы программа
/// ждала ответа в потоке окон, на это время замерли бы все панели. Поэтому вопрос задаётся в фоне, а спрашивающий
/// сразу получает последний известный ответ; когда ответ меняется, приходит событие <see cref="Updated"/>.
/// </summary>
/// <param name="look">Собственно обращение к диску; выполняется в фоновом потоке.</param>
sealed class FolderProbe<T>(Func<string, T> look)
{
    sealed class Entry
    {
        public T? Value;
        public bool Known;
        public int Busy;       // 1, пока идёт обращение к диску: второе к той же папке не начинается
        public long AskedAt;
    }

    const long ForgetAfterMs = 10 * 60 * 1000;

    readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ответ о какой-то папке изменился (приходит из фонового потока).</summary>
    public event Action? Updated;

    /// <summary>
    /// Последний известный ответ о папке; заодно запускает его обновление.
    /// <c>Known == false</c> — ответа ещё нет (или папка не ответила).
    /// </summary>
    public (bool Known, T? Value) Get(string folder)
    {
        var entry = _entries.GetOrAdd(folder, _ => new Entry());
        Volatile.Write(ref entry.AskedAt, Environment.TickCount64);
        if (Interlocked.CompareExchange(ref entry.Busy, 1, 0) == 0) _ = Task.Run(() => Look(folder, entry));
        lock (entry) return (entry.Known, entry.Value);
    }

    void Look(string folder, Entry entry)
    {
        bool changed;
        try
        {
            var value = look(folder);
            lock (entry)
            {
                changed = !entry.Known || !EqualityComparer<T>.Default.Equals(entry.Value, value);
                entry.Value = value;
                entry.Known = true;
            }
        }
        catch (Exception)
        {
            // папка не ответила: ответа нет, а не «ничего нет»
            lock (entry)
            {
                changed = entry.Known;
                entry.Known = false;
                entry.Value = default;
            }
        }
        finally { Volatile.Write(ref entry.Busy, 0); }

        if (changed) Updated?.Invoke();

        // папки, о которых давно не спрашивали, забываются
        long now = Environment.TickCount64;
        foreach (var (key, old) in _entries)
            if (now - Volatile.Read(ref old.AskedAt) > ForgetAfterMs && Volatile.Read(ref old.Busy) == 0)
                _entries.TryRemove(key, out _);
    }
}

static class Retry
{
    /// <summary>
    /// Повторяет попытку, пока она не удастся или пока <paramref name="stop"/> не скажет прекратить.
    /// Между попытками ждёт всё дольше: по очереди значения из <paramref name="delays"/>, дальше — последнее.
    /// </summary>
    /// <param name="onFailure">Вызывается после каждой неудачи: ошибка и сколько будем ждать до следующей попытки.</param>
    public static async Task UntilDone(Func<Task> attempt, Func<bool> stop, IReadOnlyList<TimeSpan> delays, Action<Exception, TimeSpan> onFailure)
    {
        for (int failures = 0; ; failures++)
        {
            try
            {
                await attempt();
                return;
            }
            catch (Exception ex)
            {
                var wait = delays[Math.Min(failures, delays.Count - 1)];
                onFailure(ex, wait);
                if (stop()) return;
                await Task.Delay(wait);
                if (stop()) return;
            }
        }
    }
}
