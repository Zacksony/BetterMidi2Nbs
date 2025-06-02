using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Midi2Nbs;

public class ExecuteOnExit : IDisposable
{
  public List<Action> Actions { get; } = [];

  void IDisposable.Dispose()
  {
    Actions.ForEach(x =>
    {
      try
      {
        x?.Invoke(); 
      }
      catch { }
    });
    Actions.Clear();
    GC.SuppressFinalize(this);
  }
}
