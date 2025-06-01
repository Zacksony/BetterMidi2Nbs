using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Midi2Nbs;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Midi2NbsGui;

public partial class MainViewModel : ObservableObject
{
  private const string ConfigFileName = "m2nconfig.json";

  [ObservableProperty]
  private M2NConfig config = new();

  public event Func<M2NConfig, bool>? StartingConversion;
  public event Func<bool>? AskRestoreConfig;

  public MainViewModel()
  {
    LoadConfig();
  }

  [RelayCommand]
  private void BrowseMidiFile()
  {
    // ok, some gui shit going on here viewmodel..
    // nbcs
    OpenFileDialog dialog = new()
    {
      Filter = "MIDI file (*.mid)|*.mid",
      Title = "Select MIDI file..",
      CheckFileExists = true
    };

    if (dialog.ShowDialog() == true)
    {
      Config.InputMidiPath = dialog.FileName;
      OnPropertyChanged(nameof(Config));
    }
  }

  [RelayCommand]
  private void BrowseNbsSaveFile()
  {
    SaveFileDialog dialog = new()
    {
      Filter = "NBS file v4 / v5 (*.nbs)|*.nbs",
      Title = "Save NBS file to..",
      CheckFileExists = false
    };

    if (dialog.ShowDialog() == true)
    {
      Config.NbsSavePath = dialog.FileName;
      OnPropertyChanged(nameof(Config));
    }
  }

  [RelayCommand]
  private void StartConversion()
  {
    StartingConversion?.Invoke(Config);
  }

  [RelayCommand]
  private void RestoreConfig()
  {
    if (AskRestoreConfig?.Invoke() ?? true)
    {
      var oldConfig = Config;
      Config = new()
      {
        InputMidiPath = oldConfig.InputMidiPath,
        NbsSavePath = oldConfig.NbsSavePath,
        LetUserSelectNbsSavePath = oldConfig.LetUserSelectNbsSavePath
      };
      OnPropertyChanged(nameof(Config));
    }
  }

  private void LoadConfig()
  {
    if (File.Exists(ConfigFileName))
    {
      try
      {
        var json = File.ReadAllText(ConfigFileName);
        var loaded = JsonSerializer.Deserialize<M2NConfig>(json);
        if (loaded != null)
          Config = loaded;

        if (!File.Exists(Config.InputMidiPath))
        {
          Config.InputMidiPath = null;
        }
        if (!Directory.Exists(Path.GetDirectoryName(Config.NbsSavePath)))
        {
          Config.NbsSavePath = null;
        }
      }
      catch (Exception e)
      { 
        Debug.WriteLine($"Failed to read config json:\n{e}"); 
      }
    }
  }

  static JsonSerializerOptions MyJsonSerializerOptions = new() { WriteIndented = true };
  public void SaveConfig()
  {
    var json = JsonSerializer.Serialize(Config, MyJsonSerializerOptions);
    File.WriteAllText(ConfigFileName, json);
  }
}
