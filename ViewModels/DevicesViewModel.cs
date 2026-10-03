using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Core;
using SentinelX.Services.Link;

namespace SentinelX.ViewModels;

/// <summary>Real, local-network phone-link state for the Devices page.</summary>
public partial class DevicesViewModel : ObservableObject, IDisposable
{
    private readonly LinkService link;
    private readonly IUiDispatcher dispatcher;
    private string? statusNotice;

    public ObservableCollection<string> Addresses { get; } = [];
    public ObservableCollection<LinkDeviceInfo> Devices { get; } = [];

    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private string status = "Ładowanie stanu połączenia…";
    [ObservableProperty] private string deviceStoreWarning = "";
    [ObservableProperty] private LinkDeviceInfo? pendingDevice;
    [ObservableProperty] private bool isConfirmationOpen;
    [ObservableProperty] private string confirmationText = "";

    public string PrimaryAddress => Addresses.FirstOrDefault() ?? "";
    public bool HasAddress => Addresses.Count > 0;
    public bool HasDevices => Devices.Count > 0;
    public string DeviceCountLabel => Devices.Count == 1 ? "1 sparowane urządzenie" : $"{Devices.Count} sparowanych urządzeń";
    public string ConnectionLabel => IsRunning ? "Połączenie aktywne" : "Połączenie nieaktywne";

    public DevicesViewModel(LinkService link, IUiDispatcher dispatcher)
    {
        this.link = link;
        this.dispatcher = dispatcher;
        link.Changed += LinkChanged;
        Refresh();
    }

    private void LinkChanged() => dispatcher.Post(Refresh);

    [RelayCommand]
    public void Refresh()
    {
        bool wasRunning = IsRunning;
        IsRunning = link.IsRunning;
        Addresses.Clear();
        foreach (string address in link.Urls) Addresses.Add(address);
        Devices.Clear();
        foreach (LinkDeviceInfo device in link.Devices) Devices.Add(device);
        DeviceStoreWarning = link.DeviceStoreError ?? "";
        Status = IsRunning
            ? $"Łącze działa w sieci lokalnej · port {link.Port}. Nowe telefony wymagają zgody na tym komputerze."
            : link.Status;
        if (!string.IsNullOrWhiteSpace(statusNotice)) Status += " " + statusNotice;
        OnPropertyChanged(nameof(PrimaryAddress));
        OnPropertyChanged(nameof(HasAddress));
        OnPropertyChanged(nameof(HasDevices));
        OnPropertyChanged(nameof(DeviceCountLabel));
        OnPropertyChanged(nameof(ConnectionLabel));
        if (wasRunning != IsRunning) OnPropertyChanged(nameof(ConnectionLabel));
    }

    private void SetStatusNotice(string message)
    {
        statusNotice = message;
        Refresh();
    }

    [RelayCommand]
    private void RequestDisconnect(LinkDeviceInfo? device)
    {
        if (device == null) return;
        PendingDevice = device;
        ConfirmationText = $"Odłączyć „{device.Name}”? To unieważni zapisane zaufanie. Przy kolejnym połączeniu telefon ponownie poprosi o zgodę.";
        IsConfirmationOpen = true;
    }

    [RelayCommand]
    private void RequestDisconnectAll()
    {
        if (Devices.Count == 0)
        {
            SetStatusNotice("Nie ma sparowanych telefonów do odłączenia.");
            return;
        }
        PendingDevice = null;
        ConfirmationText = "Odłączyć wszystkie telefony? Każdy będzie musiał ponownie uzyskać zgodę na komputerze.";
        IsConfirmationOpen = true;
    }

    [RelayCommand]
    private void ConfirmDisconnect()
    {
        bool removed;
        string subject;
        if (PendingDevice is { } device)
        {
            subject = $"„{device.Name}”";
            removed = link.RemoveDevice(device.Id);
        }
        else
        {
            subject = "wszystkie telefony";
            removed = link.RemoveAllDevices();
        }

        IsConfirmationOpen = false;
        PendingDevice = null;
        Refresh();
        string notice = removed
            ? $"Odłączono {subject}. W razie ponownego połączenia poproszę o zgodę."
            : (link.DeviceStoreError ?? $"Nie udało się trwale odłączyć {subject}. Spróbuj ponownie.");
        SetStatusNotice(notice);
    }

    [RelayCommand]
    private void CancelDisconnect()
    {
        IsConfirmationOpen = false;
        PendingDevice = null;
        ConfirmationText = "";
    }

    [RelayCommand]
    private void CopyAddress()
    {
        if (PrimaryAddress.Length == 0)
        {
            SetStatusNotice("Brak adresu w sieci. Połącz komputer z Wi‑Fi lub Ethernetem i odśwież.");
            return;
        }
        try
        {
            Clipboard.SetText(PrimaryAddress);
            SetStatusNotice("Adres skopiowany do schowka. Otwórz go w przeglądarce telefonu.");
        }
        catch (Exception ex)
        {
            SetStatusNotice("Nie udało się skopiować adresu (" + ex.GetType().Name + "). Zaznacz go ręcznie.");
        }
    }

    public void Dispose() => link.Changed -= LinkChanged;
}
