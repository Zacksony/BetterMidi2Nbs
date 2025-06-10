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
  public bool ShowInstrumentMapConfig
  {
    get { return (bool)GetValue(ShowInstrumentConfigProperty); }
    set { SetValue(ShowInstrumentConfigProperty, value); }
  }
  
  public static readonly DependencyProperty ShowInstrumentConfigProperty =
    DependencyProperty.Register(nameof(ShowInstrumentMapConfig), typeof(bool), typeof(MainWindow), new PropertyMetadata(false, (d, e) =>
    {
      ((MainWindow)d).ShowAcrylicPanel = (bool)e.NewValue;
    }));

  public bool ShowAcrylicPanel
  {
    get { return (bool)GetValue(ShowAcrylicPanelProperty); }
    set { SetValue(ShowAcrylicPanelProperty, value); }
  }

  public static readonly DependencyProperty ShowAcrylicPanelProperty =
    DependencyProperty.Register(nameof(ShowAcrylicPanel), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

  public MainWindow(MainViewModel viewModel)
  {
    DataContext = viewModel;

    viewModel.StartingConversion += OnStartingConversion;
    viewModel.AskRestoreConfig += OnAskRestoreConfig;
    viewModel.Error += OnError;
    viewModel.Info += OnInfo;

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

  private M2NCore? OnStartingConversion(M2NConfig config)
  {
    UI.MessageBox.DefaultBackdropType = BackdropType.Mica;

    if (!File.Exists(config.InputMidiPath))
    {
      UI.MessageBox.Show(this, "Invalid midi path.", "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
      return null;
    }

    if (config.LetUserSelectNbsSavePath)
    {
      if (config.NbsSavePath is null || !Directory.Exists(Path.GetDirectoryName(config.NbsSavePath)))
      {
        UI.MessageBox.Show(this, "Invalid nbs save path.", "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
        return null;
      }
    }

    if (!config.DoConsiderTempoChange && config.NbsTicksPerQuarterNote is < 1 or > 32767)
    {
      UI.MessageBox.Show(this, "'Ticks Per Quarter Note' must be in the range [1,32767]", "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
      return null;
    }

    if (config.DoConsiderTempoChange && config.NbsTPS is < 1 or > 327)
    {
      UI.MessageBox.Show(this, "'Ticks Per Second' must be in the range [1,327].", "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
      return null;
    }

    if (config.VisualAlignBarlines is < 0 or > 32767)
    {
      UI.MessageBox.Show(this, "'Note Grouping Barlines' must be in the range (0,32767].", "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
      return null;
    }

    if (config.DoForceVelocity && config.ForceMidiVelocity is < 1 or > 127)
    {
      UI.MessageBox.Show(this, "'Force Midi Velocity' must be in the range [1,127].", "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
      return null;
    }

    if (config.MinMidiVelocity is < 1 or > 127)
    {
      UI.MessageBox.Show(this, "'Min Midi Velocity' must be in the range [1,127].", "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
      return null;
    }

    if (config.StartingPatch is < 0 or > 127)
    {
      UI.MessageBox.Show(this, "'Default PC' must be in the range [0,127].", "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
      return null;
    }

    if (config.DoForcePatch && config.ForcePatch is < 0 or > 127)
    {
      UI.MessageBox.Show(this, "'Force PC' must be in the range [0,127].", "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
      return null;
    }    

    if (UI.MessageBox.Show(this, "We are ready. Click 'OK' to start conversion.", "Start Conversion?", MessageBoxButton.OKCancel, MessageBoxImage.Question) is not MessageBoxResult.OK)
    {
      return null;
    }

    M2NCore core = new(config);
    core.StartConversion();
    return core;
  }

  private void OnError(string message)
  {
    Dispatcher.Invoke(() =>
    {
      // Can't use UI.MessageBox here,
      // causes visual issue
      System.Windows.MessageBox.Show(this, message, "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
    });
  }

  private void OnInfo(string message)
  {
    Dispatcher.Invoke(() =>
    {
      UI.MessageBox.Show(this, message, "Info", MessageBoxButton.OK, MessageBoxImage.Information);
    });    
  }

  private bool OnAskRestoreConfig()
  {
    if (UI.MessageBox.Show(this, "Are you sure you want to reset all settings to their default values? This action cannot be undone.", "Warning", MessageBoxButton.YesNo, MessageBoxImage.Warning) is MessageBoxResult.Yes)
    {
      return true;
    }
    return false;
  }

  private void InstrumentMapConfigButton_Click(object sender, RoutedEventArgs e)
  {
    ShowInstrumentMapConfig = true;
  }   

  private void InstrumentMapConfigCloseButton_Click(object sender, RoutedEventArgs e)
  {
    ShowInstrumentMapConfig = false;
  }

  private void ProgressScreenGrid_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
  {
    ShowAcrylicPanel = (bool)e.NewValue;
  }
}