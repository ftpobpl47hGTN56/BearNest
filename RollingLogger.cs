using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks; 
using System.IO;

namespace VpnClient
{
    public class RollingLogger : IDisposable
    {
        private readonly string _logDir;
        private readonly long _maxBytes;
        private readonly int _maxFiles;
        private StreamWriter? _writer;
        private long _currentSize;

        public static string LogDir =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BearNest", "logs");

       public RollingLogger(long maxMb = 2, int maxFiles = 3)
        {
            _logDir   = LogDir;
            _maxBytes = maxMb * 1024 * 1024;
            _maxFiles = maxFiles;
            Directory.CreateDirectory(_logDir);

            // Удаляем лишние файлы сразу при старте
            CleanOldFiles();
            OpenNewFile();
        }

    private void CleanOldFiles()
    {
        var files = Directory.GetFiles(_logDir, "bearnest_*.log");
        Array.Sort(files);

        // Оставляем только (_maxFiles - 1) старых файлов
        // место для нового которые сейчас создадим
        while (files.Length >= _maxFiles)
        {
            File.Delete(files[0]);
            files = Directory.GetFiles(_logDir, "bearnest_*.log");
            Array.Sort(files);
        }
    }

        public void Write(string line)
        {
            if (_writer == null) return;

            if (_currentSize >= _maxBytes)
            {
                _writer.Dispose();
                RotateFiles();
                OpenNewFile();
            }

            var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {line}";
            _writer.WriteLine(entry);
            _writer.Flush();
            _currentSize += entry.Length + 2;
        }

        private void OpenNewFile()
        {
            var path = Path.Combine(_logDir, $"bearnest_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            _writer = new StreamWriter(path, append: false);
            _currentSize = 0;
        }

        private void RotateFiles()
        {
            var files = Directory.GetFiles(_logDir, "bearnest_*.log");
            Array.Sort(files);
            while (files.Length >= _maxFiles)
            {
                File.Delete(files[0]);
                files = Directory.GetFiles(_logDir, "bearnest_*.log");
                Array.Sort(files);
            }
        }

        public void Dispose() => _writer?.Dispose();
    }
}