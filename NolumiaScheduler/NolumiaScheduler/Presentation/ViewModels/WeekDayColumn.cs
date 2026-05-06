using System.Collections.ObjectModel;

namespace NolumiaScheduler.Presentation.ViewModels;

public sealed class WeekDayColumn
{
    public WeekDayColumn(string header)
    {
        Header = header;
    }

    public string Header { get; }
    public ObservableCollection<WeekEventBlock> EventBlocks { get; } = [];
}
