using NAudio.Midi;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;

namespace Midi2Nbs;

internal class Program
{
  const string SongNameEventHeader = "[SONGNAME]";
  const string SongDescEventHeader = "[SONGDESC]";
  const string OriginalAuthorEventHeader = "[ORIGINALAUTHOR]";
  const string SongAuthorEventHeader = "[SONGAUTHOR]";
  const string SongStartEventText = "[START]";
  const string LoopEventText = "[LOOP]";

  const int NbsTicksPerQuarterNote = 8;

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

  static void Main(string[] args)
  {
#if DEBUG
    string midiPath = @"D:\MIDI\nbs-mus-proj\m2n-debug\purple.mid";
#else
    if (args.Length != 1)
    {
      Console.WriteLine("Usage: Midi2Nbs <filePath>");
      return;
    }
    string midiPath = args[0].Replace("\"", null).Replace("'", null).Trim();
#endif

    string nbsOutputPath = GetNbsOutputPath(midiPath);

    MidiFile mf = new(midiPath);

    int midiTicksPerMinecraftTick = mf.DeltaTicksPerQuarterNote / NbsTicksPerQuarterNote;

    short songStartTime = 0;
    short songLoopTime = -1;
    SortedDictionary<short, HashSet<NbsNote>> nbsNotesByTick = [];
    SortedDictionary<short, TimeSignatureEvent> timeSignatureEvents = [];

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
            if (textEvent.Text.StartsWith(SongDescEventHeader))
            {
              songDesc = textEvent.Text.Replace(SongDescEventHeader, null);
            }
            else if (textEvent.Text.StartsWith(OriginalAuthorEventHeader))
            {
              songOriginalAuthor = textEvent.Text.Replace(OriginalAuthorEventHeader, null);
            }
            else if (textEvent.Text.StartsWith(SongAuthorEventHeader))
            {
              songAuthor = textEvent.Text.Replace(SongAuthorEventHeader, null);
            }
            else if (textEvent.Text.StartsWith(SongNameEventHeader))
            {
              songName = textEvent.Text.Replace(SongNameEventHeader, null);
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
      ChannelState channelState = new();

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
          if (meta.Text == SongStartEventText)
          {
            songStartTime = thisTime;
          }
          else if (meta.Text == LoopEventText)
          {
            songLoopTime = thisTime;
          }
        }
        else if (midiEvent is ControlChangeEvent { Controller: MidiController.Pan } cc)
        {
          channelState.Pan = cc.ControllerValue;
        }
        else if (midiEvent is PatchChangeEvent pc)
        {
          channelState.Inst = pc.Patch;
        }
        else if (midiEvent is NoteOnEvent noteOn)
        {
          NbsNote nbsNote = MidiNoteToNbsNote(thisTime, channelState.Inst, channelState.Pan, trackIndex, noteOn);

          nbsNotesByTick.GetOrAdd(thisTime, key => new(NbsNoteEqualityComparerForNoteSet.Instance)).Add(nbsNote);
        }
      }
    }

    #endregion

    #region write nbs file

    using FileStream fstream = File.Create(nbsOutputPath);
    using BinaryWriter writer = new(fstream);

    short songLength = (short)(nbsNotesByTick.Keys.Max() + 1 - songStartTime);

    #region nbs: header

    writer.Write((byte[])[0x00, 0x00, 0x05, 0x10]);
    writer.Write(songLength); // Song length
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

    const int SplitPeriod = 32;

    List<SortedSet<short>> tickGroups = [];
    //int currentTicksPerMea = 4 * NbsTicksPerQuarterNote;


    Dictionary<NbsNote, short> noteSorts = [];
    Dictionary<short, SortedSet<NbsNoteLayerKey>> sortedGroupedNoteLayerKeys = [];    

    foreach (var groupedNotesByTick in nbsNotesByTick.GroupBy(x => (short)(x.Key / SplitPeriod)))
    {
      SortedSet<NbsNoteLayerKey> noteKeys = new(NbsNoteLayerKeyComparer.Instance);

      var newNoteSorts =
        groupedNotesByTick.SelectMany(
          pair => pair.Value.GroupBy(note => note.InstAndTrack)
                            .SelectMany(notes => notes.GroupBy(note => note.Tick)
                                                      .SelectMany(notesByTick => notesByTick.Select((note, index) => (key: new NbsNoteLayerKey(note.Inst, note.Track, (short)index), note: note, sort: (short)index)))));

      foreach (var (key, note, sort) in newNoteSorts)
      {
        noteKeys.Add(key);
        noteSorts[note] = sort;
      }

      sortedGroupedNoteLayerKeys[groupedNotesByTick.Key] = noteKeys;
    }

    Dictionary<short, Dictionary<NbsNoteLayerKey, short>> layerByNoteKey
      = sortedGroupedNoteLayerKeys.ToDictionary(outerPair => outerPair.Key, outerPair => outerPair.Value.Index().ToDictionary(x => x.Item, x => (short)x.Index));

    short layerCount = (short)sortedGroupedNoteLayerKeys.MaxBy(x => x.Value.Count).Value.Count;
    long previousPos = fstream.Position;
    fstream.Position = layerCountPos;
    writer.Write(layerCount);
    fstream.Position = previousPos;

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

      foreach (var (note, layer) in notes.Select(x => (note: x, layer: layerByNoteKey[(short)(tick / SplitPeriod)][new(x.Inst, x.Track, noteSorts[x])])))
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

    #endregion nbs: note blocks

    #region nbs: layers

    for (int i = 0; i < layerCount; i++)
    {
      writer.WriteNbsFormatString($"Layer #{i}");
      writer.Write((sbyte)0);
      writer.Write((sbyte)100);
      writer.Write((byte)100);
    }

    #endregion nbs: layers

    #region nbs: custom instruments

    writer.Write((byte)0);

    #endregion nbs: custom instruments

    #endregion write nbs file

    Console.WriteLine("Done.");
  }

  static NbsNote MidiNoteToNbsNote(short tick, int cInst, int cPan, short trackIndex, NoteOnEvent midiNoteOn)
  {
    return new(tick,
               (sbyte)cInst,
               (sbyte)int.Clamp(midiNoteOn.NoteNumber - 21, 0, 87),
               (sbyte)int.Clamp(midiNoteOn.Velocity, 0, 100),
               (byte)double.Round(double.Clamp(cPan * 1.5625, 0, 200)),
               0,
               trackIndex);
  }

  static string GetNbsOutputPath(string midiPath)
  {
    return Path.ChangeExtension(midiPath, "nbs");
  }

  readonly record struct NbsNoteLayerKey(sbyte Inst, int Track, short Sort);

  readonly record struct NbsInstAndTrack(sbyte Inst, int Track);

  readonly record struct NbsNote(short Tick, sbyte Inst, sbyte Key, sbyte Vel, byte Pan, short Pitch, short Track)
  {
    public readonly NbsInstAndTrack InstAndTrack = new(Inst, Track);
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

    public bool Equals(NbsNote x, NbsNote y)
    {
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
