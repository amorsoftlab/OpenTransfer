using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using openTransferWPF.Models;

namespace openTransferWPF.Services
{
    public sealed class TransferHistoryService
    {
        public static TransferHistoryService Instance { get; } = new();
        private readonly string _path;
        private readonly object _gate = new();

        private TransferHistoryService()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "openTransferWPF");
            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, "transfer-history.json");
        }

        public List<TransferHistoryEntry> Load()
        {
            lock (_gate)
            {
                try
                {
                    if (!File.Exists(_path)) return new();
                    return JsonSerializer.Deserialize<List<TransferHistoryEntry>>(File.ReadAllText(_path)) ?? new();
                }
                catch { return new(); }
            }
        }

        public void Add(TransferHistoryEntry entry)
        {
            lock (_gate)
            {
                var entries = Load();
                entries.Insert(0, entry);
                entries = entries.Take(500).ToList();
                File.WriteAllText(_path, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
            }
        }

        public void Clear()
        {
            lock (_gate) { if (File.Exists(_path)) File.Delete(_path); }
        }
    }
}
