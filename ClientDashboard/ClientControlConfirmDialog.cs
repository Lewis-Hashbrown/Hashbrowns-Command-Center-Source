using System;
using System.Windows;
namespace ClientDashboard;

public sealed class ClientControlConfirmDialog : Window
{
    private readonly System.Windows.Controls.CheckBox _autoAcceptCheckBox;

    public bool AutoAcceptChecked => _autoAcceptCheckBox.IsChecked == true;

    public ClientControlConfirmDialog(string clientTitle, bool autoAcceptChecked)
    {
        Title = "Control Client";
        Width = 460;
        Height = 200;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = System.Windows.Media.Brushes.WhiteSmoke;

        var root = new System.Windows.Controls.Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });

        var title = new System.Windows.Controls.TextBlock
        {
            Text = "Control this client?",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        System.Windows.Controls.Grid.SetRow(title, 0);
        root.Children.Add(title);

        var subtitle = new System.Windows.Controls.TextBlock
        {
            Text = clientTitle,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        System.Windows.Controls.Grid.SetRow(subtitle, 1);
        root.Children.Add(subtitle);

        _autoAcceptCheckBox = new System.Windows.Controls.CheckBox
        {
            Content = "Always control immediately (don't ask again)",
            IsChecked = autoAcceptChecked,
            VerticalAlignment = VerticalAlignment.Center
        };
        System.Windows.Controls.Grid.SetRow(_autoAcceptCheckBox, 2);
        root.Children.Add(_autoAcceptCheckBox);

        var buttons = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        var noBtn = new System.Windows.Controls.Button
        {
            Content = "No",
            Width = 88,
            Margin = new Thickness(0, 0, 8, 0)
        };
        noBtn.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };

        var yesBtn = new System.Windows.Controls.Button
        {
            Content = "Yes",
            Width = 88
        };
        yesBtn.Click += (_, _) =>
        {
            DialogResult = true;
            Close();
        };

        buttons.Children.Add(noBtn);
        buttons.Children.Add(yesBtn);
        System.Windows.Controls.Grid.SetRow(buttons, 3);
        root.Children.Add(buttons);

        Content = root;
    }
}
