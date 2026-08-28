namespace TaskGuide.Infrastructure.Storage;

public static class FireRetention
{
    public static int Sweep(string dataDir, DateOnly today)
    {
        var firesDir = Path.Combine(dataDir, "fires");
        if (!Directory.Exists(firesDir)) return 0;

        var removed = 0;
        foreach (var path in Directory.EnumerateFiles(firesDir))
        {
            var date = FireCodec.DateFromFileName(Path.GetFileName(path));
            if (date is null || today.DayNumber - date.Value.DayNumber <= 30) continue;

            File.Delete(path);
            removed++;
        }

        return removed;
    }
}
