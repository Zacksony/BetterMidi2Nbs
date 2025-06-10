using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Midi2Nbs;

public enum M2NStatus : byte
{
  // order is important here

  Ready,
  Error,
  LoadMidiFile,
  ReadNotesAndEvents,
  ConvertMidiNotesToNbsNotes,
  GroupAndSortNotes,
  WriteNbsFile,
  Cleanup,
  Finish,
}

public sealed class M2NProgress
{
  private static FrozenDictionary<M2NStatus, string> StatusMessageMap = new Dictionary<M2NStatus, string>()
  {
    [M2NStatus.Ready] = "Ready",
    [M2NStatus.Error] = "Error",
    [M2NStatus.LoadMidiFile] = "Loading midi file... (1 / 5)",
    [M2NStatus.ReadNotesAndEvents] = "Reading midi notes and events... (2 / 5)",
    [M2NStatus.ConvertMidiNotesToNbsNotes] = "Converting midi notes to nbs notes... (3 / 5)",
    [M2NStatus.GroupAndSortNotes] = "Grouping and sorting notes... (4 / 5)",
    [M2NStatus.WriteNbsFile] = "Writing nbs file... (5 / 5)",
    [M2NStatus.Cleanup] = "Cleanup...",
    [M2NStatus.Finish] = "Conversion Successful!",
  }.ToFrozenDictionary();

  private static FrozenDictionary<M2NStatus, double> StatusPercentageWeightMap = new Dictionary<M2NStatus, double>()
  {
    [M2NStatus.LoadMidiFile] = 25,
    [M2NStatus.ReadNotesAndEvents] = 25,
    [M2NStatus.ConvertMidiNotesToNbsNotes] = 20,
    [M2NStatus.GroupAndSortNotes] = 20,
    [M2NStatus.WriteNbsFile] = 10,
    [M2NStatus.Cleanup] = 0
  }.ToFrozenDictionary();

  private Lock _lock = new();
  private M2NStatus _status = M2NStatus.Ready;
  private double _percentage = 0;
  private string _message = string.Empty;

  public Action? ProgressChanged;

  public M2NStatus Status
  {
    get
    {
      lock (_lock)
      {
        return _status;
      }
    }

    set
    {
      lock (_lock)
      {
        _status = value;
        ProgressChanged?.Invoke();
      }
    }
  }

  public double Percentage
  {
    get
    {
      lock (_lock)
      {
        return _percentage;
      }
    }

    set
    {
      lock (_lock)
      {
        _percentage = value;
        ProgressChanged?.Invoke();
      }
    }
  }

  public string Message
  {
    get
    {
      lock (_lock)
      {
        return _message;
      }
    }

    set
    {
      lock (_lock)
      {
        _message = value;
        ProgressChanged?.Invoke();
      }
    }
  }

  public double TotalPercentage
  {
    get
    {
      lock (_lock)
      {
        if (!StatusPercentageWeightMap.TryGetValue(_status, out double statusWeight))
        {
          return 0;
        }

        double previousStatusPercentageSum = StatusPercentageWeightMap.Where(x => x.Key < _status).Sum(x => x.Value);

        return previousStatusPercentageSum + statusWeight * _percentage / 100d;
      }
    }
  }

  public void Set(M2NStatus status, double percentage = 0, string? message = null)
  {
    lock (_lock)
    {
      (_status, _percentage, _message) = (status, percentage, message ?? StatusMessageMap.GetValueOrDefault(status, string.Empty));
      ProgressChanged?.Invoke();
    }
  }
}
