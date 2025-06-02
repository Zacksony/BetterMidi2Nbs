using NAudio.Midi;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;

namespace Midi2Nbs;

public static class Midi2Nbs
{
  static readonly FrozenDictionary<short, string> NbsInstNameMap = new Dictionary<short, string>
  {
    [0]  = "Piano",
    [1]  = "Double Bass",
    [2]  = "Bass Drum",
    [3]  = "Snare Drum",
    [4]  = "Click",
    [5]  = "Guitar",
    [6]  = "Flute",
    [7]  = "Bell",
    [8]  = "Chime",
    [9]  = "Xylophone",
    [10] = "Iron Xylophone",
    [11] = "Cow Bell",
    [12] = "Didgeridoo",
    [13] = "Bit",
    [14] = "Banjo",
    [15] = "Pling",
  }.ToFrozenDictionary();

  public static string GetDefaultNbsSavePath(string midiPath)
  {
    return Path.ChangeExtension(midiPath, "nbs");
  }

  public static void Start(M2NConfig config)
  {
    using ExecuteOnExit gcCollectionOnExit = new();
    gcCollectionOnExit.Actions.Add(() =>
    {
      GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
      GC.WaitForPendingFinalizers();
      GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
    });
    using ExecuteOnExit onExit = new();

    string midiPath = config.InputMidiPath ?? throw new InvalidOperationException("InputMidiPath can't be null.");

    string nbsOutputPath = config.LetUserSelectNbsSavePath ? config.NbsSavePath! : config.AutoSelectedNbsSavePath!;

    MidiFile mf = new(midiPath, false);
    onExit.Actions.Add(() => mf = null!);

    int midiTicksPerMinecraftTick = mf.DeltaTicksPerQuarterNote / config.NbsTicksPerQuarterNote;

    short songStartTime = 0;
    short songLoopTime = -1;
    SortedDictionary<short, HashSet<NbsNote>> nbsNotesByTick = [];
    onExit.Actions.Add(() => nbsNotesByTick = null!);

    SortedDictionary<short, TimeSignatureEvent> timeSignatureEvents = [];
    onExit.Actions.Add(() => timeSignatureEvents = null!);

    #region read events, convert to nbsNotes

    string songName = "";
    string songDesc = "";
    string songOriginalAuthor = "";
    string songAuthor = "";

    if (mf.Events.FirstOrDefault() is IList<MidiEvent> firstTrack)
    {
      foreach (var midiEvent in firstTrack.OrderBy(x => x.AbsoluteTime))
      {
        if (midiEvent is TextEvent textEvent)
        {
          if (textEvent.MetaEventType is MetaEventType.TextEvent)
          {
            if (textEvent.Text.StartsWith(config.SongDescEventHeader))
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
            else if (textEvent.Text.StartsWith(config.SongNameEventHeader))
            {
              songName = textEvent.Text.Replace(config.SongNameEventHeader, null);
            }
          }
        }
        else if (midiEvent is TimeSignatureEvent signatureEvent)
        {
          long actualTime = midiEvent.AbsoluteTime / midiTicksPerMinecraftTick;
          if (actualTime is < 0 or > 32766)
          {
            continue;
          }

          short thisTime = (short)actualTime;

          timeSignatureEvents[thisTime] = signatureEvent;
        }
      }
    }

    foreach (var channel in mf.Events.SelectMany((events, trackIndex) => events.Select(midiEvent => (midiEvent, (short)trackIndex))).GroupBy(x => x.midiEvent.Channel))
    {
      ChannelState channelState = new(inst: config.StartingPatch);

      foreach (var (midiEvent, trackIndex) in 
        channel.Where(x => x.midiEvent.CommandCode is MidiCommandCode.NoteOn
                                                   or MidiCommandCode.ControlChange
                                                   or MidiCommandCode.PatchChange
                                                   or MidiCommandCode.MetaEvent)
               .OrderBy(x => x.midiEvent.AbsoluteTime)
               .GroupBy(x => x.midiEvent.AbsoluteTime)
               .Select(x => x.OrderBy(e => e.midiEvent, MidiEventComparer.Instance))
               .SelectMany(x => x))
      {
        if (midiEvent is null)
        {
          continue;
        }

        long actualTime = midiEvent.AbsoluteTime / midiTicksPerMinecraftTick;
        if (actualTime is < 0 or > 32766)
        {
          continue;
        }

        short thisTime = (short)actualTime;

        if (midiEvent is TextEvent { MetaEventType: MetaEventType.Marker } meta)
        {
          if (meta.Text == config.SongStartMarkerEventText)
          {
            songStartTime = thisTime;
          }
          else if (meta.Text == config.LoopMarkerEventText)
          {
            songLoopTime = thisTime;
          }
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
          byte finalMidiVelocity = config.DoForceVelocity ? config.ForceMidiVelocity : (byte)noteOn.Velocity;

          if (finalMidiVelocity >= config.MinMidiVelocity)
          {
            NbsNote nbsNote =
            new(thisTime,
                (sbyte)(config.DoForcePatch ? config.ForcePatch : channelState.Inst),
                (sbyte)int.Clamp(noteOn.NoteNumber - 21, 0, 87),
                (sbyte)int.Clamp(config.DoCalculateVelocity ? MidiVelToMinecraftVel((byte)finalMidiVelocity) : finalMidiVelocity, 0, 100),
                (byte)double.Round(double.Clamp(channelState.Pan * 1.5625, 0, 200)),
                0,
                trackIndex);

            nbsNotesByTick.GetOrAdd(thisTime, key => new(NbsNoteEqualityComparerForNoteSet.Instance)).Add(nbsNote);
          }          
        }
      }
    }

    #endregion

    #region write nbs file

    using FileStream fstream = File.Create(nbsOutputPath);
    using BinaryWriter writer = new(fstream);

    onExit.Actions.Add(() => writer.Dispose());
    onExit.Actions.Add(() => fstream.Dispose());

    short songLength = (short)(nbsNotesByTick.Keys.Max() + 1);

    #region nbs: header

    writer.Write((byte[])[0x00, 0x00, 0x05, 0x10]);
    writer.Write((short)(songLength - songStartTime)); // Song length
    long layerCountPos = fstream.Position;
    writer.Write((short)0); // Layer count
    writer.WriteNbsFormatString(songName); // Song name
    writer.WriteNbsFormatString(songAuthor); // Song author
    writer.WriteNbsFormatString(songOriginalAuthor); // Song original author
    writer.WriteNbsFormatString(songDesc); // Song description
    writer.Write((short)2000); // TPS
    writer.Write((sbyte)0); // Auto-saving (obsolete)
    writer.Write((sbyte)1); // Auto-saving duration (obsolete)
    writer.Write((sbyte)4); // Time signature
    writer.Write((int)0); // Minutes spent on the project
    writer.Write((int)0); // Left-clicks
    writer.Write((int)0); // Right-clicks
    writer.Write((int)0); // Note blocks added
    writer.Write((int)0); // Note blocks removed
    writer.WriteNbsFormatString(""); // MIDI/Schematic file name

    if (songLoopTime >= 0)
    {
      writer.Write((sbyte)1); // Loop on/off
      writer.Write((sbyte)0); // Max loop count
      writer.Write((short)(songLoopTime - songStartTime)); // Loop start tick
    }
    else
    {
      writer.Write((sbyte)1); // Loop on/off
      writer.Write((sbyte)0); // Max loop count
      writer.Write((short)0); // Loop start tick
    }

    #endregion nbs: header

    #region nbs: note blocks

    List<short> groupedTickCounts = [];    
    onExit.Actions.Add(() => groupedTickCounts = null!);
    {
      short lastMeaAlignedTick = 0;
      short currentTicksPerGroup = (short)(4 * config.NbsTicksPerQuarterNote * config.VisualAlignBarlines);

      foreach (var (tick, timeSignature) in timeSignatureEvents)
      {
        short lastMeaCount = (short)double.Floor((double)(tick - lastMeaAlignedTick) / currentTicksPerGroup);
        lastMeaAlignedTick = (short)(lastMeaAlignedTick + lastMeaCount * currentTicksPerGroup);
        groupedTickCounts.AddRange(Enumerable.Repeat(currentTicksPerGroup, lastMeaCount));

        currentTicksPerGroup = (short)((4 / (double.Pow(2, timeSignature.Denominator)) * timeSignature.Numerator) * config.NbsTicksPerQuarterNote * config.VisualAlignBarlines);
      }
      if (lastMeaAlignedTick <= songLength - 1)
      {
        short lastMeaCount = (short)double.Ceiling((double)(songLength - lastMeaAlignedTick) / currentTicksPerGroup);
        groupedTickCounts.AddRange(Enumerable.Repeat(currentTicksPerGroup, lastMeaCount));
      }
    }

    Dictionary<NbsNote, int> noteGroupIndexes = [];
    onExit.Actions.Add(() => noteGroupIndexes = null!);

    Dictionary<NbsNote, short> noteSorts = [];
    onExit.Actions.Add(() => noteSorts = null!);

    Dictionary<int, SortedSet<NbsNoteLayerKey>> sortedGroupedNoteLayerKeys = [];
    onExit.Actions.Add(() => sortedGroupedNoteLayerKeys = null!);

    SortedDictionary<int, SortedDictionary<short, HashSet<NbsNote>>> groupedNbsNotes = [];
    onExit.Actions.Add(() => groupedNbsNotes = null!);

    {
      int currentTick = 0;
      foreach (var (index, count) in groupedTickCounts.Index())
      {
        SortedDictionary<short, HashSet<NbsNote>> newGroup = new(nbsNotesByTick.Where(x => x.Key >= currentTick && x.Key < currentTick + count).ToDictionary());
        if (newGroup.Count > 0)
        {
          groupedNbsNotes.Add(index, newGroup);
        }
        currentTick += count;

        foreach (var note in newGroup.SelectMany(x => x.Value))
        {
          noteGroupIndexes[note] = index;
        }
      }
    }

    foreach (var (groupIndex, groupedNotesByTick) in groupedNbsNotes)
    {
      SortedSet<NbsNoteLayerKey> noteKeys = new(NbsNoteLayerKeyComparer.Instance);

      var newNoteSorts =
        groupedNotesByTick.SelectMany(
          pair => pair.Value.GroupBy(note => note.InstAndTrack)
                            .SelectMany(notes => notes.GroupBy(note => note.Tick)
                                                      .SelectMany(notesByTick => notesByTick.Select((note, index) => (key: new NbsNoteLayerKey(note.Inst, note.Track, (short)-index), note, sort: (short)-index)))));

      foreach (var (key, note, sort) in newNoteSorts)
      {
        noteKeys.Add(key);
        noteSorts[note] = sort;
      }

      sortedGroupedNoteLayerKeys[groupIndex] = noteKeys;
    }

    Dictionary<int, Dictionary<NbsNoteLayerKey, short>> layerByNoteKey
      = sortedGroupedNoteLayerKeys.ToDictionary(outerPair => outerPair.Key, outerPair => outerPair.Value.Index().ToDictionary(x => x.Item, x => (short)x.Index));

    onExit.Actions.Add(() => layerByNoteKey = null!);

    short layerCount = (short)sortedGroupedNoteLayerKeys.MaxBy(x => x.Value.Count).Value.Count;
    long previousPos = fstream.Position;
    fstream.Position = layerCountPos;
    writer.Write(layerCount);
    fstream.Position = previousPos;

    {
      short currentTick = -1;

      foreach (var (tick, notes) in nbsNotesByTick)
      {
        if (notes.Count == 0)
        {
          continue;
        }

        short alignedTick = (short)(tick - songStartTime);

        short jumpTick = (short)(alignedTick - currentTick);
        currentTick = alignedTick;

        writer.Write(jumpTick);

        short currentLayer = -1;

        foreach (var (note, layer) in notes.Select(x => (note: x, layer: layerByNoteKey[noteGroupIndexes[x]][new(x.Inst, x.Track, noteSorts[x])])).OrderBy(x => x.layer))
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
    }

    #endregion nbs: note blocks

    #region nbs: layers

    for (int i = 0; i < layerCount; i++)
    {
      writer.WriteNbsFormatString(string.Format(config.LayerNameFormat, i));
      writer.Write((sbyte)0);
      writer.Write((sbyte)100);
      writer.Write((byte)100);
    }

    #endregion nbs: layers

    #region nbs: custom instruments

    writer.Write((byte)0);

    #endregion nbs: custom instruments

    #endregion write nbs file
  }

  static FrozenDictionary<byte, byte>? _midiVelToMinecraftVelMap;

  static FrozenDictionary<byte, byte> MidiVelToMinecraftVelMap => _midiVelToMinecraftVelMap ??= InitializeMidiVelToMinecraftVelMap();

  static byte MidiVelToMinecraftVel(byte midiVel)
  {
    midiVel = byte.Clamp(midiVel, 1, 127);
    return MidiVelToMinecraftVelMap.GetValueOrDefault(midiVel, (byte)1);
  }

  static FrozenDictionary<byte, byte> InitializeMidiVelToMinecraftVelMap()
  {
    var minecraftAttMap = Enumerable.Range(1, 100).Select(x => ((byte)x, double.Max(-20d * double.Log10(x / 100d), 0))).ToDictionary();
    var midiAttMap = Enumerable.Range(1, 127).Select(x => ((byte)x, double.Max(-20d * double.Log10(double.Pow(x, 2) / double.Pow(127, 2)), 0))).ToDictionary();

    Dictionary<byte, byte> result = [];

    foreach (var (midiVel, midiAtt) in midiAttMap)
    {
      byte closestMinecraftKey = minecraftAttMap.Aggregate((x, y) =>
            Math.Abs(x.Value - midiAtt) < Math.Abs(y.Value - midiAtt) ? x : y).Key;

      result[midiVel] = closestMinecraftKey;
    }

    return result.ToFrozenDictionary();
  }

  readonly record struct NbsNoteLayerKey(sbyte Inst, int Track, short Sort);

  readonly record struct NbsInstAndTrack(sbyte Inst, int Track);

  record class NbsNote(short Tick, sbyte Inst, sbyte Key, sbyte Vel, byte Pan, short Pitch, short Track)
  {
    public NbsInstAndTrack InstAndTrack => new(Inst, Track);
  }

  class ChannelState(int inst = 0, int pan = 64)
  {
    public int Inst { get; set; } = inst;
    public int Pan { get; set; } = pan;
  }

  class NbsNoteLayerKeyComparer : IComparer<NbsNoteLayerKey>
  {
    public static NbsNoteLayerKeyComparer Instance = new();

    public int Compare(NbsNoteLayerKey x, NbsNoteLayerKey y)
    {
      if (x.Inst == y.Inst)
      {
        if (x.Track == y.Track)
        {
          return y.Sort - x.Sort;
        }
        else
        {
          return x.Track - y.Track;
        }
      }
      else
      {
        return x.Inst - y.Inst;
      }
    }
  }

  class NbsNoteEqualityComparerForNoteSet : IEqualityComparer<NbsNote>
  {
    public static NbsNoteEqualityComparerForNoteSet Instance = new();

    public bool Equals(NbsNote? x, NbsNote? y)
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
    public int GetHashCode([DisallowNull] NbsNote obj)
    {
      return (int)obj.Inst | (obj.Key << 8) | (obj.Track << 16);
    }
  }

  class MidiEventComparer : IComparer<MidiEvent>
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
}

static class BinaryWriterExtensions
{
  public static void WriteNbsFormatString(this BinaryWriter writer, string str)
  {
    writer.Write(Encoding.ASCII.GetByteCount(str));
    writer.Write(Encoding.ASCII.GetBytes(str));
  }
}

static class DictionaryExtensions
{
  public static TValue GetOrAdd<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, Func<TKey, TValue> factory)
  {
    if (!dict.TryGetValue(key, out TValue? value))
    {
      dict[key] = value = factory(key);
    }

    return value;
  }
}