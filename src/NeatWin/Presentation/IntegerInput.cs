using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace NeatWin.Presentation;

/// <summary>A bounded integer editor with keyboard and button input.</summary>
public sealed class IntegerInput : Grid
{
    private readonly TextBox _text;
    private int _value;
    public int Minimum { get; set; }
    public int Maximum { get; set; } = 100;
    public int Step { get; set; } = 1;
    public int Value
    {
        get => _value;
        set { _value = Math.Clamp(value, Minimum, Maximum); _text.Text = _value.ToString(CultureInfo.CurrentCulture); }
    }
    public event EventHandler? ValueChanged;

    public IntegerInput()
    {
        MinWidth = 120;
        ColumnDefinitions.Add(new ColumnDefinition());
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _text = new TextBox { Text = "0", MinWidth = 65, VerticalContentAlignment = VerticalAlignment.Center };
        Children.Add(_text);
        _text.LostKeyboardFocus += (_, _) => Commit();
        _text.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Up or Key.Down)
            { Commit(); Change(_value + (e.Key == Key.Up ? Step : -Step)); e.Handled = true; }
            else if (e.Key == Key.Enter) { Commit(); e.Handled = true; }
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 0, 0) };
        foreach (var (label, delta) in new[] { ("−", -1), ("+", 1) })
        {
            var button = new Button { Content = label, MinWidth = 28, Padding = new Thickness(5, 3, 5, 3), Focusable = false };
            System.Windows.Automation.AutomationProperties.SetName(button, delta < 0 ? "减少" : "增加");
            button.Click += (_, _) => { Commit(); Change(_value + delta * Step); };
            buttons.Children.Add(button);
        }
        SetColumn(buttons, 1); Children.Add(buttons);
    }
    private void Commit()
    {
        if (int.TryParse(_text.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value)) Change(value);
        else Value = _value;
    }
    private void Change(int value)
    {
        var old = _value; Value = value;
        if (_value != old) ValueChanged?.Invoke(this, EventArgs.Empty);
    }
}
