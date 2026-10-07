using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Calendar = System.Windows.Controls.Calendar;

namespace StickyTodo
{
    public class RecordedDateConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var dates = values.Length > 1 ? values[1] as HashSet<string> : null;
            return values.Length > 0 && values[0] is DateTime && dates != null && dates.Contains(Store.Key((DateTime)values[0]));
        }
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) { throw new NotSupportedException(); }
    }
    public class Todo
    {
        public string Id { get; set; }
        public string Date { get; set; }
        public string Text { get; set; }
        public bool Done { get; set; }
        public string CompletedAt { get; set; }
        public string CreatedAt { get; set; }
    }
    public class Settings
    {
        public int Color { get; set; }
        public bool Topmost { get; set; }
        public bool ShowCompleted { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double Left { get; set; }
        public double Top { get; set; }
        public double CompactWidth { get; set; }
        public double CompactHeight { get; set; }
        public double ExpandedWidth { get; set; }
        public double ExpandedHeight { get; set; }
        public double IndexTop { get; set; }
        public double Transparency { get; set; }
        public Settings() { ShowCompleted = true; Width = CompactWidth = 330; Height = CompactHeight = 360; ExpandedWidth = 420; ExpandedHeight = 610; Left = -1; Top = -1; IndexTop = -1; }
    }
    public class NoteData
    {
        public int Version { get; set; }
        public List<Todo> Tasks { get; set; }
        public Settings Settings { get; set; }
        public NoteData() { Version = 1; Tasks = new List<Todo>(); Settings = new Settings(); }
    }
    public class Store
    {
        public readonly string PathName;
        public NoteData Data;
        public string RecoveryNotice;
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue };
        public Store(string path)
        {
            PathName = path;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            Data = new NoteData();
            if (!File.Exists(path)) return;
            try { Data = Read(path); }
            catch (Exception first)
            {
                // Never silently overwrite a corrupt primary file with an empty note.
                try
                {
                    Data = Read(path + ".bak");
                    File.Copy(path, path + ".damaged-" + DateTime.Now.ToString("yyyyMMdd-HHmmssfff"));
                    RecoveryNotice = "저장 파일을 읽지 못해 최근 백업을 복구했습니다. 원본도 보관했습니다.";
                }
                catch { throw new IOException("메모 파일을 읽을 수 없습니다. data 폴더의 원본을 보관한 뒤 복구가 필요합니다.", first); }
            }
        }
        NoteData Read(string path)
        {
            NoteData value = json.Deserialize<NoteData>(File.ReadAllText(path, Encoding.UTF8));
            if (value == null || value.Version != 1 || value.Tasks == null || value.Settings == null)
                throw new InvalidDataException("잘못된 메모 형식입니다.");
            var ids = new HashSet<string>();
            foreach (Todo t in value.Tasks)
            {
                DateTime date;
                if (t == null || String.IsNullOrEmpty(t.Id) || !ids.Add(t.Id) || String.IsNullOrWhiteSpace(t.Text) ||
                    !DateTime.TryParseExact(t.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                    throw new InvalidDataException("잘못된 할 일입니다.");
            }
            return value;
        }
        public void Save()
        {
            string temp = PathName + ".tmp";
            byte[] bytes = Encoding.UTF8.GetBytes(json.Serialize(Data));
            using (FileStream stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
            if (File.Exists(PathName)) File.Replace(temp, PathName, PathName + ".bak", true);
            else File.Move(temp, PathName);
        }
        public Todo Add(DateTime date, string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0 || text.Length > 1000) throw new ArgumentException("할 일을 1~1,000자로 입력해 주세요.");
            Todo item = new Todo { Id = Guid.NewGuid().ToString("N"), Date = Key(date), Text = text,
                CreatedAt = DateTimeOffset.Now.ToString("o") };
            Data.Tasks.Add(item);
            return item;
        }
        public static string Key(DateTime date) { return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
        public IEnumerable<Todo> Query(bool history, DateTime selected, bool showDone, int filter, string search, DateTime? from, DateTime? to)
        {
            IEnumerable<Todo> items = Data.Tasks;
            if (!history) items = items.Where(t => t.Date == Key(selected) && (showDone || !t.Done));
            else
            {
                if (filter == 1) items = items.Where(t => t.Done);
                if (filter == 2) items = items.Where(t => !t.Done);
                if (!String.IsNullOrWhiteSpace(search)) items = items.Where(t => t.Text.IndexOf(search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
                if (from.HasValue) items = items.Where(t => String.CompareOrdinal(t.Date, Key(from.Value)) >= 0);
                if (to.HasValue) items = items.Where(t => String.CompareOrdinal(t.Date, Key(to.Value)) <= 0);
            }
            return items.OrderByDescending(t => t.Date).ThenBy(t => t.CreatedAt).ThenBy(t => t.Id);
        }
        public static string Csv(IEnumerable<Todo> tasks)
        {
            StringBuilder text = new StringBuilder("날짜,요일,내용,상태,완료 시각\r\n");
            foreach (Todo t in tasks)
            {
                string[] fields = { t.Date, DateTime.ParseExact(t.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToString("dddd", CultureInfo.GetCultureInfo("ko-KR")),
                    t.Text, t.Done ? "완료" : "미완료", t.CompletedAt ?? "" };
                text.AppendLine(String.Join(",", fields.Select(f => "\"" + SafeCsv(f).Replace("\"", "\"\"") + "\"")));
            }
            return text.ToString();
        }
        static string SafeCsv(string value)
        {
            string trimmed = value.TrimStart();
            return trimmed.Length > 0 && "=+-@".IndexOf(trimmed[0]) >= 0 ? "'" + value : value;
        }
    }
    public class NoteApp
    {
        static readonly Regex webLinks = new Regex(@"(?<![\p{L}\p{N}_@])(?:https?://|www\.)[^\s<>""'，。！？]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        public Window Window;
        public readonly Store Store;
        readonly CultureInfo korean = CultureInfo.GetCultureInfo("ko-KR");
        readonly string[] colors = { "#FFF2BB", "#F8DED8", "#E2EDDB", "#DFECF4", "#EAE3F2" };
        readonly string[] colorNames = { "크림", "로즈", "세이지", "하늘", "라벤더" };
        readonly string[] hoverColors = { "#EADFAD", "#E8CEC8", "#D2DDCB", "#CFDCE4", "#DAD3E2" };
        readonly string[] ruleColors = { "#CABF91", "#CCAAA2", "#ADBEA3", "#AABDCB", "#BAADC9" };
        readonly Stack<Todo> deleted = new Stack<Todo>();
        readonly DispatcherTimer geometryTimer = new DispatcherTimer();
        readonly DispatcherTimer dayTimer = new DispatcherTimer();
        readonly DispatcherTimer undoTimer = new DispatcherTimer();
        readonly List<CheckBox> renderedChecks = new List<CheckBox>();
        Window indexWindow;
        Button indexButton;
        bool docked, shuttingDown;
        bool ready, rendering, history, expanded, saveFailed;
        DateTime selected = DateTime.Today, lastToday = DateTime.Today;
        public NoteApp(Store store)
        {
            Store = store;
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Main.xaml"))
                Window = (Window)XamlReader.Load(stream);
            var iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Note.ico");
            if (iconStream != null) using (iconStream) Window.Icon = BitmapFrame.Create(iconStream);
            Wire();
            Find<Slider>("TransparencySlider").Value = Math.Max(0,Math.Min(80,Valid(Store.Data.Settings.Transparency,0)));
            ApplyTransparency();
            SetGeometry();
            SetColor(Store.Data.Settings.Color);
            Window.Topmost = Store.Data.Settings.Topmost;
            Find<CheckBox>("ShowCompleted").IsChecked = Store.Data.Settings.ShowCompleted;
            Find<DatePicker>("DayPicker").SelectedDate = selected;
            UpdateView();
            geometryTimer.Interval = TimeSpan.FromMilliseconds(500);
            geometryTimer.Tick += delegate { geometryTimer.Stop(); if (ready) { SaveGeometry(); Persist(); } };
            Window.SizeChanged += delegate { if (ready) { geometryTimer.Stop(); geometryTimer.Start(); } };
            Window.LocationChanged += delegate { if (ready) { geometryTimer.Stop(); geometryTimer.Start(); } };
            Window.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            {
                SaveGeometry();
                if (!Persist()) e.Cancel = MessageBox.Show(Window, "저장하지 못한 변경 사항이 있습니다. 그래도 닫을까요?", "저장 오류", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes;
            };
            Window.Closed += delegate { shuttingDown = true; geometryTimer.Stop(); dayTimer.Stop(); undoTimer.Stop(); if(indexWindow != null) indexWindow.Close(); };
            undoTimer.Interval = TimeSpan.FromSeconds(7);
            undoTimer.Tick += delegate { HideUndoNotice(); };
            Window.SourceInitialized += delegate
            {
                var source = System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(Window).Handle);
                source.AddHook(delegate(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
                {
                    if (message == Program.RestoreMessage) { RestoreFromIndex(); handled = true; }
                    return IntPtr.Zero;
                });
            };
            dayTimer.Interval = TimeSpan.FromSeconds(30);
            dayTimer.Tick += delegate
            {
                if (lastToday != DateTime.Today)
                {
                    bool follow = selected == lastToday;
                    lastToday = DateTime.Today;
                    if (follow) selected = lastToday;
                    Render();
                }
            };
            Window.Loaded += delegate { ready = true; dayTimer.Start(); Render(); ApplyTopmost(); if (Store.RecoveryNotice != null) MessageBox.Show(Window, Store.RecoveryNotice, "백업 복구", MessageBoxButton.OK, MessageBoxImage.Information); };
        }
        public T Find<T>(string name) where T : FrameworkElement { return (T)Window.FindName(name); }
        Brush Brush(string key) { return (Brush)Window.FindResource(key); }
        static SolidColorBrush Hex(string value) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); }
        void SetGeometry()
        {
            Settings s = Store.Data.Settings;
            Rect screen = SystemParameters.WorkArea;
            Window.Width = Math.Min(screen.Width, Math.Max(280, Valid(s.CompactWidth, 330)));
            Window.Height = Math.Min(screen.Height, Math.Max(240, Valid(s.CompactHeight, 360)));
            Window.Left = s.Left < 0 ? screen.Right - Window.Width - 36 : Math.Max(screen.Left, Math.Min(Valid(s.Left, screen.Left), screen.Right - Window.Width));
            Window.Top = s.Top < 0 ? screen.Top + 56 : Math.Max(screen.Top, Math.Min(Valid(s.Top, screen.Top), screen.Bottom - Window.Height));
        }
        static double Valid(double n, double fallback) { return Double.IsNaN(n) || Double.IsInfinity(n) ? fallback : n; }
        void SaveGeometry()
        {
            if (Window.WindowState != WindowState.Normal) return;
            Settings s = Store.Data.Settings;
            s.Width = Window.Width; s.Height = Window.Height; s.Left = Window.Left; s.Top = Window.Top;
            if (expanded) { s.ExpandedWidth = Window.Width; s.ExpandedHeight = Window.Height; }
            else { s.CompactWidth = Window.Width; s.CompactHeight = Window.Height; }
        }
        public bool Persist()
        {
            try { Store.Save(); saveFailed = false; return true; }
            catch (Exception error)
            {
                bool firstFailure = !saveFailed;
                saveFailed = true;
                Find<TextBlock>("Status").Text = "저장 실패 · 메뉴에서 다시 저장";
                Find<TextBlock>("Status").Foreground = Brush("Error");
                Find<Grid>("Footer").Visibility = Visibility.Visible;
                if (firstFailure) MessageBox.Show(Window, "메모를 저장하지 못했습니다. 저장 위치를 확인해 주세요.\n\n" + error.Message, "저장 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }
        void Wire()
        {
            Find<Slider>("TransparencySlider").ValueChanged += delegate { ApplyTransparency(); };
            var dateButton = Find<Button>("DateButton");
            var headerPopup = Find<Popup>("HeaderDatePopup");
            var headerCalendar = Find<Calendar>("HeaderCalendar");
            dateButton.Click += delegate
            {
                headerCalendar.SelectedDate = selected; headerCalendar.DisplayDate = selected;
                headerPopup.IsOpen = !headerPopup.IsOpen;
                if (headerPopup.IsOpen) headerCalendar.Focus();
            };
            Action selectHeaderDate = delegate
            {
                if (!headerPopup.IsOpen || !headerCalendar.SelectedDate.HasValue) return;
                DateTime date = headerCalendar.SelectedDate.Value;
                headerPopup.IsOpen = false; SetMode(false); SelectDate(date); dateButton.Focus();
            };
            headerCalendar.SelectedDatesChanged += delegate { selectHeaderDate(); };
            headerCalendar.PreviewKeyDown += delegate(object sender,KeyEventArgs e) { if (e.Key == Key.Escape) { headerPopup.IsOpen = false; dateButton.Focus(); e.Handled = true; } };
            headerCalendar.AddHandler(Button.ClickEvent,new RoutedEventHandler(delegate(object sender,RoutedEventArgs e) { if (e.OriginalSource is CalendarDayButton) selectHeaderDate(); }));
            Point? dateDragStart = null;
            dateButton.PreviewMouseLeftButtonDown += delegate(object sender,MouseButtonEventArgs e) { dateDragStart = e.GetPosition(Window); };
            dateButton.PreviewMouseMove += delegate(object sender,MouseEventArgs e)
            {
                if (!dateDragStart.HasValue || e.LeftButton != MouseButtonState.Pressed) return;
                Point current = e.GetPosition(Window), start = dateDragStart.Value;
                if (Math.Abs(current.X-start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(current.Y-start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                dateDragStart = null; dateButton.ReleaseMouseCapture(); e.Handled = true;
                try { Window.DragMove(); } catch (InvalidOperationException) { }
            };
            Find<Button>("CloseButton").Click += delegate { Window.Close(); };
            Find<Button>("ExpandButton").Click += delegate { SetExpanded(!expanded); };
            Find<Button>("DockButton").Click += delegate { DockToIndex(); };
            Find<Button>("PinButton").Click += delegate { Window.Topmost = !Window.Topmost; ApplyTopmost(); Store.Data.Settings.Topmost = Window.Topmost; Persist(); RenderPin(); Window.Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(ApplyTopmost)); };
            Find<Grid>("DragArea").MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                DependencyObject source = e.OriginalSource as DependencyObject;
                while (source != null && source != sender) { if (source is Button) return; source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source); }
                if (e.ButtonState == MouseButtonState.Pressed) { try { Window.DragMove(); } catch (InvalidOperationException) { } }
            };
            Find<Button>("DayTab").Click += delegate { SetMode(false); };
            Find<Button>("HistoryTab").Click += delegate { SetMode(true); };
            Find<Button>("PrevButton").Click += delegate { SelectDate(selected.AddDays(-1)); };
            Find<Button>("NextButton").Click += delegate { SelectDate(selected.AddDays(1)); };
            Find<Button>("TodayButton").Click += delegate { SelectDate(DateTime.Today); };
            Find<DatePicker>("DayPicker").SelectedDateChanged += delegate { if (!rendering && Find<DatePicker>("DayPicker").SelectedDate.HasValue) SelectDate(Find<DatePicker>("DayPicker").SelectedDate.Value); };
            foreach (string name in new[] { "FromPicker", "ToPicker" }) Find<DatePicker>(name).SelectedDateChanged += delegate { if (ready && !rendering) RenderTasks(); };
            Find<ComboBox>("HistoryFilter").SelectionChanged += delegate { if (ready && !rendering) RenderTasks(); };
            Find<TextBox>("SearchBox").TextChanged += delegate { if (ready && !rendering) RenderTasks(); };
            Find<Button>("ClearFilter").Click += delegate
            {
                rendering = true;
                Find<TextBox>("SearchBox").Clear(); Find<DatePicker>("FromPicker").SelectedDate = null; Find<DatePicker>("ToPicker").SelectedDate = null; Find<ComboBox>("HistoryFilter").SelectedIndex = 0;
                rendering = false; RenderTasks();
            };
            Find<CheckBox>("ShowCompleted").Click += delegate { Store.Data.Settings.ShowCompleted = Find<CheckBox>("ShowCompleted").IsChecked == true; Persist(); RenderTasks(); };
            Find<Button>("AddButton").Click += delegate { AddTask(); };
            Find<TextBox>("NewTask").KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { AddTask(); e.Handled = true; } };
            Find<TextBox>("NewTask").TextChanged += delegate { Find<Button>("AddButton").IsEnabled = !String.IsNullOrWhiteSpace(Find<TextBox>("NewTask").Text); };
            Find<Button>("AddButton").IsEnabled = false;
            Find<Button>("UndoButton").Click += delegate { Undo(); };
            Find<Button>("MenuButton").Click += delegate { ShowMenu(); };
            Window.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (Keyboard.Modifiers != ModifierKeys.Control) return;
                if (e.Key == Key.N) { SetMode(false); Find<TextBox>("NewTask").Focus(); e.Handled = true; }
                if (e.Key == Key.H) { SetMode(!history); e.Handled = true; }
                if (e.Key == Key.T) { SetMode(false); SelectDate(DateTime.Today); e.Handled = true; }
                if (e.Key == Key.S) { SaveGeometry(); Persist(); RenderTasks(); e.Handled = true; }
                if (e.Key == Key.M) { MinimizeToTaskbar(); e.Handled = true; }
                if (e.Key == Key.Z && !(Keyboard.FocusedElement is TextBox)) { Undo(); e.Handled = true; }
            };
        }
        public void SetMode(bool value)
        {
            if (value && !expanded) SetExpanded(true);
            history = value;
            UpdateView();
            Find<ScrollViewer>("TaskScroll").ScrollToTop();
            Render();
        }
        public void SetExpanded(bool value)
        {
            if (expanded == value) return;
            SaveGeometry();
            double right = Window.Left + Window.Width;
            expanded = value;
            if (!expanded) history = false;
            Settings s = Store.Data.Settings;
            Rect screen = SystemParameters.WorkArea;
            Window.MinWidth = expanded ? 320 : 280;
            Window.MinHeight = expanded ? 480 : 240;
            Window.Width = Math.Min(screen.Width, Math.Max(Window.MinWidth, Valid(expanded ? s.ExpandedWidth : s.CompactWidth, expanded ? 420 : 330)));
            Window.Height = Math.Min(screen.Height, Math.Max(Window.MinHeight, Valid(expanded ? s.ExpandedHeight : s.CompactHeight, expanded ? 610 : 360)));
            Window.Left = Math.Max(screen.Left, Math.Min(right - Window.Width, screen.Right - Window.Width));
            Window.Top = Math.Max(screen.Top, Math.Min(Window.Top, screen.Bottom - Window.Height));
            UpdateView(); Render(); SaveGeometry(); Persist();
            Window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(ApplyTopmost));
        }
        void UpdateView()
        {
            Find<StackPanel>("HeaderExtras").Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            Find<StackPanel>("ExpandedTools").Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            Find<StackPanel>("DayHeader").Visibility = expanded && !history ? Visibility.Visible : Visibility.Collapsed;
            Find<StackPanel>("HistoryHeader").Visibility = history ? Visibility.Visible : Visibility.Collapsed;
            Find<StackPanel>("AddPanel").Visibility = history ? Visibility.Collapsed : Visibility.Visible;
            Find<TextBlock>("DateTitle").FontSize = expanded ? 13 : 15;
            Button button = Find<Button>("ExpandButton");
            button.Content = expanded ? "\uE73F" : "\uE740";
            button.ToolTip = expanded ? "접기 · 간단한 메모로 돌아가기" : "확장 · 기록 및 설정";
            AutomationProperties.SetName(button, button.ToolTip.ToString());
            Find<Grid>("Footer").Visibility = expanded || saveFailed ? Visibility.Visible : Visibility.Collapsed;
        }
        public void SelectDate(DateTime date) { selected = date.Date; Render(); Find<ScrollViewer>("TaskScroll").ScrollToTop(); }
        void ApplyTransparency()
        {
            double value = Find<Slider>("TransparencySlider").Value;
            Window.Opacity = 1-value/100;
            Store.Data.Settings.Transparency = value;
            Find<TextBlock>("TransparencyValue").Text = value.ToString("0",CultureInfo.InvariantCulture) + "%";
            if (ready) { geometryTimer.Stop(); geometryTimer.Start(); }
        }
        void AddTask()
        {
            TextBox input = Find<TextBox>("NewTask");
            if (String.IsNullOrWhiteSpace(input.Text)) return;
            Store.Add(selected, input.Text); Persist(); input.Clear(); Render(); input.Focus();
            Find<ScrollViewer>("TaskScroll").ScrollToBottom();
        }
        public void SetColor(int index)
        {
            index = Math.Max(0, Math.Min(colors.Length - 1, index));
            Store.Data.Settings.Color = index;
            Window.Resources["Paper"] = Hex(colors[index]);
            Window.Resources["Hover"] = Hex(hoverColors[index]);
            Window.Resources["Rule"] = Hex(ruleColors[index]);
            if (indexWindow != null) { ((Border)indexWindow.Content).Background = Brush("Paper"); ((Border)indexWindow.Content).BorderBrush = Brush("Rule"); }
            var palette = Find<StackPanel>("Palette"); palette.Children.Clear();
            for (int i = 0; i < colors.Length; i++)
            {
                int pick = i;
                var swatch = new Border { Background = Hex(colors[i]), BorderBrush = Brush(i == index ? "Ink" : "Rule"), BorderThickness = new Thickness(i == index ? 2 : 1), CornerRadius = new CornerRadius(8), Width = 16, Height = 16 };
                var button = new Button { Content = swatch, Width = 24, Height = 32, MinHeight = 32, Padding = new Thickness(0), ToolTip = colorNames[i] + (i == index ? " · 선택됨" : " 메모지") };
                AutomationProperties.SetName(button, colorNames[i] + " 색상");
                button.Click += delegate { SetColor(pick); Persist(); Render(); };
                palette.Children.Add(button);
            }
        }
        [DllImport("user32.dll", SetLastError=true)] static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
        void ApplyTopmost()
        {
            if (!Window.IsVisible || Window.WindowState == WindowState.Minimized) return;
            IntPtr handle = new System.Windows.Interop.WindowInteropHelper(Window).Handle;
            if (handle == IntPtr.Zero) return;
            SetWindowPos(handle, new IntPtr(Window.Topmost ? -1 : -2),0,0,0,0,0x13);
        }
        void RenderPin()
        {
            Button pin = Find<Button>("PinButton");
            pin.Background = Window.Topmost ? Brush("Hover") : Brushes.Transparent;
            pin.Content = Window.Topmost ? "\uE840" : "\uE718";
            pin.ToolTip = Window.Topmost ? "항상 위에 고정됨 · 클릭하여 해제" : "항상 위에 고정";
            AutomationProperties.SetName(pin, pin.ToolTip.ToString());
        }
        public void Render()
        {
            rendering = true;
            Window.Resources["RecordedDates"] = new HashSet<string>(Store.Data.Tasks.Select(t => t.Date));
            Find<DatePicker>("DayPicker").SelectedDate = selected;
            Find<TextBlock>("DateTitle").Text = selected.ToString("M월 d일 dddd", korean);
            Find<Button>("DayTab").Background = !history ? Brush("Hover") : Brushes.Transparent;
            Find<Button>("DayTab").FontWeight = !history ? FontWeights.Bold : FontWeights.Normal;
            Find<Button>("HistoryTab").Background = history ? Brush("Hover") : Brushes.Transparent;
            Find<Button>("HistoryTab").FontWeight = history ? FontWeights.Bold : FontWeights.Normal;
            RenderPin(); RenderWeek(); RenderTasks(); rendering = false;
        }
        void RenderWeek()
        {
            var week = Find<UniformGrid>("Week"); week.Children.Clear();
            DateTime monday = selected.AddDays(-(((int)selected.DayOfWeek + 6) % 7));
            for (int i = 0; i < 7; i++)
            {
                DateTime date = monday.AddDays(i);
                string key = Store.Key(date);
                var tasks = Store.Data.Tasks.Where(t => t.Date == key).ToList();
                var stack = new StackPanel();
                stack.Children.Add(new TextBlock { Text = date.ToString("ddd", korean), FontSize = 10, Foreground = Brush("Muted"), TextAlignment = TextAlignment.Center });
                var number = new TextBlock { Text = date.Day.ToString(), FontSize = 15, FontWeight = date == selected ? FontWeights.Bold : FontWeights.Normal, TextAlignment = TextAlignment.Center, Margin = new Thickness(0,4,0,4) };
                if (tasks.Count > 0 && tasks.All(t => t.Done)) number.TextDecorations = TextDecorations.Strikethrough;
                stack.Children.Add(number);
                var dot = new Border { Width = 3, Height = 3, CornerRadius = new CornerRadius(2), Background = tasks.Count > 0 ? Brush("Muted") : Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Center };
                stack.Children.Add(dot);
                var button = new Button { Content = stack, Padding = new Thickness(0,6,0,6), Margin = new Thickness(1,0,1,0), Background = date == selected ? Brush("Hover") : Brushes.Transparent, BorderBrush = date == DateTime.Today ? Brush("Rule") : Brushes.Transparent };
                button.ToolTip = date.ToString("yyyy년 M월 d일 dddd", korean) + " · " + tasks.Count + "개 / 완료 " + tasks.Count(t => t.Done) + "개";
                AutomationProperties.SetName(button, button.ToolTip.ToString());
                button.Click += delegate { SelectDate(date); };
                week.Children.Add(button);
            }
        }
        public List<Todo> VisibleTasks()
        {
            return Store.Query(history, selected, Store.Data.Settings.ShowCompleted, Find<ComboBox>("HistoryFilter").SelectedIndex,
                Find<TextBox>("SearchBox").Text, Find<DatePicker>("FromPicker").SelectedDate, Find<DatePicker>("ToPicker").SelectedDate).ToList();
        }
        public void RenderTasks()
        {
            if (Window == null) return;
            var list = Find<StackPanel>("TaskList"); list.Children.Clear(); renderedChecks.Clear();
            DateTime? from = Find<DatePicker>("FromPicker").SelectedDate, to = Find<DatePicker>("ToPicker").SelectedDate;
            List<Todo> items = VisibleTasks();
            if (history && from.HasValue && to.HasValue && from > to)
                list.Children.Add(Empty("날짜 범위를 확인해 주세요.", "시작 날짜는 마지막 날짜보다 앞이어야 합니다."));
            else if (items.Count == 0)
            {
                bool hidden = !history && Store.Data.Tasks.Any(t => t.Date == Store.Key(selected));
                string title = history ? "조건에 맞는 기록이 없어요." : hidden ? "오늘의 할 일을 모두 마쳤어요." : "이날의 첫 메모를 남겨 보세요.";
                string help = history ? "검색어나 날짜 범위를 바꿔 보세요." : hidden ? "‘완료된 작업도 보기’로 다시 확인할 수 있어요." : "위에 할 일을 쓰고 Enter를 누르세요.";
                list.Children.Add(Empty(title, help));
            }
            string lastDate = null;
            foreach (Todo item in items)
            {
                if (history && item.Date != lastDate)
                {
                    DateTime date = DateTime.ParseExact(item.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                    var heading = new Button { Content = date.ToString("yyyy년 M월 d일 dddd", korean), FontWeight = FontWeights.Bold, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-4,lastDate == null ? 0 : 16,0,4), Padding = new Thickness(4,5,4,5), ToolTip = "이 날짜의 메모 열기" };
                    heading.Click += delegate { selected = date; SetMode(false); };
                    list.Children.Add(heading); lastDate = item.Date;
                }
                list.Children.Add(TaskRow(item));
            }
            int total = Store.Data.Tasks.Count(t => t.Date == Store.Key(selected));
            int done = Store.Data.Tasks.Count(t => t.Date == Store.Key(selected) && t.Done);
            Find<TextBlock>("Status").Foreground = Brush(saveFailed ? "Error" : "Muted");
            Find<TextBlock>("Status").Text = saveFailed ? "저장 실패 · 메뉴에서 다시 저장" : history ? "" + items.Count + "개 기록 · 날짜별로 모아 보기" : "" + done + " / " + total + " 완료  ·  자동 저장";
            Find<Grid>("Footer").Visibility = expanded || saveFailed ? Visibility.Visible : Visibility.Collapsed;
        }
        FrameworkElement Empty(string title, string help)
        {
            if (!expanded && !history)
                return new TextBlock { Text = "할 일을 적어보세요.", Foreground = Brush("Muted"), FontSize = 12, Margin = new Thickness(3,16,0,0) };
            bool compact = Window.ActualHeight < 530;
            var stack = new StackPanel { Margin = new Thickness(4, compact ? 4 : 32,4,compact ? 4 : 20) };
            if (!compact) stack.Children.Add(new TextBlock { Text = "\uE70B", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 24, Foreground = Brush("Muted"), Margin = new Thickness(0,0,0,14) });
            stack.Children.Add(new TextBlock { Text = title, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,6) });
            stack.Children.Add(new TextBlock { Text = help, FontSize = 11, Foreground = Brush("Muted"), TextWrapping = TextWrapping.Wrap, LineHeight = 19 });
            return stack;
        }
        FrameworkElement TaskRow(Todo item)
        {
            var row = new Grid { Margin = new Thickness(0,0,0,0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(27) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(expanded ? 28 : 0) });
            var check = new CheckBox { IsChecked = item.Done, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0,6,0,0), ToolTip = item.Done ? "완료 취소" : "완료로 표시" };
            AutomationProperties.SetName(check, item.Text + (item.Done ? " · 완료" : " · 미완료"));
            check.Tag = item.Id;
            check.Click += delegate
            {
                bool restoreFocus = check.IsKeyboardFocused;
                item.Done = check.IsChecked == true; item.CompletedAt = item.Done ? DateTimeOffset.Now.ToString("o") : null; Persist(); Render();
                if (restoreFocus) { CheckBox next = renderedChecks.FirstOrDefault(c => (string)c.Tag == item.Id); if (next != null) next.Focus(); }
            };
            renderedChecks.Add(check); row.Children.Add(check);
            var text = TaskText(item);
            var edit = new Button { Content = text, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(3,10,3,10), ToolTip = "클릭하여 내용·날짜 수정" };
            text.HorizontalAlignment = HorizontalAlignment.Left;
            edit.HorizontalAlignment = HorizontalAlignment.Stretch;
            edit.Click += delegate(object sender, RoutedEventArgs e) { if (!(e.OriginalSource is Hyperlink)) Edit(item); };
            AutomationProperties.SetName(edit, item.Text + " 수정");
            Grid.SetColumn(edit, 1); row.Children.Add(edit);
            var more = new Button { Content = "\uE712", Style = (Style)Window.FindResource("IconButton"), Width = 28, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0,5,0,0), ToolTip = "할 일 메뉴" };
            more.Click += delegate { ShowTaskMenu(item, more); };
            more.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            var menu = new ContextMenu { Resources = Window.Resources };
            menu.Items.Add(MenuItem("내용·날짜 수정", delegate { Edit(item); }));
            menu.Items.Add(MenuItem("삭제", delegate { Delete(item); }));
            row.ContextMenu = menu;
            AutomationProperties.SetName(more, item.Text + " 메뉴");
            Grid.SetColumn(more, 2); row.Children.Add(more);
            return new Border { Child = row, BorderBrush = Brush("Rule"), BorderThickness = new Thickness(0,0,0,expanded ? 0.5 : 0) };
        }
        static string WebAddress(string value)
        {
            value = value.TrimEnd('.', ',', ';', '!', ':');
            while (value.Length > 0)
            {
                int closing = ")]}".IndexOf(value[value.Length-1]);
                if (closing < 0 || value.Count(c => c == ")]}"[closing]) <= value.Count(c => c == "([{"[closing])) break;
                value = value.Substring(0,value.Length-1);
            }
            return value;
        }
        TextBlock TaskText(Todo item)
        {
            var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 22, VerticalAlignment = VerticalAlignment.Center, Foreground = Brush(item.Done ? "Muted" : "Ink") };
            if (item.Done) text.TextDecorations = TextDecorations.Strikethrough;
            int position = 0;
            foreach (Match match in webLinks.Matches(item.Text))
            {
                string address = WebAddress(match.Value);
                Uri uri;
                if (!Uri.TryCreate(address.StartsWith("www.",StringComparison.OrdinalIgnoreCase) ? "https://" + address : address, UriKind.Absolute, out uri) ||
                    (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) || String.IsNullOrEmpty(uri.Host)) continue;
                text.Inlines.Add(new Run(item.Text.Substring(position,match.Index-position)));
                var link = new Hyperlink(new Run(address)) { NavigateUri = uri, Foreground = item.Done ? Brush("Muted") : Hex("#365B7A"), Cursor = Cursors.Hand, ToolTip = "브라우저에서 열기\n" + uri.AbsoluteUri };
                if (item.Done) link.TextDecorations = new TextDecorationCollection { TextDecorations.Underline[0], TextDecorations.Strikethrough[0] };
                AutomationProperties.SetName(link,address + " · 브라우저에서 열기");
                link.Click += delegate(object sender, RoutedEventArgs e) { e.Handled = true; };
                link.RequestNavigate += delegate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
                {
                    e.Handled = true;
                    try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
                    catch (Exception error) { MessageBox.Show(Window,"링크를 열지 못했습니다.\n" + error.Message,"하루 메모",MessageBoxButton.OK,MessageBoxImage.Information); }
                };
                text.Inlines.Add(link);
                position = match.Index + address.Length;
            }
            text.Inlines.Add(new Run(item.Text.Substring(position)));
            return text;
        }
        MenuItem MenuItem(string title, Action action)
        {
            var item = new MenuItem { Header = title, Tag = title == "삭제" ? "delete" : "", Style = (Style)Window.FindResource(typeof(MenuItem)) }; item.Click += delegate { action(); }; return item;
        }
        ContextMenu ShowTaskMenu(Todo item, Button owner)
        {
            var menu = new ContextMenu { Resources = Window.Resources, PlacementTarget = owner, Placement = PlacementMode.Bottom };
            menu.Items.Add(MenuItem("내용·날짜 수정", delegate { Edit(item); }));
            menu.Items.Add(MenuItem(item.Done ? "완료 취소" : "완료로 표시", delegate { item.Done = !item.Done; item.CompletedAt = item.Done ? DateTimeOffset.Now.ToString("o") : null; Persist(); Render(); }));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItem("삭제", delegate { Delete(item); }));
            menu.IsOpen = true;
            return menu;
        }
        public void Delete(Todo item)
        {
            if (Store.Data.Tasks.Remove(item))
            {
                deleted.Push(item); Persist(); Render();
                Find<Border>("UndoNotice").Visibility = Visibility.Visible;
                undoTimer.Stop(); undoTimer.Start();
            }
        }
        void HideUndoNotice() { undoTimer.Stop(); Find<Border>("UndoNotice").Visibility = Visibility.Collapsed; }
        public void Undo() { if (deleted.Count > 0) { Store.Data.Tasks.Add(deleted.Pop()); HideUndoNotice(); Persist(); Render(); } }
        Rect WorkArea(Window target)
        {
            IntPtr handle = new System.Windows.Interop.WindowInteropHelper(target).Handle;
            if (handle == IntPtr.Zero) return SystemParameters.WorkArea;
            var area = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
            var source = System.Windows.Interop.HwndSource.FromHwnd(handle);
            Matrix transform = source.CompositionTarget.TransformFromDevice;
            return new Rect(transform.Transform(new Point(area.Left,area.Top)),transform.Transform(new Point(area.Right,area.Bottom)));
        }
        public void DockToIndex()
        {
            if (docked) return;
            SaveGeometry(); Persist(); geometryTimer.Stop();
            Rect area = WorkArea(Window);
            if (indexWindow == null)
            {
                indexWindow = new Window { Title = "하루 메모 · 인덱스", Width = 38, Height = 78, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                    AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false, ShowActivated = false, Topmost = true, Icon = Window.Icon, FontFamily = Window.FontFamily, Resources = Window.Resources };
                var label = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                label.Children.Add(new TextBlock { Text = "\uE70B", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,0,0,7) });
                label.Children.Add(new TextBlock { Text = "메모", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center });
                indexButton = new Button { Content = label, Padding = new Thickness(0), Style = (Style)Window.FindResource(typeof(Button)), ToolTip = "메모 열기 · 드래그하여 높이 조절" };
                AutomationProperties.SetName(indexButton,"하루 메모 다시 열기");
                indexWindow.Content = new Border { Child = indexButton, CornerRadius = new CornerRadius(7,0,0,7), Background = Brush("Paper"), BorderBrush = Brush("Rule"), BorderThickness = new Thickness(1,1,0,1) };
                indexButton.Click += delegate { RestoreFromIndex(); };
                Point pressed = new Point(); bool dragged = false;
                indexButton.PreviewMouseLeftButtonDown += delegate { pressed = Mouse.GetPosition(indexWindow); dragged = false; };
                indexButton.PreviewMouseMove += delegate(object sender, MouseEventArgs e)
                {
                    if (e.LeftButton != MouseButtonState.Pressed || dragged || (Mouse.GetPosition(indexWindow)-pressed).Length < 5) return;
                    dragged = true; indexButton.ReleaseMouseCapture();
                    try { indexWindow.DragMove(); } catch (InvalidOperationException) { }
                    Rect currentArea = WorkArea(indexWindow);
                    indexWindow.Left = currentArea.Right-indexWindow.Width;
                    indexWindow.Top = Math.Max(currentArea.Top,Math.Min(indexWindow.Top,currentArea.Bottom-indexWindow.Height));
                    Store.Data.Settings.IndexTop = indexWindow.Top; Persist(); e.Handled = true;
                };
                indexButton.PreviewMouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e) { if(dragged) e.Handled = true; };
                var menu = new ContextMenu { Resources = Window.Resources };
                menu.Items.Add(MenuItem("메모 열기", RestoreFromIndex));
                menu.Items.Add(MenuItem("작업 표시줄로 최소화", MinimizeToTaskbar));
                indexButton.ContextMenu = menu;
                indexWindow.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { if(!shuttingDown) { e.Cancel=true; RestoreFromIndex(); } };
            }
            ((Border)indexWindow.Content).Background = Brush("Paper");
            ((Border)indexWindow.Content).BorderBrush = Brush("Rule");
            indexWindow.Left = area.Right-indexWindow.Width;
            double top = Store.Data.Settings.IndexTop < 0 ? Window.Top : Store.Data.Settings.IndexTop;
            indexWindow.Top = Math.Max(area.Top,Math.Min(top,area.Bottom-indexWindow.Height));
            docked = true; Window.Hide(); indexWindow.Show();
        }
        public void RestoreFromIndex()
        {
            if (indexWindow != null) indexWindow.Hide();
            docked = false;
            Window.Show(); Window.WindowState = WindowState.Normal; ApplyTopmost(); Window.Activate();
        }
        public void MinimizeToTaskbar()
        {
            if(indexWindow != null) indexWindow.Hide();
            docked = false; SaveGeometry(); Persist();
            Window.WindowState = WindowState.Minimized; Window.Show();
        }
        void Edit(Todo item)
        {
            var dialog = new Window { Title = "메모 수정", Owner = Window, Width = 350, Height = 355, ResizeMode = ResizeMode.NoResize, WindowStyle = WindowStyle.None, AllowsTransparency = true, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brushes.Transparent, Foreground = Brush("Ink"), FontFamily = Window.FontFamily, FontSize = 13, ShowInTaskbar = false, Topmost = Window.Topmost };
            dialog.Resources = Window.Resources;
            NameScope.SetNameScope(dialog,new NameScope());
            var panel = new StackPanel { Margin = new Thickness(20) };
            var header = new DockPanel { Margin = new Thickness(0,0,0,18) };
            var close = new Button { Content = "\uE8BB", Style = (Style)Window.FindResource("IconButton"), IsCancel = true, FontSize = 12, Width = 28 };
            DockPanel.SetDock(close,Dock.Right); header.Children.Add(close);
            header.Children.Add(new TextBlock { Text = "메모 수정", FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            header.MouseLeftButtonDown += delegate(object sender,MouseButtonEventArgs e) { if (e.OriginalSource is TextBlock && e.ButtonState == MouseButtonState.Pressed) dialog.DragMove(); };
            panel.Children.Add(header);
            panel.Children.Add(new TextBlock { Text = "할 일", Margin = new Thickness(0,0,0,6) });
            var input = new TextBox { Text = item.Text, MaxLength = 1000, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 86, VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Style = (Style)Window.FindResource(typeof(TextBox)) };
            panel.Children.Add(input);
            dialog.RegisterName("EditText",input);
            panel.Children.Add(new TextBlock { Text = "메모 날짜", Margin = new Thickness(0,12,0,6) });
            var picker = new DatePicker { SelectedDate = DateTime.ParseExact(item.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture), SelectedDateFormat = DatePickerFormat.Short };
            panel.Children.Add(picker);
            dialog.RegisterName("EditDate",picker);
            var errorLabel = new TextBlock { Foreground = Brush("Error"), FontSize = 11, Margin = new Thickness(0,6,0,0), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
            panel.Children.Add(errorLabel);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,12,0,0) };
            var cancel = new Button { Content = "취소", IsCancel = true, Style = (Style)Window.FindResource(typeof(Button)) };
            var save = new Button { Content = "저장", IsDefault = true, Style = (Style)Window.FindResource("Primary"), Margin = new Thickness(8,0,0,0) };
            dialog.RegisterName("EditSave",save);
            buttons.Children.Add(cancel); buttons.Children.Add(save); panel.Children.Add(buttons);
            dialog.Content = new Border { Child = panel, Background = Brush("Paper"), BorderBrush = Brush("Rule"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8) };
            save.Click += delegate
            {
                if (String.IsNullOrWhiteSpace(input.Text) || !picker.SelectedDate.HasValue) { errorLabel.Text = "할 일과 유효한 날짜를 입력해 주세요."; errorLabel.Visibility = Visibility.Visible; return; }
                item.Text = input.Text.Trim(); item.Date = Store.Key(picker.SelectedDate.Value);
                Persist(); dialog.DialogResult = true; Render();
            };
            dialog.Loaded += delegate { input.Focus(); input.SelectAll(); };
            dialog.ShowDialog();
        }
        void ShowMenu()
        {
            var menu = new ContextMenu { Resources = Window.Resources, PlacementTarget = Find<Button>("MenuButton"), Placement = PlacementMode.Bottom };
            menu.Items.Add(MenuItem("오른쪽 인덱스로 접기", DockToIndex));
            menu.Items.Add(MenuItem("작업 표시줄로 최소화", MinimizeToTaskbar));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItem("현재 목록을 CSV로 내보내기", delegate { Export(false); }));
            menu.Items.Add(MenuItem("전체 기록을 CSV로 내보내기", delegate { Export(true); }));
            menu.Items.Add(MenuItem("전체 데이터 백업 (JSON)", Backup));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItem("다시 저장", delegate { SaveGeometry(); Persist(); Render(); }));
            menu.Items.Add(MenuItem("사용 방법", delegate { MessageBox.Show(Window,
                "기본은 날짜·입력창·체크리스트만 보이는 작은 메모지입니다.\n확장 아이콘으로 설정과 기록을 열고, 다시 누르면 접습니다.\n− 버튼은 오른쪽의 작은 인덱스 탭으로 접습니다.\n탭을 클릭하면 메모가 다시 열립니다. 탭을 위아래로 드래그할 수도 있어요.\n메뉴의 ‘작업 표시줄로 최소화’ 또는 Ctrl+M은 기존 최소화입니다.\n\n날짜 부분을 드래그하면 메모지를 옮길 수 있어요.\n가장자리로 크기를 조절하세요.\n확장 화면의 핀 버튼으로 항상 위에 고정하고, 색 점으로 색상을 바꾸세요.\n\n할 일을 쓰고 Enter로 추가하세요.\n체크한 할 일은 취소선으로 남습니다.\n글을 클릭하면 내용과 날짜를 수정할 수 있어요.\n작업을 오른쪽 클릭해서 삭제하세요. 되돌리기 알림은 7초 후 사라집니다.\n알림이 사라진 후에도 입력창 밖에서 Ctrl+Z로 삭제를 취소할 수 있어요.\n\nCtrl+N 새 할 일 · Ctrl+H 기록\nCtrl+T 오늘 · Ctrl+S 저장\n\n내용과 창 설정은 자동 저장됩니다.\n데이터 위치: " + Path.GetDirectoryName(Store.PathName), "하루 메모 사용 방법", MessageBoxButton.OK, MessageBoxImage.Information); }));
            menu.IsOpen = true;
        }
        void Export(bool all)
        {
            var dialog = new SaveFileDialog { Title = all ? "전체 기록 내보내기" : "현재 목록 내보내기", Filter = "CSV 파일 (*.csv)|*.csv", FileName = "하루메모-" + (all ? "전체" : Store.Key(selected)) + ".csv" };
            if (dialog.ShowDialog(Window) != true) return;
            try { File.WriteAllText(dialog.FileName, Store.Csv(all ? (IEnumerable<Todo>)Store.Data.Tasks.OrderByDescending(t => t.Date).ThenBy(t => t.CreatedAt) : VisibleTasks()), new UTF8Encoding(true)); }
            catch (Exception error) { MessageBox.Show(Window, "파일을 내보내지 못했습니다.\n" + error.Message, "내보내기 오류"); }
        }
        void Backup()
        {
            var dialog = new SaveFileDialog { Title = "전체 데이터 백업", Filter = "JSON 파일 (*.json)|*.json", FileName = "하루메모-백업-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json" };
            if (dialog.ShowDialog(Window) != true || !Persist()) return;
            try { if (!String.Equals(Path.GetFullPath(dialog.FileName), Path.GetFullPath(Store.PathName), StringComparison.OrdinalIgnoreCase)) File.Copy(Store.PathName, dialog.FileName, true); }
            catch (Exception error) { MessageBox.Show(Window, "백업을 저장하지 못했습니다.\n" + error.Message, "백업 오류"); }
        }
        public void Capture(string path)
        {
            Window.UpdateLayout();
            CaptureElement(Window,path);
        }
        static void CaptureElement(FrameworkElement element, string path)
        {
            element.UpdateLayout();
            var bitmap = new RenderTargetBitmap(Math.Max(1,(int)element.ActualWidth), Math.Max(1,(int)element.ActualHeight), 96,96,PixelFormats.Pbgra32);
            bitmap.Render(element);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream stream = File.Create(path)) png.Save(stream);
        }
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
        static long NativeStyle(IntPtr hwnd, int index) { return IntPtr.Size == 8 ? GetWindowLongPtr(hwnd,index).ToInt64() : GetWindowLong(hwnd,index); }
        static void FlushUi(Dispatcher dispatcher)
        {
            var frame = new DispatcherFrame();
            dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate { frame.Continue = false; }));
            Dispatcher.PushFrame(frame);
        }
        static void WaitForTopmost(IntPtr handle)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            int ticks = 0;
            timer.Tick += delegate { if ((NativeStyle(handle,-20) & 8) != 0 || ++ticks >= 25) { timer.Stop(); frame.Continue = false; } };
            timer.Start(); Dispatcher.PushFrame(frame);
        }
        static void PumpFor(int milliseconds)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += delegate { timer.Stop(); frame.Continue = false; };
            timer.Start(); Dispatcher.PushFrame(frame);
        }
        public void UiCheck(string dir)
        {
            Window.Show(); Window.Activate(); FlushUi(Window.Dispatcher);
            Capture(Path.Combine(dir,"compact-empty.png"));
            Require(!expanded && !history && Math.Abs(Window.Width-330)<2 && Math.Abs(Window.Height-360)<2, "기본 작은 메모 " + Window.Width + "x" + Window.Height);
            Require(Find<StackPanel>("ExpandedTools").Visibility == Visibility.Collapsed && Find<StackPanel>("DayHeader").Visibility == Visibility.Collapsed && Find<Grid>("Footer").Visibility == Visibility.Collapsed, "기본 확장 UI 숨김");
            Capture(Path.Combine(dir,"compact-empty.png"));
            DateTime today = DateTime.Today;
            var transparencySlider = Find<Slider>("TransparencySlider");
            Require(transparencySlider.IsVisible && Window.Opacity == 1,"접힌 화면의 투명도 슬라이더와 기본 불투명 상태");
            transparencySlider.Value = 80;
            Require(Math.Abs(Window.Opacity-0.2)<0.001 && Store.Data.Settings.Transparency == 80 && Find<TextBlock>("TransparencyValue").Text == "80%","투명도 즉시 반영과 최댓값");
            transparencySlider.Value = 0;
            Require(Window.Opacity == 1,"투명도 0%로 복원");
            transparencySlider.ApplyTemplate();
            var transparencyTrack = (Track)transparencySlider.Template.FindName("PART_Track",transparencySlider);
            transparencyTrack.Thumb.RaiseEvent(new DragDeltaEventArgs(30,0) { RoutedEvent = Thumb.DragDeltaEvent });
            Require(transparencySlider.Value > 0 && Window.Opacity < 1,"투명도 손잡이 드래그 동작");
            transparencySlider.Value = 0;
            Todo compactDateTask = Store.Add(today.AddDays(-2),"접힌 화면에서 날짜 이동 확인"); compactDateTask.Done = true; Render();
            Find<Button>("DateButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); FlushUi(Window.Dispatcher);
            var compactPopup = Find<Popup>("HeaderDatePopup");
            var compactCalendar = Find<Calendar>("HeaderCalendar");
            Require(compactPopup.IsOpen && !expanded && compactCalendar.SelectedDate == today,"접힌 화면의 날짜 클릭으로 달력 열기");
            var compactBody = (CalendarItem)compactCalendar.Template.FindName("PART_CalendarItem",compactCalendar);
            var compactMonth = (Grid)compactBody.Template.FindName("PART_MonthView",compactBody);
            ((Button)compactBody.Template.FindName("PART_NextButton",compactBody)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(compactPopup.IsOpen && compactCalendar.DisplayDate.Month == today.AddMonths(1).Month,"상단 달력의 다음 달 이동");
            ((Button)compactBody.Template.FindName("PART_PreviousButton",compactBody)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); FlushUi(Window.Dispatcher);
            var compactRecordedDay = compactMonth.Children.OfType<CalendarDayButton>().First(b => b.DataContext is DateTime && ((DateTime)b.DataContext).Date == today.AddDays(-2));
            Require(Object.Equals(compactRecordedDay.Tag,true),"상단 달력의 기록 날짜 색상 표시");
            CaptureElement(compactCalendar,Path.Combine(dir,"compact-calendar.png"));
            compactRecordedDay.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); FlushUi(Window.Dispatcher);
            Require(!compactPopup.IsOpen && !expanded && selected == today.AddDays(-2) && VisibleTasks().Count == 1,"날짜 선택 후 접힌 상태로 해당 목록 이동");
            Find<Button>("DateButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); FlushUi(Window.Dispatcher);
            var compactSelectedDay = compactMonth.Children.OfType<CalendarDayButton>().First(b => b.DataContext is DateTime && ((DateTime)b.DataContext).Date == selected);
            compactSelectedDay.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(!compactPopup.IsOpen,"현재 날짜를 다시 선택해도 달력 닫기");
            Find<Button>("DateButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); FlushUi(Window.Dispatcher);
            compactCalendar.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(Window),0,Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Require(!compactPopup.IsOpen,"Escape로 상단 달력 닫기");
            Store.Data.Tasks.Remove(compactDateTask); SelectDate(today);
            var linkItem = new Todo { Id = "link-check", Date = Store.Key(today), Text = "자료 (https://example.com/a_(b)?q=1&next=2#part), www.example.org/path. 끝 https://예시.한국/문서" };
            var linkRow = (Border)TaskRow(linkItem);
            var linkEdit = ((Grid)linkRow.Child).Children.OfType<Button>().First();
            var linkText = (TextBlock)linkEdit.Content;
            var detectedLinks = linkText.Inlines.OfType<Hyperlink>().ToList();
            Require(detectedLinks.Count == 3 && detectedLinks[0].NavigateUri.AbsoluteUri == "https://example.com/a_(b)?q=1&next=2#part" && detectedLinks[1].NavigateUri.AbsoluteUri == "https://www.example.org/path", "웹 주소 감지, 여러 링크, 문장 부호 제외");
            Require(new TextRange(linkText.ContentStart,linkText.ContentEnd).Text == linkItem.Text, "링크 변환 시 원문 보존");
            bool editClicked = false;
            linkEdit.Click += delegate { editClicked = true; };
            var linkClick = new RoutedEventArgs(Hyperlink.ClickEvent,detectedLinks[0]);
            detectedLinks[0].RaiseEvent(linkClick);
            Require(linkClick.Handled && !editClicked, "링크 클릭은 수정창을 열지 않음");
            linkItem.Done = true;
            Require(TaskText(linkItem).Inlines.OfType<Hyperlink>().All(l => l.TextDecorations.Count == 2), "완료한 링크도 취소선과 연결 유지");
            Require(!TaskText(new Todo { Text = "javascript:alert(1) file:///C:/Windows/calc.exe 일반 메모" }).Inlines.OfType<Hyperlink>().Any(), "웹 주소만 링크로 처리");
            Require(TaskText(new Todo { Text = "HTTP://example.com/ [https://example.com/a_(b)]" }).Inlines.OfType<Hyperlink>().Count() == 2, "HTTP와 대문자 주소 감지");
            Find<StackPanel>("TaskList").Children.Clear(); Find<StackPanel>("TaskList").Children.Add(linkRow); FlushUi(Window.Dispatcher);
            Require(detectedLinks.All(l => l.IsEnabled), "목록 링크 클릭 가능");
            Capture(Path.Combine(dir,"links.png")); Render();
            Find<TextBox>("NewTask").Text = "화면과 저장 상태 확인하기";
            Find<Button>("AddButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(VisibleTasks().Count == 1, "추가 버튼");
            renderedChecks[0].IsChecked = true;
            renderedChecks[0].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Require(VisibleTasks()[0].Done, "체크 완료");
            Find<Button>("ExpandButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(expanded && Window.Height >= 480 && Find<StackPanel>("ExpandedTools").Visibility == Visibility.Visible, "확장 버튼");
            Find<CheckBox>("ShowCompleted").IsChecked = false;
            Find<CheckBox>("ShowCompleted").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Require(VisibleTasks().Count == 0, "완료 숨기기");
            Find<CheckBox>("ShowCompleted").IsChecked = true;
            Find<CheckBox>("ShowCompleted").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Store.Add(today, "책상 정리하고 내일 할 일 적기");
            Store.Add(today.AddDays(-1), "어제 남긴 메모").Done = true;
            Store.Add(today.AddDays(-7), "지난주 회의 내용 정리");
            Store.Add(today, "길게 쓴 메모도 창 너비에 맞춰 자연스럽게 여러 줄로 표시되는지 확인합니다.");
            Persist(); Render(); Capture(Path.Combine(dir, "note.png"));
            Find<Button>("ExpandButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(!expanded && !history && VisibleTasks().Count == 3 && Find<Grid>("Footer").Visibility == Visibility.Collapsed, "접기와 항목 보존");
            Capture(Path.Combine(dir,"compact-note.png"));
            Find<Button>("ExpandButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Find<Button>("PinButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            FlushUi(Window.Dispatcher);
            Require(Window.Topmost && Store.Data.Settings.Topmost, "항상 위");
            IntPtr nativeHandle = new System.Windows.Interop.WindowInteropHelper(Window).Handle;
            WaitForTopmost(nativeHandle);
            Require((NativeStyle(nativeHandle,-20) & 8) != 0, "네이티브 항상 위 플래그: " + NativeStyle(nativeHandle,-20).ToString("X") + " visible=" + Window.IsVisible + " state=" + Window.WindowState + " handles=" + nativeHandle + "/" + Process.GetCurrentProcess().MainWindowHandle);
            Require(Window.ResizeMode == ResizeMode.CanResizeWithGrip, "메모 크기 조절 손잡이 설정");
            Find<Button>("PinButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Find<Button>("PrevButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(VisibleTasks().Count == 1, "날짜 이동");
            Find<Button>("TodayButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var picker = Find<DatePicker>("DayPicker");
            picker.IsDropDownOpen = true; FlushUi(Window.Dispatcher);
            var calendarPopup = (Popup)picker.Template.FindName("PART_Popup",picker);
            var calendar = (Calendar)calendarPopup.Child;
            Require(calendar != null && calendar.ActualWidth > 200,"종이 달력 팝업");
            CaptureElement(calendar,Path.Combine(dir,"calendar.png"));
            var calendarBody = (CalendarItem)calendar.Template.FindName("PART_CalendarItem",calendar);
            var calendarNext = (Button)calendarBody.Template.FindName("PART_NextButton",calendarBody);
            var calendarPrevious = (Button)calendarBody.Template.FindName("PART_PreviousButton",calendarBody);
            DateTime displayed = calendar.DisplayDate;
            calendarNext.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(calendar.DisplayDate.Month == displayed.AddMonths(1).Month,"달력 다음 달");
            calendarPrevious.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(calendar.DisplayDate.Month == displayed.Month,"달력 이전 달");
            var monthView = (Grid)calendarBody.Template.FindName("PART_MonthView",calendarBody);
            FlushUi(Window.Dispatcher);
            var recordedDay = monthView.Children.OfType<CalendarDayButton>().First(b => b.DataContext is DateTime && ((DateTime)b.DataContext).Date == today.AddDays(-1));
            Require(Object.Equals(recordedDay.Tag,true), "완료 기록이 있는 날짜 표시: " + recordedDay.Tag + " calendar=" + calendar.Tag);
            Require(((Border)recordedDay.Template.FindName("Day",recordedDay)).Background == Brush("Hover"), "기록 날짜 배경색");
            Require(((FrameworkElement)recordedDay.Template.FindName("RecordDot",recordedDay)).Visibility == Visibility.Visible, "기록 날짜 점 표시");
            SetColor(1); Render(); FlushUi(Window.Dispatcher);
            Require(((Border)recordedDay.Template.FindName("Day",recordedDay)).Background == Brush("Hover"), "메모 색상 변경 시 날짜 표시 색상 갱신");
            SetColor(0); Render(); FlushUi(Window.Dispatcher);
            var recordDay = monthView.Children.OfType<CalendarDayButton>().First(b => b.DataContext is DateTime && ((DateTime)b.DataContext).Date == today.AddDays(1));
            Require(Object.Equals(recordDay.Tag,false), "기록 없는 날짜 표시 없음");
            Todo calendarTask = Store.Add(today.AddDays(1), "달력 표시 검증"); Render(); FlushUi(Window.Dispatcher);
            Require(Object.Equals(recordDay.Tag,true), "기록 추가 시 날짜 표시 갱신");
            calendarTask.Date = Store.Key(today.AddDays(2)); Render(); FlushUi(Window.Dispatcher);
            Require(Object.Equals(recordDay.Tag,false), "기록 날짜 이동 시 이전 표시 제거");
            calendarTask.Date = Store.Key(today.AddDays(1)); Render(); FlushUi(Window.Dispatcher);
            Delete(calendarTask); FlushUi(Window.Dispatcher);
            Require(Object.Equals(recordDay.Tag,false), "마지막 기록 삭제 시 날짜 표시 제거");
            Undo(); FlushUi(Window.Dispatcher);
            Require(Object.Equals(recordDay.Tag,true), "삭제 취소 시 날짜 표시 복구");
            Store.Data.Tasks.Remove(calendarTask); Render(); FlushUi(Window.Dispatcher);
            var dayButton = monthView.Children.OfType<CalendarDayButton>().First(b => b.DataContext is DateTime && ((DateTime)b.DataContext).Date == today.AddDays(1));
            dayButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(picker.SelectedDate.HasValue && picker.SelectedDate.Value.Date == today.AddDays(1),"달력 날짜 선택");
            picker.IsDropDownOpen = false;
            Find<Button>("TodayButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Find<Button>("HistoryTab").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(VisibleTasks().Count == 5, "전체 기록");
            Capture(Path.Combine(dir, "history.png"));
            Find<ComboBox>("HistoryFilter").SelectedIndex = 1;
            Require(VisibleTasks().Count == 2, "완료 기록 필터");
            Find<TextBox>("SearchBox").Text = "어제";
            Require(VisibleTasks().Count == 1, "검색");
            Find<DatePicker>("FromPicker").SelectedDate = today;
            Require(VisibleTasks().Count == 0, "기간 필터");
            Find<Button>("ClearFilter").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(VisibleTasks().Count == 5, "필터 초기화");
            Todo removed = VisibleTasks()[0];
            var taskMenu = ShowTaskMenu(removed,Find<Button>("ExpandButton")); FlushUi(Window.Dispatcher);
            CaptureElement(taskMenu,Path.Combine(dir,"task-menu.png"));
            taskMenu.Items.OfType<MenuItem>().Last().RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent)); taskMenu.IsOpen = false;
            Require(VisibleTasks().Count == 4 && Find<Border>("UndoNotice").Visibility == Visibility.Visible, "삭제 알림");
            Capture(Path.Combine(dir,"delete-notice.png"));
            Undo(); Require(VisibleTasks().Count == 5 && Find<Border>("UndoNotice").Visibility == Visibility.Collapsed, "삭제 취소와 알림 닫기");
            Require(undoTimer.Interval == TimeSpan.FromSeconds(7),"7초 삭제 알림");
            undoTimer.Interval = TimeSpan.FromMilliseconds(50); Delete(removed); PumpFor(100);
            Require(Find<Border>("UndoNotice").Visibility == Visibility.Collapsed,"삭제 알림 자동 사라짐");
            Undo(); undoTimer.Interval = TimeSpan.FromSeconds(7);
            SetMode(false);
            Todo edited = Store.Data.Tasks.First(t => t.Date == Store.Key(today));
            bool editSaved = false;
            Window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate
            {
                Window dialog = Application.Current.Windows.OfType<Window>().First(w => w.Title == "메모 수정");
                TextBox input = (TextBox)dialog.FindName("EditText");
                DatePicker date = (DatePicker)dialog.FindName("EditDate");
                input.Text = "수정한 메모"; date.SelectedDate = today.AddDays(2);
                FlushUi(dialog.Dispatcher);
                dialog.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)dialog.ActualWidth, (int)dialog.ActualHeight,96,96,PixelFormats.Pbgra32);
                bitmap.Render(dialog); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using(FileStream stream = File.Create(Path.Combine(dir,"edit.png"))) png.Save(stream);
                ((Button)dialog.FindName("EditSave")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                editSaved = true;
            }));
            Edit(edited);
            Require(editSaved && edited.Text == "수정한 메모" && edited.Date == Store.Key(today.AddDays(2)), "내용과 날짜 수정");
            edited.Date = Store.Key(today); edited.Text = "화면과 저장 상태 확인하기";
            for (int i = 0; i < colors.Length; i++) { SetColor(i); Render(); Capture(Path.Combine(dir, "color-" + i + ".png")); }
            foreach (int width in new[] {320,375,414,768}) { Window.Width = width; Window.Height = 610; Render(); Capture(Path.Combine(dir, "width-" + width + ".png")); }
            SetMode(true); Window.Width = 320; Render(); Capture(Path.Combine(dir, "history-320.png"));
            Window.Height = 480; Render(); Capture(Path.Combine(dir, "history-minimum.png"));
            SetMode(false); SelectDate(today.AddDays(10)); Render(); Capture(Path.Combine(dir,"empty-minimum.png"));
            SelectDate(today); SetExpanded(false);
            foreach (int width in new[] {280,320,375}) { Window.Width = width; Window.Height = 350; Render(); Capture(Path.Combine(dir,"compact-width-" + width + ".png")); }
            Window.Width = 320; Window.Height = 350; SaveGeometry();
            SetMode(true);
            Require(expanded && history && Find<StackPanel>("HistoryHeader").Visibility == Visibility.Visible, "기록 열 때 자동 확장");
            SetExpanded(false);
            Require(!history && Math.Abs(Window.Width-320)<2 && Math.Abs(Window.Height-350)<2 && VisibleTasks().Count == 3, "기록에서 접기와 크기 복원");
            Window.Width = 280; Window.Height = 240; Render(); Capture(Path.Combine(dir,"compact-minimum.png"));
            SaveGeometry();
            double originalWidth = Window.Width, originalHeight = Window.Height;
            Find<Button>("DockButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); FlushUi(Window.Dispatcher);
            Require(docked && !Window.IsVisible && indexWindow.IsVisible && indexWindow.Topmost && !indexWindow.ShowInTaskbar,"작은 인덱스로 접기");
            Require(indexWindow.ActualWidth < 50 && indexWindow.ActualHeight < 100 && Math.Abs(indexWindow.Left+indexWindow.Width-WorkArea(indexWindow).Right)<2,"오른쪽 가장자리 인덱스 크기와 위치");
            CaptureElement(indexWindow,Path.Combine(dir,"index-tab.png"));
            indexButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); FlushUi(Window.Dispatcher);
            Require(!docked && Window.IsVisible && !indexWindow.IsVisible && Math.Abs(Window.Width-originalWidth)<2 && Math.Abs(Window.Height-originalHeight)<2 && VisibleTasks().Count==3,"인덱스 클릭 복원");
            DockToIndex();
            Program.PostMessage(nativeHandle,Program.RestoreMessage,IntPtr.Zero,IntPtr.Zero); PumpFor(100);
            Require(!docked && Window.IsVisible && !indexWindow.IsVisible,"다시 실행 시 인덱스 복원");
            DockToIndex(); MinimizeToTaskbar(); FlushUi(Window.Dispatcher);
            Require(Window.WindowState==WindowState.Minimized && Window.ShowInTaskbar && !indexWindow.IsVisible,"기존 작업 표시줄 최소화 유지");
            RestoreFromIndex(); FlushUi(Window.Dispatcher);
            transparencySlider.Value = 40; PumpFor(650);
            var reloaded = new Store(Store.PathName); Require(reloaded.Data.Tasks.Count == 5 && reloaded.Data.Settings.Color == 4 && reloaded.Data.Settings.Transparency == 40, "재실행 저장과 투명도 자동 저장");
            var reopened = new NoteApp(reloaded);
            Require(Math.Abs(reopened.Window.Opacity-0.6)<0.001 && reopened.Find<Slider>("TransparencySlider").Value == 40,"투명도 설정 재실행 복원");
            Require(!reopened.expanded && Math.Abs(reopened.Window.Width-280)<2 && Math.Abs(reopened.Window.Height-240)<2 && reopened.Find<StackPanel>("ExpandedTools").Visibility == Visibility.Collapsed, "재실행은 항상 접힌 화면");
            reopened.Window.Close();
            File.WriteAllText(Path.Combine(dir, "ui-test-result.txt"), "PASS: transparency slider drag, opacity updates and persistence, compact header date calendar and navigation, web link detection and click routing, text preservation, completed links, minimal default, expansion/collapse, native topmost/resize, custom calendar navigation/day selection, recorded-date colors and dots, completed-date marking, live add/move/delete/undo/color updates, styled task menu, delete/undo notice and timed expiry, edit text/date, history/filter/search, right-edge index tab and click restoration, duplicate-launch restoration, normal taskbar minimization, size/settings/data persistence.");
        }
        static void Require(bool value, string message) { if (!value) throw new Exception("확인 실패: " + message); }
    }
    public static class Program
    {
        public const int RestoreMessage = 0x8068;
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern IntPtr FindWindow(string className, string title);
        [STAThread]
        public static int Main(string[] args)
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("ko-KR");
            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("ko-KR");
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            bool self = args.Length > 0 && args[0] == "--self-test";
            bool ui = args.Length > 0 && args[0] == "--ui-test";
            try
            {
                if (self) { SelfTest(args.Length > 1 ? args[1] : Path.Combine(baseDir,"test")); return 0; }
                string uiDir = args.Length > 1 ? args[1] : Path.Combine(baseDir,"test");
                bool created;
                using (var mutex = new Mutex(true, "Local\\HaruMemo-" + StableKey(baseDir), out created))
                {
                    if (!created && !ui)
                    {
                        IntPtr hwnd = FindWindow(null, "하루 메모"); if (hwnd != IntPtr.Zero) PostMessage(hwnd,RestoreMessage,IntPtr.Zero,IntPtr.Zero);
                        return 0;
                    }
                    string dataPath = ui ? Path.Combine(uiDir,"data",Guid.NewGuid().ToString("N") + ".json") : Path.Combine(baseDir, "data", "notes.json");
                    var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                    var note = new NoteApp(new Store(dataPath));
                    if (ui)
                    {
                        Directory.CreateDirectory(uiDir);
                        note.Window.Loaded += delegate
                        {
                            note.Window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate
                            {
                                try { note.UiCheck(uiDir); note.Window.Close(); }
                                catch (Exception error) { File.WriteAllText(Path.Combine(uiDir,"ui-test-result.txt"), error.ToString()); application.Shutdown(1); }
                            }));
                        };
                    }
                    application.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e)
                    {
                        MessageBox.Show(note.Window, "작업을 처리하지 못했습니다.\n" + e.Exception.Message, "하루 메모", MessageBoxButton.OK, MessageBoxImage.Warning); e.Handled = true;
                    };
                    return application.Run(note.Window);
                }
            }
            catch (Exception error)
            {
                if (self || ui)
                { string dir = args.Length > 1 ? args[1] : baseDir; Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir,"error.txt"),error.ToString()); }
                else MessageBox.Show("하루 메모를 열지 못했습니다.\n" + error.Message, "하루 메모", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }
        static string StableKey(string path)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(path.ToLowerInvariant()))).Replace("-", "").Substring(0,16);
        }
        static void Check(bool value, string name) { if (!value) throw new Exception(name); }
        static void SelfTest(string dir)
        {
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, Guid.NewGuid().ToString("N"), "notes.json");
            Store store = new Store(path);
            DateTime date = new DateTime(2026,10,2);
            Todo first = store.Add(date, "한글 메모, \"따옴표\"\n두 줄");
            Todo previous = store.Add(date.AddDays(-1), "지난 메모"); previous.Done = true; previous.CompletedAt = DateTimeOffset.Now.ToString("o");
            Check(store.Query(false,date,true,0,"",null,null).Count() == 1,"날짜 분리");
            first.Done = true;
            Check(store.Query(false,date,false,0,"",null,null).Count() == 0,"완료 숨김");
            Check(store.Data.Tasks.Count == 2,"완료 보존");
            Check(store.Query(true,date,true,1,"한글",date,date).Count() == 1,"완료/검색/기간");
            Check(store.Query(true,date,true,2,"",null,null).Count() == 0,"미완료 필터");
            Check(Store.Csv(store.Data.Tasks).Contains("\"\"따옴표\"\""),"CSV 인용");
            Todo formula = store.Add(date,"=1+1"); Check(Store.Csv(new[] {formula}).Contains("'=1+1"),"CSV 수식 방지");
            bool blankRejected = false; try {store.Add(date,"  ");} catch(ArgumentException) {blankRejected=true;} Check(blankRejected,"빈 할 일");
            store.Data.Settings.Color = 3; store.Data.Settings.Topmost = true; store.Data.Settings.Transparency = 35; store.Save();
            Store reload = new Store(path); Check(reload.Data.Tasks.Count == 3 && reload.Data.Settings.Color == 3 && reload.Data.Settings.Topmost && reload.Data.Settings.Transparency == 35,"영구 저장");
            reload.Data.Tasks[0].Text = "수정됨"; reload.Save(); Check(File.Exists(path+".bak"),"원자적 백업");
            File.WriteAllText(path,"{broken"); Store recovery = new Store(path); Check(recovery.Data.Tasks.Count == 3 && recovery.RecoveryNotice != null,"손상 파일 백업 복구");
            string invalid = Path.Combine(dir,Guid.NewGuid().ToString("N"),"notes.json"); Directory.CreateDirectory(Path.GetDirectoryName(invalid)); File.WriteAllText(invalid,"null");
            bool refused = false; try { new Store(invalid); } catch(IOException) {refused=true;} Check(refused && File.ReadAllText(invalid)=="null","손상 파일 덮어쓰기 방지");
            File.WriteAllText(Path.Combine(dir,"self-test-result.txt"),"PASS: dates, retained completion, filters/search/range, CSV escaping/formula safety, validation, persistence/settings, atomic backup/recovery, corrupt-file preservation.");
        }
    }
}
