using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace Midi2NbsGui;

public class ChainedConverter : IMultiValueConverter
{
  public List<object> Converters { get; set; } = [];

  public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
  {
    object? currentValue = values;
    foreach (var converter in Converters)
    {
      if (converter is IValueConverter singleConverter)
      {
        currentValue = singleConverter.Convert(currentValue, targetType, parameter, culture);
      }
      else if (converter is IMultiValueConverter multiConverter)
      {
        if (currentValue is IEnumerable multiValue)
        {
          currentValue = multiConverter.Convert([.. multiValue.OfType<object>()], targetType, parameter, culture);
        }
        else
        {
          currentValue = multiConverter.Convert([currentValue], targetType, parameter, culture);
        }
      }
    }

    return currentValue;
  }

  public object[] ConvertBack(object value, Type[] targetType, object parameter, CultureInfo culture)
  {
    throw new NotImplementedException();
  }
}
