using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace SmartHostMEP.UI
{
    /// <summary>
    /// The look of every SmartHost MEP window: one accent colour, an 8 px spacing grid, rounded
    /// corners and three font sizes. Light or dark, following Revit's theme (Revit 2024+; earlier
    /// versions are always light). Colours are resources ("Sh.*"), the control styles are plain XAML
    /// parsed at run time (the add-in has no compiled XAML), so they work on any .NET SDK. If the
    /// styles cannot be loaded the windows still work with the standard Windows look.
    /// </summary>
    public static class Theme
    {
        /// <summary>Set by the command from Revit's UI theme.</summary>
        public static bool Dark { get; set; }

        public const double Space = 8;
        public const double TitleSize = 18, LabelSize = 12, SmallSize = 11;

        public static readonly FontFamily Font = new FontFamily("Segoe UI");

        /// <summary>Colour name -> (light, dark).</summary>
        static readonly (string key, string light, string dark)[] Palette =
        {
            ("Sh.Bg", "#FFFFFF", "#1F2329"),
            ("Sh.Surface", "#F5F7FA", "#272C33"),
            ("Sh.Surface2", "#EDF1F6", "#2F353D"),
            ("Sh.Input", "#FFFFFF", "#1B1F24"),
            ("Sh.Border", "#D3DAE4", "#3E4651"),
            ("Sh.Text", "#1F2933", "#E7EAEE"),
            ("Sh.Muted", "#687384", "#9BA4B0"),
            ("Sh.Accent", "#2060B0", "#4C8DDB"),
            ("Sh.AccentHover", "#1A4F92", "#6BA3E6"),
            ("Sh.AccentSoft", "#E5EEF9", "#243A55"),
            ("Sh.OnAccent", "#FFFFFF", "#FFFFFF"),
            ("Sh.Hover", "#EEF3FA", "#323A45"),
            ("Sh.Selection", "#D4E3F6", "#2C4868"),
            ("Sh.RowAlt", "#F9FAFC", "#23282E"),
            ("Sh.Danger", "#C62828", "#F07373"),
            ("Sh.DangerSoft", "#FDECEC", "#4A2A2A"),
            ("Sh.Good", "#2E7D32", "#7CC47F"),
            ("Sh.GoodSoft", "#E8F5E9", "#24402A"),
            ("Sh.Warn", "#A65F00", "#F0B35E"),
            ("Sh.WarnSoft", "#FFF3E0", "#45361F"),
        };

        public static Color ColorOf(string key)
        {
            foreach (var p in Palette)
                if (p.key == key) return (Color)ColorConverter.ConvertFromString(Dark ? p.dark : p.light);
            return Colors.Magenta;
        }

        public static SolidColorBrush Brush(string key)
        {
            var b = new SolidColorBrush(ColorOf(key));
            b.Freeze();
            return b;
        }

        /// <summary>WinForms colour (for the few WinForms dialogs left).</summary>
        public static System.Drawing.Color Gdi(string key)
        {
            var c = ColorOf(key);
            return System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);
        }

        static ResourceDictionary _styles;
        static bool? _stylesDark;

        /// <summary>Puts the colours and control styles on <paramref name="win"/> (font, background, text colour too).</summary>
        public static void Apply(Window win)
        {
            win.FontFamily = Font;
            win.FontSize = LabelSize;
            foreach (var p in Palette) win.Resources[p.key] = Brush(p.key);
            win.Background = Brush("Sh.Bg");
            win.Foreground = Brush("Sh.Text");
            try
            {
                if (_styles == null || _stylesDark != Dark)
                {
                    _styles = (ResourceDictionary)XamlReader.Parse(StylesXaml);
                    _stylesDark = Dark;
                }
                win.Resources.MergedDictionaries.Add(_styles);
                // Implicit styles apply to the exact type only: the family dropdown is a subclass.
                if (_styles.Contains(typeof(ComboBox)))
                    win.Resources[typeof(FilterComboBox)] = new Style(typeof(FilterComboBox), (Style)_styles[typeof(ComboBox)]);
            }
            catch (Exception)
            {
                /* standard look */
            }
        }

        public static Style Named(FrameworkElement owner, string key) => owner.TryFindResource(key) as Style;

        /// <summary>A rounded card (surface colour, border).</summary>
        public static Border Card(UIElement child, Thickness? padding = null)
        {
            var b = new Border
            {
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Padding = padding ?? new Thickness(Space * 1.5, Space, Space * 1.5, Space),
                Child = child,
            };
            b.SetResourceReference(Border.BackgroundProperty, "Sh.Surface");
            b.SetResourceReference(Border.BorderBrushProperty, "Sh.Border");
            return b;
        }

        public static TextBlock Text(string text, double size = LabelSize, bool muted = false, bool bold = false)
        {
            var t = new TextBlock { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
            if (bold) t.FontWeight = FontWeights.SemiBold;
            if (muted) t.SetResourceReference(TextBlock.ForegroundProperty, "Sh.Muted");
            return t;
        }

        /// <summary>Button: plain, or the accent "primary" one.</summary>
        public static Button Button(FrameworkElement owner, string text, RoutedEventHandler click, string tooltip = null, bool primary = false)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, Space, 0), MinWidth = 88 };
            if (primary)
            {
                var st = Named(owner, "Sh.Primary");
                if (st != null) b.Style = st;
                else b.FontWeight = FontWeights.SemiBold;
                b.MinWidth = 104;
            }
            if (tooltip != null) b.ToolTip = tooltip;
            if (click != null) b.Click += click;
            return b;
        }

        // ---- styles (XAML) ------------------------------------------------------------------

        const string StylesXaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>

  <Style TargetType='ToolTip'>
    <Setter Property='Background' Value='{DynamicResource Sh.Surface2}'/>
    <Setter Property='Foreground' Value='{DynamicResource Sh.Text}'/>
    <Setter Property='BorderBrush' Value='{DynamicResource Sh.Border}'/>
    <Setter Property='MaxWidth' Value='420'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ToolTip'>
          <Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='4' Padding='8,6'>
            <ContentPresenter>
              <ContentPresenter.Resources>
                <Style TargetType='TextBlock'><Setter Property='TextWrapping' Value='Wrap'/></Style>
              </ContentPresenter.Resources>
            </ContentPresenter>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='Button'>
    <Setter Property='Foreground' Value='{DynamicResource Sh.Text}'/>
    <Setter Property='Background' Value='{DynamicResource Sh.Surface}'/>
    <Setter Property='BorderBrush' Value='{DynamicResource Sh.Border}'/>
    <Setter Property='Padding' Value='14,0'/>
    <Setter Property='Height' Value='32'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='Bd' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='6' Padding='{TemplateBinding Padding}'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' RecognizesAccessKey='True'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='Bd' Property='Background' Value='{DynamicResource Sh.Hover}'/>
              <Setter TargetName='Bd' Property='BorderBrush' Value='{DynamicResource Sh.Accent}'/>
            </Trigger>
            <Trigger Property='IsKeyboardFocused' Value='True'>
              <Setter TargetName='Bd' Property='BorderBrush' Value='{DynamicResource Sh.Accent}'/>
            </Trigger>
            <Trigger Property='IsPressed' Value='True'>
              <Setter TargetName='Bd' Property='Background' Value='{DynamicResource Sh.Selection}'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter Property='Opacity' Value='0.45'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Sh.Primary' TargetType='Button' BasedOn='{StaticResource {x:Type Button}}'>
    <Setter Property='Foreground' Value='{DynamicResource Sh.OnAccent}'/>
    <Setter Property='Background' Value='{DynamicResource Sh.Accent}'/>
    <Setter Property='BorderBrush' Value='{DynamicResource Sh.Accent}'/>
    <Setter Property='FontWeight' Value='SemiBold'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='Bd' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='6' Padding='{TemplateBinding Padding}'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' RecognizesAccessKey='True'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='Bd' Property='Background' Value='{DynamicResource Sh.AccentHover}'/>
              <Setter TargetName='Bd' Property='BorderBrush' Value='{DynamicResource Sh.AccentHover}'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter Property='Opacity' Value='0.45'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Sh.Chip' TargetType='RadioButton'>
    <Setter Property='Foreground' Value='{DynamicResource Sh.Text}'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Margin' Value='0,0,8,0'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='RadioButton'>
          <Border x:Name='Bd' Background='{DynamicResource Sh.Surface}' BorderBrush='{DynamicResource Sh.Border}' BorderThickness='1' CornerRadius='14' Padding='12,0' Height='28'>
            <ContentPresenter VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='Bd' Property='BorderBrush' Value='{DynamicResource Sh.Accent}'/>
            </Trigger>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='Bd' Property='Background' Value='{DynamicResource Sh.AccentSoft}'/>
              <Setter TargetName='Bd' Property='BorderBrush' Value='{DynamicResource Sh.Accent}'/>
              <Setter Property='Foreground' Value='{DynamicResource Sh.Accent}'/>
              <Setter Property='FontWeight' Value='SemiBold'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='TextBox'>
    <Setter Property='Foreground' Value='{DynamicResource Sh.Text}'/>
    <Setter Property='Background' Value='{DynamicResource Sh.Input}'/>
    <Setter Property='BorderBrush' Value='{DynamicResource Sh.Border}'/>
    <Setter Property='CaretBrush' Value='{DynamicResource Sh.Text}'/>
    <Setter Property='SelectionBrush' Value='{DynamicResource Sh.Accent}'/>
    <Setter Property='Height' Value='32'/>
    <Setter Property='Padding' Value='6,0'/>
    <Setter Property='VerticalContentAlignment' Value='Center'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='TextBox'>
          <Border x:Name='Bd' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='6'>
            <ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}' VerticalAlignment='Center' Focusable='False'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='Bd' Property='BorderBrush' Value='{DynamicResource Sh.Accent}'/>
            </Trigger>
            <Trigger Property='IsKeyboardFocused' Value='True'>
              <Setter TargetName='Bd' Property='BorderBrush' Value='{DynamicResource Sh.Accent}'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter Property='Opacity' Value='0.5'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='CheckBox'>
    <Setter Property='Foreground' Value='{DynamicResource Sh.Text}'/>
    <Setter Property='VerticalAlignment' Value='Center'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='CheckBox'>
          <StackPanel Orientation='Horizontal' Background='Transparent'>
            <Border x:Name='Box' Width='16' Height='16' CornerRadius='4' BorderThickness='1' Background='{DynamicResource Sh.Input}' BorderBrush='{DynamicResource Sh.Border}' VerticalAlignment='Center'>
              <Path x:Name='Mark' Data='M3,8 L6.5,11.5 L13,4.5' Stroke='{DynamicResource Sh.OnAccent}' StrokeThickness='2' Visibility='Collapsed'/>
            </Border>
            <ContentPresenter Margin='8,0,0,0' VerticalAlignment='Center'/>
          </StackPanel>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='Box' Property='BorderBrush' Value='{DynamicResource Sh.Accent}'/>
            </Trigger>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='Box' Property='Background' Value='{DynamicResource Sh.Accent}'/>
              <Setter TargetName='Box' Property='BorderBrush' Value='{DynamicResource Sh.Accent}'/>
              <Setter TargetName='Mark' Property='Visibility' Value='Visible'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='ComboBoxItem'>
    <Setter Property='Foreground' Value='{DynamicResource Sh.Text}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ComboBoxItem'>
          <Border x:Name='Bd' Background='Transparent' Padding='8,5' CornerRadius='4' Margin='2,1'>
            <ContentPresenter/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsHighlighted' Value='True'>
              <Setter TargetName='Bd' Property='Background' Value='{DynamicResource Sh.Hover}'/>
            </Trigger>
            <Trigger Property='IsSelected' Value='True'>
              <Setter TargetName='Bd' Property='Background' Value='{DynamicResource Sh.AccentSoft}'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='ComboBox'>
    <Setter Property='Foreground' Value='{DynamicResource Sh.Text}'/>
    <Setter Property='Background' Value='{DynamicResource Sh.Input}'/>
    <Setter Property='BorderBrush' Value='{DynamicResource Sh.Border}'/>
    <Setter Property='Height' Value='32'/>
    <Setter Property='MaxDropDownHeight' Value='360'/>
    <Setter Property='VerticalContentAlignment' Value='Center'/>
    <Setter Property='ScrollViewer.HorizontalScrollBarVisibility' Value='Disabled'/>
    <Setter Property='ScrollViewer.CanContentScroll' Value='True'/>
    <Setter Property='ItemsPanel'>
      <Setter.Value>
        <ItemsPanelTemplate><VirtualizingStackPanel/></ItemsPanelTemplate>
      </Setter.Value>
    </Setter>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ComboBox'>
          <Grid>
            <ToggleButton x:Name='Toggle' Focusable='False' ClickMode='Press'
                          IsChecked='{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}'>
              <ToggleButton.Template>
                <ControlTemplate TargetType='ToggleButton'>
                  <Border x:Name='Bd' Background='{DynamicResource Sh.Input}' BorderBrush='{DynamicResource Sh.Border}' BorderThickness='1' CornerRadius='6'>
                    <Path HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,10,0' Data='M0,0 L4,4 L8,0'
                          Stroke='{DynamicResource Sh.Muted}' StrokeThickness='1.6'/>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property='IsMouseOver' Value='True'>
                      <Setter TargetName='Bd' Property='BorderBrush' Value='{DynamicResource Sh.Accent}'/>
                    </Trigger>
                    <Trigger Property='IsChecked' Value='True'>
                      <Setter TargetName='Bd' Property='BorderBrush' Value='{DynamicResource Sh.Accent}'/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </ToggleButton.Template>
            </ToggleButton>
            <ContentPresenter x:Name='Content' IsHitTestVisible='False' Margin='8,0,26,0' VerticalAlignment='Center'
                              Content='{TemplateBinding SelectionBoxItem}'
                              ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}'
                              ContentTemplateSelector='{TemplateBinding ItemTemplateSelector}'/>
            <TextBox x:Name='PART_EditableTextBox' Visibility='Hidden' Margin='4,0,26,0' VerticalAlignment='Center'
                     Background='Transparent' Foreground='{TemplateBinding Foreground}' CaretBrush='{DynamicResource Sh.Text}'
                     IsReadOnly='{Binding IsReadOnly, RelativeSource={RelativeSource TemplatedParent}}'>
              <TextBox.Template>
                <ControlTemplate TargetType='TextBox'>
                  <ScrollViewer x:Name='PART_ContentHost' Focusable='False' VerticalAlignment='Center'/>
                </ControlTemplate>
              </TextBox.Template>
            </TextBox>
            <Popup x:Name='PART_Popup' Placement='Bottom' IsOpen='{TemplateBinding IsDropDownOpen}' AllowsTransparency='True'
                   Focusable='False' PopupAnimation='Fade'>
              <Border Background='{DynamicResource Sh.Surface}' BorderBrush='{DynamicResource Sh.Border}' BorderThickness='1'
                      CornerRadius='6' Margin='0,2,0,0' Padding='0,2'
                      MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}'
                      MaxHeight='{TemplateBinding MaxDropDownHeight}'>
                <ScrollViewer SnapsToDevicePixels='True'>
                  <ItemsPresenter KeyboardNavigation.DirectionalNavigation='Contained'/>
                </ScrollViewer>
              </Border>
            </Popup>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsEditable' Value='True'>
              <Setter TargetName='PART_EditableTextBox' Property='Visibility' Value='Visible'/>
              <Setter TargetName='Content' Property='Visibility' Value='Hidden'/>
              <Setter Property='IsTabStop' Value='False'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter Property='Opacity' Value='0.5'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='ContextMenu'>
    <Setter Property='Foreground' Value='{DynamicResource Sh.Text}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ContextMenu'>
          <Border Background='{DynamicResource Sh.Surface}' BorderBrush='{DynamicResource Sh.Border}' BorderThickness='1' CornerRadius='6' Padding='4'>
            <StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Cycle'/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='MenuItem'>
    <Setter Property='Foreground' Value='{DynamicResource Sh.Text}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='MenuItem'>
          <Border x:Name='Bd' Background='Transparent' CornerRadius='4' Padding='8,6' MinWidth='220'>
            <Grid>
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width='22'/>
                <ColumnDefinition Width='*'/>
                <ColumnDefinition Width='Auto'/>
              </Grid.ColumnDefinitions>
              <Path x:Name='Check' Data='M1,6 L4.5,9.5 L11,2.5' Stroke='{DynamicResource Sh.Accent}' StrokeThickness='2' Visibility='Collapsed' VerticalAlignment='Center'/>
              <ContentPresenter Grid.Column='1' ContentSource='Header' RecognizesAccessKey='True' VerticalAlignment='Center'/>
              <TextBlock Grid.Column='2' Text='{TemplateBinding InputGestureText}' Margin='16,0,0,0' Foreground='{DynamicResource Sh.Muted}' VerticalAlignment='Center'/>
            </Grid>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsHighlighted' Value='True'>
              <Setter TargetName='Bd' Property='Background' Value='{DynamicResource Sh.Hover}'/>
            </Trigger>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='Check' Property='Visibility' Value='Visible'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter Property='Opacity' Value='0.45'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='Separator'>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Separator'>
          <Border Height='1' Margin='4,4' Background='{DynamicResource Sh.Border}'/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='ProgressBar'>
    <Setter Property='Height' Value='8'/>
    <Setter Property='Foreground' Value='{DynamicResource Sh.Accent}'/>
    <Setter Property='Background' Value='{DynamicResource Sh.Surface2}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ProgressBar'>
          <Grid>
            <Border x:Name='PART_Track' Background='{TemplateBinding Background}' CornerRadius='4'/>
            <Border x:Name='PART_Indicator' Background='{TemplateBinding Foreground}' CornerRadius='4' HorizontalAlignment='Left'/>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='Expander'>
    <Setter Property='Foreground' Value='{DynamicResource Sh.Text}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Expander'>
          <Border Background='{DynamicResource Sh.Surface}' BorderBrush='{DynamicResource Sh.Border}' BorderThickness='1' CornerRadius='6'>
            <DockPanel>
              <ToggleButton DockPanel.Dock='Top' Cursor='Hand' Focusable='False'
                            IsChecked='{Binding IsExpanded, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}'>
                <ToggleButton.Template>
                  <ControlTemplate TargetType='ToggleButton'>
                    <Border Background='Transparent' Padding='12,8'>
                      <StackPanel Orientation='Horizontal'>
                        <Path x:Name='Arrow' Data='M0,0 L4,4 L0,8' Stroke='{DynamicResource Sh.Muted}' StrokeThickness='1.6' VerticalAlignment='Center' Margin='0,0,8,0'/>
                        <ContentPresenter Content='{Binding Header, RelativeSource={RelativeSource AncestorType=Expander}}' VerticalAlignment='Center'/>
                      </StackPanel>
                    </Border>
                    <ControlTemplate.Triggers>
                      <Trigger Property='IsChecked' Value='True'>
                        <Setter TargetName='Arrow' Property='Data' Value='M0,0 L4,4 L8,0'/>
                      </Trigger>
                    </ControlTemplate.Triggers>
                  </ControlTemplate>
                </ToggleButton.Template>
              </ToggleButton>
              <ContentPresenter x:Name='Body' Margin='12,0,12,12' Visibility='Collapsed'/>
            </DockPanel>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsExpanded' Value='True'>
              <Setter TargetName='Body' Property='Visibility' Value='Visible'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='ScrollBar'>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Width' Value='12'/>
    <Setter Property='MinWidth' Value='12'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ScrollBar'>
          <Track x:Name='PART_Track' IsDirectionReversed='True'>
            <Track.Thumb>
              <Thumb>
                <Thumb.Template>
                  <ControlTemplate TargetType='Thumb'>
                    <Border Background='{DynamicResource Sh.Border}' CornerRadius='4' Margin='3'/>
                  </ControlTemplate>
                </Thumb.Template>
              </Thumb>
            </Track.Thumb>
          </Track>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
    <Style.Triggers>
      <Trigger Property='Orientation' Value='Horizontal'>
        <Setter Property='Width' Value='Auto'/>
        <Setter Property='MinWidth' Value='0'/>
        <Setter Property='Height' Value='12'/>
        <Setter Property='MinHeight' Value='12'/>
        <Setter Property='Template'>
          <Setter.Value>
            <ControlTemplate TargetType='ScrollBar'>
              <Track x:Name='PART_Track' IsDirectionReversed='False'>
                <Track.Thumb>
                  <Thumb>
                    <Thumb.Template>
                      <ControlTemplate TargetType='Thumb'>
                        <Border Background='{DynamicResource Sh.Border}' CornerRadius='4' Margin='3'/>
                      </ControlTemplate>
                    </Thumb.Template>
                  </Thumb>
                </Track.Thumb>
              </Track>
            </ControlTemplate>
          </Setter.Value>
        </Setter>
      </Trigger>
    </Style.Triggers>
  </Style>

  <Style TargetType='DataGrid'>
    <Setter Property='Foreground' Value='{DynamicResource Sh.Text}'/>
    <Setter Property='Background' Value='{DynamicResource Sh.Bg}'/>
    <Setter Property='RowBackground' Value='{DynamicResource Sh.Bg}'/>
    <Setter Property='AlternatingRowBackground' Value='{DynamicResource Sh.RowAlt}'/>
    <Setter Property='HorizontalGridLinesBrush' Value='{DynamicResource Sh.Border}'/>
    <Setter Property='VerticalGridLinesBrush' Value='Transparent'/>
    <Setter Property='BorderThickness' Value='0'/>
    <Setter Property='GridLinesVisibility' Value='Horizontal'/>
    <Setter Property='HeadersVisibility' Value='Column'/>
  </Style>

  <Style TargetType='DataGridColumnHeader'>
    <Setter Property='Foreground' Value='{DynamicResource Sh.Muted}'/>
    <Setter Property='FontWeight' Value='SemiBold'/>
    <Setter Property='FontSize' Value='11'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='DataGridColumnHeader'>
          <Grid>
            <Border Background='{DynamicResource Sh.Surface}' BorderBrush='{DynamicResource Sh.Border}' BorderThickness='0,0,1,1' Padding='8,8'>
              <ContentPresenter VerticalAlignment='Center'/>
            </Border>
            <Thumb x:Name='PART_LeftHeaderGripper' HorizontalAlignment='Left' Width='6' Cursor='SizeWE'>
              <Thumb.Template><ControlTemplate TargetType='Thumb'><Border Background='Transparent'/></ControlTemplate></Thumb.Template>
            </Thumb>
            <Thumb x:Name='PART_RightHeaderGripper' HorizontalAlignment='Right' Width='6' Cursor='SizeWE'>
              <Thumb.Template><ControlTemplate TargetType='Thumb'><Border Background='Transparent'/></ControlTemplate></Thumb.Template>
            </Thumb>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='DataGridCell'>
    <Setter Property='BorderThickness' Value='0'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='DataGridCell'>
          <Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
                  BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='4' Padding='6,0' Margin='1,1'>
            <ContentPresenter VerticalAlignment='Center'/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
    <Style.Triggers>
      <Trigger Property='IsSelected' Value='True'>
        <Setter Property='Background' Value='Transparent'/>
        <Setter Property='Foreground' Value='{DynamicResource Sh.Text}'/>
      </Trigger>
    </Style.Triggers>
  </Style>

</ResourceDictionary>";
    }
}
