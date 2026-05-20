using System;
using System.Windows;

namespace VpnClient
{
    public static class LocalizationManager
    {
        public static readonly string[] Languages = { "🇷🇺 RU", "🇬🇧 EN" };

        private static readonly string[] LangFiles =
            { "Strings/ru.xaml", "Strings/en.xaml" };

        // Индекс строкового словаря в MergedDictionaries
        // App.xaml: [0]=тема, [1]=стили, [2]=строки
        private const int StringsDictIndex = 2;

        public static void Apply(int index)
        {
            if (index < 0 || index >= LangFiles.Length) return;

            var dict = System.Windows.Application.Current.Resources.MergedDictionaries;

            if (dict.Count > StringsDictIndex)
                dict.RemoveAt(StringsDictIndex);

            dict.Insert(StringsDictIndex, new ResourceDictionary
            {
                Source = new Uri(LangFiles[index], UriKind.Relative)
            });
        }

        public static void Apply(string name)
        {
            for (int i = 0; i < Languages.Length; i++)
                if (Languages[i] == name) { Apply(i); return; }
        }

        public static string Get(string key)
        {
            var val = System.Windows.Application.Current.Resources[key];
            return val as string ?? key;
        }
    }
}