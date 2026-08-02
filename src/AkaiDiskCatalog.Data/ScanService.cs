using System;
using System.IO;
using System.Linq;
using System.Threading;
using AkaiDiskCatalog.Core;

namespace AkaiDiskCatalog.Data;

public sealed record ScanProgress(int Current, int Total, string CurrentFile, bool FromCache);

public sealed class ScanService
{
    private readonly CatalogRepository _repo;

    public ScanService(CatalogRepository repo) => _repo = repo;

    /// <summary>
    /// Scans <paramref name="folderPath"/> recursively for .hfe and .img files, decoding
    /// any that are new or have changed since the last scan, and removes catalog entries
    /// for files that no longer exist. Reports progress via <paramref name="onProgress"/>.
    /// </summary>
    public void ScanFolder(string folderPath, IProgress<ScanProgress>? onProgress, CancellationToken ct = default)
    {
        var files = Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".hfe", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".img", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _repo.DeleteDisksNotIn(files);

        int i = 0;
        foreach (var path in files)
        {
            ct.ThrowIfCancellationRequested();
            i++;

            var fi = new FileInfo(path);
            var cache = _repo.TryGetCacheInfo(path);
            bool unchanged = cache != null
                && cache.FileSizeBytes == fi.Length
                && Math.Abs((cache.FileModifiedUtc - fi.LastWriteTimeUtc).TotalSeconds) < 1;

            if (unchanged)
            {
                onProgress?.Report(new ScanProgress(i, files.Count, Path.GetFileName(path), FromCache: true));
                continue;
            }

            onProgress?.Report(new ScanProgress(i, files.Count, Path.GetFileName(path), FromCache: false));
            var disk = DiskImageLoader.Load(path);
            _repo.UpsertDisk(disk, fi.Length, fi.LastWriteTimeUtc);
        }
    }
}
