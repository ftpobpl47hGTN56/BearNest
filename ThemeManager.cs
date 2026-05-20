using System;
using System.Windows;

namespace VpnClient
{
    public static class ThemeManager
    {
        public static readonly string[] ThemeNames =
            { "🌙 Dark", "☀️ Light", "🏔️ Nord", "🐻 Bear" };

        private static readonly string[] ThemeFiles =
            { "Themes/Dark.xaml", "Themes/Light.xaml",
              "Themes/Nord.xaml", "Themes/Bear.xaml" };

        public static void Apply(int index)
        {
            if (index < 0 || index >= ThemeFiles.Length) return;

            var dict = System.Windows.Application.Current.Resources.MergedDictionaries;

            // Удаляем старую тему (первый словарь)
            if (dict.Count > 0)
                dict.RemoveAt(0);

            // Вставляем новую тему на первое место
            dict.Insert(0, new ResourceDictionary
            {
                Source = new Uri(ThemeFiles[index], UriKind.Relative)
            });
        }

        public static void Apply(string name)
        {
            for (int i = 0; i < ThemeNames.Length; i++)
                if (ThemeNames[i] == name)
                {
                    Apply(i);
                    return;
                }
        }

        public static void ApplyFromFile(string path)
        {
            if (!System.IO.File.Exists(path)) return;

            var dict = System.Windows.Application.Current.Resources.MergedDictionaries;
            if (dict.Count > 0)
                dict.RemoveAt(0);

            dict.Insert(0, new ResourceDictionary
            {
                Source = new Uri(path, UriKind.Absolute)
            });
        }

    }
}