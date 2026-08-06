using System.Collections.ObjectModel;
using System.Text.Json;
using AkaiDiskCatalog.Core.Filesystem.Models;
using AkaiDiskCatalog.Data.Models;

namespace AkaiDiskCatalog.App.Models;

public sealed class LoopRow
{
    public LoopRow(int index, AkaiLoopInfo loop)
    {
        Index = index + 1;
        At = loop.At;
        LengthSamples = loop.LengthSamples;
        TimeMs = loop.TimeMs;
    }
    public int Index { get; }
    public int At { get; }
    public int LengthSamples { get; }
    public int TimeMs { get; }

    // AKAI S1000 convention: a loop time of 9999 is a sentinel meaning "HOLD"
    // (loop indefinitely, no release-driven timing) rather than a literal 9999ms.
    public string TimeDisplay => TimeMs == 9999 ? "HOLD" : TimeMs.ToString();
}

public sealed class VelocityZoneRow
{
    public VelocityZoneRow(int keygroupNumber, int zoneNumber, AkaiVelocityZoneInfo vz)
    {
        KeygroupNumber = keygroupNumber;
        ZoneNumber = zoneNumber;
        VelocityRange = $"{vz.VelocityLow}-{vz.VelocityHigh}";
        SampleName = vz.SampleName;
        PlaybackMode = vz.PlaybackMode;
        CentsTune = vz.CentsTune;
        SemitoneTune = vz.SemitoneTune;
        Loudness = vz.Loudness;
        Filter = vz.Filter;
        Pan = vz.Pan;
    }
    public int KeygroupNumber { get; }
    public int ZoneNumber { get; }
    public string VelocityRange { get; }
    public string SampleName { get; }
    public string PlaybackMode { get; }
    public int CentsTune { get; }
    public int SemitoneTune { get; }
    public int Loudness { get; }
    public int Filter { get; }
    public int Pan { get; }
}

public sealed class KeygroupRow
{
    public KeygroupRow(int index, AkaiKeygroupInfo kg)
    {
        Index = index + 1;
        KeyRange = $"{kg.KeyLow}-{kg.KeyHigh}";
        CentsTune = kg.CentsTune;
        SemitoneTune = kg.SemitoneTune;
        Filter = kg.Filter;
        VelocityCrossfade = kg.VelocityCrossfade;
    }
    public int Index { get; }
    public string KeyRange { get; }
    public int CentsTune { get; }
    public int SemitoneTune { get; }
    public int Filter { get; }
    public bool VelocityCrossfade { get; }
}

/// <summary>
/// Presents the rich, kind-specific detail for whatever file is currently selected
/// in the results grid (sample loop points, or program keygroup/velocity-zone map).
/// </summary>
public sealed class SelectedFileDetail
{
    public FileSearchResult Source { get; }
    public bool IsSample { get; }
    public bool IsProgram { get; }
    public bool ProgramKeygroupsUnparsed { get; }

    public ObservableCollection<LoopRow> Loops { get; } = new();
    public ObservableCollection<KeygroupRow> Keygroups { get; } = new();
    public ObservableCollection<VelocityZoneRow> VelocityZones { get; } = new();

    public bool HasLoops => IsSample && Loops.Count > 0;
    public bool HasNoLoops => IsSample && Loops.Count == 0;

    public SelectedFileDetail(FileSearchResult r)
    {
        Source = r;
        IsSample = r.Kind == "Sample";
        IsProgram = r.Kind == "Program";

        if (string.IsNullOrEmpty(r.DetailsJson)) return;

        if (IsSample)
        {
            var sample = JsonSerializer.Deserialize<AkaiSampleInfo>(r.DetailsJson);
            if (sample == null) return;
            for (int i = 0; i < sample.Loops.Count; i++)
                Loops.Add(new LoopRow(i, sample.Loops[i]));
        }
        else if (IsProgram)
        {
            var program = JsonSerializer.Deserialize<AkaiProgramInfo>(r.DetailsJson);
            if (program == null) return;
            ProgramKeygroupsUnparsed = program.KeygroupsUnparsed;
            for (int k = 0; k < program.Keygroups.Count; k++)
            {
                var kg = program.Keygroups[k];
                Keygroups.Add(new KeygroupRow(k, kg));
                for (int v = 0; v < kg.VelocityZones.Count; v++)
                {
                    var vz = kg.VelocityZones[v];
                    if (string.IsNullOrEmpty(vz.SampleName)) continue;
                    VelocityZones.Add(new VelocityZoneRow(k + 1, v + 1, vz));
                }
            }
        }
    }
}
