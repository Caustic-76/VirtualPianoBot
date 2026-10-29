using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using VirtualPianoBot.ViewModels;

namespace VirtualPianoBot;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainWindowViewModel(
                Services.AppServices.Paths,
                Services.AppServices.Logger,
                Services.AppServices.SettingsService,
                Services.AppServices.LibraryService,
                Services.AppServices.LegacyMigrationService,
                Services.AppServices.Settings);

            var window = new MainWindow
            {
                DataContext = viewModel
            };
            viewModel.ConfigureMidiFolderPicker(async () =>
            {
                var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Choose a folder containing MIDI files",
                    AllowMultiple = false
                });
                return folders.FirstOrDefault()?.TryGetLocalPath();
            });
            desktop.MainWindow = window;

            desktop.Exit += (_, _) =>
            {
                viewModel.Dispose();
                Services.AppServices.Logger.Info("Virtual Piano Bot 6 shutting down.");
                Services.AppServices.Logger.Flush();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
