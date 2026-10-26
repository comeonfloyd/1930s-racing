namespace RacingSim.Tests;

internal static class TestData
{
    private static readonly Lazy<GameData> Shared = new(() => GameData.FromDirectory(FindDataDir()));

    public static GameData Load() => Shared.Value;

    private static string FindDataDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "game", "data");
            if (Directory.Exists(candidate))
                return candidate;
        }
        throw new DirectoryNotFoundException("Could not find game/data above the test output folder");
    }
}
