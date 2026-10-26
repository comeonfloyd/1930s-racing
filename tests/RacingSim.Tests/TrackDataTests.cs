using System.Text.Json;

namespace RacingSim.Tests;

public class TrackDataTests
{
    private static string TracksDir() => Path.Combine(TestData.DataDir, "tracks");

    [Fact]
    public void EveryCircuitHasATrackDrawing()
    {
        var dir = TracksDir();
        foreach (var id in TestData.Load().Circuits.Keys)
            Assert.True(File.Exists(Path.Combine(dir, $"{id}.json")), $"No track drawing for {id}");
    }

    [Fact]
    public void TrackPointsStayInsideTheDrawing()
    {
        foreach (var file in Directory.GetFiles(TracksDir(), "*.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var points = doc.RootElement.GetProperty("points").EnumerateArray().ToList();
            Assert.True(points.Count >= 4, file);
            Assert.All(points, p =>
            {
                Assert.InRange(p[0].GetDouble(), 0, 1);
                Assert.InRange(p[1].GetDouble(), 0, 1);
            });
            Assert.Equal(Path.GetFileNameWithoutExtension(file), doc.RootElement.GetProperty("circuitId").GetString());
        }
    }
}
