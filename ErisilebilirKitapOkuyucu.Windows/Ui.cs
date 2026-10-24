using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace ErisilebilirKitapOkuyucu;

/// <summary>Tüm sayfaların (kitaplık, okuyucu, oynatıcı, ayarlar) ortak tabanı.</summary>
public abstract class PageBase : UserControl
{
    public abstract string PageTitle { get; }
    /// <summary>Sayfa gösterildiğinde odaklanacak öğe (ekran okuyucu sayfayı buradan okumaya başlar).</summary>
    public abstract UIElement? InitialFocus { get; }
    public virtual void OnShown() { }
    public virtual void OnHidden() { }
    /// <summary>Esc tuşu. true döndürürse işlenmiş sayılır.</summary>
    public virtual bool HandleBack() { MainWindow.Instance.ShowLibrary(); return true; }
    protected MainWindow Host => MainWindow.Instance;
}

public static class Ui
{
    public static Button Button(string text, Action click, string? help = null, string? name = null, string? tip = null)
    {
        var b = new Button { Content = text };
        AutomationProperties.SetName(b, name ?? text.Replace("_", ""));
        if (help != null) AutomationProperties.SetHelpText(b, help);
        if (tip != null) b.ToolTip = tip;
        b.Click += (_, _) => click();
        return b;
    }

    public static TextBlock Text(string text, string? style = null)
    {
        var t = new TextBlock { Text = text };
        if (style != null) t.Style = (Style)Application.Current.Resources[style];
        return t;
    }

    public static void Name(DependencyObject o, string name) => AutomationProperties.SetName(o, name);

    public static RadioButton Radio(string text, string group, Action onChecked, bool isChecked = false)
    {
        var r = new RadioButton { Content = text, GroupName = group, IsChecked = isChecked };
        r.Checked += (_, _) => onChecked();
        return r;
    }

    public static bool IsTypingTarget(object? el) =>
        el is TextBox { IsReadOnly: false } || el is PasswordBox;

    public static bool IsActivatableTarget(object? el) =>
        el is System.Windows.Controls.Button or CheckBox or RadioButton or ListBoxItem or Slider or ListBox or TextBox { IsReadOnly: false };
}
