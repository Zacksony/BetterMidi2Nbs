using NAudio.Midi;
using System;
using System.Collections;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Midi2Nbs;

public sealed class M2NCore(M2NConfig config)
{
  private Task? _runningTask = null;

  public void StartConversion()
  {
    Convert();
  }

  private void Convert()
  {
    MidiFile midiFile = new(config.InputMidiPath, strictChecking: false);

    string songName = string.Empty;
    string songDesc = string.Empty;
    string songOriginalAuthor = string.Empty;
    string songAuthor = string.Empty;

    // read conductor track

    long songStartTime = 0;
    long songLoopTime = -1;
    SortedDictionary<long, int> timePerBarChanges = [];
    SortedDictionary<long, decimal> ticksPerTimeChanges = [];

    // read main tracks    

    SortedDictionary<long, HashSet<MidiNote>> midiNotesByTime = [];

    foreach (var channel in
      midiFile.Events.SelectMany((events, trackIndex) => events.Select((e) => (Event: e, TrackIndex: trackIndex)))
                     .GroupBy(x => x.Event.Channel))
    {
      ChannelState channelState = new(inst: config.StartingPatch);

      foreach (var (midiEvent, trackIndex) in
        channel.Where(x => x.Event.CommandCode is MidiCommandCode.NoteOn
                                               or MidiCommandCode.ControlChange
                                               or MidiCommandCode.PatchChange
                                               or MidiCommandCode.MetaEvent)
               .OrderBy(x => x.Event.AbsoluteTime)
               .GroupBy(x => x.Event.AbsoluteTime)
               .Select(x => x.OrderBy(e => e.Event, MidiEventComparer.Instance))
               .SelectMany(x => x))
      {
        if (midiEvent is null)
        {
          continue;
        }
        else if (midiEvent is TextEvent textEvent)
        {
          if (textEvent.Text == config.SongStartMarkerEventText)
          {
            songStartTime = midiEvent.AbsoluteTime;
          }
          else if (textEvent.Text == config.LoopMarkerEventText)
          {
            songLoopTime = midiEvent.AbsoluteTime;
          }
          else if (textEvent.Text.StartsWith(config.SongNameEventHeader))
          {
            songName = textEvent.Text.Replace(config.SongNameEventHeader, null);
          }
          else if (textEvent.Text.StartsWith(config.SongDescEventHeader))
          {
            songDesc = textEvent.Text.Replace(config.SongDescEventHeader, null);
          }
          else if (textEvent.Text.StartsWith(config.OriginalAuthorEventHeader))
          {
            songOriginalAuthor = textEvent.Text.Replace(config.OriginalAuthorEventHeader, null);
          }
          else if (textEvent.Text.StartsWith(config.SongAuthorEventHeader))
          {
            songAuthor = textEvent.Text.Replace(config.SongAuthorEventHeader, null);
          }
        }
        else if (midiEvent is TimeSignatureEvent timeSignatureEvent)
        {
          timePerBarChanges[midiEvent.AbsoluteTime] = (int)(4 / double.Pow(2, timeSignatureEvent.Denominator) * timeSignatureEvent.Numerator) * midiFile.DeltaTicksPerQuarterNote;
        }
        else if (midiEvent is TempoEvent tempoEvent)
        {
          ticksPerTimeChanges[midiEvent.AbsoluteTime] = (decimal)config.NbsTPS * tempoEvent.MicrosecondsPerQuarterNote / ((decimal)1_000_000 * midiFile.DeltaTicksPerQuarterNote);
        }
        else if (config.EnablePanpot && midiEvent is ControlChangeEvent { Controller: MidiController.Pan } cc)
        {
          channelState.Pan = cc.ControllerValue;
        }
        else if (!config.DoForcePatch && config.EnableProgramChange && midiEvent is PatchChangeEvent pc)
        {
          channelState.Inst = pc.Patch;
        }
        else if (midiEvent is NoteOnEvent noteOn)
        {
          if (noteOn.Velocity < config.MinMidiVelocity)
          {
            continue;
          }

          sbyte outputInst = (sbyte)(config.DoForcePatch ? config.ForcePatch : channelState.Inst);
          sbyte outputKey = (sbyte)noteOn.NoteNumber;
          sbyte outputVel = (sbyte)(config.DoForceVelocity ? config.ForceMidiVelocity : noteOn.Velocity);

          if (config.BetterLowerRegisterOfPiano && outputInst == 0 && outputKey <= 42)
          {
            outputInst = 1;
            outputKey += 12;
          }
          
          MidiNote midiNote =
            new(outputInst,
                outputKey,
                outputVel,
                channelState.Pan,
                trackIndex);

          midiNotesByTime.GetOrAdd(midiEvent.AbsoluteTime, key => new(MidiNoteAvoidDuplicatedEqualityComparer.Instance)).Add(midiNote);
        }
      }
    }

    // convert midi time to nbs tick

    SortedDictionary<long, int> timeToTickMap = [];
    if (config.DoConsiderTempoChange)
    {
      long maxTime = midiNotesByTime.Keys.Max();

      // 120 bpm by default
      decimal currentticksPerTime = (decimal)config.NbsTPS * 500_000 / ((decimal)1_000_000 * midiFile.DeltaTicksPerQuarterNote);

      decimal lastActualTick = 0;
      timeToTickMap[0] = 0;
      for (long time = 0; time <= maxTime; time++)
      {
        // midi time to nbs tick
        currentticksPerTime = ticksPerTimeChanges.GetValueOrDefault(time, currentticksPerTime);
        timeToTickMap[time + 1] = (int)decimal.Clamp(decimal.Floor(lastActualTick += currentticksPerTime), 0, int.MaxValue);
      }
    }
    else
    {
      long maxTime = midiNotesByTime.Keys.Max();
      double tpqRatio = (double)config.NbsTicksPerQuarterNote / midiFile.DeltaTicksPerQuarterNote;
      for (long time = 0; time <= maxTime; time++)
      {
        timeToTickMap[time] = (int)double.Clamp(double.Floor(time * tpqRatio), 0, int.MaxValue);
      }
    }

    // convert midi notes to nbs notes

    SortedDictionary<long, HashSet<NbsNote>> nbsNotesByTime = [];
    {
      foreach (var (time, midiNote) in midiNotesByTime.SelectMany(x => x.Value.Select(y => (x.Key, y))))
      {
        if (midiNote.Key is < 21 or > 108)
        {
          continue;
        }

        int tick = timeToTickMap[time];
        NbsNote nbsNote
          = new(tick,
                time,
                midiNote.Inst,
                sbyte.Clamp((sbyte)(midiNote.Key - 21), 0, 87),
                sbyte.Clamp(config.DoCalculateVelocity ? MidiVelToMinecraftVel(midiNote.Vel) : midiNote.Vel, 0, 100),
                (byte)double.Round(double.Clamp(midiNote.Pan * 1.5625, 0, 200)),
                0,
                midiNote.Track);

        nbsNotesByTime.GetOrAdd(time, key => []).Add(nbsNote);
      }
    }

    // generate group ids for grouping nbs notes by midi barlines

    SortedDictionary<long, int> groupIdsByTime = [];
    {
      long maxTime = midiNotesByTime.Keys.Max();

      int currentGroupId = 0;
      int currentTimePerBar = int.Max(1, (int)double.Round(midiFile.DeltaTicksPerQuarterNote * 4d * config.VisualAlignBarlines));

      for (long time = 0; time <= maxTime; time += currentTimePerBar)
      {
        if (timePerBarChanges.TryGetValue(time, out int changedTimePerBar))
        {
          currentTimePerBar = int.Max(1, (int)double.Round(changedTimePerBar * config.VisualAlignBarlines));
        }

        for (int j = 0; j < currentTimePerBar; j++)
        {
          groupIdsByTime[time + j] = currentGroupId;
        }

        currentGroupId++;
      }
    }

    // group & sort nbs notes

    SortedDictionary<int, SortedDictionary<int, NbsNote>> layeredNbsNotesByTick = [];
    {
      foreach (var nbsNotesAligned in nbsNotesByTime.GroupBy(x => groupIdsByTime[x.Key]))
      {
        int startingLayerIndex = 0;

        foreach (var nbsNotesGroupedByInstAndTrack in
          nbsNotesAligned.SelectMany(x => x.Value)
                         .OrderByDescending(x => x.Key)
                         .OrderBy(x => x.Track)
                         .OrderBy(x => x.Inst)
                         .GroupBy(x => x.InstAndTrack))
        {
          SortedDictionary<long, List<NbsNote>> nbsNotesInBigLayerByTime = [];

          foreach (var nbsNote in nbsNotesGroupedByInstAndTrack)
          {
            nbsNotesInBigLayerByTime.GetOrAdd(nbsNote.Time, key => []).Add(nbsNote);
          }

          int maxIndexInThisLayer = 0;
          foreach (var (tick, noteList) in nbsNotesInBigLayerByTime.GroupBy(x => timeToTickMap[x.Key])
                                                                   .Select(x => (x.Key, x.SelectMany(y => y.Value))))
          {
            foreach (var (indexInThisLayer, nbsNote) in noteList.Index())
            {
              layeredNbsNotesByTick.GetOrAdd(tick, key => [])[startingLayerIndex + indexInThisLayer] = nbsNote;
              maxIndexInThisLayer = indexInThisLayer > maxIndexInThisLayer ? indexInThisLayer : maxIndexInThisLayer;
            }
          }

          startingLayerIndex += maxIndexInThisLayer + 1;
        }
      }
    }

    // cleanup

    midiFile = null!;
    midiNotesByTime = null!;
    timePerBarChanges = null!;
    ticksPerTimeChanges = null!;
    groupIdsByTime = null!;
    nbsNotesByTime = null!;
    ForceGC();

    // write nbs header

    using FileStream fstream = File.Create(config.LetUserSelectNbsSavePath ? config.NbsSavePath! : config.AutoSelectedNbsSavePath!);
    using BinaryWriter writer = new(fstream);

    int songStartTick = timeToTickMap.GetValueOrDefault(songStartTime, 0);
    int songLoopTick = timeToTickMap.GetValueOrDefault(songLoopTime, -1);
    int maxNbsTick = layeredNbsNotesByTick.Keys.Max();
    short layerCount = (short)(layeredNbsNotesByTick.SelectMany(x => x.Value).Select(x => x.Key).Max() + 1);

    writer.Write((byte[])[0x00, 0x00, 0x05, 0x10]);
    writer.Write((short)int.Clamp(maxNbsTick - songStartTick + 1, 0, 32767)); // Song length
    long layerCountPos = fstream.Position;
    writer.Write(layerCount); // Layer count
    writer.WriteNbsFormatString(songName); // Song name
    writer.WriteNbsFormatString(songAuthor); // Song author
    writer.WriteNbsFormatString(songOriginalAuthor); // Song original author
    writer.WriteNbsFormatString(songDesc); // Song description
    writer.Write((short)(config.DoConsiderTempoChange ? config.NbsTPS * 100 : 2000)); // TPS
    writer.Write((sbyte)0); // Auto-saving (obsolete)
    writer.Write((sbyte)1); // Auto-saving duration (obsolete)
    writer.Write((sbyte)4); // Time signature
    writer.Write((int)0); // Minutes spent on the project
    writer.Write((int)0); // Left-clicks
    writer.Write((int)0); // Right-clicks
    writer.Write((int)0); // Note blocks added
    writer.Write((int)0); // Note blocks removed
    writer.WriteNbsFormatString(""); // MIDI/Schematic file name

    if (songLoopTick >= 0)
    {
      writer.Write((sbyte)1); // Loop on/off
      writer.Write((sbyte)0); // Max loop count
      writer.Write((short)int.Clamp(songLoopTick - songStartTick, 0, 32767)); // Loop start tick
    }
    else
    {
      writer.Write((sbyte)0); // Loop on/off
      writer.Write((sbyte)0); // Max loop count
      writer.Write((short)0); // Loop start tick
    }

    // write nbs notes

    {
      short currentTick = -1;

      foreach (var (tick, notes) in layeredNbsNotesByTick)
      {
        if (notes.Count == 0)
        {
          continue;
        }

        short alignedTick = (short)(tick - songStartTick);

        if (alignedTick < 0)
        {
          continue;
        }

        short jumpTick = (short)(alignedTick - currentTick);
        currentTick = alignedTick;

        writer.Write(jumpTick);

        int currentLayer = -1;

        foreach (var (layer, note) in notes)
        {
          short jumpLayer = (short)(layer - currentLayer);
          currentLayer = layer;

          writer.Write(jumpLayer);
          writer.Write(note.Inst);
          writer.Write(note.Key);
          writer.Write(note.Vel);
          writer.Write(note.Pan);
          writer.Write(note.Pitch);
        }

        writer.Write((short)0);
      }

      writer.Write((short)0);

      // write nbs layers

      for (int i = 0; i < layerCount; i++)
      {
        writer.WriteNbsFormatString(string.Format(config.LayerNameFormat, i));
        writer.Write((sbyte)0);
        writer.Write((sbyte)100);
        writer.Write((byte)100);
      }

      // write nbs custom instruments

      writer.Write((byte)0);
    }

    // cleanup

    timeToTickMap = null!;
    layeredNbsNotesByTick = null!;
    ForceGC();
  }

  #region statics

  private static FrozenDictionary<sbyte, sbyte>? _midiVelToMinecraftVelMap;

  private static FrozenDictionary<sbyte, sbyte> MidiVelToMinecraftVelMap => _midiVelToMinecraftVelMap ??= InitializeMidiVelToMinecraftVelMap();

  private static sbyte MidiVelToMinecraftVel(sbyte midiVel)
  {
    midiVel = sbyte.Clamp(midiVel, 1, 127);
    return MidiVelToMinecraftVelMap.GetValueOrDefault(midiVel, (sbyte)1);
  }

  private static FrozenDictionary<sbyte, sbyte> InitializeMidiVelToMinecraftVelMap()
  {
    var minecraftAttMap = Enumerable.Range(1, 100).Select(x => ((sbyte)x, double.Max(-20d * double.Log10(x / 100d), 0))).ToDictionary();
    var midiAttMap = Enumerable.Range(1, 127).Select(x => ((sbyte)x, double.Max(-20d * double.Log10(double.Pow(x, 2) / double.Pow(127, 2)), 0))).ToDictionary();

    Dictionary<sbyte, sbyte> result = [];

    foreach (var (midiVel, midiAtt) in midiAttMap)
    {
      sbyte closestMinecraftKey = minecraftAttMap.Aggregate((x, y) => double.Abs(x.Value - midiAtt) < double.Abs(y.Value - midiAtt) ? x : y).Key;

      result[midiVel] = closestMinecraftKey;
    }

    return result.ToFrozenDictionary();
  }

  private static void ForceGC()
  {
    GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
    GC.WaitForPendingFinalizers();
    GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
  }

  #endregion

  #region subclasses

  private record struct InstAndTrack(sbyte Inst, int Track);

  private record class NbsNote(int Tick, long Time, sbyte Inst, sbyte Key, sbyte Vel, byte Pan, short Pitch, int Track)
  {
    public InstAndTrack InstAndTrack => new(Inst, Track);
  }

  private record class MidiNote(sbyte Inst, sbyte Key, sbyte Vel, int Pan, int Track);

  private class ChannelState(int inst = 0, int pan = 64)
  {
    public int Inst { get; set; } = inst;
    public int Pan { get; set; } = pan;
  }

  private class MidiNoteAvoidDuplicatedEqualityComparer : IEqualityComparer<MidiNote>
  {
    public static MidiNoteAvoidDuplicatedEqualityComparer Instance = new();

    public bool Equals(MidiNote? x, MidiNote? y)
    {
      if (x == null && y == null)
      {
        return true;
      }
      if (x == null || y == null)
      {
        return false;
      }
      return x.Inst == y.Inst && x.Key == y.Key && x.Track == y.Track;
    }

    public int GetHashCode([DisallowNull] MidiNote obj)
    {
      return (int)obj.Inst | (obj.Key << 7) | (obj.Track << 14);
    }
  }

  private class MidiEventComparer : IComparer<MidiEvent>
  {
    public static MidiEventComparer Instance = new();

    public int Compare(MidiEvent? x, MidiEvent? y)
    {
      if (x is null && y is not null)
      {
        return -1;
      }

      if (x is not null && y is null)
      {
        return 1;
      }

      if (x is null && y is null)
      {
        return 0;
      }

      if (x is not null && y is not null)
      {
        if (x.CommandCode is MidiCommandCode.NoteOn && y.CommandCode is not MidiCommandCode.NoteOn)
        {
          return 1;
        }

        if (x.CommandCode is not MidiCommandCode.NoteOn && y.CommandCode is MidiCommandCode.NoteOn)
        {
          return -1;
        }
      }

      return 0;
    }
  }

  #endregion
}