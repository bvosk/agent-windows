using System.Globalization;
using System.Windows;
using System.Windows.Threading;

namespace AgentWindows.E2eTarget;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        for (var i = 1; i <= 100; i++)
        {
            BigList.Items.Add(string.Create(CultureInfo.InvariantCulture, $"Item {i}"));
        }
    }

    private void OnSubmitClick(object sender, RoutedEventArgs e) =>
        ResultLabel.Text = $"Submitted: {InputBox.Text}";

    private void OnSlowClick(object sender, RoutedEventArgs e)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            ResultLabel.Text = "Done waiting";
            timer.Stop();
        };
        timer.Start();
    }
}
