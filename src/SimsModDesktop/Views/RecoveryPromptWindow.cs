using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SimsModDesktop.Application.Recovery;

namespace SimsModDesktop.Views;

public sealed class RecoveryPromptWindow : Window
{
    public RecoveryPromptWindow(IReadOnlyList<RecoverableOperationRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var latest = records.FirstOrDefault() ?? throw new ArgumentException("At least one record is required.", nameof(records));

        Title = "Recover Interrupted Task";
        Width = 520;
        MinWidth = 440;
        Height = 240;
        MinHeight = 220;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        var createdText = latest.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        var message = new TextBlock
        {
            Text = $"{latest.DisplayTitle}\n\nStarted before: {createdText}\nAction: {latest.Action}",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 20)
        };

        var resumeButton = new Button
        {
            Content = "Resume",
            MinWidth = 96
        };
        resumeButton.Click += (_, _) => Close(new RecoveryPromptDecision
        {
            OperationId = latest.OperationId,
            Action = RecoveryPromptAction.Resume
        });

        var abandonButton = new Button
        {
            Content = "Ignore",
            MinWidth = 96
        };
        abandonButton.Click += (_, _) => Close(new RecoveryPromptDecision
        {
            OperationId = latest.OperationId,
            Action = RecoveryPromptAction.Abandon
        });

        var clearButton = new Button
        {
            Content = "Clear",
            MinWidth = 96
        };
        clearButton.Click += (_, _) => Close(new RecoveryPromptDecision
        {
            OperationId = latest.OperationId,
            Action = RecoveryPromptAction.Clear
        });

        Content = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(24),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = "The app found an unfinished task from the previous run.",
                    FontWeight = Avalonia.Media.FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                },
                message,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children =
                    {
                        abandonButton,
                        clearButton,
                        resumeButton
                    }
                }
            }
        };
    }
}
