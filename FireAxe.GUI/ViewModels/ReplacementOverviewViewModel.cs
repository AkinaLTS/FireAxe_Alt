using FireAxe.Resources;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FireAxe.ViewModels;

public enum ReplacementOverviewFilter
{
    All,
    Replaced,
    Vanilla,
    Conflict,
    InstalledButDisabled
}

public sealed class ReplacementOverviewFilterOption
{
    public ReplacementOverviewFilterOption(string name, ReplacementOverviewFilter filter)
    {
        ArgumentNullException.ThrowIfNull(name);

        Name = name;
        Filter = filter;
    }

    public string Name { get; }

    public ReplacementOverviewFilter Filter { get; }

    public override string ToString() => Name;
}

public sealed class ReplacementCategoryGroupViewModel
{
    public ReplacementCategoryGroupViewModel(string title, IReadOnlyList<ReplacementSlotItemViewModel> items)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(items);

        Title = title;
        Items = items;
    }

    public string Title { get; }

    public IReadOnlyList<ReplacementSlotItemViewModel> Items { get; }
}

public sealed class ReplacementSlotItemViewModel : ViewModelBase
{
    public ReplacementSlotItemViewModel(ReplacementSlotStatus status, Action<VpkAddon> locate)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(locate);

        Slot = status.Slot;
        State = status.State;
        StateText = GetStateText(status.State);
        SecondaryName = Slot.IsChineseCulture ? Slot.DisplayNameEn : Slot.DisplayNameZh;
        Addons = status.Addons
            .Select(addon => new ReplacementAddonItemViewModel(addon, status.IsEffective(addon), locate))
            .ToArray();
        SummaryText = string.Join("、", Addons.Select(addon => addon.Name));
    }

    public ReplacementSlot Slot { get; }

    public ReplacementSlotState State { get; }

    public string StateText { get; }

    public string DisplayName => Slot.DisplayName;

    public string SecondaryName { get; }

    public IReadOnlyList<ReplacementAddonItemViewModel> Addons { get; }

    public string? SummaryText { get; }

    private static string GetStateText(ReplacementSlotState state)
    {
        return state switch
        {
            ReplacementSlotState.Vanilla => Texts.ReplacementStateVanilla,
            ReplacementSlotState.InstalledButDisabled => Texts.ReplacementStateInstalledButDisabled,
            ReplacementSlotState.Replaced => Texts.ReplacementStateReplaced,
            ReplacementSlotState.Overridden => Texts.ReplacementStateOverridden,
            ReplacementSlotState.Conflict => Texts.ReplacementStateConflict,
            _ => Texts.ReplacementStateVanilla
        };
    }
}

public sealed class ReplacementAddonItemViewModel : ViewModelBase
{
    public ReplacementAddonItemViewModel(ReplacementAddonEntry entry, bool isEffective, Action<VpkAddon> locate)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(locate);

        Name = entry.Addon.Name;
        Priority = entry.Priority;
        IsEnabled = entry.IsEnabledInHierarchy;
        IsEffective = isEffective;
        RoleText = GetRoleText(entry, isEffective);
        DetailsText = string.Format(Texts.ReplacementAddonDetails, entry.Priority, entry.MatchedFileCount);
        LocateCommand = ReactiveCommand.Create(() => locate(entry.Addon));
    }

    public string Name { get; }

    public int Priority { get; }

    public bool IsEnabled { get; }

    public bool IsEffective { get; }

    public string RoleText { get; }

    public string DetailsText { get; }

    public ReactiveCommand<Unit, Unit> LocateCommand { get; }

    private static string GetRoleText(ReplacementAddonEntry entry, bool isEffective)
    {
        if (!entry.IsEnabledInHierarchy)
        {
            return Texts.ReplacementRoleDisabled;
        }
        return isEffective ? Texts.ReplacementRoleEffective : Texts.ReplacementRoleCovered;
    }
}

public sealed class ReplacementOverviewViewModel : ViewModelBase, IActivatableViewModel, IValidity, IDisposable
{
    private static readonly (ReplacementSlotCategory Category, string Title)[] s_categoryTitles =
    [
        (ReplacementSlotCategory.PrimaryWeapon, Texts.ReplacementCategoryPrimaryWeapon),
        (ReplacementSlotCategory.SecondaryWeapon, Texts.ReplacementCategorySecondaryWeapon),
        (ReplacementSlotCategory.MeleeWeapon, Texts.ReplacementCategoryMeleeWeapon),
        (ReplacementSlotCategory.Equipment, Texts.ReplacementCategoryEquipment),
        (ReplacementSlotCategory.CommonInfected, Texts.ReplacementCategoryCommonInfected),
        (ReplacementSlotCategory.SpecialInfected, Texts.ReplacementCategorySpecialInfected)
    ];

    private readonly AddonRoot _addonRoot;
    private readonly Action<VpkAddon> _locateAddon;
    private readonly CancellationTokenSource _cts = new();

    private bool _disposed = false;
    private bool _valid = true;
    private bool _refreshing = false;
    private ReplacementOverview? _overview = null;
    private ReplacementOverviewFilterOption _selectedFilterOption;
    private IReadOnlyList<ReplacementCategoryGroupViewModel> _groups = [];
    private bool _hasNoVisibleSlots = true;
    private string _summary = "";

    public ReplacementOverviewViewModel(AddonRoot addonRoot, Action<VpkAddon> locateAddon)
    {
        ArgumentNullException.ThrowIfNull(addonRoot);
        ArgumentNullException.ThrowIfNull(locateAddon);

        _addonRoot = addonRoot;
        _locateAddon = locateAddon;

        FilterOptions =
        [
            new(Texts.ReplacementFilterAll, ReplacementOverviewFilter.All),
            new(Texts.ReplacementFilterReplaced, ReplacementOverviewFilter.Replaced),
            new(Texts.ReplacementFilterVanilla, ReplacementOverviewFilter.Vanilla),
            new(Texts.ReplacementFilterConflict, ReplacementOverviewFilter.Conflict),
            new(Texts.ReplacementFilterInstalledButDisabled, ReplacementOverviewFilter.InstalledButDisabled)
        ];
        _selectedFilterOption = FilterOptions[0];

        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);

        this.WhenActivated((CompositeDisposable disposables) =>
        {
            addonRoot.RegisterInvalidHandler(() => IsValid = false)
                .DisposeWith(disposables);
            if (!IsValid)
            {
                return;
            }

            RefreshCommand.Execute().Subscribe();
        });
    }

    public ViewModelActivator Activator { get; } = new();

    public bool IsValid
    {
        get => _valid;
        private set => this.RaiseAndSetIfChanged(ref _valid, value);
    }

    public bool IsRefreshing
    {
        get => _refreshing;
        private set => this.RaiseAndSetIfChanged(ref _refreshing, value);
    }

    public IReadOnlyList<ReplacementOverviewFilterOption> FilterOptions { get; }

    public ReplacementOverviewFilterOption SelectedFilterOption
    {
        get => _selectedFilterOption;
        set
        {
            if (value is null || value == _selectedFilterOption)
            {
                return;
            }
            _selectedFilterOption = value;
            this.RaisePropertyChanged();
            ApplyFilter();
        }
    }

    public IReadOnlyList<ReplacementCategoryGroupViewModel> Groups
    {
        get => _groups;
        private set => this.RaiseAndSetIfChanged(ref _groups, value);
    }

    public bool HasNoVisibleSlots
    {
        get => _hasNoVisibleSlots;
        private set => this.RaiseAndSetIfChanged(ref _hasNoVisibleSlots, value);
    }

    public string Summary
    {
        get => _summary;
        private set => this.RaiseAndSetIfChanged(ref _summary, value);
    }

    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

    public Interaction<Exception, Unit> ShowExceptionInteraction { get; } = new();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        IsValid = false;
        _cts.Cancel();
        _cts.Dispose();
        _disposed = true;
    }

    public void LocateAddon(VpkAddon addon)
    {
        ArgumentNullException.ThrowIfNull(addon);

        _locateAddon(addon);
    }

    private async Task RefreshAsync()
    {
        this.ThrowIfInvalid();

        if (IsRefreshing)
        {
            return;
        }

        IsRefreshing = true;
        try
        {
            var cancellationToken = _cts.Token;
            ReplacementOverview overview;
            try
            {
                overview = await ReplacementOverviewAnalyzer.AnalyzeAsync(_addonRoot, cancellationToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                await ShowExceptionInteraction.Handle(ex);
                return;
            }

            if (!IsValid)
            {
                return;
            }

            _overview = overview;
            Summary = string.Format(
                Texts.ReplacementSummary,
                overview.Slots.Count,
                overview.ReplacedCount,
                overview.VanillaCount,
                overview.ConflictCount,
                overview.InstalledButDisabledCount);
            ApplyFilter();
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private void ApplyFilter()
    {
        var overview = _overview;
        if (overview is null)
        {
            Groups = [];
            HasNoVisibleSlots = true;
            return;
        }

        var filter = _selectedFilterOption.Filter;
        var groups = new List<ReplacementCategoryGroupViewModel>();
        foreach (var (category, title) in s_categoryTitles)
        {
            var items = overview.Slots
                .Where(status => status.Slot.Category == category && IsVisible(status, filter))
                .Select(status => new ReplacementSlotItemViewModel(status, LocateAddon))
                .ToArray();
            if (items.Length > 0)
            {
                groups.Add(new(title, items));
            }
        }

        Groups = groups;
        HasNoVisibleSlots = groups.Count == 0;
    }

    private static bool IsVisible(ReplacementSlotStatus status, ReplacementOverviewFilter filter)
    {
        return filter switch
        {
            ReplacementOverviewFilter.Replaced =>
                status.State is ReplacementSlotState.Replaced or ReplacementSlotState.Overridden,
            ReplacementOverviewFilter.Vanilla => status.State == ReplacementSlotState.Vanilla,
            ReplacementOverviewFilter.Conflict => status.State == ReplacementSlotState.Conflict,
            ReplacementOverviewFilter.InstalledButDisabled => status.State == ReplacementSlotState.InstalledButDisabled,
            _ => true
        };
    }
}
