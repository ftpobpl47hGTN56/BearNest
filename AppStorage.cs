using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite; 
using System.IO;

namespace VpnClient
{
    public class AppStorage : IDisposable
    {
        private readonly SqliteConnection _db;

        // Храним базу рядом с exe — в папке AppData
        private static string DbPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BearNest", "settings.db");

        public AppStorage()
        {
            // Создаём папку если нет
            Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);

            _db = new SqliteConnection($"Data Source={DbPath}");
            _db.Open();
            InitSchema();
        }

        // ── СОЗДАЁМ ТАБЛИЦУ ──────────────────────────────────────────
        private void InitSchema()
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS settings (
                    key   TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );
                """;
            cmd.ExecuteNonQuery();
        }

        // ── СОХРАНИТЬ ЗНАЧЕНИЕ ───────────────────────────────────────
        public void Set(string key, string value)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                INSERT INTO settings (key, value) VALUES ($k, $v)
                ON CONFLICT(key) DO UPDATE SET value = $v;
                """;
            cmd.Parameters.AddWithValue("$k", key);
            cmd.Parameters.AddWithValue("$v", value);
            cmd.ExecuteNonQuery();
        }

        // ── ПРОЧИТАТЬ ЗНАЧЕНИЕ ───────────────────────────────────────
        public string? Get(string key)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT value FROM settings WHERE key = $k";
            cmd.Parameters.AddWithValue("$k", key);
            var result = cmd.ExecuteScalar();
            return result as string;
        }

        // ── УДОБНЫЕ МЕТОДЫ ───────────────────────────────────────────
        public void SetSubscriptionUrl(string url) => Set("sub_url", url);
        public string? GetSubscriptionUrl() => Get("sub_url");

        public void SetLastServer(string name) => Set("last_server", name);
        public string? GetLastServer() => Get("last_server");

        public void SetAutoConnect(bool value) => Set("auto_connect", value ? "1" : "0");
        public bool GetAutoConnect() => Get("auto_connect") == "1";

        public void Dispose() => _db.Dispose();
    }
}