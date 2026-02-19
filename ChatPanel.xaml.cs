using Autodesk.Revit.UI;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ArchieCopilot
{
    public enum MessageType
    {
        User,
        Assistant,
        Code,
        Result,
        Loading,
        Welcome
    }

    public class ChatMessage : INotifyPropertyChanged
    {
        public string Text { get; set; } = "";
        public MessageType Type { get; set; }
        public bool IsError { get; set; }
        public string Timestamp { get; set; } = "";

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public class ChatMessageTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? UserTemplate { get; set; }
        public DataTemplate? AssistantTemplate { get; set; }
        public DataTemplate? CodeTemplate { get; set; }
        public DataTemplate? ResultTemplate { get; set; }
        public DataTemplate? LoadingTemplate { get; set; }
        public DataTemplate? WelcomeTemplate { get; set; }

        public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        {
            if (item is ChatMessage msg)
            {
                return msg.Type switch
                {
                    MessageType.User => UserTemplate,
                    MessageType.Assistant => AssistantTemplate,
                    MessageType.Code => CodeTemplate,
                    MessageType.Result => ResultTemplate,
                    MessageType.Loading => LoadingTemplate,
                    MessageType.Welcome => WelcomeTemplate,
                    _ => AssistantTemplate
                };
            }
            return base.SelectTemplate(item, container);
        }
    }

    public partial class ChatPanel : Page, IDockablePaneProvider
    {
        private readonly ObservableCollection<ChatMessage> _messages = new();
        private ClaudeService? _claudeService;
        private bool _isProcessing;

        public ChatPanel()
        {
            InitializeComponent();

            var selector = new ChatMessageTemplateSelector
            {
                UserTemplate = (DataTemplate)Resources["UserMessageTemplate"],
                AssistantTemplate = (DataTemplate)Resources["AssistantMessageTemplate"],
                CodeTemplate = (DataTemplate)Resources["CodeBlockTemplate"],
                ResultTemplate = (DataTemplate)Resources["ResultMessageTemplate"],
                LoadingTemplate = (DataTemplate)Resources["LoadingMessageTemplate"],
                WelcomeTemplate = (DataTemplate)Resources["WelcomeMessageTemplate"]
            };

            MessagesPanel.ItemTemplateSelector = selector;
            MessagesPanel.ItemsSource = _messages;

            InitializeService();
            ShowWelcome();
        }

        private void InitializeService()
        {
            var apiKey = Config.GetApiKey();
            if (apiKey != null)
            {
                _claudeService = new ClaudeService(apiKey);
            }
        }

        private void ShowWelcome()
        {
            _messages.Add(new ChatMessage { Type = MessageType.Welcome });
        }

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = this;
            data.InitialState = new DockablePaneState
            {
                DockPosition = DockPosition.Right
            };
        }

        private async void Send_Click(object sender, RoutedEventArgs e)
        {
            await SendMessage();
        }

        private async void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !_isProcessing &&
                (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                e.Handled = true;
                await SendMessage();
            }
        }

        private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            PlaceholderText.Visibility = string.IsNullOrEmpty(InputBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private async void Suggestion_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string suggestion)
            {
                InputBox.Text = suggestion;
                await SendMessage();
            }
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            _messages.Clear();
            ShowWelcome();
        }

        private void CopyCode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string code)
            {
                try
                {
                    Clipboard.SetText(code);
                }
                catch { }
            }
        }

        private async Task SendMessage()
        {
            string input = InputBox.Text.Trim();
            if (string.IsNullOrEmpty(input) || _isProcessing)
                return;

            // Remove welcome message on first interaction
            var welcome = _messages.FirstOrDefault(m => m.Type == MessageType.Welcome);
            if (welcome != null)
                _messages.Remove(welcome);

            if (_claudeService == null)
            {
                if (input.StartsWith("sk-ant-"))
                {
                    Config.SaveApiKey(input);
                    _claudeService = new ClaudeService(input);
                    InputBox.Text = "";
                    AddMessage("API key saved. You can now ask me anything about your Revit project!", MessageType.Assistant);
                    return;
                }

                AddMessage("No API key configured. Please enter your Anthropic API key, or set the ARCHIE_COPILOT_API_KEY environment variable and restart Revit.", MessageType.Result, isError: true);
                return;
            }

            AddMessage(input, MessageType.User);
            InputBox.Text = "";
            _isProcessing = true;
            SendButton.IsEnabled = false;

            var loadingMsg = new ChatMessage { Text = "", Type = MessageType.Loading };
            _messages.Add(loadingMsg);
            MessagesScroll.ScrollToBottom();

            try
            {
                var (fullResponse, codeBlocks) = await Task.Run(() =>
                    _claudeService.SendMessageAsync(input));

                _messages.Remove(loadingMsg);

                var parts = ParseResponse(fullResponse, codeBlocks);
                foreach (var (text, type) in parts)
                {
                    AddMessage(text, type);
                }
            }
            catch (Exception ex)
            {
                _messages.Remove(loadingMsg);
                AddMessage("Failed to get response: " + ex.Message, MessageType.Result, isError: true);
            }
            finally
            {
                _isProcessing = false;
                SendButton.IsEnabled = true;
            }
        }

        private List<(string text, MessageType type)> ParseResponse(string fullResponse, List<string> codeBlocks)
        {
            var parts = new List<(string, MessageType)>();

            if (codeBlocks.Count == 0)
            {
                parts.Add((fullResponse.Trim(), MessageType.Assistant));
                return parts;
            }

            string remaining = fullResponse;
            foreach (var code in codeBlocks)
            {
                int codeIndex = remaining.IndexOf(code);
                if (codeIndex > 0)
                {
                    int fenceStart = remaining.LastIndexOf("```", codeIndex);
                    if (fenceStart > 0)
                    {
                        string textBefore = remaining[..fenceStart].Trim();
                        if (!string.IsNullOrEmpty(textBefore))
                            parts.Add((textBefore, MessageType.Assistant));
                    }
                }

                parts.Add((code, MessageType.Code));

                int endFence = remaining.IndexOf("```", remaining.IndexOf(code) + code.Length);
                if (endFence >= 0)
                    remaining = remaining[(endFence + 3)..];
                else
                    remaining = "";
            }

            string trailing = remaining.Trim();
            if (!string.IsNullOrEmpty(trailing))
                parts.Add((trailing, MessageType.Assistant));

            return parts;
        }

        private void AddMessage(string text, MessageType type, bool isError = false)
        {
            Dispatcher.Invoke(() =>
            {
                _messages.Add(new ChatMessage
                {
                    Text = text,
                    Type = type,
                    IsError = isError,
                    Timestamp = DateTime.Now.ToString("h:mm tt")
                });
                MessagesScroll.ScrollToBottom();
            });
        }

        private void ExecuteCode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string code)
            {
                ExecuteInRevit(code);
            }
        }

        private void ExecuteInRevit(string code, int retryCount = 0)
        {
            var handler = App.CommandHandler;
            var externalEvent = App.RevitExternalEvent;

            if (handler == null || externalEvent == null)
            {
                AddMessage("External event not initialized.", MessageType.Result, isError: true);
                return;
            }

            handler.SetCode(code, (success, result) =>
            {
                Dispatcher.Invoke(async () =>
                {
                    AddMessage(result, MessageType.Result, isError: !success);
                    _claudeService?.AddExecutionResult(result);

                    // Auto-retry: if execution failed and we haven't retried too many times,
                    // send the error back to Claude and auto-execute the fix
                    if (!success && retryCount < 3 && _claudeService != null)
                    {
                        AddMessage("Fixing...", MessageType.Assistant);

                        var loadingMsg = new ChatMessage { Text = "", Type = MessageType.Loading };
                        _messages.Add(loadingMsg);
                        MessagesScroll.ScrollToBottom();

                        try
                        {
                            var (fullResponse, codeBlocks) = await Task.Run(() =>
                                _claudeService.SendMessageAsync(
                                    "The code failed with this error: " + result +
                                    "\nPlease fix the code and try again."));

                            _messages.Remove(loadingMsg);

                            var parts = ParseResponse(fullResponse, codeBlocks);
                            foreach (var (text, type) in parts)
                            {
                                AddMessage(text, type);
                            }

                            // Auto-execute the first code block from the fix
                            if (codeBlocks.Count > 0)
                            {
                                ExecuteInRevit(codeBlocks[0], retryCount + 1);
                            }
                        }
                        catch (Exception ex)
                        {
                            _messages.Remove(loadingMsg);
                            AddMessage("Failed to get fix: " + ex.Message, MessageType.Result, isError: true);
                        }
                    }
                });
            });

            externalEvent.Raise();
        }
    }
}
