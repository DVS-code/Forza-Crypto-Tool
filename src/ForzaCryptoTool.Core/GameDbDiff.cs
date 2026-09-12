using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace ForzaCryptoTool;

internal static class GameDbDiff
{
    private const int MaxModifiedRowsPerTable = 2000;
    private const int MaxCellChangesPerRow = 64;

    public sealed class ColumnChange
    {
        public string Column { get; set; } = "";
        public string? Old { get; set; }
        public string? New { get; set; }
    }

    public sealed class RowChange
    {
        public string Key { get; set; } = "";
        public List<ColumnChange> Columns { get; set; } = new();
    }

    public sealed class TableChange
    {
        public string Table { get; set; } = "";
        public int Added { get; set; }
        public int Removed { get; set; }
        public int Modified { get; set; }
        public List<string> AddedKeys { get; set; } = new();
        public List<string> RemovedKeys { get; set; } = new();
        public List<RowChange> ModifiedRows { get; set; } = new();
        public bool Truncated { get; set; }

        [JsonIgnore] public bool HasChanges => Added > 0 || Removed > 0 || Modified > 0;
    }

    public sealed class DiffReport
    {
        public string BaselinePath { get; set; } = "";
        public string EditedPath { get; set; } = "";
        public DateTimeOffset GeneratedUtc { get; set; } = DateTimeOffset.UtcNow;
        public List<TableChange> Tables { get; set; } = new();
        public List<string> AddedTables { get; set; } = new();
        public List<string> RemovedTables { get; set; } = new();

        [JsonIgnore]
        public bool HasChanges =>
            Tables.Any(t => t.HasChanges) || AddedTables.Count > 0 || RemovedTables.Count > 0;

        [JsonIgnore]
        public int TotalRowsChanged =>
            Tables.Sum(t => t.Added + t.Removed + t.Modified);
    }

    public sealed class DonorChangeSet
    {
        public string Table { get; set; } = "";
        public List<string> KeyColumns { get; set; } = new();

        public List<string?[]> ChangedKeyTuples { get; set; } = new();
        public int Count => ChangedKeyTuples.Count;
    }

    public static List<DonorChangeSet> ChangedDonorRows(string basePath, string donorPath)
    {
        var sets = new List<DonorChangeSet>();
        using var a = OpenReadOnly(basePath);
        using var b = OpenReadOnly(donorPath);
        var tablesA = UserTables(a);
        var tablesB = UserTables(b);

        foreach (var table in tablesA.Intersect(tablesB).OrderBy(x => x))
        {
            var colsA = Columns(a, table);
            var colsB = Columns(b, table);
            var sharedCols = colsA.Where(c => colsB.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
            var keyCols = PrimaryKeyColumns(a, table);

            bool usablePk = keyCols.Count > 0 && keyCols.All(k => sharedCols.Contains(k, StringComparer.OrdinalIgnoreCase));

            var rowsA = ReadRows(a, table, sharedCols, usablePk ? keyCols : new List<string> { "rowid" });
            var rowsB = ReadRows(b, table, sharedCols, usablePk ? keyCols : new List<string> { "rowid" });

            var set = new DonorChangeSet { Table = table, KeyColumns = usablePk ? keyCols : new List<string>() };
            if (!usablePk)
            {
                if (!RowsEqual(rowsA, rowsB)) sets.Add(set);
                continue;
            }

            foreach (var kv in rowsB)
            {
                if (!rowsA.TryGetValue(kv.Key, out var aRow))
                    set.ChangedKeyTuples.Add(KeyTuple(kv.Value, keyCols));
                else if (!RowEquals(aRow, kv.Value, sharedCols, keyCols))
                    set.ChangedKeyTuples.Add(KeyTuple(kv.Value, keyCols));
            }
            if (set.ChangedKeyTuples.Count > 0) sets.Add(set);
        }
        return sets;
    }

    private static string?[] KeyTuple(Dictionary<string, string?> row, List<string> keyCols)
        => keyCols.Select(k => row.TryGetValue(k, out var v) ? v : null).ToArray();

    private static bool RowEquals(Dictionary<string, string?> a, Dictionary<string, string?> b, List<string> cols, List<string> keyCols)
    {
        foreach (var col in cols)
        {
            if (keyCols.Contains(col, StringComparer.OrdinalIgnoreCase)) continue;
            var av = a.TryGetValue(col, out var x) ? x : null;
            var bv = b.TryGetValue(col, out var y) ? y : null;
            if (!string.Equals(av, bv, StringComparison.Ordinal)) return false;
        }
        return true;
    }

    private static bool RowsEqual(Dictionary<string, Dictionary<string, string?>> a, Dictionary<string, Dictionary<string, string?>> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var kv in b)
            if (!a.TryGetValue(kv.Key, out var ar) || ar.Count != kv.Value.Count
                || kv.Value.Any(c => !ar.TryGetValue(c.Key, out var av) || !string.Equals(av, c.Value, StringComparison.Ordinal)))
                return false;
        return true;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static DiffReport Compare(string baselinePath, string editedPath)
    {
        var report = new DiffReport { BaselinePath = baselinePath, EditedPath = editedPath };

        using var a = OpenReadOnly(baselinePath);
        using var b = OpenReadOnly(editedPath);

        var tablesA = UserTables(a);
        var tablesB = UserTables(b);

        report.AddedTables = tablesB.Where(t => !tablesA.Contains(t)).OrderBy(x => x).ToList();
        report.RemovedTables = tablesA.Where(t => !tablesB.Contains(t)).OrderBy(x => x).ToList();

        foreach (var table in tablesA.Intersect(tablesB).OrderBy(x => x))
        {
            var change = CompareTable(a, b, table);
            if (change.HasChanges) report.Tables.Add(change);
        }
        return report;
    }

    private static TableChange CompareTable(SqliteConnection a, SqliteConnection b, string table)
    {
        var change = new TableChange { Table = table };

        var colsA = Columns(a, table);
        var colsB = Columns(b, table);
        var sharedCols = colsA.Where(c => colsB.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
        var keyCols = PrimaryKeyColumns(a, table);
        if (keyCols.Count == 0 || keyCols.Any(k => !sharedCols.Contains(k, StringComparer.OrdinalIgnoreCase)))
            keyCols = new List<string> { "rowid" };

        var rowsA = ReadRows(a, table, sharedCols, keyCols);
        var rowsB = ReadRows(b, table, sharedCols, keyCols);

        foreach (var kv in rowsB)
            if (!rowsA.ContainsKey(kv.Key))
            {
                change.Added++;
                if (change.AddedKeys.Count < 200) change.AddedKeys.Add(kv.Key);
            }

        foreach (var kv in rowsA)
        {
            if (!rowsB.TryGetValue(kv.Key, out var bRow))
            {
                change.Removed++;
                if (change.RemovedKeys.Count < 200) change.RemovedKeys.Add(kv.Key);
                continue;
            }

            var aRow = kv.Value;
            List<ColumnChange>? cellChanges = null;
            foreach (var col in sharedCols)
            {
                if (keyCols.Contains(col, StringComparer.OrdinalIgnoreCase)) continue;
                var av = aRow.TryGetValue(col, out var x) ? x : null;
                var bv = bRow.TryGetValue(col, out var y) ? y : null;
                if (!string.Equals(av, bv, StringComparison.Ordinal))
                {
                    cellChanges ??= new List<ColumnChange>();
                    if (cellChanges.Count < MaxCellChangesPerRow)
                        cellChanges.Add(new ColumnChange { Column = col, Old = av, New = bv });
                }
            }
            if (cellChanges is { Count: > 0 })
            {
                change.Modified++;
                if (change.ModifiedRows.Count < MaxModifiedRowsPerTable)
                    change.ModifiedRows.Add(new RowChange { Key = kv.Key, Columns = cellChanges });
                else
                    change.Truncated = true;
            }
        }
        return change;
    }

    private static SqliteConnection OpenReadOnly(string path)
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
        var conn = new SqliteConnection(cs);
        conn.Open();
        return conn;
    }

    private static List<string> UserTables(SqliteConnection conn)
    {
        var tables = new List<string>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
        using var r = cmd.ExecuteReader();
        while (r.Read()) tables.Add(r.GetString(0));
        return tables;
    }

    private static List<string> Columns(SqliteConnection conn, string table)
    {
        var cols = new List<string>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({Quote(table)});";
        using var r = cmd.ExecuteReader();
        while (r.Read()) cols.Add(r.GetString(1));
        return cols;
    }

    private static List<string> PrimaryKeyColumns(SqliteConnection conn, string table)
    {
        var pk = new List<(int order, string name)>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({Quote(table)});";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var pkPos = r.GetInt32(5);
            if (pkPos > 0) pk.Add((pkPos, r.GetString(1)));
        }
        return pk.OrderBy(x => x.order).Select(x => x.name).ToList();
    }

    private static Dictionary<string, Dictionary<string, string?>> ReadRows(
        SqliteConnection conn, string table, List<string> cols, List<string> keyCols)
    {
        var rows = new Dictionary<string, Dictionary<string, string?>>(StringComparer.Ordinal);
        bool useRowid = keyCols.Count == 1 && keyCols[0] == "rowid";

        var selectCols = useRowid
            ? "rowid, " + string.Join(", ", cols.Select(Quote))
            : string.Join(", ", cols.Select(Quote));

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {selectCols} FROM {Quote(table)};";
        using var r = cmd.ExecuteReader();

        int colOffset = useRowid ? 1 : 0;
        while (r.Read())
        {
            var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < cols.Count; i++)
                row[cols[i]] = CellToString(r, i + colOffset);

            string key = useRowid
                ? CellToString(r, 0) ?? "?"
                : string.Join("", keyCols.Select(k => row.TryGetValue(k, out var v) ? v ?? "∅" : "∅"));

            rows[key] = row;
        }
        return rows;
    }

    private static string? CellToString(SqliteDataReader r, int i)
    {
        if (r.IsDBNull(i)) return null;
        return r.GetFieldType(i) == typeof(byte[])
            ? "0x" + Convert.ToHexString((byte[])r.GetValue(i))
            : r.GetValue(i)?.ToString();
    }

    private static string Quote(string ident) => "\"" + ident.Replace("\"", "\"\"") + "\"";

    public static string ToJson(DiffReport report) => JsonSerializer.Serialize(report, JsonOptions);

    public static string ToText(DiffReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("FH6 GameDB change report");
        sb.AppendLine($"Generated: {report.GeneratedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Baseline (before): {report.BaselinePath}");
        sb.AppendLine($"Edited   (after):  {report.EditedPath}");
        sb.AppendLine(new string('─', 70));

        if (!report.HasChanges)
        {
            sb.AppendLine("No changes detected. The edited database is structurally identical to the baseline.");
            return sb.ToString();
        }

        sb.AppendLine($"SUMMARY: {report.TotalRowsChanged} row change(s) across {report.Tables.Count} table(s).");
        if (report.AddedTables.Count > 0) sb.AppendLine($"  + tables added:   {string.Join(", ", report.AddedTables)}");
        if (report.RemovedTables.Count > 0) sb.AppendLine($"  - tables removed: {string.Join(", ", report.RemovedTables)}");
        sb.AppendLine();

        foreach (var t in report.Tables.OrderByDescending(x => x.Added + x.Removed + x.Modified))
        {
            sb.AppendLine($"[{t.Table}]  +{t.Added} added  -{t.Removed} removed  ~{t.Modified} modified"
                          + (t.Truncated ? "  (modified-row detail truncated)" : ""));

            foreach (var row in t.ModifiedRows)
            {
                sb.AppendLine($"   ~ row {row.Key}");
                foreach (var c in row.Columns)
                    sb.AppendLine($"       {c.Column}: {Trunc(c.Old)}  →  {Trunc(c.New)}");
            }
            if (t.AddedKeys.Count > 0)
                sb.AppendLine($"   + added rows: {string.Join(", ", t.AddedKeys.Take(50))}{(t.Added > 50 ? " …" : "")}");
            if (t.RemovedKeys.Count > 0)
                sb.AppendLine($"   - removed rows: {string.Join(", ", t.RemovedKeys.Take(50))}{(t.Removed > 50 ? " …" : "")}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string Trunc(string? v)
    {
        if (v is null) return "∅(null)";
        return v.Length > 80 ? v[..80] + "…" : v;
    }
}
