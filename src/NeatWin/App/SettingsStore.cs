using System.Text.Json;
using NeatWin.Core;

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
        var settings = LoadStoredSettings();
        if (!Enum.IsDefined(typeof(Keys), settings.Key))
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

    internal TidyOptions LoadTidyOptions()
    {
        var stored = LoadStoredSettings();
        var defaults = new TidyOptions();
        var mode = stored.AlgorithmMode is int rawMode && Enum.IsDefined(typeof(TidyAlgorithmMode), rawMode)
            ? (TidyAlgorithmMode)rawMode
            : defaults.AlgorithmMode;

        return defaults with
        {
            AlgorithmMode = mode,
            PreserveLayoutWeight = Math.Clamp(stored.PreserveLayoutWeight ?? defaults.PreserveLayoutWeight, 0.10, 5.0),
            OrderlinessWeight = Math.Clamp(stored.OrderlinessWeight ?? defaults.OrderlinessWeight, 0.10, 5.0),
            SpaceUsageWeight = Math.Clamp(stored.SpaceUsageWeight ?? defaults.SpaceUsageWeight, 0.0, 5.0),
            SmartIterations = Math.Clamp(stored.SmartIterations ?? defaults.SmartIterations, 4, 128),
            NeighborSnapDistance = Math.Clamp(stored.NeighborSnapDistance ?? defaults.NeighborSnapDistance, 0, 240),
            AlignmentSnapDistance = Math.Clamp(stored.AlignmentSnapDistance ?? defaults.AlignmentSnapDistance, 0, 120),
            ScreenSnapDistance = Math.Clamp(stored.ScreenSnapDistance ?? defaults.ScreenSnapDistance, 0, 240),
            MaximumEdgeAdjustment = Math.Clamp(stored.MaximumEdgeAdjustment ?? defaults.MaximumEdgeAdjustment, 0, 480),
            MaximumSizeChangeRatio = Math.Clamp(stored.MaximumSizeChangeRatio ?? defaults.MaximumSizeChangeRatio, 0, 0.50),
            RescueOffscreenWindows = stored.RescueOffscreenWindows ?? defaults.RescueOffscreenWindows,
            Passes = Math.Clamp(stored.Passes ?? defaults.Passes, 1, 5),
        };
    }

    internal void SaveHotkey(HotkeyBinding binding)
    {
        var settings = LoadStoredSettings();
        settings.Key = (int)binding.Key;
        settings.Control = binding.Control;
        settings.Alt = binding.Alt;
        settings.Shift = binding.Shift;
        settings.Win = binding.Win;
        WriteStoredSettings(settings);
    }

    internal void SaveTidyOptions(TidyOptions options)
    {
        var settings = LoadStoredSettings();
        settings.AlgorithmMode = (int)options.AlgorithmMode;
        settings.PreserveLayoutWeight = options.PreserveLayoutWeight;
        settings.OrderlinessWeight = options.OrderlinessWeight;
        settings.SpaceUsageWeight = options.SpaceUsageWeight;
        settings.SmartIterations = options.SmartIterations;
        settings.NeighborSnapDistance = options.NeighborSnapDistance;
        settings.AlignmentSnapDistance = options.AlignmentSnapDistance;
        settings.ScreenSnapDistance = options.ScreenSnapDistance;
        settings.MaximumEdgeAdjustment = options.MaximumEdgeAdjustment;
        settings.MaximumSizeChangeRatio = options.MaximumSizeChangeRatio;
        settings.RescueOffscreenWindows = options.RescueOffscreenWindows;
        settings.Passes = options.Passes;
        WriteStoredSettings(settings);
    }

    private StoredSettings LoadStoredSettings()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return StoredSettings.CreateDefault();
            }

            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<StoredSettings>(json) ?? StoredSettings.CreateDefault();
        }
        catch
        {
            return StoredSettings.CreateDefault();
        }
    }

    private void WriteStoredSettings(StoredSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);

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

        public int? AlgorithmMode { get; set; }
        public double? PreserveLayoutWeight { get; set; }
        public double? OrderlinessWeight { get; set; }
        public double? SpaceUsageWeight { get; set; }
        public int? SmartIterations { get; set; }
        public int? NeighborSnapDistance { get; set; }
        public int? AlignmentSnapDistance { get; set; }
        public int? ScreenSnapDistance { get; set; }
        public int? MaximumEdgeAdjustment { get; set; }
        public double? MaximumSizeChangeRatio { get; set; }
        public bool? RescueOffscreenWindows { get; set; }
        public int? Passes { get; set; }

        internal static StoredSettings CreateDefault()
        {
            var hotkey = HotkeyBinding.Default;
            return new StoredSettings
            {
                Key = (int)hotkey.Key,
                Control = hotkey.Control,
                Alt = hotkey.Alt,
                Shift = hotkey.Shift,
                Win = hotkey.Win,
            };
        }
    }
}
