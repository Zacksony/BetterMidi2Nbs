using NAudio.Midi;
using System;
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

  static void Main(string[] args)
  {
#if DEBUG
    string midiPath = @"D:\MIDI\Black MIDIs\Necrofantasia all hell freezes over.mid";
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

    int midiTicksPerMinecraftTick = mf.DeltaTicksPerQuarterNote / 8;

    short songStartTime = 0; 
    short songLoopTime = -1; 
    SortedDictionary<short, List<NbsNote>> nbsNotesByTick = [];

    #region read events, convert to nbsNotes

    ChannelState[] channelStates = [.. Enumerable.Repeat<ChannelState>(new(), 16)];

    string songName = "";
    string songDesc = "";
    string songOriginalAuthor = "";
    string songAuthor = "";    

    if (mf.Events.FirstOrDefault() is IList<MidiEvent> firstTrack)
    {
      foreach (var midiEvent in firstTrack)
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
      }
    }

    foreach (var track in mf.Events)
    {
      foreach (var midiEvent in track.Where(x => x.CommandCode is MidiCommandCode.NoteOn 
                                                               or MidiCommandCode.ControlChange 
                                                               or MidiCommandCode.PatchChange
                                                               or MidiCommandCode.MetaEvent)
                                     .OrderBy(x => x.AbsoluteTime)
                                     .GroupBy(x => x.AbsoluteTime)
                                     .Select(x => x.Order(MidiEventComparer.Instance))
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

        ChannelState channelState = channelStates[midiEvent.GetChannelIndex()];

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
          NbsNote nbsNote = MidiNoteToNbsNote(channelState.Inst, channelState.Pan, noteOn);          
          

          if (!nbsNotesByTick.TryGetValue(thisTime, out List<NbsNote>? notesInThisTick))
          {
            nbsNotesByTick[thisTime] = notesInThisTick = [];
          }

          notesInThisTick.Add(nbsNote);
        }
      }
    }

    #endregion

    #region write nbs file

    using FileStream fstream = File.Create(nbsOutputPath);
    using BinaryWriter writer = new(fstream);

    short songLength = (short)(nbsNotesByTick.Keys.Max() + 1 - songStartTime);
    short layerCount = (short)int.Clamp(nbsNotesByTick.Values.MaxBy(x => x.Count)?.Count ?? 0, 0, 32767);

    #region nbs: header

    writer.Write((byte[])[0x00, 0x00, 0x05, 0x10]);
    writer.Write(songLength); // Song length
    writer.Write(layerCount); // Layer count
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

      foreach (var note in notes)
      {        
        writer.Write((short)1);
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
      writer.WriteNbsFormatString("");
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

  static NbsNote MidiNoteToNbsNote(int cInst, int cPan, NoteOnEvent midiNoteOn)
  {
    return new((sbyte)cInst,
               (sbyte)int.Clamp(midiNoteOn.NoteNumber - 21, 0, 87),
               (sbyte)int.Clamp(midiNoteOn.Velocity, 0, 100),
               (byte)double.Round(double.Clamp(cPan * 1.5625, 0, 200)),
               0);
  }

  static string GetNbsOutputPath(string midiPath)
  {
    return Path.ChangeExtension(midiPath, "nbs");
  }

  readonly record struct NbsNote(sbyte Inst, sbyte Key, sbyte Vel, byte Pan, short Pitch);

  class ChannelState(int inst = 0, int pan = 64)
  {
    public int Inst { get; set; } = inst;
    public int Pan { get; set; } = pan;
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
          return -1;
        }

        if (x.CommandCode is not MidiCommandCode.NoteOn && y.CommandCode is MidiCommandCode.NoteOn)
        {
          return 1;
        }
      }      

      return 0;
    }
  }
}

static class MidiEventExtensions
{
  public static int GetChannelIndex(this MidiEvent midiEvent) => midiEvent.Channel - 1;
}

static class BinaryWriterExtensions
{
  public static void WriteNbsFormatString(this BinaryWriter writer, string str)
  {
    writer.Write(Encoding.ASCII.GetByteCount(str));
    writer.Write(Encoding.ASCII.GetBytes(str));
  }
}
