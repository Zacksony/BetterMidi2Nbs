using System.Configuration;
using System.Data;
using System.Windows;

namespace Midi2NbsGui;
/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
  private MainViewModel? viewModel;

  protected override void OnStartup(StartupEventArgs e)
  {
    base.OnStartup(e);    
    var mainWindow = new MainWindow(viewModel = new());
    mainWindow.Show();
  }

  protected override void OnExit(ExitEventArgs e)
  {
    base.OnExit(e);
    viewModel?.SaveConfig();
  }
}

