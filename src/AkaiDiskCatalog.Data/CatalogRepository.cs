using System;
using System.Collections.Generic;
using System.Text.Json;
using AkaiDiskCatalog.Core.Filesystem.Models;
using AkaiDiskCatalog.Data.Models;
using Microsoft.Data.Sqlite;

namespace AkaiDiskCatalog.Data;

public sealed class CatalogRepository
{
    private readonly SqliteConnection _conn;

    public CatalogRepository(SqliteConnection conn) => _conn = conn;

    public sealed record CacheInfo(long DiskId, long FileSizeBytes, DateTime FileModifiedUtc);

    public CacheInfo? TryGetCacheInfo(string sourcePath)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT Id, FileSizeBytes, FileModifiedUtc FROM Disks WHERE SourcePath = $p";
        cmd.Parameters.AddWithValue("$p", sourcePath);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new CacheInfo(r.GetInt64(0), r.GetInt64(1), DateTime.Parse(r.GetString(2)).ToUniversalTime());
    }

    public void DeleteDisk(string sourcePath)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Disks WHERE SourcePath = $p";
        cmd.Parameters.AddWithValue("$p", sourcePath);
        cmd.ExecuteNonQuery();
    }

    public void DeleteDisksNotIn(IReadOnlyCollection<string> keepPaths)
    {
        using var tx = _conn.BeginTransaction();
        using (var cmd = _conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT SourcePath FROM Disks";
            var toDelete = new List<string>();
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    var p = r.GetString(0);
                    if (!keepPaths.Contains(p)) toDelete.Add(p);
                }
            }
            foreach (var p in toDelete)
            {
                using var del = _conn.CreateCommand();
                del.Transaction = tx;
                del.CommandText = "DELETE FROM Disks WHERE SourcePath = $p";
                del.Parameters.AddWithValue("$p", p);
                del.ExecuteNonQuery();
            }
        }
        tx.Commit();
    }

    public void UpsertDisk(AkaiDiskImage disk, long fileSizeBytes, DateTime fileModifiedUtc)
    {
        using var tx = _conn.BeginTransaction();

        using (var del = _conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM Disks WHERE SourcePath = $p";
            del.Parameters.AddWithValue("$p", disk.SourcePath);
            del.ExecuteNonQuery();
        }

        long diskId;
        using (var cmd = _conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO Disks (SourcePath, FileName, Density, DecodeOk, MissingSectors, TotalSectors, FileSizeBytes, FileModifiedUtc, ScannedAtUtc, WarningsJson)
                VALUES ($path, $name, $density, $ok, $missing, $total, $size, $modified, $scanned, $warnings);
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$path", disk.SourcePath);
            cmd.Parameters.AddWithValue("$name", disk.SourceFileName);
            cmd.Parameters.AddWithValue("$density", disk.Density.ToString());
            cmd.Parameters.AddWithValue("$ok", disk.DecodeOk ? 1 : 0);
            cmd.Parameters.AddWithValue("$missing", disk.MissingSectorCount);
            cmd.Parameters.AddWithValue("$total", disk.TotalSectorsExpected);
            cmd.Parameters.AddWithValue("$size", fileSizeBytes);
            cmd.Parameters.AddWithValue("$modified", fileModifiedUtc.ToUniversalTime().ToString("o"));
            cmd.Parameters.AddWithValue("$scanned", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("$warnings", JsonSerializer.Serialize(disk.Warnings));
            diskId = (long)cmd.ExecuteScalar()!;
        }

        foreach (var vol in disk.Volumes)
        {
            long volId;
            using (var cmd = _conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO Volumes (DiskId, Name, OsVersion, Platform)
                    VALUES ($diskId, $name, $osver, $platform);
                    SELECT last_insert_rowid();
                    """;
                cmd.Parameters.AddWithValue("$diskId", diskId);
                cmd.Parameters.AddWithValue("$name", vol.Name);
                cmd.Parameters.AddWithValue("$osver", vol.OsVersion);
                cmd.Parameters.AddWithValue("$platform", vol.Platform.ToString());
                volId = (long)cmd.ExecuteScalar()!;
            }

            foreach (var file in vol.Files)
            {
                using var cmd = _conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO Files (VolumeId, Name, TypeByte, Platform, Kind, SizeBytes, StartBlock, OsVersion, ParseWarning,
                                        SampleRateHz, NumSamples, DurationMs, RootKey, CentsTune, SemitoneTune, PlaybackMode, NumLoops,
                                        MidiChannel, KeyLow, KeyHigh, NumKeygroups, DetailsJson)
                    VALUES ($volId, $name, $type, $platform, $kind, $size, $start, $osver, $warn,
                            $srate, $nsamp, $dur, $rkey, $ctune, $stune, $pmode, $nloops,
                            $midich, $klo, $khi, $nkg, $details);
                    """;
                cmd.Parameters.AddWithValue("$volId", volId);
                cmd.Parameters.AddWithValue("$name", file.Name);
                cmd.Parameters.AddWithValue("$type", file.TypeByte);
                cmd.Parameters.AddWithValue("$platform", file.Platform.ToString());
                cmd.Parameters.AddWithValue("$kind", file.Kind.ToString());
                cmd.Parameters.AddWithValue("$size", file.SizeBytes);
                cmd.Parameters.AddWithValue("$start", file.StartBlock);
                cmd.Parameters.AddWithValue("$osver", file.OsVersion);
                cmd.Parameters.AddWithValue("$warn", (object?)file.ParseWarning ?? DBNull.Value);

                cmd.Parameters.AddWithValue("$srate", (object?)file.Sample?.SampleRateHz ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$nsamp", (object?)file.Sample?.NumSamples ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$dur", (object?)file.Sample?.DurationMs ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$rkey", (object?)file.Sample?.RootKey ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$ctune", (object?)file.Sample?.CentsTune ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$stune", (object?)file.Sample?.SemitoneTune ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$pmode", (object?)file.Sample?.PlaybackMode ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$nloops", (object?)file.Sample?.NumLoops ?? DBNull.Value);

                cmd.Parameters.AddWithValue("$midich", (object?)file.Program?.MidiChannel ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$klo", (object?)file.Program?.KeyLow ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$khi", (object?)file.Program?.KeyHigh ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$nkg", (object?)file.Program?.NumKeygroups ?? DBNull.Value);

                string? detailsJson = file.Sample != null
                    ? JsonSerializer.Serialize(file.Sample)
                    : file.Program != null
                        ? JsonSerializer.Serialize(file.Program)
                        : null;
                cmd.Parameters.AddWithValue("$details", (object?)detailsJson ?? DBNull.Value);

                cmd.ExecuteNonQuery();
            }
        }

        tx.Commit();
    }

    public List<FileSearchResult> Search(string? searchText, string? kindFilter, string? diskFilter, int limit = 5000)
    {
        var results = new List<FileSearchResult>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT f.Id, d.FileName, d.SourcePath, v.Name, v.Platform, v.OsVersion,
                   f.Name, f.Kind, f.SizeBytes, f.StartBlock, f.ParseWarning,
                   f.SampleRateHz, f.DurationMs, f.RootKey, f.CentsTune, f.SemitoneTune, f.PlaybackMode, f.NumLoops,
                   f.MidiChannel, f.KeyLow, f.KeyHigh, f.NumKeygroups, f.DetailsJson
            FROM Files f
            JOIN Volumes v ON v.Id = f.VolumeId
            JOIN Disks d ON d.Id = v.DiskId
            WHERE ($search IS NULL OR f.Name LIKE $search OR v.Name LIKE $search OR d.FileName LIKE $search)
              AND ($kind IS NULL OR f.Kind = $kind)
              AND ($disk IS NULL OR d.SourcePath = $disk)
            ORDER BY d.FileName, v.Name, f.StartBlock
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$search", string.IsNullOrWhiteSpace(searchText) ? DBNull.Value : $"%{searchText.Trim()}%");
        cmd.Parameters.AddWithValue("$kind", string.IsNullOrWhiteSpace(kindFilter) || kindFilter == "All" ? DBNull.Value : kindFilter);
        cmd.Parameters.AddWithValue("$disk", string.IsNullOrWhiteSpace(diskFilter) || diskFilter == "All disks" ? DBNull.Value : diskFilter);
        cmd.Parameters.AddWithValue("$limit", limit);

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            results.Add(new FileSearchResult
            {
                FileId = r.GetInt64(0),
                DiskFileName = r.GetString(1),
                DiskSourcePath = r.GetString(2),
                VolumeName = r.GetString(3),
                Platform = r.GetString(4),
                OsVersion = r.GetString(5),
                Name = r.GetString(6),
                Kind = r.GetString(7),
                SizeBytes = r.GetInt32(8),
                StartBlock = r.GetInt32(9),
                ParseWarning = r.IsDBNull(10) ? null : r.GetString(10),
                SampleRateHz = r.IsDBNull(11) ? null : r.GetInt32(11),
                DurationMs = r.IsDBNull(12) ? null : r.GetDouble(12),
                RootKey = r.IsDBNull(13) ? null : r.GetInt32(13),
                CentsTune = r.IsDBNull(14) ? null : r.GetInt32(14),
                SemitoneTune = r.IsDBNull(15) ? null : r.GetInt32(15),
                PlaybackMode = r.IsDBNull(16) ? null : r.GetString(16),
                NumLoops = r.IsDBNull(17) ? null : r.GetInt32(17),
                MidiChannel = r.IsDBNull(18) ? null : r.GetInt32(18),
                KeyLow = r.IsDBNull(19) ? null : r.GetInt32(19),
                KeyHigh = r.IsDBNull(20) ? null : r.GetInt32(20),
                NumKeygroups = r.IsDBNull(21) ? null : r.GetInt32(21),
                DetailsJson = r.IsDBNull(22) ? null : r.GetString(22),
            });
        }
        return results;
    }

    public List<DiskSummary> GetDiskSummaries()
    {
        var results = new List<DiskSummary>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT d.Id, d.FileName, d.SourcePath, d.Density, d.DecodeOk, d.MissingSectors, d.TotalSectors, d.ScannedAtUtc,
                   COALESCE(v.Name, ''), COUNT(f.Id)
            FROM Disks d
            LEFT JOIN Volumes v ON v.DiskId = d.Id
            LEFT JOIN Files f ON f.VolumeId = v.Id
            GROUP BY d.Id
            ORDER BY d.FileName;
            """;
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            results.Add(new DiskSummary
            {
                DiskId = r.GetInt64(0),
                FileName = r.GetString(1),
                SourcePath = r.GetString(2),
                Density = r.GetString(3),
                DecodeOk = r.GetInt32(4) != 0,
                MissingSectors = r.GetInt32(5),
                TotalSectors = r.GetInt32(6),
                ScannedAtUtc = DateTime.Parse(r.GetString(7)).ToUniversalTime(),
                VolumeName = r.GetString(8),
                FileCount = r.GetInt32(9),
            });
        }
        return results;
    }
}
