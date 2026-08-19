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

    internal bool LoadAutoTidyEnabled() => LoadStoredSettings().AutoTidyEnabled ?? false;

    internal TidyOptions LoadTidyOptions()
    {
        var stored = LoadStoredSettings();
        var defaults = new TidyOptions();

        return defaults with
        {
            AlgorithmMode = ReadEnum(stored.AlgorithmMode, defaults.AlgorithmMode),
            SmartStrength = ReadEnum(stored.SmartStrength, defaults.SmartStrength),
            SmartHitTendency = ReadEnum(stored.SmartHitTendency, defaults.SmartHitTendency),
            SmartSizeTendency = ReadEnum(stored.SmartSizeTendency, defaults.SmartSizeTendency),

            // Raw solver fields are retained for Classic mode and for backward-compatible
            // settings files. Smart mode derives its internal values from the intent profiles.
            PreserveLayoutWeight = Math.Clamp(stored.PreserveLayoutWeight ?? defaults.PreserveLayoutWeight, 0.10, 5.0),
            ResizeResistanceWeight = Math.Clamp(stored.ResizeResistanceWeight ?? defaults.ResizeResistanceWeight, 0.0, 5.0),
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

    internal SmartBehaviorOptions LoadSmartBehaviorOptions()
    {
        var stored = LoadStoredSettings();
        var defaults = new SmartBehaviorOptions();
        return defaults with
        {
            PreferReversibleVerticalFill = stored.PreferReversibleVerticalFill ?? defaults.PreferReversibleVerticalFill,
            OverlapAvoidance = ReadEnum(stored.SmartOverlapAvoidance, defaults.OverlapAvoidance),
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

    internal void SaveAutoTidyEnabled(bool enabled)
    {
        var settings = LoadStoredSettings();
        settings.AutoTidyEnabled = enabled;
        WriteStoredSettings(settings);
    }

    internal void SaveTidyOptions(TidyOptions options)
    {
        var settings = LoadStoredSettings();
        settings.AlgorithmMode = (int)options.AlgorithmMode;
        settings.SmartStrength = (int)options.SmartStrength;
        settings.SmartHitTendency = (int)options.SmartHitTendency;
        settings.SmartSizeTendency = (int)options.SmartSizeTendency;

        settings.PreserveLayoutWeight = options.PreserveLayoutWeight;
        settings.ResizeResistanceWeight = options.ResizeResistanceWeight;
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

    internal void SaveSmartBehaviorOptions(SmartBehaviorOptions options)
    {
        var settings = LoadStoredSettings();
        settings.PreferReversibleVerticalFill = options.PreferReversibleVerticalFill;
        settings.SmartOverlapAvoidance = (int)options.OverlapAvoidance;
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

    private static TEnum ReadEnum<TEnum>(int? raw, TEnum fallback)
        where TEnum : struct, Enum
    {
        return raw is int value && Enum.IsDefined(typeof(TEnum), value)
            ? (TEnum)Enum.ToObject(typeof(TEnum), value)
            : fallback;
    }

    private sealed class StoredSettings
    {
        public int Key { get; set; }
        public bool Control { get; set; }
        public bool Alt { get; set; }
        public bool Shift { get; set; }
        public bool Win { get; set; }
        public bool? AutoTidyEnabled { get; set; }

        public int? AlgorithmMode { get; set; }
        public int? SmartStrength { get; set; }
        public int? SmartHitTendency { get; set; }
        public int? SmartSizeTendency { get; set; }
        public int? SmartOverlapAvoidance { get; set; }
        public bool? PreferReversibleVerticalFill { get; set; }

        public double? PreserveLayoutWeight { get; set; }
        public double? ResizeResistanceWeight { get; set; }
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
                AutoTidyEnabled = false,
            };
        }
    }
}
