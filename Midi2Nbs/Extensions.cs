using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Midi2Nbs;

internal static class BinaryWriterExtensions
{
  public static void WriteNbsFormatString(this BinaryWriter writer, string str)
  {
    writer.Write(Encoding.ASCII.GetByteCount(str));
    writer.Write(Encoding.ASCII.GetBytes(str));
  }
}

internal static class DictionaryExtensions
{
  public static TValue GetOrAdd<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, Func<TKey, TValue> factory)
  {
    if (!dict.TryGetValue(key, out TValue? value))
    {
      dict[key] = value = factory(key);
    }

    return value;
  }
}