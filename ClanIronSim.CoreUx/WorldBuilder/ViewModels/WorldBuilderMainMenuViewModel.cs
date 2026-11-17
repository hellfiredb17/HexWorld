using System.Windows.Input;
using HawkNet.CoreUx.Common.Infrastructure;

namespace ClanIronSim.CoreUx.WorldBuilder.ViewModels;

public sealed class WorldBuilderMainMenuViewModel : ObservableObject
{
    private string _statusMessage = "Choose how to start world authoring.";

    public WorldBuilderMainMenuViewModel()
    {
        ModifyExistingCommand = new AsyncRelayCommand(_ =>
        {
            StatusMessage = "Modify existing world flow is not built yet.";
            ModifyExistingRequested?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        });
        CreateNewCommand = new AsyncRelayCommand(_ =>
        {
            StatusMessage = "Create new world flow is not built yet.";
            CreateNewRequested?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        });
        CreateSectionCommand = new AsyncRelayCommand(_ =>
        {
            StatusMessage = "Opening section editor.";
            CreateSectionRequested?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        });
        BackCommand = new AsyncRelayCommand(_ =>
        {
            BackRequested?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        });
    }

    public string Title => "World Builder";

    public string Subtitle => "Create or modify WorldMap data.";

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public ICommand ModifyExistingCommand { get; }

    public ICommand CreateNewCommand { get; }
    public ICommand CreateSectionCommand { get; }

    public ICommand BackCommand { get; }

    public event EventHandler? ModifyExistingRequested;

    public event EventHandler? CreateNewRequested;
    public event EventHandler? CreateSectionRequested;

    public event EventHandler? BackRequested;
}
