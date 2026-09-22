using System.IO;
using System.Windows;
using System.Windows.Controls;
using ToolsBox.App.WebResources;
using ToolsBox.MediaDownloads;

namespace ToolsBox.App.Tests;

public class EnvironmentWindowTests
{
    [Fact]
    public void DialogProvidesSingleMissingInstallActionAndTwoEnvironmentItems()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var type = typeof(WebResourceWindow).Assembly.GetType("ToolsBox.App.WebResources.EnvironmentWindow");
                Assert.NotNull(type);
                var window = (Window)Activator.CreateInstance(type!, new ComponentManager(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))))!;
                Assert.Equal("安装缺失项", Assert.IsType<Button>(window.FindName("InstallMissingButton")).Content);
                Assert.NotNull(window.FindName("RuntimeStatus"));
                Assert.NotNull(window.FindName("VideoStatus"));
                Assert.Equal(SizeToContent.Manual, window.SizeToContent);
                ((TextBlock)window.FindName("RuntimeStatus")).Text = "已安装 · 153.0.4234.32";
                ((TextBlock)window.FindName("VideoStatus")).Text = "缺失、不完整或检查失败 · 需要安装完整组件包以下载视频";
                var details = (TextBox)window.FindName("Details");
                details.Text = "WebView2 Evergreen x64 · Microsoft 官方\nhttps://go.microsoft.com/fwlink/?linkid=2124701\n\n视频组件：yt-dlp + FFmpeg + ffprobe";
                ((Expander)details.Parent).IsExpanded = true;
                WpfTestSnapshot.SaveWindowContent(window, 620, 500, "environment-window-minimum.png");
                window.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        Assert.Null(failure);
    }
}
