using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

// Явные алиасы для устранения неоднозначности
using WpfColor = System.Windows.Media.Color;
using WpfColors = System.Windows.Media.Colors;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace VpnClient
{
    public partial class ThemeEditorWindow : Window
    {
        // ============================================================
        // Массив цветов с русскими метками (оставлено как вы просили)
        // ============================================================
      // Было: (Key, Label) — русская строка
        // Стало: (Key, LabelKey) — ключ из ResourceDictionary
        private static readonly (string Key, string LabelKey)[] ColorKeys =
        {
            ("BrushBackground",   "ColorLabelBackground"),
            ("BrushSurface",      "ColorLabelSurface"),
            ("BrushSurface2",     "ColorLabelSurface2"),
            ("BrushAccent",       "ColorLabelAccent"),
            ("BrushAccentHover",  "ColorLabelAccentHover"),
            ("BrushText",         "ColorLabelText"),
            ("BrushTextMuted",    "ColorLabelTextMuted"),
            ("BrushBorder",       "ColorLabelBorder"),
            ("BrushConnected",    "ColorLabelConnected"),
            ("BrushDisconnected", "ColorLabelDisconnected"),
            ("BrushButtonConnect","ColorLabelButtonConnect"),
            ("BrushButtonStop",   "ColorLabelButtonStop"),
            ("BrushButtonPing",   "ColorLabelButtonPing"),
            ("BrushButtonLoad",   "ColorLabelButtonLoad"),
            ("BrushButtonFg",     "ColorLabelButtonFg"),
            ("BrushListHover",    "ColorLabelListHover"),
            ("BrushListSelected", "ColorLabelListSelected"),
            ("BrushTabActive",    "ColorLabelTabActive"),
            ("BrushTabInactive",  "ColorLabelTabInactive"),
        };
                    

        private readonly Dictionary<string, WpfColor> _colors = new();
        private string _customThemeName = "Custom";

        public ThemeEditorWindow()
        {
            InitializeComponent();

            LoadCurrentColors();

            CmbBaseTheme.SelectionChanged -= CmbBaseTheme_SelectionChanged;

            foreach (var name in ThemeManager.ThemeNames)
                CmbBaseTheme.Items.Add(name);
            CmbBaseTheme.SelectedIndex = 0;

            CmbBaseTheme.SelectionChanged += CmbBaseTheme_SelectionChanged;

            BuildColorRows();
        }

        private void LoadCurrentColors()
        {
            _colors.Clear();
            foreach (var (key, _) in ColorKeys)
            {
                var brush = System.Windows.Application.Current.Resources[key] as SolidColorBrush;
                _colors[key] = brush?.Color ?? WpfColors.Gray;
            }
        }

        private void BuildColorRows()
            {
                ColorPanel.Children.Clear();

                foreach (var (key, labelKey) in ColorKeys)
                {
                    var row = CreateColorRow(key, labelKey);
                    ColorPanel.Children.Add(row);
                }
            }


       private UIElement CreateColorRow(string key, string labelKey)
        {
            var labelText = System.Windows.Application.Current.Resources[labelKey] as string ?? labelKey;

            var container = new Border
            {
                Margin       = new Thickness(0, 0, 0, 8),
                Padding      = new Thickness(10, 8, 10, 8),
                CornerRadius = new CornerRadius(6),
            }; 
            container.SetResourceReference(Border.BackgroundProperty, "BrushSurface");
            container.SetResourceReference(Border.BorderBrushProperty, "BrushBorder");
            container.BorderThickness = new Thickness(1);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });

            var lbl = new TextBlock
            {
                Text              = labelText,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize          = 12,
            };
            lbl.SetResourceReference(TextBlock.ForegroundProperty, "BrushText");
            Grid.SetColumn(lbl, 0);

            var rightStack = new StackPanel { Orientation = WpfOrientation.Horizontal };
            Grid.SetColumn(rightStack, 1);

            var colorPreview = new Border
            {
                Width           = 28,
                Height          = 28,
                CornerRadius    = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                Margin          = new Thickness(0, 0, 6, 0),
                Background      = new SolidColorBrush(_colors[key]),
                Cursor          = System.Windows.Input.Cursors.Hand,
            };
            colorPreview.SetResourceReference(Border.BorderBrushProperty, "BrushBorder");

            var hexBox = new WpfTextBox
            {
                Text                     = ColorToHex(_colors[key]),
                Width                    = 75,
                Height                   = 28,
                FontFamily               = new WpfFontFamily("Consolas"),
                FontSize                 = 11,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding                  = new Thickness(4, 0, 0, 0),
            };
            hexBox.SetResourceReference(WpfTextBox.BackgroundProperty, "BrushSurface2");
            hexBox.SetResourceReference(WpfTextBox.ForegroundProperty, "BrushText");
            hexBox.SetResourceReference(WpfTextBox.BorderBrushProperty, "BrushBorder");

            var popup = CreateRgbPopup(key, colorPreview, hexBox);
            colorPreview.MouseLeftButtonDown += (_, _) => popup.IsOpen = !popup.IsOpen;

            popup.KeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape)
                    popup.IsOpen = false;
            };

            hexBox.LostFocus += (_, _) =>
            {
                if (TryParseHex(hexBox.Text, out var parsedColor))
                {
                    _colors[key] = parsedColor;
                    colorPreview.Background = new SolidColorBrush(parsedColor);
                    ApplyColorToResources(key, parsedColor);
                }
                else
                {
                    hexBox.Text = ColorToHex(_colors[key]);
                }
            };

            hexBox.KeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                    hexBox.MoveFocus(new System.Windows.Input.TraversalRequest(
                        System.Windows.Input.FocusNavigationDirection.Next));
            };

            rightStack.Children.Add(colorPreview);
            rightStack.Children.Add(popup);
            rightStack.Children.Add(hexBox);

            grid.Children.Add(lbl);
            grid.Children.Add(rightStack);
            container.Child = grid;

            return container;
        }

        private System.Windows.Controls.Primitives.Popup CreateRgbPopup(
            string key, Border colorPreview, WpfTextBox hexBox)
        {
            var popup = new System.Windows.Controls.Primitives.Popup
            {
                AllowsTransparency = true,
                PlacementTarget    = colorPreview,
                Placement          = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                StaysOpen          = true,
            };

            var border = new Border
            {
                Padding         = new Thickness(12),
                CornerRadius    = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Width           = 230,
            };
            border.SetResourceReference(Border.BackgroundProperty,  "BrushSurface2");
            border.SetResourceReference(Border.BorderBrushProperty, "BrushBorder");

            var stack = new StackPanel();

            var bigPreview = new Border
            {
                Height       = 40,
                CornerRadius = new CornerRadius(6),
                Margin       = new Thickness(0, 0, 0, 10),
                Background   = new SolidColorBrush(_colors[key]),
            };

            var currentColor = _colors[key];
            var (rSlider, rLabel) = CreateChannelSlider("R", currentColor.R, WpfColors.Red);
            var (gSlider, gLabel) = CreateChannelSlider("G", currentColor.G, WpfColors.Green);
            var (bSlider, bLabel) = CreateChannelSlider("B", currentColor.B, WpfColors.Blue);

            void UpdateFromSliders()
            {
                var c = WpfColor.FromRgb((byte)rSlider.Value, (byte)gSlider.Value, (byte)bSlider.Value);

                _colors[key]            = c;
                colorPreview.Background = new SolidColorBrush(c);
                bigPreview.Background   = new SolidColorBrush(c);
                hexBox.Text             = ColorToHex(c);
                rLabel.Text             = $"R: {(int)rSlider.Value}";
                gLabel.Text             = $"G: {(int)gSlider.Value}";
                bLabel.Text             = $"B: {(int)bSlider.Value}";
                ApplyColorToResources(key, c);
            }

            rSlider.ValueChanged += (_, _) => UpdateFromSliders();
            gSlider.ValueChanged += (_, _) => UpdateFromSliders();
            bSlider.ValueChanged += (_, _) => UpdateFromSliders();

            stack.Children.Add(bigPreview);
            stack.Children.Add(rLabel);
            stack.Children.Add(rSlider);
            stack.Children.Add(gLabel);
            stack.Children.Add(gSlider);
            stack.Children.Add(bLabel);
            stack.Children.Add(bSlider);

            border.Child = stack;
            popup.Child  = border;
            return popup;
        }

        private static (Slider slider, TextBlock label) CreateChannelSlider(string name, byte value, WpfColor trackColor)
        {
            var label = new TextBlock
            {
                Text     = $"{name}: {value}",
                FontSize = 11,
                Margin   = new Thickness(0, 4, 0, 2),
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "BrushText");

            var slider = new Slider
            {
                Minimum             = 0,
                Maximum             = 255,
                Value               = value,
                TickFrequency       = 1,
                IsSnapToTickEnabled = true,
                Margin              = new Thickness(0, 0, 0, 4),
            };

            return (slider, label);
        }

        private static void ApplyColorToResources(string key, WpfColor color)
        {
            System.Windows.Application.Current.Resources[key] = new SolidColorBrush(color);
        }

        private void CmbBaseTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbBaseTheme.SelectedItem is string theme)
            {
                ThemeManager.Apply(theme);
                LoadCurrentColors();
                BuildColorRows();
            }
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            if (CmbBaseTheme.SelectedItem is string theme)
            {
                ThemeManager.Apply(theme);
                LoadCurrentColors();
                BuildColorRows();
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                FileName         = "MyTheme",
                DefaultExt       = ".xaml",
                Filter           = "XAML тема|*.xaml",
                InitialDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Themes")
            };

            if (dlg.ShowDialog() != true) return;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"");
            sb.AppendLine("                    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"");
            sb.AppendLine("                    xmlns:sys=\"clr-namespace:System;assembly=mscorlib\">");
            sb.AppendLine();

            foreach (var (key, labelKey) in ColorKeys)
            {
                var c        = _colors[key];
                var hex      = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
                var labelText = System.Windows.Application.Current.Resources[labelKey] as string ?? labelKey;
                sb.AppendLine($"    <!-- {labelText} -->");
                sb.AppendLine($"    <SolidColorBrush x:Key=\"{key}\" Color=\"{hex}\"/>");
            }

            sb.AppendLine();
            sb.AppendLine("    <sys:Double x:Key=\"FontSizeNormal\">13</sys:Double>");
            sb.AppendLine("    <sys:Double x:Key=\"FontSizeSmall\">11</sys:Double>");
            sb.AppendLine("    <sys:Double x:Key=\"FontSizeLarge\">18</sys:Double>");
            sb.AppendLine("    <sys:Double x:Key=\"FontSizeTitle\">14</sys:Double>");
            sb.AppendLine("    <CornerRadius x:Key=\"CornerRadius\">6</CornerRadius>");
            sb.AppendLine("    <Thickness x:Key=\"BorderThickness\">1</Thickness>");
            sb.AppendLine("</ResourceDictionary>");

            File.WriteAllText(dlg.FileName, sb.ToString());

            _customThemeName = Path.GetFileNameWithoutExtension(dlg.FileName);
            System.Windows.MessageBox.Show($"Тема сохранена: {dlg.FileName}", "BearNest",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private static string ColorToHex(WpfColor c) =>
            $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        private static bool TryParseHex(string hex, out WpfColor result)
        {
            result = WpfColors.Gray;
            try
            {
                hex = hex.TrimStart('#');
                if (hex.Length != 6) return false;

                var r = Convert.ToByte(hex[..2], 16);
                var g = Convert.ToByte(hex[2..4], 16);
                var b = Convert.ToByte(hex[4..6], 16);
                result = WpfColor.FromRgb(r, g, b);
                return true;
            }
            catch { return false; }
        }
    }
}