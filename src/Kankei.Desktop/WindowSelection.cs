namespace Kankei.Desktop;

public static class WindowSelection
{
    public static IReadOnlyList<SavedWindow> CaptureSelected(IReadOnlyList<SavedWindow> selected, IReadOnlyList<SavedWindow> current)
    {
        if (selected.Count == 0) throw new InvalidOperationException("保存するウィンドウを1つ以上選んでください。");
        return selected.Select(saved =>
        {
            var live = current.SingleOrDefault(x => x.WindowHandle == saved.WindowHandle && x.ProcessStartTicks == saved.ProcessStartTicks
                && x.ExecutablePath == saved.ExecutablePath && x.ClassName == saved.ClassName);
            if (live is null) throw new InvalidOperationException("選んだウィンドウが閉じられました。一覧を更新して選び直してください。");
            return live with { AdapterId = null, AdapterStateJson = null };
        }).ToArray();
    }
}
