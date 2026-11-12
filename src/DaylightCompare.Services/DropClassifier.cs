using DaylightCompare.Domain;
using DaylightCompare.Domain.Drop;

namespace DaylightCompare.Services;

/// <summary>놓은 경로를 <see cref="DroppedItem"/>으로 (폴더 / 압축 파일 / 파일). 없는 경로는 뺀다.</summary>
public static class DropClassifier
{
    public static IReadOnlyList<DroppedItem> Classify(IEnumerable<string> paths)
    {
        var items = new List<DroppedItem>();
        foreach (var path in paths)
        {
            if (Directory.Exists(path)) items.Add(new DroppedItem(path, DroppedKind.Folder));
            else if (File.Exists(path))
                items.Add(new DroppedItem(path, ArchivePaths.IsArchiveName(Path.GetFileName(path)) ? DroppedKind.Archive : DroppedKind.File));
        }
        return items;
    }
}
