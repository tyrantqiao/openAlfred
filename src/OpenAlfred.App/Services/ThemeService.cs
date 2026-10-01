using System.Windows;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Application = System.Windows.Application;

namespace OpenAlfred.App.Services;

/// <summary>
/// 主题切换：联动 WPF-UI 控件主题与 openAlfred 自定义令牌字典。
/// </summary>
public static class ThemeService
{
    private const string LightDictionary = "Themes/Tokens.Light.xaml";
    private const string DarkDictionary = "Themes/Tokens.Dark.xaml";

    public static void Apply(ThemeMode mode)
    {
        var actual = mode switch
        {
            ThemeMode.Dark => ApplicationTheme.Dark,
            ThemeMode.Light => ApplicationTheme.Light,
            _ => SystemThemeReader.IsSystemDark() ? ApplicationTheme.Dark : ApplicationTheme.Light,
        };

        ApplicationThemeManager.Apply(actual, updateAccent: false);
        SwapTokenDictionary(actual == ApplicationTheme.Dark ? DarkDictionary : LightDictionary);
    }

    private static void SwapTokenDictionary(string source)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        for (var i = 0; i < dictionaries.Count; i++)
        {
            var uri = dictionaries[i].Source?.OriginalString;
            if (uri is not null && (uri.EndsWith("Tokens.Light.xaml") || uri.EndsWith("Tokens.Dark.xaml")))
            {
                dictionaries[i] = new ResourceDictionary { Source = new Uri(source, UriKind.Relative) };
                return;
            }
        }
        dictionaries.Add(new ResourceDictionary { Source = new Uri(source, UriKind.Relative) });
    }
}

/// <summary>读注册表判断系统是否为深色模式。</summary>
internal static class SystemThemeReader
{
    public static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
