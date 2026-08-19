using System.Text.Json;

namespace NeatWin.App;

internal sealed class SettingsStore
{
    private readonly string _settingsPath;

    internal SettingsStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NeatWin");
        _settingsPath = Path.Combine(directory, "settings.json");
    }

    internal HotkeyBinding LoadHotkey()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return HotkeyBinding.Default;
            }

            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<StoredSettings>(json);
            if (settings is null || !Enum.IsDefined(typeof(Keys), settings.Key))
            {
                return HotkeyBinding.Default;
            }

            return new HotkeyBinding(
                (Keys)settings.Key,
                settings.Control,
                settings.Alt,
                settings.Shift,
                settings.Win);
        }
        catch
        {
            return HotkeyBinding.Default;
        }
    }

    internal void SaveHotkey(HotkeyBinding binding)
    {
        var directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);

        var settings = new StoredSettings
        {
            Key = (int)binding.Key,
            Control = binding.Control,
            Alt = binding.Alt,
            Shift = binding.Shift,
            Win = binding.Win,
        };

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
        File.WriteAllText(_settingsPath, json);
    }

    private sealed class StoredSettings
    {
        public int Key { get; set; }
        public bool Control { get; set; }
        public bool Alt { get; set; }
        public bool Shift { get; set; }
        public bool Win { get; set; }
    }
}
