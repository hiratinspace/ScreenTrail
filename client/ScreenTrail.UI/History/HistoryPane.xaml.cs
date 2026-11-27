using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ScreenTrail.UI.History;

/// <summary>
/// Layout and three gestures: a double-click opens a row, the table's selection is handed to the model
/// (a DataGrid's selected items are not bindable), and focus goes back to the table when a discard has
/// taken the focused control away. What a row is, which rows qualify and what discarding does are
/// <see cref="HistoryViewModel"/>'s.
/// </summary>
public partial class HistoryPane : UserControl
{
    private HistoryViewModel? _listening;

    /// <summary>True while the table's selection is being made the model's, so those changes are not handed back.</summary>
    private bool _applying;

    public HistoryPane()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnOpen(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is HistoryViewModel model && Sessions.SelectedItem is HistoryRow row)
        {
            model.OpenCommand.Execute(row);
        }
    }

    /// <summary>
    /// <c>Enter</c> opens the row with keyboard focus, as Open and a double-click do (spec v0.6.9 S4). Taken
    /// before the table sees it: a DataGrid's own Enter moves to the next row, which is not what a
    /// technician on a session's row means. With modifiers held, the key is the table's.
    /// </summary>
    private void OnTableKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None || DataContext is not HistoryViewModel model)
        {
            return;
        }

        var row = (e.OriginalSource as DependencyObject) is { } source && ItemsControl.ContainerFromElement(Sessions, source) is DataGridRow { Item: HistoryRow focused }
            ? focused
            : Sessions.CurrentItem as HistoryRow ?? Sessions.SelectedItem as HistoryRow;
        if (row is not null)
        {
            e.Handled = true;
            model.OpenCommand.Execute(row);
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_applying && DataContext is HistoryViewModel model)
        {
            model.Select(Sessions.SelectedItems.OfType<HistoryRow>());
        }
    }

    /// <summary>
    /// Listens for the model's discards while on screen, and not after: the model outlives every pane the
    /// shell builds for it.
    ///
    /// Shown after Review's Discard, whose button went with the pane it was in, focus is on nothing a
    /// keyboard user can see, so it goes to the list, where the discarded session no longer waits for
    /// review. Arriving any other way (the History tab, say) leaves focus where the technician put it.
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is HistoryViewModel model && !ReferenceEquals(model, _listening))
        {
            OnUnloaded(sender, e);
            model.Discarded += OnDiscarded;
            model.SelectionRestored += OnSelectionRestored;
            _listening = model;
            OnSelectionRestored(model, EventArgs.Empty);
        }

        if (Keyboard.FocusedElement is not UIElement { IsVisible: true })
        {
            FocusTheList();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_listening is not null)
        {
            _listening.Discarded -= OnDiscarded;
            _listening.SelectionRestored -= OnSelectionRestored;
            _listening = null;
        }
    }

    /// <summary>
    /// The rows were read again (review finding D-22): the table selects what the model kept, by id, since a
    /// row the service changed is a new object the table no longer counts as selected. The current row
    /// stays where it was, so the arrows go on from the same place.
    /// </summary>
    private void OnSelectionRestored(object? sender, EventArgs e)
    {
        if (DataContext is not HistoryViewModel model)
        {
            return;
        }

        var want = model.Selected;
        if (Sessions.SelectedItems.Count == want.Count && want.All(Sessions.SelectedItems.Contains))
        {
            return;
        }

        _applying = true;
        try
        {
            Sessions.SelectedItems.Clear();
            foreach (var row in want)
            {
                _ = Sessions.SelectedItems.Add(row);
            }
        }
        finally
        {
            _applying = false;
        }
    }

    /// <summary>After a bulk discard the button is disabled, having nothing selected, and would hold focus nowhere.</summary>
    private void OnDiscarded(object? sender, EventArgs e) => FocusTheList();

    private void FocusTheList()
    {
        if (Sessions.IsVisible)
        {
            _ = Sessions.Focus();
        }
    }
}
