using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace GameShelf
{
    [DataContract]
    public class Game
    {
        [DataMember] public string Id = Guid.NewGuid().ToString("N");
        [DataMember] public string Name = "";
        [DataMember] public string Executable = "";
        [DataMember] public string TrackingExecutable = "";
        [DataMember] public string Icon = "";
        [DataMember] public string Description = "";
        [DataMember] public string Review = "";
        [DataMember] public int Rating;
        [DataMember] public bool Favorite;
        [DataMember] public double Seconds;
        [DataMember] public string LastPlayedUtc;
        [DataMember] public List<GameLink> Links = new List<GameLink>();
        [DataMember] public string Status = "未分类";
        [DataMember] public bool PosterFill;
        [DataMember] public List<string> CompanionPrograms = new List<string>();
        public bool Running;
        public bool Tracking;
        public double SessionSeconds;
    }
    [DataContract]
    public class GameLink
    {
        [DataMember] public string Title;
        [DataMember] public string Url;
    }
    [DataContract]
    public class Library
    {
        [DataMember] public int Version = 1;
        [DataMember] public List<Game> Games = new List<Game>();
        [DataMember] public AppSettings Settings = new AppSettings();
    }
    [DataContract]
    public class AppSettings
    {
        [DataMember] public bool FocusOnly;
        [DataMember] public string AccentColor = "#5268DD";
        [DataMember] public string BackgroundColor = "#F5F6FA";
        [DataMember] public string BackgroundImage = "";
        [DataMember] public double ImageOpacity = .3;
        public static bool ValidColor(string value)
        {
            return value != null && System.Text.RegularExpressions.Regex.IsMatch(value, "^#[0-9a-fA-F]{6}$");
        }
        public void Normalize()
        {
            if (!ValidColor(AccentColor)) AccentColor = "#5268DD";
            if (!ValidColor(BackgroundColor)) BackgroundColor = "#F5F6FA";
            if (double.IsNaN(ImageOpacity) || double.IsInfinity(ImageOpacity)) ImageOpacity = .3;
            ImageOpacity = Math.Max(0, Math.Min(1, ImageOpacity));
            BackgroundImage = BackgroundImage ?? "";
        }
    }
    public static class Texts
    {
        public static string Duration(double seconds)
        {
            if (seconds < 1) return "0 分钟";
            if (seconds < 60) return "不足 1 分钟";
            if (seconds < 3600) return Math.Floor(seconds / 60).ToString("0") + " 分钟";
            return (seconds / 3600).ToString("0.#", CultureInfo.InvariantCulture) + " 小时";
        }
        public static string Last(Game g)
        {
            if (g.Running) return g.Tracking ? "正在游玩" : "未聚焦 · 暂停计时";
            DateTime time;
            if (!DateTime.TryParse(g.LastPlayedUtc, null, DateTimeStyles.RoundtripKind, out time)) return "尚未游玩";
            var local = time.ToLocalTime();
            if (local.Date == DateTime.Today) return "今天 " + local.ToString("HH:mm");
            if (local.Date == DateTime.Today.AddDays(-1)) return "昨天 " + local.ToString("HH:mm");
            return local.ToString("yyyy/MM/dd HH:mm");
        }
        public static bool WebUrl(string value)
        {
            Uri uri;
            return Uri.TryCreate(value, UriKind.Absolute, out uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
                !string.IsNullOrWhiteSpace(uri.Host);
        }
        public static string NormalPath(string value) { return Path.GetFullPath(value).TrimEnd('\\'); }
        public static string TrackingPath(Game game) { return NormalPath(string.IsNullOrWhiteSpace(game.TrackingExecutable) ? game.Executable : game.TrackingExecutable); }
    }
    public class Store
    {
        public readonly string Root;
        public string FilePath { get { return Path.Combine(Root, "library.json"); } }
        public string RecoveryNotice;
        public Store(string root) { Root = root; Directory.CreateDirectory(root); }
        public Library Load()
        {
            if (!File.Exists(FilePath)) return new Library();
            try { return Read(FilePath); }
            catch (Exception first)
            {
                var preserved = FilePath + ".damaged-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Copy(FilePath, preserved, true);
                if (File.Exists(FilePath + ".bak"))
                {
                    try
                    {
                        var recovered = Read(FilePath + ".bak");
                        RecoveryNotice = "资料文件异常，已恢复上一份备份。原文件已保留在数据文件夹。";
                        return recovered;
                    }
                    catch { }
                }
                throw new IOException("无法读取游戏库，已保留原文件。请检查数据文件夹中的 library.json 和备份。\n" + first.Message);
            }
        }
        public static Library Read(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                var lib = (Library)new DataContractJsonSerializer(typeof(Library)).ReadObject(stream);
                if (lib == null || lib.Games == null || lib.Version != 1) throw new IOException("不支持的资料格式。");
                var ids = new HashSet<string>();
                lib.Settings = lib.Settings ?? new AppSettings();
                lib.Settings.Normalize();
                foreach (var g in lib.Games)
                {
                    if (g == null || string.IsNullOrEmpty(g.Id) || !ids.Add(g.Id) || string.IsNullOrWhiteSpace(g.Name) ||
                        string.IsNullOrWhiteSpace(g.Executable) || double.IsNaN(g.Seconds) || double.IsInfinity(g.Seconds) || g.Seconds < 0)
                        throw new IOException("游戏资料不完整。");
                    g.Links = g.Links ?? new List<GameLink>();
                    g.CompanionPrograms = g.CompanionPrograms ?? new List<string>();
                    g.TrackingExecutable = g.TrackingExecutable ?? "";
                    if (!new[] { "想玩", "在玩", "玩过" }.Contains(g.Status)) g.Status = "未分类";
                    g.Rating = Math.Max(0, Math.Min(10, g.Rating));
                }
                return lib;
            }
        }
        public void Save(Library library)
        {
            var temp = FilePath + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                new DataContractJsonSerializer(typeof(Library)).WriteObject(stream, library);
                stream.Flush(true);
            }
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak");
            else File.Move(temp, FilePath);
        }
    }
    public static class ProcessWatch
    {
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder text, ref int size);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        public static string ForegroundPath()
        {
            uint pid;
            GetWindowThreadProcessId(GetForegroundWindow(), out pid);
            IntPtr handle = OpenProcess(0x1000, false, (int)pid);
            if (handle == IntPtr.Zero) return "";
            try
            {
                int size = 32768; var path = new StringBuilder(size);
                return QueryFullProcessImageName(handle, 0, path, ref size) ? Texts.NormalPath(path.ToString()) : "";
            }
            catch { return ""; }
            finally { CloseHandle(handle); }
        }
        public static HashSet<string> RunningPaths(IEnumerable<Game> games)
        {
            var names = new HashSet<string>(games.Select(g => Path.GetFileNameWithoutExtension(Texts.TrackingPath(g))), StringComparer.OrdinalIgnoreCase);
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in names)
            {
                Process[] processes;
                try { processes = Process.GetProcessesByName(name); } catch { continue; }
                foreach (var p in processes)
                {
                    using (p)
                    {
                        IntPtr handle = IntPtr.Zero;
                        try
                        {
                            handle = OpenProcess(0x1000, false, p.Id);
                            if (handle == IntPtr.Zero) continue;
                            int size = 32768;
                            var path = new StringBuilder(size);
                            if (QueryFullProcessImageName(handle, 0, path, ref size)) result.Add(Texts.NormalPath(path.ToString()));
                        }
                        catch { }
                        finally { if (handle != IntPtr.Zero) CloseHandle(handle); }
                    }
                }
            }
            return result;
        }
        public static bool Apply(IEnumerable<Game> games, HashSet<string> paths, double elapsed, DateTime utcNow, bool focusOnly = false, string foreground = "")
        {
            bool changed = false;
            // Ignore sleep, hibernation and long stalled ticks instead of inventing playtime.
            double delta = elapsed >= 0 && elapsed <= 6 ? elapsed : 0;
            foreach (var g in games)
            {
                bool running = paths.Contains(Texts.TrackingPath(g));
                bool tracking = running && (!focusOnly || string.Equals(Texts.TrackingPath(g), foreground, StringComparison.OrdinalIgnoreCase));
                if (focusOnly ? g.Tracking && tracking : g.Running)
                {
                    g.Seconds += delta;
                    g.SessionSeconds += delta;
                }
                if (running && !g.Running) g.SessionSeconds = 0;
                if (running || g.Running) g.LastPlayedUtc = utcNow.ToString("o");
                if (running != g.Running || tracking != g.Tracking) changed = true;
                g.Running = running;
                g.Tracking = tracking;
            }
            return changed;
        }
    }
    public static class Design
    {
        public static Brush Ink = Brush("#202534"), Muted = Brush("#747B8C"), Accent = Brush("#5268DD"), Line = Brush("#E4E7EE");
        public static Brush Surface = Brushes.White, Base = Brush("#F5F6FA"), Side = Brush("#EEF0F7"), SoftAccent = Brush("#EEF1FF"), AccentInk = Brushes.White;
        public static bool Dark;
        public static Color Mix(Color a, Color b, double amount)
        {
            return Color.FromRgb((byte)(a.R + (b.R - a.R) * amount), (byte)(a.G + (b.G - a.G) * amount), (byte)(a.B + (b.B - a.B) * amount));
        }
        public static void Configure(Application app, AppSettings settings)
        {
            settings.Normalize();
            Color bg = ((SolidColorBrush)Brush(settings.BackgroundColor)).Color;
            Color accent = ((SolidColorBrush)Brush(settings.AccentColor)).Color;
            Dark = .2126 * bg.R + .7152 * bg.G + .0722 * bg.B < 125;
            Base = new SolidColorBrush(bg); Accent = new SolidColorBrush(accent);
            Surface = new SolidColorBrush(Mix(bg, Colors.White, Dark ? .08 : .83));
            Side = new SolidColorBrush(Mix(bg, Dark ? Colors.Black : accent, Dark ? .12 : .05));
            Ink = Brush(Dark ? "#F2F4FA" : "#202534"); Muted = Brush(Dark ? "#BBC1D0" : "#687083");
            Line = new SolidColorBrush(Mix(bg, Dark ? Colors.White : Colors.Black, Dark ? .20 : .12));
            SoftAccent = new SolidColorBrush(Mix(((SolidColorBrush)Surface).Color, accent, .14));
            AccentInk = Brush(.2126 * accent.R + .7152 * accent.G + .0722 * accent.B > 160 ? "#151A24" : "#FFFFFF");
            Styles(app);
        }
        public static string Hex(Brush brush) { return ((SolidColorBrush)brush).Color.ToString(); }
        public static Brush Brush(string color) { return (Brush)new BrushConverter().ConvertFromString(color); }
        public static TextBlock Text(string text, double size, Brush color = null, bool bold = false)
        {
            return new TextBlock { Text = text, FontSize = size, Foreground = color ?? Ink, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap };
        }
        public static TextBlock Glyph(string glyph, double size = 18)
        {
            return new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = size, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        }
        public static Button Button(string text, Action action, bool primary = false)
        {
            var b = new Button { Content = text, Padding = new Thickness(16, 10, 16, 10), Cursor = Cursors.Hand };
            if (primary) b.Style = (Style)Application.Current.FindResource("PrimaryButton");
            b.Click += delegate { action(); };
            return b;
        }
        public static Border Panel(UIElement content, Thickness padding)
        {
            return new Border { Background = Surface, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = padding, Child = content };
        }
        public static void Styles(Application app)
        {
            const string xaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
 <Style TargetType='Window'><Setter Property='FontFamily' Value='Segoe UI, Microsoft YaHei UI'/><Setter Property='FontSize' Value='14'/><Setter Property='Foreground' Value='#202534'/><Setter Property='Background' Value='#F5F6FA'/></Style>
 <Style TargetType='Button'>
  <Setter Property='Background' Value='White'/><Setter Property='Foreground' Value='#33394B'/><Setter Property='BorderBrush' Value='#DFE3ED'/><Setter Property='BorderThickness' Value='1'/><Setter Property='Padding' Value='14,9'/><Setter Property='HorizontalContentAlignment' Value='Center'/><Setter Property='VerticalContentAlignment' Value='Center'/><Setter Property='Cursor' Value='Hand'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
   <Border x:Name='Bd' CornerRadius='8' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='{TemplateBinding VerticalContentAlignment}'/></Border>
   <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Bd' Property='Opacity' Value='0.78'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='Bd' Property='Opacity' Value='0.6'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Bd' Property='BorderBrush' Value='#5268DD'/><Setter TargetName='Bd' Property='BorderThickness' Value='2'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter TargetName='Bd' Property='Opacity' Value='0.45'/></Trigger></ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style x:Key='PrimaryButton' TargetType='Button' BasedOn='{StaticResource {x:Type Button}}'><Setter Property='Background' Value='#5268DD'/><Setter Property='Foreground' Value='White'/><Setter Property='BorderBrush' Value='#5268DD'/></Style>
 <Style TargetType='TextBox'><Setter Property='Padding' Value='11,9'/><Setter Property='Background' Value='White'/><Setter Property='BorderBrush' Value='#DDE1EB'/><Setter Property='BorderThickness' Value='1'/><Setter Property='VerticalContentAlignment' Value='Center'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='TextBox'><Border x:Name='Bd' CornerRadius='7' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'><ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}'/></Border><ControlTemplate.Triggers><Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='Bd' Property='BorderBrush' Value='#5268DD'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
 <Style TargetType='ComboBoxItem'>
  <Setter Property='Padding' Value='12,9'/><Setter Property='HorizontalContentAlignment' Value='Stretch'/><Setter Property='Foreground' Value='#202534'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBoxItem'><Border x:Name='Item' Background='Transparent' CornerRadius='6' Padding='{TemplateBinding Padding}' Margin='3,1'><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property='IsHighlighted' Value='True'><Setter TargetName='Item' Property='Background' Value='#EEF1FF'/></Trigger><Trigger Property='IsSelected' Value='True'><Setter TargetName='Item' Property='Background' Value='#EEF1FF'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='ComboBox'><Setter Property='MinHeight' Value='40'/><Setter Property='Foreground' Value='#202534'/><Setter Property='Background' Value='White'/><Setter Property='BorderBrush' Value='#DFE3ED'/><Setter Property='ScrollViewer.HorizontalScrollBarVisibility' Value='Disabled'/><Setter Property='Template'><Setter.Value>
  <ControlTemplate TargetType='ComboBox'><Grid>
   <ToggleButton Focusable='False' ClickMode='Press' IsChecked='{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}'>
    <ToggleButton.Template><ControlTemplate TargetType='ToggleButton'><Border x:Name='ToggleBorder' Background='White' BorderBrush='#DFE3ED' BorderThickness='1' CornerRadius='8'><Grid><TextBlock Text='&#xE70D;' FontFamily='Segoe MDL2 Assets' FontSize='11' Foreground='#747B8C' HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,12,0'/></Grid></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='ToggleBorder' Property='BorderBrush' Value='#5268DD'/></Trigger></ControlTemplate.Triggers></ControlTemplate></ToggleButton.Template>
   </ToggleButton>
   <ContentPresenter IsHitTestVisible='False' Content='{TemplateBinding SelectionBoxItem}' ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}' VerticalAlignment='Center' Margin='12,7,34,7'/>
   <Popup x:Name='PART_Popup' Placement='Bottom' IsOpen='{TemplateBinding IsDropDownOpen}' AllowsTransparency='True' Focusable='False' PopupAnimation='Fade'>
    <Border MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}' Background='White' BorderBrush='#DFE3ED' BorderThickness='1' CornerRadius='9' Margin='0,5,0,4' Padding='3'><ScrollViewer MaxHeight='300' CanContentScroll='True' VerticalScrollBarVisibility='Auto'><ItemsPresenter KeyboardNavigation.DirectionalNavigation='Contained'/></ScrollViewer></Border>
   </Popup>
  </Grid></ControlTemplate>
 </Setter.Value></Setter></Style>
 <Style TargetType='ToolTip'><Setter Property='MaxWidth' Value='380'/></Style>
</ResourceDictionary>";
            var themed = xaml.Replace("#202534", Hex(Ink)).Replace("#33394B", Hex(Ink)).Replace("#747B8C", Hex(Muted))
                .Replace("#DFE3ED", Hex(Line)).Replace("#DDE1EB", Hex(Line)).Replace("#EEF1FF", Hex(SoftAccent))
                .Replace("#F5F6FA", Hex(Base)).Replace("#5268DD", Hex(Accent)).Replace("Value='White'", "Value='" + Hex(Surface) + "'")
                .Replace("Background='White'", "Background='" + Hex(Surface) + "'");
            themed = themed.Replace("<Setter Property='Foreground' Value='" + Hex(Surface) + "'/>", "<Setter Property='Foreground' Value='" + Hex(AccentInk) + "'/>");
            app.Resources = (ResourceDictionary)XamlReader.Parse(themed);
        }
        public static ImageSource Image(Game g, string dataRoot)
        {
            try
            {
                if (!string.IsNullOrEmpty(g.Icon))
                {
                    var path = Path.IsPathRooted(g.Icon) ? g.Icon : Path.Combine(dataRoot, g.Icon);
                    if (File.Exists(path)) return ReadImage(path);
                }
                if (File.Exists(g.Executable))
                {
                    using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(g.Executable))
                    {
                        if (icon != null)
                        {
                            var bitmap = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                            bitmap.Freeze();
                            return bitmap;
                        }
                    }
                }
            }
            catch { }
            return null;
        }
        public static ImageSource ReadImage(string path, int pixels = 1200)
        {
            var img = new BitmapImage();
            img.BeginInit(); img.CacheOption = BitmapCacheOption.OnLoad; img.UriSource = new Uri(Path.GetFullPath(path)); img.DecodePixelWidth = pixels; img.EndInit(); img.Freeze();
            return img;
        }
        public static Border GameIcon(Game g, string root, double size)
        {
            var source = Image(g, root);
            UIElement content = source == null ? (UIElement)Glyph("\uE7FC", size * .43) : new Image { Source = source, Width = g.PosterFill ? size : size * .62, Height = g.PosterFill ? size : size * .62, Stretch = g.PosterFill ? Stretch.UniformToFill : Stretch.Uniform };
            var border = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size * .22), Background = Surface, Child = content };
            border.Clip = new RectangleGeometry(new Rect(0, 0, size, size), size * .22, size * .22); return border;
        }
    }
    public class SettingsWindow : Window
    {
        public AppSettings Result;
        readonly Store store;
        string imagePath, originalImage;
        bool clearImage;
        TextBox accent, background;
        CheckBox focus;
        Slider opacity;
        Border preview;
        TextBlock imageLabel, previewTitle, previewHint;
        public SettingsWindow(Window owner, Store data, AppSettings settings)
        {
            Owner = owner; store = data; originalImage = settings.BackgroundImage;
            Title = "设置 · GameShelf"; Width = 660; Height = 780; MinWidth = 540; MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Design.Base; Foreground = Design.Ink;
            FontFamily = owner.FontFamily; FontSize = 14;
            var root = new DockPanel { Margin = new Thickness(28) };
            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
            var cancel = Design.Button("取消", Close); cancel.IsCancel = true; footer.Children.Add(cancel);
            var save = Design.Button("保存设置", SaveSettings, true); save.Margin = new Thickness(10, 0, 0, 0); footer.Children.Add(save);
            DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
            var body = new StackPanel();
            body.Children.Add(Design.Text("设置", 27, null, true));
            AddLabel(body, "游玩计时");
            focus = new CheckBox { Content = "仅游戏窗口聚焦时计时", IsChecked = settings.FocusOnly, Foreground = Design.Ink, Margin = new Thickness(0, 8, 0, 8) }; body.Children.Add(focus);
            body.Children.Add(Design.Text("开启后，切换窗口或最小化游戏会暂停累计；回到游戏后恢复。\n关闭时，只要游戏程序仍在运行就会计时。", 12, Design.Muted));
            AddLabel(body, "界面颜色");
            var presets = new WrapPanel();
            string[] titles = { "鸢尾", "森林", "玫瑰", "琥珀", "深夜" };
            string[] colors = { "#5268DD", "#27816B", "#C24D78", "#AD7425", "#91A4FF" };
            string[] bases = { "#F5F6FA", "#F0F6F3", "#FAF2F5", "#F9F5ED", "#1C2030" };
            for (int i = 0; i < titles.Length; i++)
            {
                int n = i;
                var b = Design.Button(titles[n], () => { accent.Text = colors[n]; background.Text = bases[n]; });
                b.Margin = new Thickness(0, 0, 8, 8); presets.Children.Add(b);
            }
            body.Children.Add(presets);
            accent = ColorRow(body, "主题色", settings.AccentColor);
            background = ColorRow(body, "背景底色", settings.BackgroundColor);
            body.Children.Add(Design.Text("支持 #RRGGBB 颜色值，也可以打开调色板选择。深色底色会自动搭配深色卡片与浅色文字。", 12, Design.Muted));
            AddLabel(body, "界面背景图");
            var imageButtons = new WrapPanel();
            imageButtons.Children.Add(Design.Button("选择背景图片…", () =>
            {
                var dialog = new OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp", Title = "选择界面背景图片" };
                if (dialog.ShowDialog(this) == true)
                {
                    try { Design.ReadImage(dialog.FileName, 1920); imagePath = dialog.FileName; clearImage = false; imageLabel.Text = Path.GetFileName(imagePath); UpdatePreview(); }
                    catch (Exception ex) { MessageBox.Show(this, "图片无法读取：" + ex.Message, "背景图片"); }
                }
            }));
            var clear = Design.Button("移除背景图", () => { imagePath = null; clearImage = true; imageLabel.Text = "未设置背景图片"; UpdatePreview(); });
            clear.Margin = new Thickness(8, 0, 0, 0); imageButtons.Children.Add(clear); body.Children.Add(imageButtons);
            imageLabel = Design.Text(string.IsNullOrEmpty(originalImage) ? "未设置背景图片" : "已使用自定义背景图片", 12, Design.Muted); imageLabel.Margin = new Thickness(0, 8, 0, 12); body.Children.Add(imageLabel);
            body.Children.Add(Design.Text("背景图片可见度", 13, null, true));
            var sliderRow = new DockPanel();
            var percentage = Design.Text("", 12, Design.Muted); percentage.Width = 48; percentage.TextAlignment = TextAlignment.Right;
            DockPanel.SetDock(percentage, Dock.Right); sliderRow.Children.Add(percentage);
            opacity = new Slider { Minimum = 0, Maximum = 1, Value = settings.ImageOpacity, TickFrequency = .05, IsSnapToTickEnabled = true, Margin = new Thickness(0, 10, 12, 10) };
            sliderRow.Children.Add(opacity); body.Children.Add(sliderRow);
            percentage.Text = (opacity.Value * 100).ToString("0") + "%";
            var previewText = new StackPanel { Margin = new Thickness(22) };
            previewTitle = Design.Text("你的游戏时光", 22, null, true); previewHint = Design.Text("外观预览 · 保存后应用到整个界面", 12, Design.Muted); previewHint.Margin = new Thickness(0, 8, 0, 0);
            previewText.Children.Add(previewTitle); previewText.Children.Add(previewHint);
            preview = new Border { Height = 115, CornerRadius = new CornerRadius(12), BorderBrush = Design.Line, BorderThickness = new Thickness(1), Child = previewText, Margin = new Thickness(0, 10, 0, 0) }; body.Children.Add(preview);
            accent.TextChanged += delegate { UpdatePreview(); }; background.TextChanged += delegate { UpdatePreview(); };
            opacity.ValueChanged += delegate { percentage.Text = (opacity.Value * 100).ToString("0") + "%"; UpdatePreview(); };
            var reset = Design.Button("恢复默认外观", () => { accent.Text = "#5268DD"; background.Text = "#F5F6FA"; opacity.Value = .3; imagePath = null; clearImage = true; imageLabel.Text = "未设置背景图片"; UpdatePreview(); });
            reset.Margin = new Thickness(0, 16, 0, 0); reset.HorizontalAlignment = HorizontalAlignment.Left; body.Children.Add(reset);
            root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 10, 0) });
            Content = root; UpdatePreview();
        }
        static void AddLabel(Panel panel, string value)
        {
            var label = Design.Text(value, 16, null, true); label.Margin = new Thickness(0, 24, 0, 12); panel.Children.Add(label);
        }
        TextBox ColorRow(Panel body, string label, string value)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var title = Design.Text(label, 13); title.Width = 90; title.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(title);
            var box = new TextBox { Text = value, Width = 140, Height = 40, Padding = new Thickness(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Left };
            var choose = Design.Button("调色板…", () =>
            {
                using (var dialog = new Forms.ColorDialog { FullOpen = true })
                {
                    if (AppSettings.ValidColor(box.Text)) dialog.Color = System.Drawing.ColorTranslator.FromHtml(box.Text);
                    if (dialog.ShowDialog() == Forms.DialogResult.OK) box.Text = "#" + dialog.Color.R.ToString("X2") + dialog.Color.G.ToString("X2") + dialog.Color.B.ToString("X2");
                }
            });
            choose.Margin = new Thickness(12, 0, 0, 0); DockPanel.SetDock(choose, Dock.Right); row.Children.Add(choose); row.Children.Add(box); body.Children.Add(row); return box;
        }
        void UpdatePreview()
        {
            if (preview == null || !AppSettings.ValidColor(accent.Text) || !AppSettings.ValidColor(background.Text)) return;
            Color bg = ((SolidColorBrush)Design.Brush(background.Text)).Color;
            var draw = new DrawingGroup();
            draw.Children.Add(new GeometryDrawing(new SolidColorBrush(bg), null, new RectangleGeometry(new Rect(0, 0, 600, 120))));
            string path = clearImage ? null : imagePath ?? (string.IsNullOrEmpty(originalImage) ? null : Path.Combine(store.Root, originalImage));
            if (path != null && File.Exists(path))
            {
                var picture = new DrawingGroup { Opacity = opacity.Value };
                picture.Children.Add(new GeometryDrawing(new ImageBrush(Design.ReadImage(path, 1200)) { Stretch = Stretch.UniformToFill }, null, new RectangleGeometry(new Rect(0, 0, 600, 120))));
                draw.Children.Add(picture);
            }
            preview.Background = new DrawingBrush(draw);
            previewTitle.Foreground = Design.Brush(accent.Text);
            previewHint.Foreground = Design.Brush((bg.R + bg.G + bg.B) / 3 < 125 ? "#F2F4FA" : "#33394B");
        }
        void SaveSettings()
        {
            try
            {
                if (!AppSettings.ValidColor(accent.Text.Trim()) || !AppSettings.ValidColor(background.Text.Trim())) throw new Exception("颜色请填写 #RRGGBB，例如 #5268DD。");
                var result = new AppSettings { FocusOnly = focus.IsChecked == true, AccentColor = accent.Text.Trim(), BackgroundColor = background.Text.Trim(), ImageOpacity = opacity.Value, BackgroundImage = clearImage ? "" : originalImage };
                if (imagePath != null)
                {
                    Directory.CreateDirectory(Path.Combine(store.Root, "backgrounds"));
                    result.BackgroundImage = Path.Combine("backgrounds", Guid.NewGuid().ToString("N") + Path.GetExtension(imagePath));
                    File.Copy(imagePath, Path.Combine(store.Root, result.BackgroundImage));
                }
                Result = result; DialogResult = true;
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法保存设置"); }
        }
    }
    public class Editor : Window
    {
        public Game Result;
        readonly Store store;
        TextBox name, executable, trackingExecutable, intro, review, links, companions;
        ComboBox rating, gameStatus;
        CheckBox favorite, poster;
        string iconPath;
        bool resetIcon;
        TextBlock iconLabel;
        public Editor(Window owner, Store data, Game original, string initialExe)
        {
            Owner = owner; store = data; Title = original == null ? "添加游戏 · GameShelf" : "编辑游戏资料 · GameShelf";
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"); FontSize = 14; Background = Design.Base; Foreground = Design.Ink;
            Width = 680; Height = 770; MinWidth = 540; MinHeight = 500; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var outer = new DockPanel { Margin = new Thickness(28) };
            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            var cancel = Design.Button("取消", () => Close()); cancel.IsCancel = true; footer.Children.Add(cancel);
            var save = Design.Button(original == null ? "添加到游戏库" : "保存更改", () => Save(original), true); save.Margin = new Thickness(10, 0, 0, 0); footer.Children.Add(save);
            DockPanel.SetDock(footer, Dock.Bottom); outer.Children.Add(footer);
            var body = new StackPanel();
            body.Children.Add(Design.Text(original == null ? "新的冒险，从这里开始" : "让游戏库更像你", 24, null, true));
            var subtitle = Design.Text("游戏资料与笔记只保存在这台电脑上。", 13, Design.Muted); subtitle.Margin = new Thickness(0, 7, 0, 22); body.Children.Add(subtitle);
            name = Field(body, "游戏名称", original == null ? Path.GetFileNameWithoutExtension(initialExe) : original.Name, false);
            var pathRow = new DockPanel();
            var browse = Design.Button("浏览…", () => { var path = PickExe(this); if (path != null) executable.Text = path; });
            browse.Margin = new Thickness(8, 0, 0, 0); DockPanel.SetDock(browse, Dock.Right); pathRow.Children.Add(browse);
            executable = new TextBox { Text = original == null ? initialExe : original.Executable, Height = 42, Padding = new Thickness(10, 4, 10, 4) }; pathRow.Children.Add(executable);
            Label(body, "游戏程序"); body.Children.Add(pathRow);
            var pathHint = Design.Text("选择实际运行的 .exe。若启动器打开另一个进程，请选择游戏本体。", 12, Design.Muted);
            pathHint.Margin = new Thickness(0, 6, 0, 0); body.Children.Add(pathHint);
            Label(body, "计时进程（可选）");
            var trackingRow = new DockPanel();
            var trackingBrowse = Design.Button("浏览…", () => { var path = PickExe(this); if (path != null) trackingExecutable.Text = path; });
            trackingBrowse.Margin = new Thickness(8, 0, 0, 0); DockPanel.SetDock(trackingBrowse, Dock.Right); trackingRow.Children.Add(trackingBrowse);
            trackingExecutable = new TextBox { Text = original == null ? "" : original.TrackingExecutable, Height = 42, Padding = new Thickness(10, 4, 10, 4) }; trackingRow.Children.Add(trackingExecutable); body.Children.Add(trackingRow);
            var trackingHint = Design.Text("通常留空即可。如果汉化启动器很快退出，选择它实际打开的游戏本体，例如启动器目录下的游戏本体。启动游戏仍使用上面的程序。", 12, Design.Muted);
            trackingHint.Margin = new Thickness(0, 6, 0, 0); body.Children.Add(trackingHint);
            Label(body, "游戏图标");
            var iconRow = new StackPanel { Orientation = Orientation.Horizontal };
            iconLabel = Design.Text(original != null && !string.IsNullOrEmpty(original.Icon) ? "已使用自定义图标" : "使用程序原图标", 12, Design.Muted);
            var choose = Design.Button("选择图片…", () =>
            {
                var dialog = new OpenFileDialog { Filter = "图标与图片|*.png;*.jpg;*.jpeg;*.ico;*.bmp", Title = "选择自定义图标" };
                if (dialog.ShowDialog(this) == true)
                {
                    try { Design.ReadImage(dialog.FileName); iconPath = dialog.FileName; resetIcon = false; iconLabel.Text = Path.GetFileName(iconPath); }
                    catch { MessageBox.Show(this, "无法读取该图片，请选择 PNG、JPG、ICO 或 BMP 文件。", "图标"); }
                }
            });
            iconRow.Children.Add(choose);
            var reset = Design.Button("恢复程序图标", () => { resetIcon = true; iconPath = null; iconLabel.Text = "使用程序原图标"; }); reset.Margin = new Thickness(8, 0, 10, 0);
            iconRow.Children.Add(reset); iconLabel.VerticalAlignment = VerticalAlignment.Center; iconRow.Children.Add(iconLabel); body.Children.Add(iconRow);
            poster = new CheckBox { Content = "海报模式：图片铺满卡片图片区域", IsChecked = original != null && original.PosterFill, Foreground = Design.Ink, Margin = new Thickness(0, 13, 0, 0) }; body.Children.Add(poster);
            var posterHint = Design.Text("保持原始比例，居中裁切以铺满；关闭后恢复居中小图标。", 12, Design.Muted); posterHint.Margin = new Thickness(0, 6, 0, 0); body.Children.Add(posterHint);
            Label(body, "游戏状态");
            gameStatus = new ComboBox { Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
            foreach (var state in new[] { "未分类", "想玩", "在玩", "玩过" }) gameStatus.Items.Add(state);
            gameStatus.SelectedItem = original == null ? "未分类" : original.Status; body.Children.Add(gameStatus);
            Label(body, "我的评分");
            var ratingRow = new StackPanel { Orientation = Orientation.Horizontal };
            rating = new ComboBox { Width = 160 };
            rating.Items.Add("暂不评分"); for (int i = 1; i <= 10; i++) rating.Items.Add(i + " / 10");
            rating.SelectedIndex = original == null ? 0 : original.Rating; ratingRow.Children.Add(rating);
            favorite = new CheckBox { Content = "加入收藏", IsChecked = original != null && original.Favorite, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(22, 0, 0, 0) };
            ratingRow.Children.Add(favorite); body.Children.Add(ratingRow);
            companions = Field(body, "连带启动的程序 · 每行一个 .exe 完整路径", original == null ? "" : string.Join("\r\n", original.CompanionPrograms), true);
            var companionButton = Design.Button("＋  浏览并添加程序", () =>
            {
                var path = PickExe(this);
                if (path != null) companions.Text = companions.Text.TrimEnd() + (string.IsNullOrWhiteSpace(companions.Text) ? "" : "\r\n") + path;
            });
            companionButton.HorizontalAlignment = HorizontalAlignment.Left; companionButton.Margin = new Thickness(0, 8, 0, 7); body.Children.Add(companionButton);
            body.Children.Add(Design.Text("点击「开始游戏」时一并启动；外部启动游戏时不触发。已运行的辅助程序会跳过，辅助程序不会计入游玩时长。删除对应行即可取消连带启动。", 12, Design.Muted));
            intro = Field(body, "游戏简介", original == null ? "" : original.Description, true);
            review = Field(body, "我的评论", original == null ? "" : original.Review, true);
            links = Field(body, "相关链接 · 每行一个，格式为「名称 | 网址」",
                original == null ? "" : string.Join("\r\n", original.Links.Select(l => l.Title + " | " + l.Url)), true);
            links.ToolTip = "例如：Bangumi | https://bgm.tv/subject/123\nVNDB | https://vndb.org/v123";
            var help = Design.Text("例如：Bangumi | https://bgm.tv/subject/123\nVNDB | https://vndb.org/v123", 12, Design.Muted); help.Margin = new Thickness(0, 6, 0, 0); body.Children.Add(help);
            outer.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 10, 0) });
            Content = outer;
        }
        static void Label(Panel panel, string label)
        {
            var text = Design.Text(label, 13, null, true); text.Margin = new Thickness(0, 16, 0, 7); panel.Children.Add(text);
        }
        static TextBox Field(Panel panel, string label, string value, bool multiline)
        {
            Label(panel, label);
            var box = new TextBox { Text = value ?? "", AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap };
            if (multiline) { box.Height = 90; box.VerticalContentAlignment = VerticalAlignment.Top; box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; }
            else { box.Height = 42; box.Padding = new Thickness(10, 4, 10, 4); }
            panel.Children.Add(box); return box;
        }
        public static string PickExe(Window owner)
        {
            var dialog = new OpenFileDialog { Filter = "游戏程序 (*.exe)|*.exe", Title = "选择游戏程序", CheckFileExists = true };
            return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
        }
        void Save(Game original)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name.Text)) throw new Exception("请填写游戏名称。");
                string path = Texts.NormalPath(executable.Text.Trim().Trim('"'));
                bool sameExisting = original != null && string.Equals(path, original.Executable, StringComparison.OrdinalIgnoreCase);
                if (!sameExisting && (!File.Exists(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))) throw new Exception("请选择有效的 .exe 游戏程序。");
                var parsed = new List<GameLink>();
                foreach (var line in links.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    int split = line.IndexOf('|');
                    if (split < 1) throw new Exception("链接格式应为：名称 | https://网址");
                    string title = line.Substring(0, split).Trim(), url = line.Substring(split + 1).Trim();
                    if (title.Length == 0 || !Texts.WebUrl(url)) throw new Exception("链接需要名称和有效的 http / https 网址。");
                    parsed.Add(new GameLink { Title = title, Url = url });
                }
                var result = new Game { Name = name.Text.Trim(), Executable = path, Description = intro.Text.Trim(), Review = review.Text.Trim(), Links = parsed, Rating = Math.Max(0, rating.SelectedIndex), Favorite = favorite.IsChecked == true };
                if (!string.IsNullOrWhiteSpace(trackingExecutable.Text))
                {
                    result.TrackingExecutable = Texts.NormalPath(trackingExecutable.Text.Trim().Trim('"'));
                    bool unchangedTracker = original != null && string.Equals(original.TrackingExecutable, result.TrackingExecutable, StringComparison.OrdinalIgnoreCase);
                    if (!unchangedTracker && (!File.Exists(result.TrackingExecutable) || !result.TrackingExecutable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))) throw new Exception("请选择有效的计时进程 .exe。");
                }
                result.Status = gameStatus.SelectedItem as string ?? "未分类"; result.PosterFill = poster.IsChecked == true;
                foreach (string entry in companions.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (string.IsNullOrWhiteSpace(entry)) continue;
                    string helper = Texts.NormalPath(entry.Trim().Trim('"'));
                    bool existingHelper = original != null && original.CompanionPrograms.Contains(helper, StringComparer.OrdinalIgnoreCase);
                    if (!helper.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || (!File.Exists(helper) && !existingHelper)) throw new Exception("连带启动程序不存在或不是 .exe：\n" + helper);
                    if (string.Equals(helper, path, StringComparison.OrdinalIgnoreCase)) throw new Exception("连带启动列表不能包含游戏自身。");
                    if (!result.CompanionPrograms.Contains(helper, StringComparer.OrdinalIgnoreCase)) result.CompanionPrograms.Add(helper);
                }
                if (original != null)
                {
                    result.Id = original.Id; result.Seconds = original.Seconds; result.LastPlayedUtc = original.LastPlayedUtc;
                    bool sameTracker = string.Equals(Texts.TrackingPath(original), Texts.TrackingPath(result), StringComparison.OrdinalIgnoreCase);
                    result.Running = original.Running && sameTracker; result.Tracking = original.Tracking && sameTracker; result.SessionSeconds = original.SessionSeconds;
                    result.Icon = original.Icon;
                }
                if (resetIcon) result.Icon = "";
                if (iconPath != null)
                {
                    Directory.CreateDirectory(Path.Combine(store.Root, "icons"));
                    result.Icon = Path.Combine("icons", Guid.NewGuid().ToString("N") + Path.GetExtension(iconPath));
                    File.Copy(iconPath, Path.Combine(store.Root, result.Icon));
                }
                Result = result; DialogResult = true;
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "请检查游戏资料", MessageBoxButton.OK, MessageBoxImage.Information); }
        }
    }
    public class MainWindow : Window
    {
        readonly Store store;
        readonly Library library;
        readonly DispatcherTimer timer = new DispatcherTimer();
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Dictionary<string, TextBlock> cardTimes = new Dictionary<string, TextBlock>();
        readonly Dictionary<string, TextBlock> cardStates = new Dictionary<string, TextBlock>();
        readonly Dictionary<string, Button> categoryNavs = new Dictionary<string, Button>();
        readonly HashSet<string> expandedDescriptions = new HashSet<string>();
        Forms.NotifyIcon tray;
        StackPanel sidebarStats;
        ContentControl content;
        TextBlock status, totalValue, countValue, runningValue, detailTotal, detailLast, detailSession, detailState;
        Button libraryNav, favoriteNav, launchButton;
        TextBox search;
        WrapPanel cards;
        ComboBox sorting;
        Game selected;
        string filter = "all", query = "";
        int sortIndex, ticks;
        double previous;
        bool exitRequested, saveFailed, rebuilding;
        string saveError;
        public MainWindow(Store data, Library lib)
        {
            store = data; library = lib;
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"); FontSize = 14; Background = Design.Base; Foreground = Design.Ink;
            Title = "GameShelf · 游戏时光"; Width = 1180; Height = 800; MinWidth = 940; MinHeight = 620;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            BuildShell();
            ShowLibrary();
            tray = new Forms.NotifyIcon { Text = "GameShelf · 正在记录游戏时光", Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location), Visible = true };
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("打开游戏库", null, delegate { Dispatcher.Invoke(new Action(Restore)); });
            menu.Items.Add("退出并停止计时", null, delegate { Dispatcher.Invoke(new Action(Quit)); });
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { Dispatcher.Invoke(new Action(Restore)); };
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            {
                if (!exitRequested)
                {
                    e.Cancel = true; Save(); Hide();
                    tray.ShowBalloonTip(2500, "GameShelf 仍在记录", "双击托盘图标打开游戏库；右键图标可完全退出。", Forms.ToolTipIcon.Info);
                }
            };
            Closed += delegate { timer.Stop(); tray.Visible = false; tray.Dispose(); SystemEvents.PowerModeChanged -= PowerChanged; };
            SystemEvents.PowerModeChanged += PowerChanged;
            timer.Interval = TimeSpan.FromSeconds(2);
            timer.Tick += delegate { Tick(); }; timer.Start();
            Loaded += delegate { Tick(); if (store.RecoveryNotice != null) MessageBox.Show(this, store.RecoveryNotice, "已恢复备份"); };
            SourceInitialized += delegate
            {
                try { int rounded = 2; Native.DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 33, ref rounded, 4); } catch { }
            };
        }
        void PowerChanged(object sender, PowerModeChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                previous = clock.Elapsed.TotalSeconds;
                if (e.Mode == PowerModes.Suspend) { Save(); foreach (var g in library.Games) { g.Running = false; g.Tracking = false; } }
            }));
        }
        void BuildShell()
        {
            Background = Design.Base; Foreground = Design.Ink; categoryNavs.Clear();
            var shell = new Grid { Background = Design.Base };
            shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(224) });
            shell.ColumnDefinitions.Add(new ColumnDefinition());
            if (!string.IsNullOrEmpty(library.Settings.BackgroundImage))
            {
                try
                {
                    var wallpaper = new Image { Source = Design.ReadImage(Path.Combine(store.Root, library.Settings.BackgroundImage), 2560), Stretch = Stretch.UniformToFill, Opacity = library.Settings.ImageOpacity, IsHitTestVisible = false };
                    Grid.SetColumnSpan(wallpaper, 2); shell.Children.Add(wallpaper);
                }
                catch { /* Missing wallpaper must not prevent library access. */ }
            }
            var side = new DockPanel { Margin = new Thickness(20, 28, 18, 18) };
            var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 24) };
            var logo = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(11), Background = Design.Accent, Child = Design.Glyph("\uE7FC", 21) };
            ((TextBlock)logo.Child).Foreground = Design.AccentInk; brand.Children.Add(logo);
            var brandText = new StackPanel { Margin = new Thickness(10, -1, 0, 0) };
            brandText.Children.Add(Design.Text("GameShelf", 19, null, true)); brandText.Children.Add(Design.Text("你的游戏时光", 11, Design.Muted)); brand.Children.Add(brandText);
            DockPanel.SetDock(brand, Dock.Top); side.Children.Add(brand);
            var bottom = new StackPanel();
            sidebarStats = new StackPanel { Margin = new Thickness(10, 0, 0, 18) }; bottom.Children.Add(sidebarStats);
            var dataButton = Nav("\uE8B7", "数据文件夹", () => OpenPath(store.Root)); bottom.Children.Add(dataButton);
            bottom.Children.Add(Nav("\uE713", "设置", OpenSettings));
            bottom.Children.Add(Nav("\uE946", "使用说明", Help));
            bottom.Children.Add(Nav("\uE7E8", "退出工具", Quit));
            var version = Design.Text("GAMESHELF   /   1.1", 10, Design.Muted); version.Margin = new Thickness(12, 16, 0, 0); bottom.Children.Add(version);
            DockPanel.SetDock(bottom, Dock.Bottom); side.Children.Add(bottom);
            var navs = new StackPanel();
            var caption = Design.Text("我的空间", 11, Design.Muted, true); caption.Margin = new Thickness(12, 0, 0, 12); navs.Children.Add(caption);
            libraryNav = Nav("\uE8F1", "全部游戏", () => { filter = "all"; ShowLibrary(); }); navs.Children.Add(libraryNav);
            favoriteNav = Nav("\uE734", "我的收藏", () => { filter = "favorite"; ShowLibrary(); }); navs.Children.Add(favoriteNav);
            var categoryLabel = Design.Text("游玩状态", 11, Design.Muted, true); categoryLabel.Margin = new Thickness(12, 18, 0, 8); navs.Children.Add(categoryLabel);
            string[] stateNames = { "想玩", "在玩", "玩过", "未分类" }, stateGlyphs = { "\uE8A5", "\uE7FC", "\uE73E", "\uE8B7" };
            for (int i = 0; i < stateNames.Length; i++)
            {
                string stateName = stateNames[i];
                var nav = Nav(stateGlyphs[i], stateName, () => { filter = stateName; ShowLibrary(); }); navs.Children.Add(nav); categoryNavs[stateName] = nav;
            }
            var add = Design.Button("＋  添加游戏", AddGame); add.Margin = new Thickness(4, 16, 4, 5); navs.Children.Add(add);
            side.Children.Add(new ScrollViewer { Content = navs, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            var sidebar = new Border { Background = Design.Side, BorderBrush = Design.Line, BorderThickness = new Thickness(0, 0, 1, 0), Child = side };
            shell.Children.Add(sidebar);
            var main = new DockPanel();
            var statusBar = new Border { BorderBrush = Design.Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(28, 11, 28, 11), Background = Design.Surface };
            status = Design.Text("●  自动记录已开启 · 数据保存在本机", 11, Design.Muted); statusBar.Child = status;
            DockPanel.SetDock(statusBar, Dock.Bottom); main.Children.Add(statusBar);
            content = new ContentControl { Margin = new Thickness(32, 28, 32, 22), HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
            main.Children.Add(content); Grid.SetColumn(main, 1); shell.Children.Add(main); Content = shell;
        }
        Button Nav(string glyph, string title, Action action)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = Design.Glyph(glyph, 17); icon.Width = 24; row.Children.Add(icon);
            var text = Design.Text(title, 14); text.Margin = new Thickness(12, 0, 0, 0); text.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(text);
            var button = Design.Button("", action); button.Content = row; button.HorizontalContentAlignment = HorizontalAlignment.Left;
            System.Windows.Automation.AutomationProperties.SetName(button, title);
            button.Background = Brushes.Transparent; button.BorderThickness = new Thickness(0); button.Margin = new Thickness(0, 1, 0, 1); button.Padding = new Thickness(12, 9, 12, 9);
            return button;
        }
        void SetNav()
        {
            libraryNav.Background = filter == "all" ? Design.Surface : Brushes.Transparent;
            favoriteNav.Background = filter == "favorite" ? Design.Surface : Brushes.Transparent;
            foreach (var pair in categoryNavs) pair.Value.Background = filter == pair.Key ? Design.Surface : Brushes.Transparent;
        }
        void ShowLibrary()
        {
            selected = null; SetNav(); rebuilding = true;
            var root = new DockPanel();
            var top = new StackPanel();
            var heading = new DockPanel();
            var add = Design.Button("＋  添加游戏", AddGame, true); add.VerticalAlignment = VerticalAlignment.Top; DockPanel.SetDock(add, Dock.Right); heading.Children.Add(add);
            var headingText = new StackPanel();
            headingText.Children.Add(Design.Text(filter == "favorite" ? "我的收藏" : categoryNavs.ContainsKey(filter) ? filter : "游戏库", 29, null, true));
            var sub = Design.Text("每一段冒险，都值得被记住。", 13, Design.Muted); sub.Margin = new Thickness(0, 7, 0, 0); headingText.Children.Add(sub); heading.Children.Add(headingText); top.Children.Add(heading);
            var stats = new Grid { Margin = new Thickness(0, 26, 0, 25) };
            for (int i = 0; i < 3; i++) stats.ColumnDefinitions.Add(new ColumnDefinition());
            AddStat(stats, 0, "\uE8F1", "库中游戏", out countValue);
            AddStat(stats, 1, "\uE916", "累计游玩", out totalValue);
            AddStat(stats, 2, "\uE768", "正在游玩", out runningValue);
            top.Children.Add(stats);
            var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 17), Height = 42 };
            sorting = new ComboBox { Width = 142, SelectedIndex = sortIndex, Margin = new Thickness(12, 0, 0, 0) };
            sorting.Items.Add("最近游玩"); sorting.Items.Add("游玩时长"); sorting.Items.Add("游戏名称"); sorting.Items.Add("我的评分"); sorting.SelectedIndex = sortIndex;
            sorting.SelectionChanged += delegate { if (!rebuilding) { sortIndex = sorting.SelectedIndex; RenderCards(); } };
            DockPanel.SetDock(sorting, Dock.Right); toolbar.Children.Add(sorting);
            search = new TextBox { Text = query, ToolTip = "搜索游戏名称", MaxWidth = 440, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 240, Padding = new Thickness(10, 4, 10, 4) };
            System.Windows.Automation.AutomationProperties.SetName(search, "搜索游戏");
            var searchRow = new DockPanel();
            var searchIcon = Design.Glyph("\uE721", 16); searchIcon.Margin = new Thickness(0, 0, 10, 0); searchRow.Children.Add(searchIcon); searchRow.Children.Add(search);
            search.TextChanged += delegate { query = search.Text; if (!rebuilding) RenderCards(); }; toolbar.Children.Add(searchRow); top.Children.Add(toolbar);
            DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
            cards = new WrapPanel { Orientation = Orientation.Horizontal };
            var scroller = new ScrollViewer { Content = cards, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            scroller.SizeChanged += delegate { ResizeCards(scroller.ViewportWidth > 0 ? scroller.ViewportWidth : scroller.ActualWidth); };
            root.Children.Add(scroller); content.Content = root;
            rebuilding = false; RenderCards(); UpdateStats();
        }
        void AddStat(Grid grid, int col, string glyph, string label, out TextBlock value)
        {
            var row = new DockPanel();
            var icon = new Border { Width = 39, Height = 39, CornerRadius = new CornerRadius(11), Background = Design.SoftAccent, Child = Design.Glyph(glyph, 19), Margin = new Thickness(0, 0, 13, 0), VerticalAlignment = VerticalAlignment.Center };
            ((TextBlock)icon.Child).Foreground = Design.Accent; DockPanel.SetDock(icon, Dock.Left); row.Children.Add(icon);
            var texts = new StackPanel(); texts.Children.Add(Design.Text(label, 11, Design.Muted));
            value = Design.Text("0", 23, null, true); value.Margin = new Thickness(0, 3, 0, 0); texts.Children.Add(value); row.Children.Add(texts);
            var panel = Design.Panel(row, new Thickness(18)); panel.Margin = new Thickness(0, 0, col < 2 ? 12 : 0, 0); Grid.SetColumn(panel, col); grid.Children.Add(panel);
        }
        void ResizeCards(double width)
        {
            if (cards == null || width < 1) return;
            int columns = Math.Max(1, (int)(width / 228));
            double cardWidth = Math.Max(195, (width - 3) / columns - 14);
            foreach (var item in cards.Children.OfType<Button>()) item.Width = cardWidth;
        }
        void RenderCards()
        {
            if (selected != null || cards == null) return;
            cards.Children.Clear(); cardTimes.Clear(); cardStates.Clear();
            IEnumerable<Game> games = library.Games;
            if (filter == "favorite") games = games.Where(g => g.Favorite);
            if (categoryNavs.ContainsKey(filter)) games = games.Where(g => g.Status == filter);
            if (!string.IsNullOrWhiteSpace(query)) games = games.Where(g => g.Name.IndexOf(query.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0);
            if (sortIndex == 1) games = games.OrderByDescending(g => g.Seconds);
            else if (sortIndex == 2) games = games.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase);
            else if (sortIndex == 3) games = games.OrderByDescending(g => g.Rating);
            else games = games.OrderByDescending(g => g.Running).ThenByDescending(g => g.LastPlayedUtc ?? "");
            var list = games.ToList();
            if (list.Count == 0)
            {
                var empty = new StackPanel { Margin = new Thickness(28, 50, 28, 50), HorizontalAlignment = HorizontalAlignment.Center };
                var icon = new Border { Width = 82, Height = 82, CornerRadius = new CornerRadius(24), Background = Design.SoftAccent, Child = Design.Glyph("\uE7FC", 35) };
                ((TextBlock)icon.Child).Foreground = Design.Accent; empty.Children.Add(icon);
                var title = Design.Text(library.Games.Count == 0 ? "把喜欢的游戏，放进来" : "这里还没有游戏", 24, null, true); title.TextAlignment = TextAlignment.Center; title.Margin = new Thickness(0, 24, 0, 10); empty.Children.Add(title);
                var hint = Design.Text(library.Games.Count == 0 ? "添加一个游戏程序，让每一分钟的冒险都有迹可循。\n图标、简介、评分和评论，都可以由你定义。" : "试试其他分类，或换一个搜索关键词。", 14, Design.Muted);
                hint.TextAlignment = TextAlignment.Center; hint.LineHeight = 25; empty.Children.Add(hint);
                if (library.Games.Count == 0)
                {
                    var add = Design.Button("＋  添加第一个游戏", AddGame, true); add.HorizontalAlignment = HorizontalAlignment.Center; add.Margin = new Thickness(0, 25, 0, 0); empty.Children.Add(add);
                }
                cards.Children.Add(empty); return;
            }
            string[] backgrounds = { "#E9EDFD", "#E6F1EE", "#F1EAF7", "#F9EEE2", "#E5EFF6", "#F6E9EC" };
            foreach (var game in list)
            {
                var g = game;
                var layout = new StackPanel();
                int tone = (g.Id[0] + g.Id[g.Id.Length - 1]) % backgrounds.Length;
                var heroGrid = new Grid { Height = g.PosterFill ? 300 : 142, Background = Design.Dark ? Design.SoftAccent : Design.Brush(backgrounds[tone]), ClipToBounds = true };
                var posterImage = g.PosterFill ? Design.Image(g, store.Root) : null;
                if (posterImage != null) heroGrid.Children.Add(new Image { Source = posterImage, Stretch = Stretch.UniformToFill });
                else
                {
                    var icon = Design.GameIcon(g, store.Root, 76); icon.VerticalAlignment = VerticalAlignment.Center; icon.HorizontalAlignment = HorizontalAlignment.Center; heroGrid.Children.Add(icon);
                }
                if (g.Favorite)
                {
                    var star = Design.Glyph("\uE735", 14); star.Foreground = Design.Accent; star.VerticalAlignment = VerticalAlignment.Top; star.HorizontalAlignment = HorizontalAlignment.Right; star.Margin = new Thickness(12); heroGrid.Children.Add(star);
                }
                layout.Children.Add(heroGrid);
                var info = new StackPanel { Margin = new Thickness(17, 16, 17, 17) };
                var title = Design.Text(g.Name, 16, null, true); title.TextWrapping = TextWrapping.NoWrap; title.TextTrimming = TextTrimming.CharacterEllipsis; title.ToolTip = g.Name; info.Children.Add(title);
                var category = Design.Text(g.Status, 11, Design.Muted); category.Margin = new Thickness(0, 6, 0, 0); info.Children.Add(category);
                var state = Design.Text(Texts.Last(g), 11, g.Tracking ? Design.Accent : Design.Muted); state.Margin = new Thickness(0, 6, 0, 16); info.Children.Add(state); cardStates[g.Id] = state;
                var bottom = new DockPanel();
                var ratingText = Design.Text(g.Rating > 0 ? "★ " + g.Rating + "/10" : "未评分", 11, g.Rating > 0 ? Design.Accent : Design.Muted); DockPanel.SetDock(ratingText, Dock.Right); bottom.Children.Add(ratingText);
                var time = Design.Text(Texts.Duration(g.Seconds), 12, null, true); bottom.Children.Add(time); cardTimes[g.Id] = time; info.Children.Add(bottom); layout.Children.Add(info);
                var clipBorder = new Border { CornerRadius = new CornerRadius(12), Child = layout, ClipToBounds = true };
                clipBorder.SizeChanged += delegate { clipBorder.Clip = new RectangleGeometry(new Rect(0, 0, clipBorder.ActualWidth, clipBorder.ActualHeight), 12, 12); };
                var card = Design.Button("", () => ShowDetail(g)); card.Content = clipBorder; card.Padding = new Thickness(0); card.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                card.Width = 235; card.Margin = new Thickness(0, 0, 14, 16); card.ToolTip = "查看 " + g.Name; cards.Children.Add(card);
            }
            ResizeCards(((ScrollViewer)cards.Parent).ViewportWidth);
        }
        void UpdateStats()
        {
            int playing = library.Games.Count(g => g.Running);
            if (countValue != null) countValue.Text = library.Games.Count.ToString();
            if (totalValue != null) totalValue.Text = Texts.Duration(library.Games.Sum(g => g.Seconds));
            if (runningValue != null) runningValue.Text = playing.ToString();
            sidebarStats.Children.Clear();
            sidebarStats.Children.Add(Design.Text("属于你的冒险足迹", 11, Design.Muted));
            var summary = Design.Text(library.Games.Count + " 款游戏 · " + Texts.Duration(library.Games.Sum(g => g.Seconds)), 12, null, true); summary.Margin = new Thickness(0, 7, 0, 0); sidebarStats.Children.Add(summary);
            int tracking = library.Games.Count(g => g.Tracking);
            status.Text = saveFailed ? "⚠  保存失败，请点击此处查看原因并重试" :
                library.Settings.FocusOnly ? tracking > 0 ? "●  专注计时 · 正在记录当前游戏" : playing > 0 ? "Ⅱ  游戏未聚焦 · 已暂停计时" : "●  专注计时已开启 · 仅游戏聚焦时累计" :
                playing > 0 ? "●  正在记录 " + playing + " 款游戏 · 每 10 秒自动保存" : "●  自动记录已开启 · 关闭窗口后在托盘继续运行";
            status.Foreground = saveFailed ? Design.Brush("#B24735") : Design.Muted;
            status.ToolTip = saveFailed ? saveError : library.Settings.FocusOnly ? "仅前台窗口属于游戏程序时累计；检测间隔 2 秒。" : "游戏运行时才累计时长；休眠期间不计时。";
            status.Cursor = saveFailed ? Cursors.Hand : Cursors.Arrow;
        }
        void ShowDetail(Game g)
        {
            selected = g;
            var root = new DockPanel();
            var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 24) };
            var edit = Design.Button("编辑资料", () => EditGame(g)); DockPanel.SetDock(edit, Dock.Right); toolbar.Children.Add(edit);
            var back = Design.Button("‹  返回游戏库", ShowLibrary); back.HorizontalAlignment = HorizontalAlignment.Left; back.Background = Brushes.Transparent; back.BorderThickness = new Thickness(0); toolbar.Children.Add(back);
            DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
            var body = new StackPanel();
            var hero = new Grid();
            hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hero.ColumnDefinitions.Add(new ColumnDefinition());
            var icon = Design.GameIcon(g, store.Root, 100); icon.Margin = new Thickness(0, 0, 24, 0); icon.VerticalAlignment = VerticalAlignment.Top; hero.Children.Add(icon);
            var intro = new StackPanel();
            var eyebrow = Design.Text("YOUR NEXT ADVENTURE", 10, Design.Accent, true); eyebrow.Margin = new Thickness(0, 0, 0, 10); intro.Children.Add(eyebrow);
            intro.Children.Add(Design.Text(g.Name, 30, null, true));
            detailState = Design.Text(g.Running ? Texts.Last(g) : g.Favorite ? "★  已收藏" : "已添加到你的游戏库", 12, g.Tracking ? Design.Accent : Design.Muted); detailState.Margin = new Thickness(0, 9, 0, 18); intro.Children.Add(detailState);
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            launchButton = Design.Button(g.Running ? "正在游玩" : "▶  开始游戏", () => Launch(g), true); launchButton.IsEnabled = !g.Running; actions.Children.Add(launchButton);
            var fav = Design.Button(g.Favorite ? "★  已收藏" : "☆  收藏", () => { g.Favorite = !g.Favorite; Save(); ShowDetail(g); }); fav.Margin = new Thickness(10, 0, 0, 0); actions.Children.Add(fav); intro.Children.Add(actions);
            var stateRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            var stateLabel = Design.Text("游戏状态", 12, Design.Muted); stateLabel.VerticalAlignment = VerticalAlignment.Center; stateLabel.Margin = new Thickness(0, 0, 12, 0); stateRow.Children.Add(stateLabel);
            var statusPicker = new ComboBox { Width = 130 };
            foreach (string stateName in new[] { "未分类", "想玩", "在玩", "玩过" }) statusPicker.Items.Add(stateName);
            statusPicker.SelectedItem = g.Status;
            statusPicker.SelectionChanged += delegate { g.Status = (string)statusPicker.SelectedItem; Save(); }; stateRow.Children.Add(statusPicker); intro.Children.Add(stateRow);
            Grid.SetColumn(intro, 1); hero.Children.Add(intro);
            var heroPanel = Design.Panel(hero, new Thickness(26)); heroPanel.Background = new LinearGradientBrush(Design.SoftAccent.Color(), Design.Surface.Color(), 20); body.Children.Add(heroPanel);
            var metrics = new UniformGrid { Columns = 3, Margin = new Thickness(0, 16, 0, 24) };
            detailTotal = DetailMetric(metrics, "累计游玩", Texts.Duration(g.Seconds));
            detailLast = DetailMetric(metrics, "上次游玩", Texts.Last(g));
            detailSession = DetailMetric(metrics, g.Running ? "本次游玩" : "我的评分", g.Running ? Texts.Duration(g.SessionSeconds) : g.Rating > 0 ? g.Rating + " / 10" : "暂未评分");
            body.Children.Add(metrics);
            var columns = new Grid();
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
            var prose = new StackPanel { Margin = new Thickness(0, 0, 18, 0) };
            prose.Children.Add(DescriptionSection(g));
            var review = Section("我的评论", string.IsNullOrWhiteSpace(g.Review) ? "通关后的感受，或游玩中的小小发现……\n在这里留下只属于你的记录。" : g.Review, string.IsNullOrWhiteSpace(g.Review)); review.Margin = new Thickness(0, 16, 0, 0); prose.Children.Add(review); columns.Children.Add(prose);
            var right = new StackPanel();
            var linkContent = new StackPanel(); linkContent.Children.Add(Design.Text("相关链接", 15, null, true));
            if (g.Links.Count == 0)
            {
                var tip = Design.Text("连接 Bangumi、VNDB\n或任何你喜欢的页面。", 12, Design.Muted); tip.Margin = new Thickness(0, 16, 0, 8); tip.LineHeight = 23; linkContent.Children.Add(tip);
                var addLinks = Design.Button("＋  添加链接", () => EditGame(g)); addLinks.Margin = new Thickness(0, 8, 0, 0); linkContent.Children.Add(addLinks);
            }
            foreach (var link in g.Links)
            {
                var item = link;
                var b = Design.Button(item.Title + "  ↗", () => { if (Texts.WebUrl(item.Url)) OpenPath(item.Url); }); b.ToolTip = item.Url; b.Margin = new Thickness(0, 12, 0, 0); b.HorizontalContentAlignment = HorizontalAlignment.Left; linkContent.Children.Add(b);
            }
            right.Children.Add(Design.Panel(linkContent, new Thickness(19)));
            var program = new StackPanel(); program.Children.Add(Design.Text("游戏程序", 13, null, true));
            var file = Design.Text(Path.GetFileName(g.Executable), 12, Design.Muted); file.Margin = new Thickness(0, 10, 0, 8); file.ToolTip = g.Executable; program.Children.Add(file);
            if (!string.IsNullOrWhiteSpace(g.TrackingExecutable))
            {
                var tracker = Design.Text("计时进程：" + Path.GetFileName(g.TrackingExecutable), 12, Design.Muted); tracker.ToolTip = g.TrackingExecutable; tracker.Margin = new Thickness(0, 0, 0, 8); program.Children.Add(tracker);
            }
            var showFolder = Design.Button("打开所在文件夹", () => OpenPath(Path.GetDirectoryName(g.Executable))); program.Children.Add(showFolder);
            if (!File.Exists(g.Executable))
            {
                var missing = Design.Text("程序文件已移动，请编辑路径。", 12, Design.Brush("#B24735")); missing.Margin = new Thickness(0, 10, 0, 0); program.Children.Add(missing);
            }
            var programPanel = Design.Panel(program, new Thickness(19)); programPanel.Margin = new Thickness(0, 16, 0, 0); right.Children.Add(programPanel);
            if (g.CompanionPrograms.Count > 0)
            {
                var helpers = new StackPanel(); helpers.Children.Add(Design.Text("连带启动 · " + g.CompanionPrograms.Count + " 个", 13, null, true));
                foreach (string helper in g.CompanionPrograms)
                {
                    var helperLabel = Design.Text(Path.GetFileName(helper), 12, Design.Muted); helperLabel.Margin = new Thickness(0, 8, 0, 0); helperLabel.ToolTip = helper; helpers.Children.Add(helperLabel);
                }
                var helpersPanel = Design.Panel(helpers, new Thickness(19)); helpersPanel.Margin = new Thickness(0, 16, 0, 0); right.Children.Add(helpersPanel);
            }
            var remove = Design.Button("从游戏库移除", () => Remove(g)); remove.Foreground = Design.Muted; remove.Background = Brushes.Transparent; remove.BorderThickness = new Thickness(0); remove.Margin = new Thickness(0, 16, 0, 0); right.Children.Add(remove);
            Grid.SetColumn(right, 1); columns.Children.Add(right); body.Children.Add(columns);
            root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 8, 0) });
            content.Content = root;
        }
        TextBlock DetailMetric(Panel panel, string label, string value)
        {
            var stack = new StackPanel(); stack.Children.Add(Design.Text(label, 11, Design.Muted));
            var text = Design.Text(value, label == "上次游玩" ? 16 : 21, null, true); text.Margin = new Thickness(0, 9, 0, 0); stack.Children.Add(text);
            var border = Design.Panel(stack, new Thickness(18)); border.Margin = new Thickness(0, 0, 10, 0); panel.Children.Add(border); return text;
        }
        Border Section(string title, string value, bool empty)
        {
            var stack = new StackPanel(); stack.Children.Add(Design.Text(title, 16, null, true));
            // Read-only text remains selectable for copying notes.
            var text = new TextBox { Text = value, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(0), Foreground = empty ? Design.Muted : Design.Ink, Margin = new Thickness(0, 17, 0, 0), MinHeight = 65 };
            stack.Children.Add(text); return Design.Panel(stack, new Thickness(22));
        }
        Border DescriptionSection(Game g)
        {
            var stack = new StackPanel(); stack.Children.Add(Design.Text("游戏简介", 16, null, true));
            bool empty = string.IsNullOrWhiteSpace(g.Description);
            string value = empty ? "这段冒险，等待你来介绍。\n点击「编辑资料」添加游戏简介。" : g.Description;
            var text = Design.Text(value, 14, empty ? Design.Muted : Design.Ink); text.LineHeight = 23; text.Margin = new Thickness(0, 17, 0, 0);
            bool expanded = expandedDescriptions.Contains(g.Id);
            text.MaxHeight = expanded ? double.PositiveInfinity : 138; text.ClipToBounds = true;
            stack.Children.Add(text);
            var fade = Design.Text("…", 15, Design.Muted); stack.Children.Add(fade);
            Button toggle = null;
            toggle = Design.Button(expanded ? "收起简介  ∧" : "展开全文  ∨", () =>
            {
                expanded = !expanded;
                if (expanded) expandedDescriptions.Add(g.Id); else expandedDescriptions.Remove(g.Id);
                text.MaxHeight = expanded ? double.PositiveInfinity : 138;
                toggle.Content = expanded ? "收起简介  ∧" : "展开全文  ∨"; fade.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
            });
            toggle.HorizontalAlignment = HorizontalAlignment.Left; toggle.Margin = new Thickness(0, 10, 0, 0); stack.Children.Add(toggle);
            text.SizeChanged += delegate
            {
                var measure = new TextBlock { Text = value, FontFamily = FontFamily, FontSize = 14, TextWrapping = TextWrapping.Wrap, LineHeight = 23 };
                measure.Measure(new Size(Math.Max(1, text.ActualWidth), double.PositiveInfinity));
                bool longText = measure.DesiredSize.Height > 139;
                toggle.Visibility = longText ? Visibility.Visible : Visibility.Collapsed;
                fade.Visibility = longText && !expanded ? Visibility.Visible : Visibility.Collapsed;
            };
            return Design.Panel(stack, new Thickness(22));
        }
        void OpenSettings()
        {
            var dialog = new SettingsWindow(this, store, library.Settings);
            if (dialog.ShowDialog() != true) return;
            Game previousGame = selected;
            library.Settings = dialog.Result;
            previous = clock.Elapsed.TotalSeconds;
            foreach (var game in library.Games) game.Tracking = false;
            Save(); Design.Configure(Application.Current, library.Settings);
            BuildShell(); ShowLibrary();
            if (previousGame != null) ShowDetail(previousGame);
            Tick();
        }
        void AddGame()
        {
            var path = Editor.PickExe(this); if (path == null) return;
            var existing = library.Games.FirstOrDefault(g => string.Equals(g.Executable, Texts.NormalPath(path), StringComparison.OrdinalIgnoreCase));
            if (existing != null) { ShowDetail(existing); return; }
            var editor = new Editor(this, store, null, path);
            if (editor.ShowDialog() == true)
            {
                if (library.Games.Any(g => string.Equals(g.Executable, editor.Result.Executable, StringComparison.OrdinalIgnoreCase))) { MessageBox.Show(this, "这个程序已经在游戏库里了。", "添加游戏"); return; }
                library.Games.Add(editor.Result); Save(); Tick(); ShowDetail(editor.Result);
            }
        }
        void EditGame(Game g)
        {
            var editor = new Editor(this, store, g, null);
            if (editor.ShowDialog() == true)
            {
                var result = editor.Result;
                if (library.Games.Any(other => other.Id != g.Id && string.Equals(other.Executable, result.Executable, StringComparison.OrdinalIgnoreCase))) { MessageBox.Show(this, "这个程序已被另一个游戏条目使用。", "编辑游戏"); return; }
                // The modal window pumps the timer; preserve the latest live counters.
                result.Seconds = g.Seconds; result.LastPlayedUtc = g.LastPlayedUtc; result.SessionSeconds = g.SessionSeconds;
                result.Running = g.Running && string.Equals(Texts.TrackingPath(g), Texts.TrackingPath(result), StringComparison.OrdinalIgnoreCase);
                result.Tracking = g.Tracking && result.Running;
                library.Games[library.Games.IndexOf(g)] = result; Save(); Tick(); ShowDetail(result);
            }
        }
        void Remove(Game g)
        {
            if (MessageBox.Show(this, "从游戏库移除「" + g.Name + "」及其时长、评分和笔记？\n不会删除游戏程序或游戏存档。", "移除游戏", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            library.Games.Remove(g); Save(); ShowLibrary();
        }
        void Launch(Game g)
        {
            try
            {
                if (!File.Exists(g.Executable)) throw new IOException("找不到游戏程序。请在「编辑资料」中重新选择路径。");
                Process.Start(new ProcessStartInfo(g.Executable) { WorkingDirectory = Path.GetDirectoryName(g.Executable), UseShellExecute = true });
                var failures = LaunchCompanions(g);
                Tick();
                if (failures.Count > 0) MessageBox.Show(this, "游戏已启动，但以下辅助程序启动失败：\n\n" + string.Join("\n", failures), "连带启动");
            }
            catch (Exception ex) { MessageBox.Show(this, "游戏未能启动。\n" + ex.Message, "启动游戏", MessageBoxButton.OK, MessageBoxImage.Information); }
        }
        public static List<string> LaunchCompanions(Game g)
        {
            var failures = new List<string>();
            var helpers = g.CompanionPrograms.Distinct(StringComparer.OrdinalIgnoreCase).Where(path => !string.Equals(path, g.Executable, StringComparison.OrdinalIgnoreCase)).ToList();
            var running = ProcessWatch.RunningPaths(helpers.Select(path => new Game { Executable = path }));
            foreach (var path in helpers)
            {
                try
                {
                    if (running.Contains(Texts.NormalPath(path))) continue;
                    if (!File.Exists(path)) throw new IOException("找不到程序");
                    using (var process = Process.Start(new ProcessStartInfo(path) { WorkingDirectory = Path.GetDirectoryName(path), UseShellExecute = true })) { }
                }
                catch (Exception ex) { failures.Add(Path.GetFileName(path) + "：" + ex.Message); }
            }
            return failures;
        }
        void Tick()
        {
            double now = clock.Elapsed.TotalSeconds, elapsed = now - previous; previous = now;
            bool changed = ProcessWatch.Apply(library.Games, ProcessWatch.RunningPaths(library.Games), elapsed, DateTime.UtcNow, library.Settings.FocusOnly, library.Settings.FocusOnly ? ProcessWatch.ForegroundPath() : "");
            if (++ticks % 5 == 0 || changed) Save();
            if (selected != null)
            {
                if (changed) ShowDetail(selected);
                else
                {
                    detailTotal.Text = Texts.Duration(selected.Seconds); detailLast.Text = Texts.Last(selected);
                    if (selected.Running) detailSession.Text = Texts.Duration(selected.SessionSeconds);
                }
            }
            else if (changed) RenderCards();
            else foreach (var g in library.Games)
            {
                if (cardTimes.ContainsKey(g.Id)) cardTimes[g.Id].Text = Texts.Duration(g.Seconds);
                if (cardStates.ContainsKey(g.Id)) cardStates[g.Id].Text = Texts.Last(g);
            }
            UpdateStats();
        }
        public void Save()
        {
            try { store.Save(library); saveFailed = false; saveError = null; }
            catch (Exception ex) { saveFailed = true; saveError = ex.Message; }
            if (status != null)
            {
                status.MouseLeftButtonUp -= RetrySave;
                if (saveFailed) status.MouseLeftButtonUp += RetrySave;
                UpdateStats();
            }
        }
        void RetrySave(object sender, MouseButtonEventArgs e)
        {
            MessageBox.Show(this, "无法写入数据：\n" + saveError + "\n\n请检查磁盘空间或文件夹权限。关闭此提示后会重试保存。", "保存失败");
            Save();
        }
        public void Restore() { Show(); WindowState = WindowState.Normal; Activate(); }
        public void Quit()
        {
            Tick(); Save();
            if (saveFailed && MessageBox.Show(this, "资料尚未保存成功，退出可能丢失最近更改。\n" + saveError + "\n仍要退出吗？", "保存失败", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            exitRequested = true; Close(); Application.Current.Shutdown();
        }
        void OpenPath(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法打开"); }
        }
        void Help()
        {
            MessageBox.Show(this,
                "1. 点击「添加游戏」，选择实际运行的 .exe 游戏程序。\n\n" +
                "2. 游戏运行时自动计时，也能识别在工具之外启动的游戏。工具必须保持运行；关闭窗口会缩到托盘，右键托盘可退出。\n\n" +
                "3. 不足 1 小时显示分钟，达到 1 小时显示小时（最多一位小数）。不足 1 分钟会单独提示，内部按秒保存。\n\n" +
                "4. 在详情页的「编辑资料」里修改名称、图标、简介、评论、评分和链接。链接每行一个：名称 | https://网址。\n\n" +
                "5. 按程序完整路径识别；同一个游戏开多个实例不会重复计时。在「设置」开启专注计时后，仅游戏窗口聚焦时累计；休眠不计时。\n\n" +
                "6. 若游戏通过启动器运行，请选择游戏本体。系统保护或权限限制导致无法读取进程路径时不能识别。检测间隔为 2 秒。\n\n" +
                "7. 「设置」可自定义主题色、背景底色、背景图片和图片可见度。游戏状态分类可设为想玩、在玩、玩过。\n\n" +
                "8. 「编辑资料」可开启海报模式和添加连带启动程序。长简介可展开、收起。连带启动只在点击「开始游戏」时触发。\n\n" +
                "资料位置：" + store.Root + "\n退出后复制整个数据文件夹即可备份（包含自定义图标）。\n无需联网、账户或安装。",
                "GameShelf · 使用说明", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
    static class BrushExtensions
    {
        public static Color Color(this Brush brush) { return ((SolidColorBrush)brush).Color; }
    }
    static class Native
    {
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
    public static class Program
    {
        static Mutex mutex;
        [STAThread]
        public static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--test-game") { Thread.Sleep(120000); return; }
            if (args.Length > 1 && args[0] == "--self-test")
            {
                try { SelfTests.Run(args[1]); Environment.ExitCode = 0; }
                catch (Exception ex) { File.WriteAllText(Path.Combine(args[1], "test-results.txt"), "FAIL\n" + ex); Environment.ExitCode = 1; }
                return;
            }
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
            int dataArg = Array.IndexOf(args, "--data-dir");
            if (dataArg >= 0 && args.Length > dataArg + 1) root = Path.GetFullPath(args[dataArg + 1]);
            try
            {
                string key;
                using (var hash = SHA256.Create()) key = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(root.ToUpperInvariant()))).Replace("-", "");
                bool created;
                mutex = new Mutex(true, "Local\\GameShelf-" + key, out created);
                if (!created) { MessageBox.Show("GameShelf 已在运行。\n请双击任务栏右下角的托盘图标打开游戏库。", "GameShelf"); mutex.Dispose(); return; }
                var store = new Store(root); var library = store.Load();
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                Design.Configure(app, library.Settings);
                var window = new MainWindow(store, library);
                app.SessionEnding += delegate { window.Save(); };
                app.Run(window);
                mutex.ReleaseMutex(); mutex.Dispose();
            }
            catch (Exception ex)
            {
                MessageBox.Show("GameShelf 无法启动。\n" + ex.Message + "\n\n数据文件夹：" + root, "GameShelf", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
    static class SelfTests
    {
        static readonly List<string> results = new List<string>();
        static void Check(bool condition, string name)
        {
            if (!condition) throw new Exception(name);
            results.Add("PASS " + name);
        }
        public static void Run(string root)
        {
            Directory.CreateDirectory(root);
            Check(Texts.Duration(0) == "0 分钟", "zero");
            Check(Texts.Duration(30) == "不足 1 分钟", "sub-minute");
            Check(Texts.Duration(3599) == "59 分钟", "minute boundary");
            Check(Texts.Duration(3600) == "1 小时", "hour boundary");
            Check(Texts.Duration(5400) == "1.5 小时", "hour decimal");
            Check(Texts.WebUrl("https://vndb.org/v1") && !Texts.WebUrl("file:///C:/test") && !Texts.WebUrl("javascript:alert(1)"), "safe web link schemes");
            var game = new Game { Name = "测试游戏 / 中文", Executable = Path.Combine(root, "Sample.exe"), Review = "第一行\n第二行", Links = new List<GameLink> { new GameLink { Title = "VNDB", Url = "https://vndb.org/v1" } } };
            var lib = new Library(); lib.Games.Add(game);
            var empty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { game.Executable.ToUpperInvariant() };
            var now = DateTime.UtcNow;
            ProcessWatch.Apply(lib.Games, running, 2, now);
            Check(game.Running && game.Seconds == 0, "start does not backfill");
            ProcessWatch.Apply(lib.Games, running, 2, now);
            Check(game.Seconds == 2 && game.SessionSeconds == 2, "active time accumulates once");
            ProcessWatch.Apply(lib.Games, running, 3600, now);
            Check(game.Seconds == 2, "sleep gap excluded");
            ProcessWatch.Apply(lib.Games, empty, 2, now);
            Check(!game.Running && game.Seconds == 4, "stop closes last polling interval");
            ProcessWatch.Apply(lib.Games, empty, 2, now);
            Check(game.Seconds == 4, "idle does not count");
            ProcessWatch.Apply(lib.Games, running, 2, now);
            Check(game.SessionSeconds == 0 && game.Seconds == 4, "new session preserves cumulative time");
            game.Tracking = false;
            ProcessWatch.Apply(lib.Games, running, 2, now, true, game.Executable);
            Check(game.Tracking && game.Seconds == 4, "focus gain does not backfill background time");
            ProcessWatch.Apply(lib.Games, running, 2, now, true, game.Executable.ToUpperInvariant());
            Check(game.Seconds == 6, "focused game counts with case-insensitive path");
            ProcessWatch.Apply(lib.Games, running, 2, now, true, Path.Combine(root, "Other.exe"));
            Check(game.Running && !game.Tracking && game.Seconds == 6, "focus loss pauses a running game");
            ProcessWatch.Apply(lib.Games, running, 2, now, true, "");
            Check(game.Seconds == 6, "no foreground window does not count");
            ProcessWatch.Apply(lib.Games, running, 2, now, true, game.Executable);
            ProcessWatch.Apply(lib.Games, running, 2, now, true, game.Executable);
            Check(game.Seconds == 8, "returning to game resumes counting");
            ProcessWatch.Apply(lib.Games, running, 2, now, false, "");
            Check(game.Seconds == 10, "unrestricted mode counts background game");
            game.Seconds = 4;
            game.Status = "在玩"; game.PosterFill = true; game.CompanionPrograms.Add(Path.Combine(root, "Helper.exe"));
            lib.Settings = new AppSettings { FocusOnly = true, AccentColor = "#27816B", BackgroundColor = "#1C2030", BackgroundImage = "backgrounds/test.png", ImageOpacity = .55 };
            var store = new Store(Path.Combine(root, "persistence")); store.Save(lib); store.Save(lib);
            var loaded = store.Load().Games[0];
            Check(loaded.Name == game.Name && loaded.Review == game.Review && loaded.Links[0].Url == game.Links[0].Url && loaded.Seconds == 4 && !loaded.Running, "Unicode notes links and time round-trip; runtime state excluded");
            Check(loaded.Status == "在玩" && loaded.PosterFill && loaded.CompanionPrograms.SequenceEqual(game.CompanionPrograms), "status poster and companion paths survive restart");
            var savedSettings = store.Load().Settings;
            Check(savedSettings.FocusOnly && savedSettings.AccentColor == "#27816B" && savedSettings.BackgroundColor == "#1C2030" && savedSettings.BackgroundImage == "backgrounds/test.png" && savedSettings.ImageOpacity == .55, "theme wallpaper and focus settings survive restart");
            var legacyPath = Path.Combine(root, "legacy.json");
            File.WriteAllText(legacyPath, "{\"Version\":1,\"Games\":[{\"Id\":\"legacy\",\"Name\":\"旧游戏\",\"Executable\":\"C:\\\\Legacy.exe\",\"Seconds\":12345,\"Rating\":8,\"Review\":\"保留笔记\",\"Links\":[]}]}");
            var legacy = Store.Read(legacyPath);
            Check(legacy.Games[0].Seconds == 12345 && legacy.Games[0].Review == "保留笔记" && legacy.Games[0].Rating == 8 && legacy.Games[0].Status == "未分类" && legacy.Games[0].CompanionPrograms.Count == 0 && !legacy.Settings.FocusOnly, "legacy library migrates without losing time notes or rating");
            Check(AppSettings.ValidColor("#ABC123") && !AppSettings.ValidColor("red") && !AppSettings.ValidColor("#1234"), "custom color validation");
            var launchedGame = new Game { Name = "Launcher test", Executable = Path.Combine(root, "Launcher.exe"), TrackingExecutable = game.Executable };
            ProcessWatch.Apply(new[] { launchedGame }, running, 0, now);
            for (int i = 0; i < 300; i++) ProcessWatch.Apply(new[] { launchedGame }, running, 2, now.AddSeconds(i * 2));
            Check(launchedGame.Running && launchedGame.Seconds == 600 && Texts.Duration(launchedGame.Seconds) == "10 分钟", "launcher exit does not stop tracking actual game for ten minutes");
            ProcessWatch.Apply(new[] { launchedGame }, running, 2, now, true, launchedGame.TrackingExecutable);
            Check(launchedGame.Tracking, "focus mode matches actual game instead of launcher");
            File.WriteAllText(store.FilePath, "broken");
            Check(store.Load().Games[0].Seconds == 4 && store.RecoveryNotice != null, "backup recovery preserves data");
            string dirA = Path.Combine(root, "A"), dirB = Path.Combine(root, "B");
            Directory.CreateDirectory(dirA); Directory.CreateDirectory(dirB);
            string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string exeA = Path.Combine(dirA, "GameShelfProbe.exe"), exeB = Path.Combine(dirB, "GameShelfProbe.exe");
            File.Copy(exe, exeA, true); File.Copy(exe, exeB, true);
            var a = new Game { Name = "A", Executable = exeA }; var b = new Game { Name = "B", Executable = exeB };
            Process probe = null, duplicate = null;
            try
            {
                probe = Process.Start(new ProcessStartInfo(exeA, "--test-game") { UseShellExecute = false, CreateNoWindow = true });
                duplicate = Process.Start(new ProcessStartInfo(exeA, "--test-game") { UseShellExecute = false, CreateNoWindow = true });
                Thread.Sleep(400);
                var paths = ProcessWatch.RunningPaths(new[] { a, b });
                Check(paths.Contains(exeA) && !paths.Contains(exeB), "real processes match full path; same filename elsewhere ignored");
                var launcherGame = new Game { Executable = exeB, TrackingExecutable = exeA };
                var actualPaths = ProcessWatch.RunningPaths(new[] { launcherGame });
                Check(actualPaths.Contains(exeA) && !actualPaths.Contains(exeB), "real launcher mapping finds the independently running game executable");
                var helperGame = new Game { Executable = exeB, CompanionPrograms = new List<string> { exeA, exeA } };
                Check(MainWindow.LaunchCompanions(helperGame).Count == 0 && Process.GetProcessesByName("GameShelfProbe").Length == 2, "already-running companion processes are not relaunched");
                helperGame.CompanionPrograms.Add(Path.Combine(root, "MissingHelper.exe"));
                Check(MainWindow.LaunchCompanions(helperGame).Count == 1, "missing companion is reported without blocking other helpers");
                ProcessWatch.Apply(new[] { a, b }, paths, 0, DateTime.UtcNow);
                ProcessWatch.Apply(new[] { a, b }, paths, 2, DateTime.UtcNow);
                Check(a.Seconds == 2 && b.Seconds == 0, "duplicate process instances do not double-count");
                probe.Kill(); probe.WaitForExit(); duplicate.Kill(); duplicate.WaitForExit();
                Check(!ProcessWatch.RunningPaths(new[] { a, b }).Contains(exeA), "real process exit detected");
            }
            finally
            {
                if (probe != null) { if (!probe.HasExited) probe.Kill(); probe.Dispose(); }
                if (duplicate != null) { if (!duplicate.HasExited) duplicate.Kill(); duplicate.Dispose(); }
            }
            File.WriteAllLines(Path.Combine(root, "test-results.txt"), results, Encoding.UTF8);
        }
    }
}

