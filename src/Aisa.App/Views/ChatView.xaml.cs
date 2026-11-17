using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Aisa.App.Chat;

namespace Aisa.App.Views;

public partial class ChatView : UserControl
{
    private ChatController? _controller;

    public ChatView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    public TextBox InputBox => Input;

    /// <summary>Raised when the message count changes so the island can spring to a new height.</summary>
    public event Action<int>? MessageCountChanged;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_controller is not null)
        {
            _controller.Messages.CollectionChanged -= OnMessagesChanged;
            _controller.PrefillRequested -= OnPrefill;
        }
        _controller = DataContext as ChatController;
        if (_controller is not null)
        {
            _controller.Messages.CollectionChanged += OnMessagesChanged;
            _controller.PrefillRequested += OnPrefill;
        }
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        MessageCountChanged?.Invoke(_controller?.Messages.Count ?? 0);
        // Scroll after layout so the new item is measured.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () => LogScroll.ScrollToEnd());
    }

    private void OnPrefill(string text)
    {
        Input.Text = text;
        Input.CaretIndex = text.Length;
        Input.Focus();
    }

    private void OnChip(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string chip }) _controller?.Chip(chip);
    }

    private void OnSend(object sender, RoutedEventArgs e) => Send();

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        Send();
    }

    private void Send()
    {
        if (_controller is null) return;
        string text = Input.Text;
        if (string.IsNullOrWhiteSpace(text)) return;
        Input.Clear();
        _ = _controller.SendAsync(text);
    }
}
