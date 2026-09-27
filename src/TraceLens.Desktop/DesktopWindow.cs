using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace TraceLens.Desktop;

internal sealed class DesktopWindow : Form
{
    private readonly WebView2 webView = new() { Dock = DockStyle.Fill };
    private readonly Uri origin;

    public DesktopWindow(int port)
    {
        origin = new Uri($"http://127.0.0.1:{port}/");
        Text = "TraceLens";
        Width = 1500;
        Height = 950;
        MinimumSize = new Size(960, 640);
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(webView);
        Shown += InitializeBrowserAsync;
    }

    private async void InitializeBrowserAsync(object? sender, EventArgs e)
    {
        try
        {
            var profile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TraceLens", "WebView2");
            Directory.CreateDirectory(profile);
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profile);
            await webView.EnsureCoreWebView2Async(environment);
            webView.CoreWebView2.NavigationStarting += (_, navigation) =>
            {
                if (IsLocal(navigation.Uri)) return;
                navigation.Cancel = true;
                OpenExternal(navigation.Uri);
            };
            webView.CoreWebView2.NewWindowRequested += (_, request) =>
            {
                request.Handled = true;
                if (IsLocal(request.Uri)) webView.CoreWebView2.Navigate(request.Uri);
                else OpenExternal(request.Uri);
            };
            webView.Source = origin;
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"화면을 열지 못했다. WebView2 설치 상태를 확인해야 한다.\n\n{exception.Message}",
                "TraceLens", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    private bool IsLocal(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri) &&
        uri.Scheme == origin.Scheme && uri.Host == origin.Host && uri.Port == origin.Port;

    private static void OpenExternal(string address)
    {
        if (Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
}
