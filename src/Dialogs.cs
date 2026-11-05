using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TimeTrack
{
    public class ConfirmDialog : Window
    {
        private bool _accepted;

        public bool Accepted
        {
            get { return _accepted; }
        }

        public ConfirmDialog(string title, string message, string confirmText, bool danger)
        {
            Title = title;
            Width = 430;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Ui.Bg;
            FontFamily = Ui.Font;
            WindowFx.Apply(this);
            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) Close();
            };

            Grid layout = new Grid();
            layout.Margin = new Thickness(24, 22, 24, 20);
            layout.RowDefinitions.Add(new RowDefinition());
            layout.RowDefinitions.Add(new RowDefinition());
            layout.RowDefinitions.Add(new RowDefinition());

            StackPanel heading = new StackPanel();
            heading.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ButtonState == MouseButtonState.Pressed) DragMove();
            };
            TextBlock titleBlock = Ui.TextBlock(title, 17, Ui.Text, FontWeights.SemiBold);
            heading.Children.Add(titleBlock);
            TextBlock messageBlock = Ui.TextBlock(message, 12.5, Ui.Muted);
            messageBlock.Margin = new Thickness(0, 10, 0, 0);
            messageBlock.TextWrapping = TextWrapping.Wrap;
            messageBlock.LineHeight = 20;
            heading.Children.Add(messageBlock);
            Grid.SetRow(heading, 0);
            layout.Children.Add(heading);

            StackPanel buttons = new StackPanel();
            buttons.Orientation = Orientation.Horizontal;
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            buttons.Margin = new Thickness(0, 26, 0, 0);

            Border cancel = DialogButton("取消", Ui.TextSoft, Ui.Card2, Ui.CardHover);
            cancel.Margin = new Thickness(0, 0, 10, 0);
            cancel.MouseLeftButtonUp += delegate { Close(); };
            buttons.Children.Add(cancel);

            Brush confirmBackground = danger ? Ui.RedSoft : Ui.Accent;
            Brush confirmHover = danger ? Ui.DangerHover : Ui.AccentHover;
            Brush confirmForeground = danger ? Ui.DangerText : Brushes.White;
            Border confirm = DialogButton(confirmText, confirmForeground, confirmBackground, confirmHover);
            confirm.MouseLeftButtonUp += delegate
            {
                _accepted = true;
                Close();
            };
            buttons.Children.Add(confirm);
            Grid.SetRow(buttons, 2);
            layout.Children.Add(buttons);

            Border root = new Border();
            root.Background = Ui.Bg;
            root.BorderBrush = Ui.Stroke;
            root.BorderThickness = new Thickness(1);
            root.CornerRadius = new CornerRadius(14);
            root.Child = layout;
            Content = root;
        }

        private static Border DialogButton(string text, Brush foreground, Brush background, Brush hover)
        {
            Border border = new Border();
            border.Background = background;
            border.CornerRadius = new CornerRadius(8);
            border.Padding = new Thickness(16, 7, 16, 7);
            border.Cursor = Cursors.Hand;
            border.Child = Ui.TextBlock(text, 12.5, foreground, FontWeights.Medium);
            border.MouseEnter += delegate { border.Background = hover; };
            border.MouseLeave += delegate { border.Background = background; };
            return border;
        }
    }

    public class RenameDialog : Window
    {
        private readonly TextBox _nameBox;

        public RenameDialog(string currentName, string defaultName)
        {
            Title = "重命名应用";
            Width = 460;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Ui.Bg;
            FontFamily = Ui.Font;
            WindowFx.Apply(this);
            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) Close();
            };

            StackPanel layout = new StackPanel();
            layout.Margin = new Thickness(24, 22, 24, 20);

            StackPanel heading = new StackPanel();
            heading.Cursor = Cursors.SizeAll;
            heading.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ButtonState == MouseButtonState.Pressed) DragMove();
            };
            heading.Children.Add(Ui.TextBlock("重命名应用", 17, Ui.Text, FontWeights.SemiBold));
            TextBlock subtitle = Ui.TextBlock("自定义名称会显示在排行榜、统计和应用详情中。", 11.5, Ui.Muted);
            subtitle.Margin = new Thickness(0, 8, 0, 0);
            subtitle.TextWrapping = TextWrapping.Wrap;
            layout.Children.Add(heading);
            layout.Children.Add(subtitle);

            TextBlock label = Ui.TextBlock("应用名称", 11.5, Ui.TextSoft, FontWeights.Medium);
            label.Margin = new Thickness(0, 22, 0, 7);
            layout.Children.Add(label);

            _nameBox = new TextBox();
            _nameBox.Text = currentName == null ? string.Empty : currentName;
            _nameBox.MaxLength = 40;
            _nameBox.FontFamily = Ui.Font;
            _nameBox.FontSize = 13.5;
            _nameBox.Foreground = Ui.Text;
            _nameBox.Background = Ui.Field;
            _nameBox.BorderBrush = Ui.Stroke;
            _nameBox.BorderThickness = new Thickness(1);
            _nameBox.Padding = new Thickness(11, 9, 11, 9);
            _nameBox.CaretBrush = Ui.Text;
            _nameBox.SelectionBrush = Ui.Accent;
            _nameBox.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Enter)
                {
                    Accept();
                    e.Handled = true;
                }
            };
            layout.Children.Add(_nameBox);

            TextBlock hint = Ui.TextBlock("留空，或填回默认名称「" + (defaultName == null ? "未知应用" : defaultName) + "」，即可恢复默认。", 10.5, Ui.Dim);
            hint.Margin = new Thickness(0, 8, 0, 0);
            hint.TextWrapping = TextWrapping.Wrap;
            hint.LineHeight = 17;
            layout.Children.Add(hint);

            StackPanel buttons = new StackPanel();
            buttons.Orientation = Orientation.Horizontal;
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            buttons.Margin = new Thickness(0, 24, 0, 0);

            Border cancel = DialogButton("取消", Ui.TextSoft, Ui.Card2, Ui.CardHover);
            cancel.Margin = new Thickness(0, 0, 10, 0);
            cancel.MouseLeftButtonUp += delegate { Close(); };
            buttons.Children.Add(cancel);

            Border save = DialogButton("保存", Brushes.White, Ui.Accent, Ui.AccentHover);
            save.MouseLeftButtonUp += delegate { Accept(); };
            buttons.Children.Add(save);
            layout.Children.Add(buttons);

            Border root = new Border();
            root.Background = Ui.Bg;
            root.BorderBrush = Ui.Stroke;
            root.BorderThickness = new Thickness(1);
            root.CornerRadius = new CornerRadius(14);
            root.Child = layout;
            Content = root;

            Loaded += delegate
            {
                _nameBox.Focus();
                _nameBox.SelectAll();
            };
        }

        public string NewName
        {
            get { return _nameBox.Text.Trim(); }
        }

        private void Accept()
        {
            DialogResult = true;
        }

        private static Border DialogButton(string text, Brush foreground, Brush background, Brush hover)
        {
            Border border = new Border();
            border.Background = background;
            border.CornerRadius = new CornerRadius(8);
            border.Padding = new Thickness(16, 7, 16, 7);
            border.Cursor = Cursors.Hand;
            border.Child = Ui.TextBlock(text, 12.5, foreground, FontWeights.Medium);
            border.MouseEnter += delegate { border.Background = hover; };
            border.MouseLeave += delegate { border.Background = background; };
            return border;
        }
    }
}