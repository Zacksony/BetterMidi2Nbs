using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Midi2Nbs;

internal class FrequencyLimitedExection(TimeSpan intervalLimit)
{
  private Lock _canExecuteLock = new();

  private bool _canExecute = true;

  private uint _intervalLimitMilliseconds = (uint)double.Ceiling(intervalLimit.TotalMilliseconds);

  public void Execute(Action action)
  {
    bool shouldExecute = false;

    lock (_canExecuteLock)
    {
      if (_canExecute)
      {
        _canExecute = false;
        shouldExecute = true;
      }
    }

    if (shouldExecute)
    {
      action();

      Timer? timer = null;
      timer = new Timer(_ =>
      {
        lock (_canExecuteLock)
        {
          _canExecute = true;
        }
        timer?.Dispose(); 

      }, null, _intervalLimitMilliseconds, Timeout.Infinite);
    }
  }
}
