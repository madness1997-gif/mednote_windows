using System.ComponentModel;
using MedNote.Core;
using MedNote.Windows.App.Controls;
using MedNote.Windows.App.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace MedNote.Windows.App.Controllers;

/// <summary>
/// Keeps M3 tool/history chrome out of MainWindow and projects the annotation
/// session state onto its toolbar controls.
/// </summary>
public sealed class ReaderAnnotationController : IDisposable
{
    private readonly ReaderViewModel _viewModel;
    private readonly IReadOnlyDictionary<PdfTool, ToggleButton> _tools;
    private readonly Button _undoButton;
    private readonly Button _redoButton;
    private readonly Button _exportButton;
    private bool _disposed;

    public ReaderAnnotationController(
        ReaderViewModel viewModel,
        IReadOnlyDictionary<PdfTool, ToggleButton> tools,
        Button undoButton,
        Button redoButton,
        Button exportButton)
    {
        _viewModel = viewModel;
        _tools = tools;
        _undoButton = undoButton;
        _redoButton = redoButton;
        _exportButton = exportButton;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Apply();
    }

    public void Apply()
    {
        foreach (var (tool, button) in _tools)
        {
            var active = _viewModel.ActiveTool == tool;
            button.IsChecked = active;
            if (button.Content is ReaderToolContent content)
            {
                content.IsActive = active;
            }
            button.IsEnabled = _viewModel.HasDocument;
        }

        _undoButton.IsEnabled = _viewModel.CanUndoAnnotations;
        _redoButton.IsEnabled = _viewModel.CanRedoAnnotations;
        _exportButton.IsEnabled = _viewModel.HasDocument;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_disposed && e.PropertyName is (nameof(ReaderViewModel.ActiveTool)
            or nameof(ReaderViewModel.HasDocument)
            or nameof(ReaderViewModel.CanUndoAnnotations)
            or nameof(ReaderViewModel.CanRedoAnnotations)))
        {
            Apply();
        }
    }
}
