using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevStudio.Core.Terminal;

namespace DevStudio.UI.ViewModels;

/// <summary>
/// One integrated terminal tab (SKILL.md §14–§15), backed by a real <see
/// cref="ITerminalSession"/> shell process. Piped stdio only — see <see cref="ITerminalSession"/>
/// for the documented lack of full VT100/pty emulation in this phase.
/// </summary>
public partial class TerminalViewModel : ObservableObject, IAsyncDisposable
{
    private readonly ITerminalSession _session;
    private readonly string _workingDirectory;

    [ObservableProperty]
    private string _inputText = string.Empty;

    [ObservableProperty]
    private bool _isRunning;

    public ObservableCollection<string> OutputLines { get; } = new();

    public string Title { get; } = "Terminal";

    public TerminalViewModel(ITerminalSession session, string workingDirectory)
    {
        _session = session;
        _workingDirectory = workingDirectory;
        _session.OutputReceived += OnOutputReceived;
        _session.Exited += OnExited;
    }

    public async Task StartAsync()
    {
        await _session.StartAsync(_workingDirectory).ConfigureAwait(true);
        IsRunning = true;
    }

    private void OnOutputReceived(object? sender, TerminalOutputEventArgs e)
    {
        Dispatcher.UIThread.Post(() => OutputLines.Add(e.Text));
    }

    private void OnExited(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() => IsRunning = false);
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        if (string.IsNullOrEmpty(InputText)) return;

        var text = InputText;
        InputText = string.Empty;
        OutputLines.Add($"> {text}");
        await _session.SendInputAsync(text).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RestartAsync()
    {
        OutputLines.Clear();
        await _session.RestartAsync().ConfigureAwait(true);
        IsRunning = true;
    }

    public void Terminate() => _session.Terminate();

    public async ValueTask DisposeAsync()
    {
        _session.OutputReceived -= OnOutputReceived;
        _session.Exited -= OnExited;
        await _session.DisposeAsync().ConfigureAwait(false);
    }
}
