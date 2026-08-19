namespace NeatWin.Core;

public enum SmartOverlapAvoidance
{
    Gentle,
    Balanced,
    Strong,
}

public sealed record SmartBehaviorOptions(
    bool PreferReversibleVerticalFill = true,
    SmartOverlapAvoidance OverlapAvoidance = SmartOverlapAvoidance.Balanced);
