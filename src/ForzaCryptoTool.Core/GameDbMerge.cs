using System.Text;
using Microsoft.Data.Sqlite;

namespace ForzaCryptoTool;

internal static class GameDbMerge
{
    public sealed class TableResult
    {
        public string Table { get; set; } = "";
        public int Inserted { get; set; }
        public int Replaced { get; set; }
        public int Skipped { get; set; }
        public string? Note { get; set; }
    }

    public sealed class MergeResult
    {
        public string BasePath { get; set; } = "";
        public string DonorPath { get; set; } = "";
        public string OutputPath { get; set; } = "";
        public List<TableResult> Tables { get; set; } = new();
        public List<string> SkippedTables { get; set; } = new();

        public int TotalInserted => Tables.Sum(t => t.Inserted);
        public int TotalReplaced => Tables.Sum(t => t.Replaced);

        public long OutputSizeBytes { get; set; }

        public long ContainerCapacityBytes { get; set; }

        public bool FitsContainer => ContainerCapacityBytes == 0 || OutputSizeBytes <= ContainerCapacityBytes;

        public string IntegrityResult { get; set; } = "ok";
        public bool IntegrityOk => string.Equals(IntegrityResult, "ok", StringComparison.OrdinalIgnoreCase);

        public int ForeignKeyViolations { get; set; }

        public long BytesOverCapacity => ContainerCapacityBytes > 0 ? Math.Max(0, OutputSizeBytes - ContainerCapacityBytes) : 0;

        public string ToText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("FH6 GameDB merge report");
            sb.AppendLine($"Base:   {BasePath}");
            sb.AppendLine($"Donor:  {DonorPath}");
            sb.AppendLine($"Output: {OutputPath}");
            sb.AppendLine(new string('─', 70));
            sb.AppendLine($"SUMMARY: {TotalInserted} inserted, {TotalReplaced} replaced across {Tables.Count} table(s).");
            if (ContainerCapacityBytes > 0)
            {
                sb.AppendLine($"  merged size: {OutputSizeBytes:N0} B / capacity {ContainerCapacityBytes:N0} B"
                              + (FitsContainer ? "  (fits)" : $"  (OVER by {OutputSizeBytes - ContainerCapacityBytes:N0} B — won't re-encrypt)"));
            }
            sb.AppendLine($"  integrity: {IntegrityResult}"
                          + (ForeignKeyViolations > 0 ? $"   FK violations: {ForeignKeyViolations}" : ""));
            if (SkippedTables.Count > 0)
                sb.AppendLine($"  skipped tables (not on both sides): {string.Join(", ", SkippedTables)}");
            sb.AppendLine();
            foreach (var t in Tables.Where(t => t.Inserted > 0 || t.Replaced > 0 || t.Skipped > 0)
                                    .OrderByDescending(t => t.Inserted + t.Replaced))
                sb.AppendLine($"[{t.Table}]  +{t.Inserted} inserted  ~{t.Replaced} replaced  ⨯{t.Skipped} skipped"
                              + (t.Note is null ? "" : $"  ({t.Note})"));
            return sb.ToString();
        }
    }

    public static MergeResult MergeChanges(
        string basePath, string donorPath, string outputPath, long containerCapacityBytes = 0)
    {
        if (string.Equals(Path.GetFullPath(basePath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Output path must differ from the base file.");

        var result = new MergeResult
        {
            BasePath = basePath,
            DonorPath = donorPath,
            OutputPath = outputPath,
            ContainerCapacityBytes = containerCapacityBytes,
        };

        var changes = GameDbDiff.ChangedDonorRows(basePath, donorPath);

        File.Copy(basePath, outputPath, overwrite: true);
        using var outConn = Open(outputPath, readOnly: false);
        Exec(outConn, "PRAGMA foreign_keys=OFF;");
        using (var attach = outConn.CreateCommand())
        {
            attach.CommandText = "ATTACH DATABASE $donor AS donor;";
            attach.Parameters.AddWithValue("$donor", Path.GetFullPath(donorPath));
            attach.ExecuteNonQuery();
        }

        var baseTables = new HashSet<string>(UserTables(outConn), StringComparer.OrdinalIgnoreCase);
        using var tx = outConn.BeginTransaction();
        foreach (var set in changes)
        {
            if (!baseTables.Contains(set.Table)) { result.SkippedTables.Add(set.Table); continue; }
            var sharedCols = SharedColumns(outConn, set.Table);
            if (sharedCols.Count == 0) { result.Tables.Add(new TableResult { Table = set.Table, Note = "no shared columns" }); continue; }

            var colList = string.Join(", ", sharedCols.Select(Quote));
            long before = CountRows(outConn, set.Table, tx);

            using var cmd = outConn.CreateCommand();
            cmd.Transaction = tx;
            if (set.KeyColumns.Count == 0)
            {

                cmd.CommandText = $"INSERT OR REPLACE INTO main.{Quote(set.Table)} ({colList}) SELECT {colList} FROM donor.{Quote(set.Table)};";
            }
            else
            {

                var keyTuple = "(" + string.Join(", ", set.KeyColumns.Select(Quote)) + ")";
                var paramRows = new List<string>();
                int p = 0;
                foreach (var tuple in set.ChangedKeyTuples)
                {
                    var names = new List<string>();
                    for (int i = 0; i < set.KeyColumns.Count; i++)
                    {
                        var pn = $"$k{p++}";
                        names.Add(pn);
                        cmd.Parameters.AddWithValue(pn, (object?)tuple[i] ?? DBNull.Value);
                    }
                    paramRows.Add("(" + string.Join(", ", names) + ")");
                }
                cmd.CommandText =
                    $"INSERT OR REPLACE INTO main.{Quote(set.Table)} ({colList}) " +
                    $"SELECT {colList} FROM donor.{Quote(set.Table)} WHERE {keyTuple} IN ({string.Join(", ", paramRows)});";
            }
            try { cmd.ExecuteNonQuery(); }
            catch (SqliteException ex) { result.Tables.Add(new TableResult { Table = set.Table, Note = "error: " + ex.SqliteErrorCode }); continue; }

            long after = CountRows(outConn, set.Table, tx);
            int inserted = (int)Math.Max(0, after - before);
            result.Tables.Add(new TableResult
            {
                Table = set.Table,
                Inserted = inserted,
                Replaced = Math.Max(0, set.Count - inserted),
            });
        }
        tx.Commit();
        using (var detach = outConn.CreateCommand()) { detach.CommandText = "DETACH DATABASE donor;"; detach.ExecuteNonQuery(); }
        Exec(outConn, "VACUUM;");
        result.OutputSizeBytes = new FileInfo(outputPath).Length;
        result.IntegrityResult = ScalarString(outConn, "PRAGMA integrity_check;") ?? "unknown";
        result.ForeignKeyViolations = CountResultRows(outConn, "PRAGMA foreign_key_check;");
        return result;
    }

    private static List<string> SharedColumns(SqliteConnection conn, string table)
    {
        var main = Columns(conn, table);
        var donor = new List<string>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA donor.table_info({Quote(table)});";
        using var r = cmd.ExecuteReader();
        while (r.Read()) donor.Add(r.GetString(1));
        var dset = new HashSet<string>(donor, StringComparer.OrdinalIgnoreCase);
        return main.Where(dset.Contains).ToList();
    }

    public static MergeResult Merge(
        string basePath, string donorPath, string outputPath,
        IReadOnlyCollection<string>? tablesToMerge = null,
        bool overwriteExisting = true,
        long containerCapacityBytes = 0)
    {
        if (string.Equals(Path.GetFullPath(basePath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Output path must differ from the base file.");

        var result = new MergeResult
        {
            BasePath = basePath,
            DonorPath = donorPath,
            OutputPath = outputPath,
            ContainerCapacityBytes = containerCapacityBytes,
        };

        File.Copy(basePath, outputPath, overwrite: true);

        using var outConn = Open(outputPath, readOnly: false);
        using var donor = Open(donorPath, readOnly: true);

        Exec(outConn, "PRAGMA foreign_keys=OFF;");

        var baseTables = UserTables(outConn);
        var donorTables = UserTables(donor);

        var candidates = (tablesToMerge ?? baseTables.Intersect(donorTables).ToList())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        using (var attach = outConn.CreateCommand())
        {
            attach.CommandText = "ATTACH DATABASE $donor AS donor;";
            attach.Parameters.AddWithValue("$donor", Path.GetFullPath(donorPath));
            attach.ExecuteNonQuery();
        }

        using var tx = outConn.BeginTransaction();
        foreach (var table in candidates)
        {
            if (!baseTables.Contains(table, StringComparer.OrdinalIgnoreCase) ||
                !donorTables.Contains(table, StringComparer.OrdinalIgnoreCase))
            {
                result.SkippedTables.Add(table);
                continue;
            }

            var baseCols = Columns(outConn, table);
            var donorColSet = new HashSet<string>(Columns(donor, table), StringComparer.OrdinalIgnoreCase);
            var sharedCols = baseCols.Where(donorColSet.Contains).ToList();
            if (sharedCols.Count == 0)
            {
                result.Tables.Add(new TableResult { Table = table, Note = "no shared columns" });
                continue;
            }

            long before = CountRows(outConn, table, tx);
            long donorCount = CountRows(donor, table, null);
            if (donorCount == 0)
            {
                result.Tables.Add(new TableResult { Table = table });
                continue;
            }

            var colList = string.Join(", ", sharedCols.Select(Quote));

            var verb = overwriteExisting ? "INSERT OR REPLACE" : "INSERT OR IGNORE";
            using var cmd = outConn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"{verb} INTO main.{Quote(table)} ({colList}) SELECT {colList} FROM donor.{Quote(table)};";
            try { cmd.ExecuteNonQuery(); }
            catch (SqliteException ex)
            {
                result.Tables.Add(new TableResult { Table = table, Skipped = (int)donorCount, Note = "error: " + ex.SqliteErrorCode });
                continue;
            }

            long after = CountRows(outConn, table, tx);
            int inserted = (int)Math.Max(0, after - before);
            int replaced = (int)Math.Max(0, donorCount - inserted);
            if (!overwriteExisting) replaced = 0;
            result.Tables.Add(new TableResult
            {
                Table = table,
                Inserted = inserted,
                Replaced = Math.Max(0, replaced),
                Skipped = overwriteExisting ? 0 : (int)Math.Max(0, donorCount - inserted),
            });
        }
        tx.Commit();

        using (var detach = outConn.CreateCommand())
        {
            detach.CommandText = "DETACH DATABASE donor;";
            detach.ExecuteNonQuery();
        }

        Exec(outConn, "VACUUM;");
        result.OutputSizeBytes = new FileInfo(outputPath).Length;

        result.IntegrityResult = ScalarString(outConn, "PRAGMA integrity_check;") ?? "unknown";
        result.ForeignKeyViolations = CountResultRows(outConn, "PRAGMA foreign_key_check;");
        return result;
    }

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static string? ScalarString(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar()?.ToString();
    }

    private static int CountResultRows(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        int n = 0;
        while (r.Read()) n++;
        return n;
    }

    public static long ContainerCapacityFromEncrypted(string encryptedOriginalPath)
    {
        try
        {
            const long header = 0x24, chunkData = 0x20000, chunkMac = 0x10;
            long enc = new FileInfo(encryptedOriginalPath).Length;
            long body = enc - header;
            if (body <= 0 || body % (chunkData + chunkMac) != 0) return 0;
            long chunks = body / (chunkData + chunkMac);
            return chunks * chunkData;
        }
        catch { return 0; }
    }

    public static List<string> SharedTables(string basePath, string donorPath)
    {
        using var a = Open(basePath, readOnly: true);
        using var b = Open(donorPath, readOnly: true);
        return UserTables(a).Intersect(UserTables(b), StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
    }

    public sealed record SharedTableInfo(string Table, long DonorRows, long BaseRows);

    public static List<SharedTableInfo> SharedTablesWithCounts(string basePath, string donorPath)
    {
        using var a = Open(basePath, readOnly: true);
        using var b = Open(donorPath, readOnly: true);
        var shared = UserTables(a).Intersect(UserTables(b), StringComparer.OrdinalIgnoreCase).ToList();
        var list = new List<SharedTableInfo>();
        foreach (var t in shared)
        {
            long donorRows = 0, baseRows = 0;
            try { donorRows = CountRows(b, t, null); } catch { }
            try { baseRows = CountRows(a, t, null); } catch { }
            list.Add(new SharedTableInfo(t, donorRows, baseRows));
        }
        return list.OrderByDescending(x => x.DonorRows).ThenBy(x => x.Table).ToList();
    }

    private static SqliteConnection Open(string path, bool readOnly)
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite,
        }.ToString());
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

    private static long CountRows(SqliteConnection conn, string table, SqliteTransaction? tx)
    {
        using var cmd = conn.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = $"SELECT COUNT(*) FROM {Quote(table)};";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static string Quote(string ident) => "\"" + ident.Replace("\"", "\"\"") + "\"";
}
