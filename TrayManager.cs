using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks; 
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace VpnClient
{
    public class TrayManager : IDisposable
    {
        private readonly NotifyIcon _tray;
        private readonly ToolStripMenuItem _connectItem;
        private readonly ToolStripMenuItem _disconnectItem;
        private readonly ToolStripMenuItem _autoStartItem;

        // Иконки для разных состояний
        private readonly Icon _iconStopped;
        private readonly Icon _iconConnected;
        private readonly Icon _iconError;

        public event Action? OnShowWindow;
        public event Action? OnConnect;
        public event Action? OnDisconnect;
        public event Action? OnAutoStartToggled;

        public TrayManager()
        {
            // Загружаем иконки
            _iconStopped = LoadIcon("icons\\bearnst_Started.ico");
            _iconConnected = LoadIcon("icons\\bearnst_Connected.ico");
            _iconError = LoadIcon("icons\\bearnst_error_DisConnected.ico");

            // Контекстное меню трея
            var menu = new ContextMenuStrip();

            // Показать окно
            var showItem = new ToolStripMenuItem("🐻 BearNest — Открыть");
            showItem.Font = new Font(showItem.Font, System.Drawing.FontStyle.Bold);
            showItem.Click += (_, _) => OnShowWindow?.Invoke();
            menu.Items.Add(showItem);

            menu.Items.Add(new ToolStripSeparator());

            // Подключить
            _connectItem = new ToolStripMenuItem("▶  Подключить");
            _connectItem.Click += (_, _) => OnConnect?.Invoke();
            menu.Items.Add(_connectItem);

            // Отключить
            _disconnectItem = new ToolStripMenuItem("■  Отключить");
            _disconnectItem.Enabled = false;
            _disconnectItem.Click += (_, _) => OnDisconnect?.Invoke();
            menu.Items.Add(_disconnectItem);

            menu.Items.Add(new ToolStripSeparator());

            // Автозапуск
            _autoStartItem = new ToolStripMenuItem("🚀 Автозапуск с Windows");
            _autoStartItem.Checked = AutoStart.IsEnabled();
            _autoStartItem.Click += (_, _) =>
            {
                OnAutoStartToggled?.Invoke();
                _autoStartItem.Checked = AutoStart.IsEnabled();
            };
            menu.Items.Add(_autoStartItem);

            menu.Items.Add(new ToolStripSeparator());

            // Выход
            var exitItem = new ToolStripMenuItem("✖  Выход");
            exitItem.Click += (_, _) =>
            {
                OnDisconnect?.Invoke();
                System.Windows.Application.Current.Shutdown();
            };
            menu.Items.Add(exitItem);

            // Создаём иконку трея
            _tray = new NotifyIcon
            {
                Icon = _iconStopped,
                Text = "BearNest — Остановлен",
                ContextMenuStrip = menu,
                Visible = true
            };

            // Двойной клик — открыть окно
            _tray.DoubleClick += (_, _) => OnShowWindow?.Invoke();
        }

        // ── ОБНОВИТЬ ИКОНКУ И ПОДСКАЗКУ ──────────────────────────────
        public void SetConnected(bool connected)
        {
            _tray.Icon = connected ? _iconConnected : _iconStopped;
            _tray.Text = connected ? "BearNest — Подключён" : "BearNest — Остановлен";
            _connectItem.Enabled = !connected;
            _disconnectItem.Enabled = connected;
        }

        public void SetError(string message)
        {
            _tray.Icon = _iconError;
            _tray.Text = $"BearNest — Ошибка: {message[..Math.Min(message.Length, 60)]}";
        }

        // ── ВСПЛЫВАЮЩЕЕ УВЕДОМЛЕНИЕ ──────────────────────────────────
        public void ShowBalloon(string title, string text, int ms = 3000)
        {
            _tray.ShowBalloonTip(ms,
                title, text,
                ToolTipIcon.Info);
        }

        // ── ЗАГРУЗКА ИКОНКИ ──────────────────────────────────────────
        private static Icon LoadIcon(string relativePath)
        {
            var fullPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                relativePath);

            return File.Exists(fullPath)
                ? new Icon(fullPath)
                : SystemIcons.Application;
        }

        public void Dispose()
        {
            _tray.Visible = false;
            _tray.Dispose();
            _iconStopped.Dispose();
            _iconConnected.Dispose();
            _iconError.Dispose();
        }
    }
}