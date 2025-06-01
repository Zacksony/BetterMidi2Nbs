using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Midi2NbsGui;

public class FluentIcon : TextBlock
{
  public FluentIcon(FontFamily fontFamily)
  {
    FontFamily = fontFamily;
    RenderTransformOrigin = new(0.5, 0.5);
  }

  public FluentIcon() : this(Fonts.FluentSystemIconsRegular) { }
  
  public bool IsReverse
  {
    get { return (bool)GetValue(IsReverseProperty); }
    set { SetValue(IsReverseProperty, value); }
  }

  public static readonly DependencyProperty IsReverseProperty =
    DependencyProperty.Register("IsReverse", typeof(bool), typeof(TextBlock), new PropertyMetadata(false, (d, e) =>
    {
      if (d is not FluentIcon dFluentIcon)
      {
        return;
      }
      if (e.NewValue is not bool value)
      {
        return;
      }

      if (value)
      {
        dFluentIcon.RenderTransform = new ScaleTransform(-1 , 1);
      }
      else
      {
        dFluentIcon.RenderTransform = new ScaleTransform(1 , 1);
      }
    }));

  public int IconId
  {
    get { return (int)GetValue(IconIdProperty); }
    set { SetValue(IconIdProperty, value); }
  }

  public static readonly DependencyProperty IconIdProperty =
    DependencyProperty.Register(nameof(IconId), typeof(int), typeof(FluentIcon), new PropertyMetadata(0, (d, e) =>
    {
      if (d is not FluentIcon dFluentIcon)
      {
        return;
      }
      if (e.NewValue is not int iconId)
      {
        return;
      }
    
      dFluentIcon.Text = char.ConvertFromUtf32(iconId);
    }));
}

public sealed class FluentIconFilled() : FluentIcon(Fonts.FluentSystemIconsFilled);