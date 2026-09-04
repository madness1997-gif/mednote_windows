using System.Text.Json;
using MedNote.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace MedNote.Windows.App;

public sealed partial class MainWindow
{
    private CancellationTokenSource? _driveOperationCancellation;
    private bool _driveBusy;

    private void InitializeDriveStatus()
    {
        DriveButtonLabel.Text = _driveSession.IsConnected ? "Đồng bộ Drive" : "Kết nối Drive";
        if (_driveSession.CredentialLoadError is { } error)
        {
            _ = ShowErrorAsync("Đã xóa credential Drive lỗi", error);
        }
    }

    private async void OnDriveClicked(object sender, RoutedEventArgs e)
    {
        if (_driveBusy || _closing)
        {
            return;
        }

        if (!_driveSession.IsConnected)
        {
            await ConfigureAndConnectDriveAsync(replaceExisting: false);
            return;
        }

        await SyncDriveAsync();
    }

    private async void OnDriveChangeClientClicked(object sender, RoutedEventArgs e)
    {
        if (!_driveBusy && !_closing)
        {
            await ConfigureAndConnectDriveAsync(replaceExisting: true);
        }
    }

    private async void OnDriveDisconnectClicked(object sender, RoutedEventArgs e)
    {
        if (_driveBusy || _closing || !_driveSession.IsConnected)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "Ngắt Google Drive?",
            Content = "MedNote sẽ thu hồi token Google. Dữ liệu cục bộ và bản lưu trên Drive không bị xóa.",
            PrimaryButtonText = "Ngắt kết nối",
            CloseButtonText = "Hủy",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await RunDriveOperationAsync("Đang ngắt kết nối…", async cancellationToken =>
        {
            await _driveSession.DisconnectAsync(cancellationToken);
            await _driveStateStore.ClearAsync(cancellationToken);
            DriveButtonLabel.Text = "Kết nối Drive";
        });
    }

    private async Task ConfigureAndConnectDriveAsync(bool replaceExisting)
    {
        var instructions = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "Kết nối Google Drive",
            Content = "Chọn tệp OAuth JSON loại Desktop app. MedNote chỉ xin quyền drive.appdata; Client Secret và refresh token chỉ được lưu trong Windows Credential Manager.",
            PrimaryButtonText = "Chọn OAuth JSON",
            CloseButtonText = "Hủy",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await instructions.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var configuration = await PickOAuthConfigurationAsync();
        if (configuration is null)
        {
            return;
        }

        await RunDriveOperationAsync("Đang mở trình duyệt…", async cancellationToken =>
        {
            if (replaceExisting && _driveSession.IsConnected)
            {
                await _driveSession.DisconnectAsync(cancellationToken);
                await _driveStateStore.ClearAsync(cancellationToken);
            }

            await _driveSession.ConnectAsync(configuration, cancellationToken);
            DriveButtonLabel.Text = "Đồng bộ Drive";
        });
        if (_driveSession.IsConnected)
        {
            await SyncDriveAsync();
        }
    }

    private async Task<GoogleOAuthClientConfiguration?> PickOAuthConfigurationAsync()
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.Downloads,
            ViewMode = PickerViewMode.List,
        };
        picker.FileTypeFilter.Add(".json");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file is null || string.IsNullOrWhiteSpace(file.Path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(file.Path);
            using var document = await JsonDocument.ParseAsync(stream);
            if (!document.RootElement.TryGetProperty("installed", out var installed))
            {
                throw new InvalidDataException("Tệp phải là OAuth client loại Desktop app, không phải Web application.");
            }

            var clientId = installed.TryGetProperty("client_id", out var clientIdValue)
                ? clientIdValue.GetString() ?? string.Empty
                : string.Empty;
            var clientSecret = installed.TryGetProperty("client_secret", out var secretValue)
                ? secretValue.GetString() ?? string.Empty
                : string.Empty;
            GoogleDriveOAuth.ValidateClientId(clientId);
            return new GoogleOAuthClientConfiguration
            {
                ClientId = clientId.Trim(),
                ClientSecret = clientSecret.Trim(),
            };
        }
        catch (Exception exception)
        {
            await ShowErrorAsync("OAuth JSON không hợp lệ", exception.Message);
            return null;
        }
    }

    private async Task SyncDriveAsync()
    {
        if (_driveBusy || !_driveSession.IsConnected || _closing)
        {
            return;
        }

        DriveSyncResult? syncResult = null;
        await RunDriveOperationAsync("Đang đồng bộ…", async cancellationToken =>
        {
            await FlushForDriveAsync(cancellationToken);
            syncResult = await _driveSync.SyncAsync(cancellationToken: cancellationToken);
        });
        if (syncResult is null)
        {
            return;
        }

        if (syncResult.Outcome == DriveSyncOutcome.Conflict)
        {
            await ResolveDriveConflictAsync();
            return;
        }

        await ApplyDriveResultAsync(syncResult);
    }

    private async Task ResolveDriveConflictAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "Dữ liệu đã thay đổi ở cả hai nơi",
            Content = "MedNote đã lưu hai bản đối chiếu trong thư mục dữ liệu cục bộ và sẽ không tự ghi đè. Chọn bản cần giữ làm thư viện hiện hành.",
            PrimaryButtonText = "Giữ bản máy",
            SecondaryButtonText = "Dùng bản Drive",
            CloseButtonText = "Để sau",
            DefaultButton = ContentDialogButton.Close,
        };
        var choice = await dialog.ShowAsync();
        if (choice == ContentDialogResult.None)
        {
            DriveButtonLabel.Text = "Drive có xung đột";
            return;
        }

        DriveSyncResult? result = null;
        await RunDriveOperationAsync("Đang giải quyết xung đột…", async cancellationToken =>
        {
            result = await _driveSync.SyncAsync(
                choice == ContentDialogResult.Primary
                    ? DriveConflictResolution.UseLocal
                    : DriveConflictResolution.UseRemote,
                cancellationToken);
        });
        if (result is not null)
        {
            await ApplyDriveResultAsync(result);
        }
    }

    private async Task ApplyDriveResultAsync(DriveSyncResult result)
    {
        if (result.Outcome == DriveSyncOutcome.RestoredRemote)
        {
            await NoteViewModel.ReloadFromRepositoryAsync();
            NoteWorkspacePane.LoadActiveSheet();
            await ViewModel.ReloadFromRepositoryAsync();
            _workspace?.Apply(
                NoteViewModel.Preferences.WorkspaceMode ?? WorkspaceMode.Reader,
                NoteViewModel.Preferences.ReaderShare);
            ApplyViewModelState();
        }

        DriveButtonLabel.Text = result.Outcome switch
        {
            DriveSyncOutcome.CreatedRemote => "Đã tạo bản Drive",
            DriveSyncOutcome.UploadedLocal => "Đã tải lên Drive",
            DriveSyncOutcome.RestoredRemote => "Đã nạp từ Drive",
            _ => "Drive đã đồng bộ",
        };
    }

    private async Task FlushForDriveAsync(CancellationToken cancellationToken)
    {
        await NoteWorkspacePane.FlushAsync(cancellationToken);
        await _workspacePreferenceSave.WaitAsync(cancellationToken);
        _viewport.CaptureCurrentPosition();
        await ViewModel.PersistNowAsync(cancellationToken);
    }

    private async Task RunDriveOperationAsync(string busyLabel, Func<CancellationToken, Task> operation)
    {
        _driveBusy = true;
        DriveButton.IsEnabled = false;
        DriveButtonLabel.Text = busyLabel;
        _driveOperationCancellation?.Dispose();
        _driveOperationCancellation = new CancellationTokenSource();
        try
        {
            await operation(_driveOperationCancellation.Token);
        }
        catch (OperationCanceledException) when (_closing || _driveOperationCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            DriveButtonLabel.Text = _driveSession.IsConnected ? "Thử lại Drive" : "Kết nối Drive";
            await ShowErrorAsync("Google Drive chưa hoàn tất", exception.Message);
        }
        finally
        {
            _driveBusy = false;
            DriveButton.IsEnabled = !_closing;
        }
    }

    private void StopNetworkForShutdown()
    {
        _driveOperationCancellation?.Cancel();
        _driveSession.CancelNetwork();
    }
}
