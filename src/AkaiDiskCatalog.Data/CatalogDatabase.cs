using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;

namespace AkaiDiskCatalog.Data;

public static class CatalogDatabase
{
    public static string DefaultDatabasePath()
    {
        string baseDir;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", "AkaiDiskCatalog");
        }
        else
        {
            baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AkaiDiskCatalog");
        }
        Directory.CreateDirectory(baseDir);
        return Path.Combine(baseDir, "catalog.db");
    }

    public static SqliteConnection OpenAndInitialize(string dbPath)
    {
        var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            pragma.ExecuteNonQuery();
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Disks (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                SourcePath      TEXT NOT NULL UNIQUE,
                FileName        TEXT NOT NULL,
                Density         TEXT NOT NULL,
                DecodeOk        INTEGER NOT NULL,
                MissingSectors  INTEGER NOT NULL,
                TotalSectors    INTEGER NOT NULL,
                FileSizeBytes   INTEGER NOT NULL,
                FileModifiedUtc TEXT NOT NULL,
                ScannedAtUtc    TEXT NOT NULL,
                WarningsJson    TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Volumes (
                Id         INTEGER PRIMARY KEY AUTOINCREMENT,
                DiskId     INTEGER NOT NULL REFERENCES Disks(Id) ON DELETE CASCADE,
                Name       TEXT NOT NULL,
                OsVersion  TEXT NOT NULL,
                Platform   TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Files (
                Id                INTEGER PRIMARY KEY AUTOINCREMENT,
                VolumeId          INTEGER NOT NULL REFERENCES Volumes(Id) ON DELETE CASCADE,
                Name              TEXT NOT NULL,
                TypeByte          INTEGER NOT NULL,
                Platform          TEXT NOT NULL,
                Kind              TEXT NOT NULL,
                SizeBytes         INTEGER NOT NULL,
                StartBlock        INTEGER NOT NULL,
                OsVersion         TEXT NOT NULL,
                ParseWarning      TEXT NULL,

                SampleRateHz      INTEGER NULL,
                NumSamples        INTEGER NULL,
                DurationMs        REAL NULL,
                RootKey           INTEGER NULL,
                CentsTune         INTEGER NULL,
                SemitoneTune      INTEGER NULL,
                PlaybackMode      TEXT NULL,
                NumLoops          INTEGER NULL,

                MidiChannel       INTEGER NULL,
                KeyLow            INTEGER NULL,
                KeyHigh           INTEGER NULL,
                NumKeygroups      INTEGER NULL,

                DetailsJson       TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_Files_Name ON Files(Name);
            CREATE INDEX IF NOT EXISTS IX_Files_Kind ON Files(Kind);
            CREATE INDEX IF NOT EXISTS IX_Volumes_DiskId ON Volumes(DiskId);
            CREATE INDEX IF NOT EXISTS IX_Files_VolumeId ON Files(VolumeId);
            """;
        cmd.ExecuteNonQuery();

        return conn;
    }
}
