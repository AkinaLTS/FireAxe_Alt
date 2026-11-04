using System;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using FireAxe.ViewModels;
using ReactiveUI;
using System.Reactive;
using ReactiveUI.Avalonia;

namespace FireAxe.Views;

public partial class ReplacementOverviewWindow : ReactiveWindow<ReplacementOverviewViewModel>
{
    public ReplacementOverviewWindow()
    {
        InitializeComponent();

        Closed += ReplacementOverviewWindow_Closed;

        this.RegisterViewModelConnection((viewModel, disposables) =>
        {
            viewModel.RegisterInvalidHandler(Close)
                .DisposeWith(disposables);
            if (!viewModel.IsValid)
            {
                return;
            }

            viewModel.ShowExceptionInteraction.RegisterHandler(async context =>
            {
                await CommonMessageBoxes.ShowException(this, context.Input);
                context.SetOutput(Unit.Default);
            }).DisposeWith(disposables);
        });
    }

    private void ReplacementOverviewWindow_Closed(object? sender, EventArgs e)
    {
        if (DataContext is ReplacementOverviewViewModel viewModel)
        {
            DataContext = null;
            viewModel.Dispose();
        }
    }
}
