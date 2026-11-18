using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Markup;
using System.Windows.Shapes;

namespace Akass.Optimizer;

internal sealed class Palette
{
    public Brush Canvas { get; }
    public Brush Side { get; }
    public Brush Card { get; }
    public Brush Line { get; }
    public Brush Text { get; }
    public Brush Muted { get; }
    public Brush Accent { get; }
    public Brush AccentFill { get; }
    public Brush AccentInk { get; }
    public Brush AccentBorder { get; }
    public Brush Selected { get; }
    public Brush Blue { get; }
    public Brush Warning { get; }

    public Palette(bool light)
    {
        Canvas = Color(light ? "#EDF2F7" : "#0C1322");
        Side = Color(light ? "#F7FAFC" : "#101827");
        Card = Color(light ? "#FFFFFF" : "#141F31");
        Line = Color(light ? "#CDD9E5" : "#26354A");
        Text = Color(light ? "#10233D" : "#EFF5FB");
        Muted = Color(light ? "#49617A" : "#A3B5CA");
        Accent = Color(light ? "#006D5B" : "#50E4C3");
        AccentFill = Color(light ? "#087D69" : "#4AE1BD");
        AccentInk = Color(light ? "#FFFFFF" : "#071F1A");
        AccentBorder = Color(light ? "#78BAAD" : "#296355");
        Selected = Color(light ? "#DCF3ED" : "#143832");
        Blue = Color(light ? "#106DA4" : "#70BFFA");
        Warning = Color(light ? "#8F4A00" : "#FFD08C");
    }

    private static Brush Color(string value)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromInvariantString(value)!;
        brush.Freeze();
        return brush;
    }

    public ResourceDictionary Resources()
    {
        var items = new ResourceDictionary();
        var button = new Style(typeof(Button));
        button.Setters.Add(new Setter(Control.ForegroundProperty, Text));
        button.Setters.Add(new Setter(Control.BackgroundProperty, Card));
        button.Setters.Add(new Setter(Control.BorderBrushProperty, Line));
        button.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        button.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        var template = new ControlTemplate(typeof(Button));
        var frame = new FrameworkElementFactory(typeof(Border));
        frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        frame.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        frame.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        frame.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        frame.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, new TemplateBindingExtension(Control.VerticalContentAlignmentProperty));
        presenter.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
        frame.AppendChild(presenter);
        template.VisualTree = frame;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(UIElement.OpacityProperty, .87));
        template.Triggers.Add(hover);
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(Control.BorderBrushProperty, Accent));
        focus.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(2)));
        template.Triggers.Add(focus);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .45));
        template.Triggers.Add(disabled);
        button.Setters.Add(new Setter(Control.TemplateProperty, template));
        items.Add(typeof(Button), button);
        if (!SystemParameters.HighContrast) items.Add(typeof(ScrollBar), ScrollBarStyle());
        return items;
    }

    private Style ScrollBarStyle() => (Style)XamlReader.Parse($@"
<Style xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
       xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" TargetType=""ScrollBar"">
  <Setter Property=""Background"" Value=""{Canvas}""/>
  <Setter Property=""Focusable"" Value=""False""/>
  <Setter Property=""Width"" Value=""13""/>
  <Setter Property=""Template"">
    <Setter.Value>
      <ControlTemplate TargetType=""ScrollBar"">
        <Grid Background=""{{TemplateBinding Background}}"" SnapsToDevicePixels=""True"">
          <Border Background=""{Line}"" CornerRadius=""4"" Margin=""3,2""/>
          <Track x:Name=""PART_Track"" Margin=""2"" IsDirectionReversed=""True""
                 Orientation=""{{TemplateBinding Orientation}}"">
            <Track.DecreaseRepeatButton>
              <RepeatButton x:Name=""Prev"" Command=""ScrollBar.PageUpCommand"" Focusable=""False"">
                <RepeatButton.Template><ControlTemplate><Border Background=""Transparent""/></ControlTemplate></RepeatButton.Template>
              </RepeatButton>
            </Track.DecreaseRepeatButton>
            <Track.Thumb>
              <Thumb x:Name=""Handle"" MinWidth=""8"" MinHeight=""24"" Background=""{Muted}"">
                <Thumb.Template>
                  <ControlTemplate TargetType=""Thumb"">
                    <Border CornerRadius=""4"" Background=""{{TemplateBinding Background}}""/>
                  </ControlTemplate>
                </Thumb.Template>
              </Thumb>
            </Track.Thumb>
            <Track.IncreaseRepeatButton>
              <RepeatButton x:Name=""Next"" Command=""ScrollBar.PageDownCommand"" Focusable=""False"">
                <RepeatButton.Template><ControlTemplate><Border Background=""Transparent""/></ControlTemplate></RepeatButton.Template>
              </RepeatButton>
            </Track.IncreaseRepeatButton>
          </Track>
        </Grid>
        <ControlTemplate.Triggers>
          <Trigger Property=""Orientation"" Value=""Horizontal"">
            <Setter TargetName=""PART_Track"" Property=""IsDirectionReversed"" Value=""False""/>
            <Setter TargetName=""Prev"" Property=""Command"" Value=""ScrollBar.PageLeftCommand""/>
            <Setter TargetName=""Next"" Property=""Command"" Value=""ScrollBar.PageRightCommand""/>
            <Setter TargetName=""Handle"" Property=""MinHeight"" Value=""8""/>
            <Setter TargetName=""Handle"" Property=""MinWidth"" Value=""24""/>
          </Trigger>
          <Trigger SourceName=""Handle"" Property=""IsMouseOver"" Value=""True"">
            <Setter TargetName=""Handle"" Property=""Background"" Value=""{Accent}""/>
          </Trigger>
          <Trigger SourceName=""Handle"" Property=""IsDragging"" Value=""True"">
            <Setter TargetName=""Handle"" Property=""Background"" Value=""{AccentFill}""/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
  <Style.Triggers>
    <Trigger Property=""Orientation"" Value=""Horizontal"">
      <Setter Property=""Width"" Value=""Auto""/>
      <Setter Property=""Height"" Value=""13""/>
    </Trigger>
  </Style.Triggers>
</Style>");
}

internal sealed partial class MainWindow
{
    private TextBlock Text(string value, double size = 14, bool emph = false, Brush? color = null) => new TextBlock
    {
        Text = value, FontSize = size, FontWeight = emph ? FontWeights.SemiBold : FontWeights.Normal,
        TextWrapping = TextWrapping.Wrap, Foreground = color ?? theme.Text
    };

    private TextBlock Muted(string value)
    {
        var text = Text(value, 13, false, theme.Muted);
        text.LineHeight = 20;
        return text;
    }

    private StackPanel Body() => new StackPanel();

    private Border Card(UIElement inner, Thickness? margin = null) => new Border
    {
        Background = theme.Card, BorderBrush = theme.Line, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12), Padding = new Thickness(17), Child = inner,
        Margin = margin ?? new Thickness(0, 0, 0, 13)
    };

    private Border Section(string label, params UIElement[] items)
    {
        var group = new StackPanel();
        var head = Text(label, 16, true);
        head.Margin = new Thickness(0, 0, 0, 10);
        group.Children.Add(head);
        foreach (var item in items) group.Children.Add(item);
        return Card(group);
    }

    private TextBlock Intro(string value)
    {
        var description = Muted(value);
        description.Margin = new Thickness(0, 0, 0, 16);
        return description;
    }

    private WrapPanel Actions(params UIElement[] items)
    {
        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var element in items) actions.Children.Add(element);
        return actions;
    }

    private Button ActionButton(string label, Action action, bool main = false)
    {
        var btn = MakeButton(label, main);
        btn.Click += (_, __) =>
        {
            try { action(); }
            catch (Exception e) { ShowError(e); }
        };
        return btn;
    }

    private Button ActionButton(string label, Func<Task> action, bool main = false)
    {
        var btn = MakeButton(label, main);
        btn.Click += async (_, __) =>
        {
            try
            {
                if (model.Busy) { Status(L("An operation is already running.", "هناك عملية جارية.", "Bir işlem zaten çalışıyor.")); return; }
                btn.IsEnabled = false;
                await action();
            }
            catch (Exception e) { ShowError(e); }
            finally { btn.IsEnabled = true; }
        };
        return btn;
    }

    private Button MakeButton(string label, bool primary)
    {
        var button = new Button { Content = label, Padding = new Thickness(13, 7, 13, 7), Margin = new Thickness(0, 0, 8, 6),
            Cursor = System.Windows.Input.Cursors.Hand, HorizontalContentAlignment = HorizontalAlignment.Center };
        if (primary) { button.Background = theme.AccentFill; button.Foreground = theme.AccentInk; button.BorderBrush = theme.AccentFill; }
        return button;
    }

    private CheckBox Check(string label, bool selected = false)
    {
        var checkbox = new CheckBox { Content = label, IsChecked = selected, Foreground = theme.Text,
            Margin = new Thickness(0, 4, 10, 7), VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(checkbox, label);
        return checkbox;
    }

    private TextBox Input(string contents, string label, double width = 200)
    {
        var input = new TextBox { Text = contents, MinWidth = width, Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(0, 0, 8, 6), BorderBrush = theme.Line, Foreground = theme.Text,
            Background = theme.Canvas, CaretBrush = theme.Accent, VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(input, label);
        return input;
    }

    private ComboBox Choice(IEnumerable<string> values, int index, string label)
    {
        var box = new ComboBox { ItemsSource = values, SelectedIndex = index, MinWidth = 158, Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 8, 6) };
        AutomationProperties.SetName(box, label);
        return box;
    }

    private DataGrid Table(params (string Heading, string Binding, double Width)[] columns)
    {
        var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false,
            CanUserDeleteRows = false, CanUserReorderColumns = false, SelectionMode = DataGridSelectionMode.Single,
            HeadersVisibility = DataGridHeadersVisibility.Column, MaxHeight = 375, MinHeight = 115, RowHeight = 30,
            EnableRowVirtualization = true, EnableColumnVirtualization = true, Background = theme.Canvas,
            RowBackground = theme.Canvas, AlternatingRowBackground = theme.Side, Foreground = theme.Text,
            BorderBrush = theme.Line, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = theme.Line, ColumnHeaderHeight = 32, AlternationCount = 2 };
        grid.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        var columnStyle = new Style(typeof(DataGridColumnHeader));
        columnStyle.Setters.Add(new Setter(Control.BackgroundProperty, theme.Card));
        columnStyle.Setters.Add(new Setter(Control.ForegroundProperty, theme.Muted));
        columnStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 0, 8, 0)));
        grid.ColumnHeaderStyle = columnStyle;
        foreach (var col in columns)
        {
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(7, 0, 5, 0)));
            grid.Columns.Add(new DataGridTextColumn { Header = col.Heading, Binding = new Binding(col.Binding),
                ElementStyle = style, Width = col.Width == 0 ? new DataGridLength(1, DataGridLengthUnitType.Star) :
                new DataGridLength(col.Width), MinWidth = 55, CanUserSort = true });
        }
        return grid;
    }

    private string Size(ulong? size) => size.HasValue ? Policy.Bytes(size.Value) : "—";
    private string Rate(double? value) => value.HasValue && value.Value >= 0
        ? Policy.Bytes((ulong)value.Value) + "/s" : "—";
    private static string Percent(double? value) => value.HasValue ? value.Value.ToString("0.#") + "%" : "—";
    private string YesNo(bool? value) => !value.HasValue ? words.Unknown :
        value == true ? L("Yes", "نعم", "Evet") : L("No", "لا", "Hayır");
}

internal sealed class HistoryChart : FrameworkElement
{
    private readonly Palette theme;
    private IReadOnlyList<(double? Cpu, double? Memory)> values = Array.Empty<(double?, double?)>();
    private readonly bool showMemory;
    private readonly bool showCpu;

    public HistoryChart(Palette theme, bool showCpu = true, bool showMemory = true)
    {
        this.theme = theme;
        this.showCpu = showCpu;
        this.showMemory = showMemory;
        Height = 158;
        ClipToBounds = true;
    }

    public void Update(IReadOnlyList<(double?, double?)> data)
    {
        values = data;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double width = ActualWidth, height = ActualHeight;
        if (width < 20 || height < 20) return;
        dc.DrawRoundedRectangle(theme.Canvas, null, new Rect(0, 0, width, height), 7, 7);
        for (int i = 1; i <= 4; i++)
        {
            double y = i * height / 5;
            dc.DrawLine(new Pen(theme.Line, 1), new Point(0, y), new Point(width, y));
        }
        if (showMemory) Draw(dc, values.Select(x => x.Memory), theme.Accent);
        if (showCpu) Draw(dc, values.Select(x => x.Cpu), theme.Blue);
    }

    private void Draw(DrawingContext dc, IEnumerable<double?> series, Brush brush)
    {
        var pen = new Pen(brush, 2);
        double step = Math.Max(1, (ActualWidth - 14) / Math.Max(24, values.Count - 1));
        double x = 7;
        Point? former = null;
        foreach (var value in series)
        {
            if (!value.HasValue) { former = null; x += step; continue; }
            var point = new Point(x, 7 + (ActualHeight - 14) * (1 - Math.Max(0, Math.Min(100, value.Value)) / 100));
            if (former.HasValue) dc.DrawLine(pen, former.Value, point);
            else dc.DrawEllipse(brush, null, point, 2.2, 2.2);
            former = point;
            x += step;
        }
    }
}
