using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PlcSimulator;

public partial class MainWindow : Window
{
    private readonly Configuration config;
    private readonly string settingsPath;
    private readonly (DataGridColumn Column, string Key)[] columnHeaders;
    private readonly Dictionary<PlcProtocol, IPointMemory> memories = new() { [PlcProtocol.Modbus] = new Simulation(), [PlcProtocol.Inovance] = new Simulation() };
    private IPointMemory Memory => memories[config.Protocol];
    private readonly Queue<string> logLines = new();
    private readonly ObservableCollection<WatchRow> rows = [];
    private readonly DispatcherTimer sampler = new();
    private readonly DispatcherTimer health = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private IServer? listener;
    private string messageKey = "Empty", detail = "";
    private bool initialized, stopping;

    public MainWindow() : this(Configuration.FilePath) { }

    public MainWindow(string settingsPath)
    {
        this.settingsPath = settingsPath;
        InitializeComponent();
        columnHeaders = ItemsGrid.Columns.Select(column => (column, (string)column.Header)).ToArray();
        UiText.Set("zh");
        try { config = Configuration.Load(settingsPath, settingsPath == Configuration.FilePath ? Configuration.LegacyFilePath : null); }
        catch (Exception ex)
        {
            MessageBox.Show($"{UiText.Get("LoadError")}\n{settingsPath}\n{(ex is InputError ? UiText.Get(ex.Message) : ex.Message)}",
                UiText.Get("Title"), MessageBoxButton.OK, MessageBoxImage.Error);
            Loaded += (_, _) => Close();
            config = new Configuration();
            return;
        }
        Translate();
        LanguageBox.SelectedIndex = config.Language == "en" ? 1 : 0;
        IpBox.Text = config.ListenIp;
        UnitBox.Text = config.Unit.ToString();
        SampleBox.Text = config.SampleMs.ToString();
        ItemsGrid.ItemsSource = rows;
        ProtocolBox.SelectedIndex = (int)config.Protocol;
        LoadProtocol();
        sampler.Interval = TimeSpan.FromMilliseconds(config.SampleMs);
        sampler.Tick += (_, _) => { foreach (var row in rows) row.Refresh(); };
        health.Tick += async (_, _) =>
        {
            if (!stopping && listener?.Listening.IsCompleted == true) await StopServer();
            UpdateStatus();
        };
        initialized = true;
        UpdateStatus();
        AppendLog("Ready", config.Protocol.ToString());
        sampler.Start();
        health.Start();
    }

    private void UpdateStatus()
    {
        ServerState.Text = UiText.Get(listener == null ? "Idle" : "Online");
        ServerButton.Content = UiText.Get(listener == null ? "Start" : "Stop");
        ClientsText.Text = $"{UiText.Get("Clients")}  {listener?.Clients ?? 0}";
        ProtocolBox.IsEnabled = IpBox.IsEnabled = PortBox.IsEnabled = UnitBox.IsEnabled = listener == null && !stopping;
        ServerButton.IsEnabled = !stopping;
        StatusText.Text = UiText.Get(messageKey) + (detail.Length == 0 ? "" : " · " + detail);
    }
    private void Report(string key, string extra = "")
    {
        messageKey = key;
        detail = extra;
        AppendLog(key, extra);
        UpdateStatus();
    }
    private void AppendLog(string key, string text)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff}  {UiText.Get(key)}  {text}";
        logLines.Enqueue(line);
        if (logLines.Count > 500)
        {
            logLines.Dequeue();
            LogBox.Text = string.Join(Environment.NewLine, logLines);
        }
        else LogBox.AppendText((logLines.Count > 1 ? Environment.NewLine : "") + line);
        LogBox.ScrollToEnd();
    }
    private void QueueLog(string key, string text)
    {
        if (!Dispatcher.HasShutdownStarted)
            Dispatcher.BeginInvoke(new Action(() => AppendLog(key, text)));
    }
    private void Error(Exception ex)
    {
        if (ex is InputError) Report(ex.Message);
        else Report("Failed", ex.Message);
    }
    private void Save()
    {
        StoreRows();
        config.Save(settingsPath);
    }
    private void StoreRows()
    {
        config.Points.RemoveAll(p => p.Protocol == config.Protocol);
        config.Points.AddRange(rows.Select(r => r.Spec));
    }
    private WatchRow NewRow(PointSpec point)
    {
        var row = new WatchRow(point, Memory);
        row.TaskMessage += QueueLog;
        return row;
    }
    private void LoadProtocol()
    {
        var mode = config.Protocol;
        config.Prepare(mode);
        AreaSummary.Text = UiText.Get(mode + "Hint");
        if (!memories.ContainsKey(mode)) memories[mode] = new NativeMemory(mode);
        PortBox.Text = (mode == PlcProtocol.Modbus ? config.Port : config.NativePorts[mode]).ToString();
        UnitLabel.Visibility = UnitBox.Visibility = mode is PlcProtocol.Modbus or PlcProtocol.Inovance ? Visibility.Visible : Visibility.Collapsed;
        foreach (var r in rows) r.Dispose();
        rows.Clear();
        foreach (var p in config.Points.Where(p => p.Protocol == mode)) rows.Add(NewRow(p));
        ItemsGrid.SelectedIndex = 0;
    }
    private void ProtocolChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized || listener != null || stopping || ProtocolBox.SelectedIndex < 0) return;
        Act(() =>
        {
            StoreRows();
            config.Protocol = (PlcProtocol)ProtocolBox.SelectedIndex;
            LoadProtocol();
            Report("Switched", config.Protocol.ToString());
            Save();
        });
    }
    private void Act(Action action)
    {
        try { action(); }
        catch (Exception ex) { Error(ex); }
    }
    private WatchRow Selected() => ItemsGrid.SelectedItem as WatchRow ?? throw new InputError("SelectError");
    private void Translate()
    {
        UiText.Set(config.Language);
        foreach (var (column, key) in columnHeaders) column.Header = UiText.Get(key);
        AreaSummary.Text = UiText.Get(config.Protocol + "Hint");
    }
    private void LanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized) return;
        Act(() =>
        {
            config.Language = LanguageBox.SelectedIndex == 1 ? "en" : "zh";
            Translate();
            UpdateStatus();
            Save();
        });
    }

    private async void ToggleServer(object sender, RoutedEventArgs e)
    {
        if (stopping) return;
        if (listener != null) { await StopServer(); return; }
        Act(() =>
        {
            byte unit = config.Unit;
            if (!int.TryParse(PortBox.Text, out var port) ||
                (Memory is Simulation && !byte.TryParse(UnitBox.Text, out unit))) throw new InputError("EndpointError");
            var ip = Configuration.ParseEndpoint(IpBox.Text.Trim(), port, unit);
            listener = Memory is Simulation modbus ? new Listener(modbus, ip, port, unit, QueueLog) :
                new NativeListener((NativeMemory)Memory, ip, port, QueueLog);
            config.ListenIp = ip.ToString();
            if (config.Protocol == PlcProtocol.Modbus) config.Port = port;
            else config.NativePorts[config.Protocol] = port;
            config.Unit = unit;
            Report("Online", $"{config.Protocol} · {ip}:{port}" + (config.Protocol == PlcProtocol.Modbus ? $" · Unit {unit}" : ""));
            Save();
        });
    }
    private async Task StopServer()
    {
        var previous = listener;
        if (previous == null) return;
        stopping = true;
        listener = null;
        UpdateStatus();
        try { await previous.DisposeAsync(); Report("Idle"); }
        catch (Exception ex) { Error(ex); }
        finally { stopping = false; UpdateStatus(); }
    }

    private void ApplySample(object sender, RoutedEventArgs e) => Act(() =>
    {
        if (!int.TryParse(SampleBox.Text, out var ms) || ms is < 50 or > 60000) throw new InputError("SampleError");
        config.SampleMs = ms;
        sampler.Interval = TimeSpan.FromMilliseconds(ms);
        Save();
        Report("Saved", $"{ms} ms");
    });

    private void AddPoint(object sender, RoutedEventArgs e) => Act(() => Edit(null));
    private void EditPoint(object sender, RoutedEventArgs e) => Act(() => Edit(Selected()));
    private void RowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || sender is not DataGridRow { Item: WatchRow row }) return;
        for (var node = e.OriginalSource as DependencyObject; node != null && node != sender;)
        {
            if (node is Button) return;
            node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }
        e.Handled = true;
        Act(() => Edit(row));
    }
    private void Edit(WatchRow? prior)
    {
        var dialog = new PointDialog(prior?.Spec, false, config.Protocol) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var row = NewRow(dialog.Result!);
        if (prior == null) rows.Add(row);
        else { prior.Dispose(); rows[rows.IndexOf(prior)] = row; row.Refresh(); }
        ItemsGrid.SelectedItem = row;
        ItemsGrid.ScrollIntoView(row);
        Save();
        Report("Saved", row.Spec.AddressLabel);
    }
    private void DeletePoint(object sender, RoutedEventArgs e) => Act(() =>
    {
        var row = Selected();
        row.Dispose();
        rows.Remove(row);
        Save();
        Report("Removed", row.Spec.AddressLabel);
    });
    private void WritePoint(object sender, RoutedEventArgs e) => Act(() =>
    {
        var row = (WatchRow)((Button)sender).DataContext;
        row.Write();
        row.Refresh();
        Report("Written", $"{row.Spec.AddressLabel} ← {row.Spec.WriteValue}");
    });
    private void ToggleCycle(object sender, RoutedEventArgs e) => Act(() =>
    {
        var row = (WatchRow)((Button)sender).DataContext;
        if (row.Active) { row.Stop(); Report("Halted", row.Spec.AddressLabel); }
        else { row.StartCycle(); Report("CycleOn", row.Spec.AddressLabel); }
    });
    private void SchedulePoint(object sender, RoutedEventArgs e) => Act(() =>
    {
        var row = Selected();
        if (row.Active) throw new InputError("ActiveError");
        var dialog = new PointDialog(row.Spec, true) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            row.Schedule(dialog.Due);
            Report("Scheduled", row.Spec.AddressLabel);
        }
    });

    protected override void OnClosed(EventArgs e)
    {
        sampler.Stop();
        health.Stop();
        foreach (var row in rows) row.Dispose();
        // The protocol task uses no UI dispatcher; release active sockets before shutdown.
        if (listener != null)
        {
            try { listener.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            catch (Exception ex) { System.Diagnostics.Trace.TraceWarning(ex.Message); }
        }
        base.OnClosed(e);
    }
}
