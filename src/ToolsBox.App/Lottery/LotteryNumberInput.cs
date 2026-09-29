using ToolsBox.App.Infrastructure;

namespace ToolsBox.App.Lottery;

public sealed class LotteryNumberInput : ObservableObject
{
    private string _text = "";
    public string Text { get => _text; set => SetProperty(ref _text, value); }
}
