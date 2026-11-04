using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacGuard.Core;

public enum Severity { Info, Warn, Alarm }

public sealed class LogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Date { get; set; } = DateTime.Now;
    /// <summary>Segoe simge karakteri.</summary>
    public string Icon { get; set; } = Glyphs.Info;
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    [JsonConverter(typeof(JsonStringEnumConverter<Severity>))]
    public Severity Severity { get; set; }

    [JsonIgnore] public bool HasDetail => !string.IsNullOrEmpty(Detail);
    [JsonIgnore] public string Time => Date.ToString("HH:mm:ss");
}

/// <summary>
/// Basit, kalıcı olay defteri. Son 200 kayıt tutulur.
/// Yalnızca arayüz iş parçacığından çağrılır (macOS'taki @MainActor).
/// </summary>
public sealed class EventLog
{
    public static EventLog Shared { get; } = new();

    private const int Limit = 200;
    private readonly string _path = AppPaths.File("events.json");

    public ObservableCollection<LogEntry> Entries { get; } = new();

    private EventLog()
    {
        try
        {
            if (File.Exists(_path))
            {
                var list = JsonSerializer.Deserialize<List<LogEntry>>(File.ReadAllText(_path));
                if (list != null) foreach (var e in list) Entries.Add(e);
            }
        }
        catch { /* bozuk dosya: temiz başla */ }
    }

    public void Log(string title, string detail = "", string? icon = null, Severity severity = Severity.Info)
    {
        Entries.Insert(0, new LogEntry
        {
            Title = title,
            Detail = detail,
            Icon = icon ?? Glyphs.Info,
            Severity = severity,
        });
        while (Entries.Count > Limit) Entries.RemoveAt(Entries.Count - 1);
        Persist();
    }

    public void Clear()
    {
        Entries.Clear();
        Persist();
    }

    private void Persist()
    {
        try
        {
            AppPaths.WriteAtomic(_path, JsonSerializer.SerializeToUtf8Bytes(Entries));
        }
        catch { }
    }
}
