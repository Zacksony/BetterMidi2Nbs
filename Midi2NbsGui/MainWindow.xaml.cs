using iNKORE.UI.WPF.Modern.Helpers.Styles;
using Midi2Nbs;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UI = iNKORE.UI.WPF.Modern.Controls;

namespace Midi2NbsGui;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
  public MainWindow(MainViewModel viewModel)
  {
    DataContext = viewModel;
    viewModel.StartingConversion += OnStartingConversion;
    viewModel.AskRestoreConfig += OnAskRestoreConfig;
    InitializeComponent();
  }

  public MainViewModel ViewModel => (MainViewModel)DataContext;

  private void NumberOnly(object sender, TextCompositionEventArgs e)
  {
    e.Handled = !int.TryParse(e.Text, out _);
  }

  private void DecimalOnly(object sender, TextCompositionEventArgs e)
  {
    if (sender is TextBox textBox)
    {
      e.Handled = !double.TryParse(textBox.Text + e.Text, out _);
    }    
  }

  private bool OnStartingConversion(M2NConfig config)
  {
    UI.MessageBox.DefaultBackdropType = BackdropType.Mica;

    if (!File.Exists(config.InputMidiPath))
    {
      UI.MessageBox.Show(this, "Invalid midi path.", "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
      return false;
    }

    if (config.LetUserSelectNbsSavePath)
    {
      if (config.NbsSavePath is null || !Directory.Exists(Path.GetDirectoryName(config.NbsSavePath)))
      {
        UI.MessageBox.Show(this, "Invalid nbs save path.", "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
        return false;
      }
    }
    
    if (config.NbsTicksPerQuarterNote is < 1 or > 32767)
    {
      UI.MessageBox.Show(this, "'Ticks Per Quarter Note' must be in the range 1-32767", "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
      return false;
    }

    if (config.VisualAlignBarlines is < 1 or > 32767)
    {
      UI.MessageBox.Show(this, "'Note Grouping Align Barlines' must be in the range 1-32767.", "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
      return false;
    }

    if (config.DoForceVelocity && config.ForceMidiVelocity is < 1 or > 127)
    {
      UI.MessageBox.Show(this, "'Force Midi Velocity' must be in the range 1-127.", "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
      return false;
    }

    if (config.StartingPatch is < 0 or > 127)
    {
      UI.MessageBox.Show(this, "'Default PC' must be in the range 0-127.", "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
      return false;
    }

    if (config.DoForcePatch && config.ForcePatch is < 0 or > 127)
    {
      UI.MessageBox.Show(this, "'Force PC' must be in the range 0-127.", "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
      return false;
    }

    if (UI.MessageBox.Show(this, "We are ready. Click 'OK' to start conversion.", "Start Conversion?", MessageBoxButton.OKCancel, MessageBoxImage.Question) is not MessageBoxResult.OK)
    {
      return false;
    }

    try
    {
      Midi2Nbs.Midi2Nbs.Start(config);

      // Text of UI.MessageBox would dispear sometimes without Thread.Sleep
      Thread.Sleep(100);

      UI.MessageBox.Show(this, "Conversion Successful!", "Congratulations", MessageBoxButton.OK, MessageBoxImage.Information);
      return true;
    }
    catch (Exception e)
    {
      // Can't use UI.MessageBox here,
      // causes visual issue
      System.Windows.MessageBox.Show(this, $"Conversion Failed. Message: {e}", "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
      return false;
    }    
  }

  private bool OnAskRestoreConfig()
  {
    if (UI.MessageBox.Show(this, "Are you sure you want to reset all settings to their default values? This action cannot be undone.", "Warning", MessageBoxButton.YesNo, MessageBoxImage.Warning) is MessageBoxResult.Yes)
    {
      return true;
    }
    return false;
  }
}