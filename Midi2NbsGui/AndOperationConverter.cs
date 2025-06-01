using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace Midi2NbsGui;

class AndOperationConverter : IMultiValueConverter
{
  public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
  {
    bool result = true;
    foreach (object value in values)
    {
      result = result && (value.ToString()?.Equals("true", StringComparison.InvariantCultureIgnoreCase) ?? false);
    }
    return result;
  }
  public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotImplementedException();
}
