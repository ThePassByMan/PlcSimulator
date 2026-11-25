using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace PlcSimulator;

public partial class PointDialog : Window
{
    private readonly bool scheduling;
    private readonly PlcProtocol protocol;
    public PointSpec? Result { get; private set; }
    public DateTime Due { get; private set; }

    public PointDialog(PointSpec? initial, bool scheduling = false) : this(initial, scheduling, initial?.Protocol ?? PlcProtocol.Modbus) { }
    public PointDialog(PointSpec? initial, bool scheduling, PlcProtocol protocol)
    {
        InitializeComponent();
        this.scheduling = scheduling;
        this.protocol = protocol;
        Title = UiText.Get(scheduling ? "Schedule" : initial == null ? "Add" : "Edit");
        if (scheduling)
        {
            PointFields.Visibility = Visibility.Collapsed;
            TimeFields.Visibility = Visibility.Visible;
            TimeBox.Text = DateTime.Now.AddMinutes(1).ToString("yyyy-MM-dd HH:mm:ss");
            Note.Text = $"{initial!.AddressLabel} ← {initial.WriteValue}   {UiText.Get("TimeNote")}";
            return;
        }
        OrderBox.ItemsSource = Enum.GetValues<WordOrder>();
        OrderBox.SelectedItem = initial?.Order ?? (protocol is PlcProtocol.MC or PlcProtocol.MCAscii or PlcProtocol.FxSerial or PlcProtocol.FINS or PlcProtocol.Fx5Slmp or PlcProtocol.Fx5SlmpAscii ? WordOrder.CDAB : WordOrder.ABCD);
        TypeBox.ItemsSource = Enum.GetValues<DataKind>();
        AddressBox.Text = initial?.AddressLabel ?? (protocol == PlcProtocol.S7 ? "DB1.0" : "D0");
        TypeBox.SelectedItem = initial?.Type ?? DataKind.UInt16;
        NameBox.Text = initial?.Name ?? "";
        ValueBox.Text = initial?.WriteValue ?? "0";
        IntervalBox.Text = (initial?.IntervalMs ?? 1000).ToString();
        Note.Text = initial == null ? UiText.Get(protocol + "Hint") : UiText.Get("EditNote");
        Loaded += (_, _) => { AddressBox.Focus(); AddressBox.SelectAll(); };
    }

    private void AddressChanged(object sender, TextChangedEventArgs e)
    {
        Preview();
    }

    private void TypeChanged(object sender, SelectionChangedEventArgs e) => Preview();

    private void Preview()
    {
        if (TypeBox.SelectedItem is not DataKind kind)
        {
            MappingPreview.Text = "—";
            return;
        }
        OrderBox.IsEnabled = kind is DataKind.Int32 or DataKind.UInt32 or DataKind.Float32 or DataKind.Int64 or DataKind.UInt64 or DataKind.Float64;
        try
        {
            var p = PointSpec.ForProtocol(protocol, "", AddressBox.Text, kind, (WordOrder?)OrderBox.SelectedItem ?? WordOrder.ABCD, "", 0);
            p.ValidateAddress();
            MappingPreview.Text = "";
        }
        catch (InputError ex) { MappingPreview.Text = UiText.Get(ex.Message); }
    }

    private void Accept(object sender, RoutedEventArgs e)
    {
        try
        {
            if (scheduling)
            {
                if (!DateTime.TryParseExact(TimeBox.Text.Trim(), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal, out var when) || when <= DateTime.Now) throw new InputError("TimeError");
                Due = when;
            }
            else
            {
                if (!int.TryParse(IntervalBox.Text, out var ms)) throw new InputError("IntervalError");
                var p = PointSpec.ForProtocol(protocol, NameBox.Text, AddressBox.Text, (DataKind)TypeBox.SelectedItem,
                    (WordOrder)OrderBox.SelectedItem, ValueBox.Text, ms);
                p.Validate();
                Result = p;
            }
            DialogResult = true;
        }
        catch (InputError ex) { ErrorText.Text = UiText.Get(ex.Message); }
    }
}
