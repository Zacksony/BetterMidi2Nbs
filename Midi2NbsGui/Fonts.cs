using System;
using System.Windows;
using System.Windows.Media;

namespace Midi2NbsGui;

public static class Fonts
{
  public static readonly ResourceDictionary Dictionary = new()
  {
    Source = new Uri("/Midi2NbsGui;component/FontDictionary.xaml", UriKind.Relative)
  };  

  public static FontFamily GetFont(string fontName)
  {
    if (Dictionary.Contains(fontName) && Dictionary[fontName] is FontFamily family)
    {
      return family;
    }

    return new FontFamily(fontName);
  }

  public static readonly FontFamily FluentSystemIconsRegular = GetFont("FluentSystemIcons.Regular");
  public static readonly FontFamily FluentSystemIconsFilled = GetFont("FluentSystemIcons.Filled");
}
