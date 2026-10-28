using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FaceSlapper.Core
{
    /// <summary>
    /// 日志组件：把 Unity 控制台输出同步写入文件（persistentDataPath/Logs 下），便于联机调试。
    /// </summary>
    public class LogComponent : MonoBehaviour, IGameComponent, IUpdatable
    {
        [Tooltip("Warning 及以上级别是否附带堆栈")]
        [SerializeField] private bool _includeStackTrace = true;

        private StreamWriter _writer;
        private readonly object _lock = new object();
        private readonly Queue<string> _pending = new Queue<string>(64);

        public string LogFilePath { get; private set; }

        public void OnInit()
        {
            try
            {
                string dir = Path.Combine(Application.persistentDataPath, "Logs");
                Directory.CreateDirectory(dir);
                LogFilePath = Path.Combine(dir, $"log_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                _writer = new StreamWriter(LogFilePath, false) { AutoFlush = true };
                Application.logMessageReceived += OnLogMessageReceived;
                Debug.Log($"[LogComponent] 日志文件: {LogFilePath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[LogComponent] 初始化失败: {e.Message}");
            }
        }

        public void OnShutdown()
        {
            Application.logMessageReceived -= OnLogMessageReceived;
            Flush();
            _writer?.Close();
            _writer = null;
        }

        private void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            string line = $"[{DateTime.Now:HH:mm:ss.fff}][{type}] {condition}";
            if (_includeStackTrace && type >= LogType.Warning && !string.IsNullOrEmpty(stackTrace))
                line += Environment.NewLine + stackTrace;
            lock (_lock) _pending.Enqueue(line);
        }

        public void OnUpdate(float deltaTime) => Flush();

        private void Flush()
        {
            if (_writer == null) return;
            lock (_lock)
            {
                while (_pending.Count > 0)
                    _writer.WriteLine(_pending.Dequeue());
            }
        }
    }
}
