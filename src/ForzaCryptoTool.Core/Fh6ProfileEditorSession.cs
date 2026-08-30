using Microsoft.Data.Sqlite;

namespace ForzaCryptoTool;

internal sealed class Fh6ProfileEditorSession : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _databasePath;
    private bool _disposed;

    private Fh6ProfileEditorSession(string sourcePath, Fh6ProfileEditorDocument document)
    {
        SourcePath = sourcePath;
        Document = document;
        _tempDirectory = Path.Combine(Path.GetTempPath(), "ForzaCryptoTool", "profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _databasePath = Path.Combine(_tempDirectory, "career.sqlite");
        File.WriteAllBytes(_databasePath, document.DatabaseBytes);
    }

    public string SourcePath { get; }
    public Fh6ProfileEditorDocument Document { get; }
    public HashSet<string> DirtyProperties { get; } = new(StringComparer.Ordinal);
    public bool DatabaseDirty { get; private set; }
    public bool BxmlDirty { get; set; }
    public bool XuidDirty { get; private set; }
    public HashSet<int> DirtyBinaryRecords { get; } = [];
    public bool IsDirty => DirtyProperties.Count > 0 || DatabaseDirty || BxmlDirty || XuidDirty || DirtyBinaryRecords.Count > 0;

    public static Fh6ProfileEditorSession Open(string path)
    {
        string fullPath = Path.GetFullPath(path);
        return new Fh6ProfileEditorSession(fullPath, Fh6ProfileEditorDocument.Load(fullPath));
    }

    public void UpdateProperty(string path, string text)
    {
        string normalized = "/" + path.Trim('/');
        string[] hashNames = ["EventHashes", "DLCHashes"];
        for (int setIndex = 0; setIndex < hashNames.Length; setIndex++)
        {
            string prefix = "/" + hashNames[setIndex] + "/Value";
            if (!normalized.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (!int.TryParse(normalized[prefix.Length..], out int valueIndex))
                throw new InvalidDataException("Hash-set path has an invalid value index.");
            uint value = text.Trim().StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? uint.Parse(text.Trim().AsSpan(2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture)
                : uint.Parse(text.Trim(), System.Globalization.CultureInfo.InvariantCulture);
            Document.Properties.SetHashValue(setIndex, valueIndex, value);
            DirtyProperties.Add(normalized);
            return;
        }
        var property = Document.Properties.Find(path);
        if (!property.Editable) throw new InvalidOperationException($"{path} is read-only ({property.TypeName}).");
        property.SetFromText(text);
        DirtyProperties.Add(normalized);
    }

    public void UpdateXuid(string text)
    {
        if (!Fh6ProfilePlaintext.TryParseXuidText(text, out ulong xuid))
            throw new InvalidDataException("XUID must be decimal or hexadecimal (optionally prefixed with 0x).");
        Document.Binary.Xuid = xuid;
        XuidDirty = true;
    }

    public string ReadBinaryScalar(int ordinal, int offset, BinaryScalarType type)
    {
        var record = Document.Binary.Records.ElementAtOrDefault(ordinal)
            ?? throw new ArgumentOutOfRangeException(nameof(ordinal));
        return BinaryScalar.Read(record.Payload, offset, type);
    }

    public void UpdateBinaryScalar(int ordinal, int offset, BinaryScalarType type, string text)
    {
        var record = Document.Binary.Records.ElementAtOrDefault(ordinal)
            ?? throw new ArgumentOutOfRangeException(nameof(ordinal));
        BinaryScalar.Write(record.Payload, offset, type, text);

        _ = BinaryCareerState.Parse(Document.Binary.Serialize());
        DirtyBinaryRecords.Add(ordinal);
    }

    public IReadOnlyList<ProfileDatabaseTable> ListTables()
    {
        using var connection = OpenDatabase();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name, type FROM sqlite_master WHERE type IN ('table','view') AND name NOT LIKE 'sqlite_%' ORDER BY type, name";
        using var reader = command.ExecuteReader();
        var tables = new List<ProfileDatabaseTable>();
        while (reader.Read())
        {
            string name = reader.GetString(0);
            string type = reader.GetString(1);
            long rows = 0;
            try
            {
                using var count = connection.CreateCommand();
                count.CommandText = $"SELECT COUNT(*) FROM {QuoteIdentifier(name)}";
                rows = Convert.ToInt64(count.ExecuteScalar());
            }
            catch (SqliteException) { }
            tables.Add(new ProfileDatabaseTable(name, type, rows));
        }
        return tables;
    }

    public ProfileQueryResult BrowseTable(string table, int page = 0, int limit = 100)
    {
        limit = Math.Clamp(limit, 1, 500);
        page = Math.Max(0, page);
        int offset = checked(page * limit);
        try
        {
            return ExecuteQuery($"SELECT rowid AS __rowid__, * FROM {QuoteIdentifier(table)} LIMIT {limit} OFFSET {offset}", allowChanges: false);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1)
        {

            return ExecuteQuery($"SELECT * FROM {QuoteIdentifier(table)} LIMIT {limit} OFFSET {offset}", allowChanges: false);
        }
    }

    public ProfileQueryBatch ExecuteSql(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) throw new ArgumentException("SQL is empty.", nameof(sql));
        using var connection = OpenDatabase();
        using var transaction = connection.BeginTransaction();
        var results = new List<ProfileQueryResult>();
        long totalChanges = 0;
        bool mutated = false;
        foreach (string statement in SplitStatements(sql))
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = statement;
            using var reader = command.ExecuteReader();
            var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
            var rows = new List<object?[]>();
            while (reader.Read() && rows.Count < 500)
            {
                var row = new object?[reader.FieldCount];
                reader.GetValues(row!);
                for (int i = 0; i < row.Length; i++) if (row[i] is DBNull) row[i] = null;
                rows.Add(row);
            }
            reader.Close();
            long changes = 0;
            bool readOnly = IsReadOnlyStatement(statement);
            if (!readOnly)
            {
                mutated = true;
                using var changed = connection.CreateCommand();
                changed.Transaction = transaction;
                changed.CommandText = "SELECT changes()";
                changes = Convert.ToInt64(changed.ExecuteScalar());
            }
            totalChanges += changes;
            results.Add(new ProfileQueryResult(columns, rows, changes, statement));
        }
        transaction.Commit();
        DatabaseDirty |= mutated;
        return new ProfileQueryBatch(results, totalChanges);
    }

    public string CheckDatabaseIntegrity()
    {
        using var connection = OpenDatabase();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check";
        return Convert.ToString(command.ExecuteScalar()) ?? "no result";
    }

    public ProfileSaveResult Save(string destination)
    {
        string integrity = CheckDatabaseIntegrity();
        if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"The edited SQLite database failed integrity_check: {integrity}");

        byte[] database = File.ReadAllBytes(_databasePath);
        byte[] output = Document.Serialize(database);
        string fullPath = Path.GetFullPath(destination);
        FileSafety.ReplaceWithBackup(fullPath, output);

        using var verification = Open(fullPath);
        string verifyIntegrity = verification.CheckDatabaseIntegrity();
        if (!string.Equals(verifyIntegrity, "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Saved profile database failed integrity_check: {verifyIntegrity}");
        return new ProfileSaveResult(fullPath, output.Length, verification.Document.Properties.Walk().Count(),
            verification.Document.Binary.Records.Count, verifyIntegrity);
    }

    public ProfileRoundTripResult VerifyRoundTrip()
    {
        byte[] database = File.ReadAllBytes(_databasePath);
        byte[] serialized = Document.Serialize(database);
        var reparsed = Fh6ProfileEditorDocument.Parse(serialized);
        bool identical = serialized.AsSpan().SequenceEqual(File.ReadAllBytes(SourcePath));
        return new ProfileRoundTripResult(serialized.Length, reparsed.InflatedSize,
            reparsed.Properties.Walk().Count(), reparsed.Binary.Records.Count,
            reparsed.Bxml.Walk().Count(), identical, CheckDatabaseIntegrity());
    }

    public void MarkSaved()
    {
        DirtyProperties.Clear();
        DirtyBinaryRecords.Clear();
        DatabaseDirty = false;
        BxmlDirty = false;
        XuidDirty = false;
    }

    private ProfileQueryResult ExecuteQuery(string sql, bool allowChanges)
    {
        using var connection = OpenDatabase();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        var rows = new List<object?[]>();
        while (reader.Read())
        {
            var row = new object?[reader.FieldCount]; reader.GetValues(row!);
            for (int i = 0; i < row.Length; i++) if (row[i] is DBNull) row[i] = null;
            rows.Add(row);
        }
        long changes = 0;
        if (allowChanges && changes > 0) DatabaseDirty = true;
        return new ProfileQueryResult(columns, rows, changes, sql);
    }

    private SqliteConnection OpenDatabase()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static IEnumerable<string> SplitStatements(string sql)
    {

        var current = new System.Text.StringBuilder();
        char quote = '\0';
        for (int i = 0; i < sql.Length; i++)
        {
            char c = sql[i];
            if (quote != '\0')
            {
                current.Append(c);
                if (c == quote)
                {
                    if (i + 1 < sql.Length && sql[i + 1] == quote) current.Append(sql[++i]);
                    else quote = '\0';
                }
            }
            else if (c is '\'' or '"' or '`') { quote = c; current.Append(c); }
            else if (c == ';')
            {
                string statement = current.ToString().Trim(); current.Clear();
                if (statement.Length > 0) yield return statement;
            }
            else current.Append(c);
        }
        string remainder = current.ToString().Trim();
        if (remainder.Length > 0) yield return remainder;
    }

    private static string QuoteIdentifier(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    private static bool IsReadOnlyStatement(string statement)
    {
        string trimmed = statement.TrimStart();
        if (trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("EXPLAIN", StringComparison.OrdinalIgnoreCase)) return true;
        return trimmed.StartsWith("PRAGMA", StringComparison.OrdinalIgnoreCase) && !trimmed.Contains('=');
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (Directory.Exists(_tempDirectory)) Directory.Delete(_tempDirectory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

internal sealed record ProfileDatabaseTable(string Name, string Type, long Rows);
internal sealed record ProfileQueryResult(string[] Columns, List<object?[]> Rows, long Changes, string Sql);
internal sealed record ProfileQueryBatch(List<ProfileQueryResult> Statements, long Changes);
internal sealed record ProfileSaveResult(string Path, int Size, int Properties, int BinaryRecords, string SqliteIntegrity);
internal sealed record ProfileRoundTripResult(int Size, int InflatedSize, int Properties, int BinaryRecords, int BxmlNodes, bool ByteIdentical, string SqliteIntegrity);

internal enum BinaryScalarType { UInt8, Int8, UInt16, Int16, UInt32, Int32, UInt64, Int64, Float32, Float64, Bool8, Bool32 }

internal static class BinaryScalar
{
    public static string Read(byte[] data, int offset, BinaryScalarType type)
    {
        int width = Width(type); Check(data, offset, width);
        var span = data.AsSpan(offset, width);
        return type switch
        {
            BinaryScalarType.UInt8 => span[0].ToString(),
            BinaryScalarType.Int8 => unchecked((sbyte)span[0]).ToString(),
            BinaryScalarType.UInt16 => System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(span).ToString(),
            BinaryScalarType.Int16 => System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(span).ToString(),
            BinaryScalarType.UInt32 => System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(span).ToString(),
            BinaryScalarType.Int32 => System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(span).ToString(),
            BinaryScalarType.UInt64 => System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(span).ToString(),
            BinaryScalarType.Int64 => System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(span).ToString(),
            BinaryScalarType.Float32 => BitConverter.Int32BitsToSingle(System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(span)).ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            BinaryScalarType.Float64 => BitConverter.Int64BitsToDouble(System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(span)).ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            BinaryScalarType.Bool8 => (span[0] != 0).ToString(),
            BinaryScalarType.Bool32 => (System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(span) != 0).ToString(),
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
    }

    public static void Write(byte[] data, int offset, BinaryScalarType type, string text)
    {
        int width = Width(type); Check(data, offset, width); var span = data.AsSpan(offset, width);
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        switch (type)
        {
            case BinaryScalarType.UInt8: span[0] = byte.Parse(text, invariant); break;
            case BinaryScalarType.Int8: span[0] = unchecked((byte)sbyte.Parse(text, invariant)); break;
            case BinaryScalarType.UInt16: System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(span, ushort.Parse(text, invariant)); break;
            case BinaryScalarType.Int16: System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(span, short.Parse(text, invariant)); break;
            case BinaryScalarType.UInt32: System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(span, ParseUInt32(text)); break;
            case BinaryScalarType.Int32: System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(span, int.Parse(text, invariant)); break;
            case BinaryScalarType.UInt64: System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(span, ParseUInt64(text)); break;
            case BinaryScalarType.Int64: System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(span, long.Parse(text, invariant)); break;
            case BinaryScalarType.Float32: System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(span, BitConverter.SingleToInt32Bits(float.Parse(text, invariant))); break;
            case BinaryScalarType.Float64: System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(span, BitConverter.DoubleToInt64Bits(double.Parse(text, invariant))); break;
            case BinaryScalarType.Bool8: span[0] = bool.Parse(text) ? (byte)1 : (byte)0; break;
            case BinaryScalarType.Bool32: System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(span, bool.Parse(text) ? 1u : 0u); break;
        }
    }

    private static uint ParseUInt32(string text) => text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        ? uint.Parse(text.AsSpan(2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture)
        : uint.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
    private static ulong ParseUInt64(string text) => text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        ? ulong.Parse(text.AsSpan(2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture)
        : ulong.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
    private static int Width(BinaryScalarType type) => type switch
    { BinaryScalarType.UInt8 or BinaryScalarType.Int8 or BinaryScalarType.Bool8 => 1, BinaryScalarType.UInt16 or BinaryScalarType.Int16 => 2, BinaryScalarType.UInt32 or BinaryScalarType.Int32 or BinaryScalarType.Float32 or BinaryScalarType.Bool32 => 4, _ => 8 };
    private static void Check(byte[] data, int offset, int width)
    { if (offset < 0 || offset + width > data.Length) throw new InvalidDataException($"Scalar range {offset}..{offset + width} is outside this {data.Length}-byte record."); }
}
