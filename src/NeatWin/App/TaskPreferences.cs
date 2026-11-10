using System.Text.Json;
using NeatWin.Core;

namespace NeatWin.App;

internal sealed record TaskPreferences(TaskLayoutProfile Profile, ViewingCalibration? Calibration = null)
{
    private static string PathName => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeatWin", "human-factors.json");
    internal static TaskPreferences Load()
    {
        try
        {
            var value = JsonSerializer.Deserialize<TaskPreferences>(File.ReadAllText(PathName));
            return value is null ? new TaskPreferences(new TaskLayoutProfile()) : value with { Profile = (value.Profile ?? new TaskLayoutProfile()).Normalize() };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { return new TaskPreferences(new TaskLayoutProfile()); }
    }
    internal void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        var temp = PathName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(this with { Profile = Profile.Normalize() }, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, PathName, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
