using SimsModDesktop.Application.Models;

namespace SimsModDesktop.Application.Preview;

public interface IPreviewPageBuilder
{
    SimsTrayPreviewPage BuildPage(IReadOnlyList<SimsTrayPreviewItem> items, int pageSize, int pageIndex);
}
