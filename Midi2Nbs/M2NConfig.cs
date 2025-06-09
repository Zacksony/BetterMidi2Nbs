using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Midi2Nbs;

public class M2NConfig
{
  public string? InputMidiPath { get; set; }
  public string? NbsSavePath { get; set; }
  public string? AutoSelectedNbsSavePath => InputMidiPath is null ? null : Path.ChangeExtension(InputMidiPath, "nbs");
  public bool LetUserSelectNbsSavePath { get; set; } = false;
  public string SongNameEventHeader { get; set; } = "[SONGNAME]";
  public string SongDescEventHeader { get; set; } = "[SONGDESC]";
  public string OriginalAuthorEventHeader { get; set; } = "[ORIGINALAUTHOR]";
  public string SongAuthorEventHeader { get; set; } = "[SONGAUTHOR]";
  public string SongStartMarkerEventText { get; set; } = "[START]";
  public string LoopMarkerEventText { get; set; } = "[LOOP]";
  public string LayerNameFormat { get; set; } = "Layer #{0}";
  public short NbsTicksPerQuarterNote { get; set; } = 8;
  public bool DoConsiderTempoChange { get; set; } = true;
  public double NbsTPS { get; set; } = 20;
  public double VisualAlignBarlines { get; set; } = 1;
  public bool DoCalculateVelocity { get; set; } = true;
  public bool DoForceVelocity { get; set; } = false;
  public byte ForceMidiVelocity { get; set; } = 127;
  public byte MinMidiVelocity { get; set; } = 1;
  public bool EnablePanpot { get; set; } = true;
  public bool EnableProgramChange { get; set; } = true;
  public int StartingPatch { get; set; } = 0;
  public bool DoForcePatch { get; set; } = false;
  public byte ForcePatch { get; set; } = 0;
}
