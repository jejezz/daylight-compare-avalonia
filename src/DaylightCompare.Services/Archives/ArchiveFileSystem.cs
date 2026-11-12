using System.Formats.Tar;
using System.IO.Compression;
using DaylightCompare.Domain;

namespace DaylightCompare.Services;

/// <summary>
/// 압축 파일 하나를 읽기 전용 폴더로 보이게 한 것 (ROADMAP 1단계). 폴더 비교가 로컬·원격과 같은 <see cref="IFileSystem"/>으로 다룬다.
/// 경로는 <see cref="ArchivePaths"/> 형식(<c>a.zip!/dir/file</c>). 압축 파일은 로컬에 있어야 한다.
/// <list type="bullet">
/// <item>처음 쓸 때 목록(색인)을 한 번 만든다. 폴더 항목이 따로 없는 압축 파일도 경로에서 폴더를 만들어 낸다.</item>
/// <item>zip은 System.IO.Compression, tar·tar.gz는 System.Formats.Tar — 둘 다 .NET 내장이라 NativeAOT에 문제없다.</item>
/// <item>쓰기 작업은 모두 <see cref="FileError.AccessDenied"/>로 거절한다.</item>
/// </list>
/// </summary>
public sealed class ArchiveFileSystem : IFileSystem
{
    readonly string _archive;
    readonly ArchiveKind _kind;
    readonly Lazy<Index> _index;

    public ArchiveFileSystem(string archivePath, ArchiveKind? kind = null)
    {
        _archive = archivePath;
        _kind = kind ?? ArchivePaths.KindOf(archivePath) ?? throw new ArgumentException($"압축 파일이 아니다: {archivePath}", nameof(archivePath));
        _index = new Lazy<Index>(BuildIndex, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string ArchivePath => _archive;

    /// <summary>압축 파일의 루트 경로 (<c>a.zip!/</c>).</summary>
    public string Root => ArchivePaths.Format(_archive);

    sealed record Item(string Inner, bool IsDirectory, long Size, DateTimeOffset? Modified);

    sealed class Index
    {
        public readonly Dictionary<string, Item> Items = new(StringComparer.Ordinal) { [""] = new Item("", true, 0, null) };
        public readonly Dictionary<string, List<string>> Children = new(StringComparer.Ordinal) { [""] = [] };

        public void Add(string inner, bool isDirectory, long size, DateTimeOffset? modified)
        {
            if (inner.Length == 0) return;
            EnsureFolder(ArchivePaths.ParentInner(inner)!);
            if (Items.TryGetValue(inner, out var existing))
            {
                // 같은 이름이 두 번 나오면(tar에 덧붙인 경우) 나중 것이 이긴다. 경로에서 만들어 낸 폴더는 실제 항목으로 바꾼다.
                Items[inner] = existing.IsDirectory && isDirectory ? existing with { Modified = modified ?? existing.Modified } : new Item(inner, isDirectory, size, modified);
            }
            else
            {
                Items[inner] = new Item(inner, isDirectory, size, modified);
                Children[ArchivePaths.ParentInner(inner)!].Add(inner);
            }
            if (isDirectory) Children.TryAdd(inner, []);
        }

        void EnsureFolder(string inner)
        {
            if (Items.ContainsKey(inner)) return;
            EnsureFolder(ArchivePaths.ParentInner(inner)!);
            Items[inner] = new Item(inner, true, 0, null);
            Children[ArchivePaths.ParentInner(inner)!].Add(inner);
            Children.TryAdd(inner, []);
        }
    }

    Index BuildIndex()
    {
        var index = new Index();
        try
        {
            if (_kind == ArchiveKind.Zip)
            {
                using var zip = ZipFile.OpenRead(_archive);
                foreach (var e in zip.Entries)
                {
                    if (ArchivePaths.EntryName(e.FullName) is not { } inner) continue;
                    var isDir = e.FullName.EndsWith('/') || e.FullName.EndsWith('\\');
                    index.Add(inner, isDir, isDir ? 0 : e.Length, e.LastWriteTime);
                }
            }
            else
            {
                using var reader = OpenTar(out var owner);
                using (owner)
                {
                    while (reader.GetNextEntry(copyData: false) is { } e)
                    {
                        if (ArchivePaths.EntryName(e.Name) is not { } inner) continue;
                        if (e.EntryType == TarEntryType.Directory) index.Add(inner, true, 0, e.ModificationTime);
                        else if (IsRegular(e.EntryType)) index.Add(inner, false, e.Length, e.ModificationTime);
                        // 링크·장치 파일 등은 비교 대상이 아니므로 넣지 않는다.
                    }
                }
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or FormatException or UnauthorizedAccessException)
        {
            throw Error(ex, Locations.NameOf(_archive));
        }
        return index;
    }

    static bool IsRegular(TarEntryType type) =>
        type is TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.ContiguousFile;

    TarReader OpenTar(out IDisposable owner)
    {
        Stream file = new FileStream(_archive, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024);
        Stream data = _kind == ArchiveKind.TarGz ? new GZipStream(file, CompressionMode.Decompress) : file;
        owner = data;
        return new TarReader(data, leaveOpen: true);
    }

    static Exception Error(Exception ex, string name) => ex switch
    {
        FileOperationException => ex,
        FileNotFoundException or DirectoryNotFoundException => new FileOperationException(new FileError.NotFound(name)),
        UnauthorizedAccessException => new FileOperationException(new FileError.AccessDenied(name)),
        _ => new FileOperationException(new FileError.Other(ex.Message)),
    };

    string InnerOf(string path) =>
        ArchivePaths.TryParse(path) is { } loc && string.Equals(loc.Archive, _archive, StringComparison.OrdinalIgnoreCase)
            ? loc.Inner
            : throw new ArgumentException($"이 압축 파일의 경로가 아니다: {path}", nameof(path));

    FileEntry ToEntry(Item item)
    {
        var name = item.Inner.Length == 0 ? Locations.NameOf(_archive) : ArchivePaths.NameOfInner(item.Inner);
        return new FileEntry(ArchivePaths.Format(_archive, item.Inner), name, item.IsDirectory,
            item.IsDirectory ? null : item.Size, item.Modified, name.StartsWith('.'));
    }

    public Task<IReadOnlyList<FileEntry>> ListAsync(string path, CancellationToken ct = default) => Task.Run<IReadOnlyList<FileEntry>>(() =>
    {
        var inner = InnerOf(path);
        var index = _index.Value;
        if (!index.Children.TryGetValue(inner, out var children))
            throw new FileOperationException(new FileError.NotFound(inner.Length == 0 ? Locations.NameOf(_archive) : ArchivePaths.NameOfInner(inner)));
        return EntrySorting.Sort(children.Select(c => ToEntry(index.Items[c])), SortField.Name, ascending: true);
    }, ct);

    public Task<FileEntry?> StatAsync(string path, CancellationToken ct = default) => Task.Run(() =>
    {
        var inner = InnerOf(path);
        return _index.Value.Items.TryGetValue(inner, out var item) ? ToEntry(item) : null;
    }, ct);

    public Task<Stream> OpenReadAsync(string path, CancellationToken ct = default) => Task.Run<Stream>(() =>
    {
        var inner = InnerOf(path);
        var name = ArchivePaths.NameOfInner(inner);
        if (!_index.Value.Items.TryGetValue(inner, out var item) || item.IsDirectory)
            throw new FileOperationException(new FileError.NotFound(name));
        try
        {
            if (_kind == ArchiveKind.Zip)
            {
                var zip = ZipFile.OpenRead(_archive);
                var entry = zip.Entries.LastOrDefault(e => ArchivePaths.EntryName(e.FullName) == inner);
                if (entry is null)
                {
                    zip.Dispose();
                    throw new FileOperationException(new FileError.NotFound(name));
                }
                return new OwnedStream(entry.Open(), zip);
            }
            // tar는 순서대로만 읽을 수 있다 → 그 항목까지 넘긴다. 같은 이름이 여러 번이면 마지막 것.
            // 마지막 것을 알려면 끝까지 봐야 하므로, 몇 번째인지 먼저 세고 다시 연다.
            var occurrence = 0;
            {
                using var counter = OpenTar(out var counterOwner);
                using (counterOwner)
                    while (counter.GetNextEntry(copyData: false) is { } e)
                        if (IsRegular(e.EntryType) && ArchivePaths.EntryName(e.Name) == inner) occurrence++;
            }
            var reader = OpenTar(out var owner);
            var seen = 0;
            while (reader.GetNextEntry(copyData: false) is { } e)
            {
                ct.ThrowIfCancellationRequested();
                if (!IsRegular(e.EntryType) || ArchivePaths.EntryName(e.Name) != inner || ++seen < occurrence) continue;
                return new OwnedStream(e.DataStream ?? Stream.Null, reader, owner);
            }
            reader.Dispose();
            owner.Dispose();
            throw new FileOperationException(new FileError.NotFound(name));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or FormatException or UnauthorizedAccessException)
        {
            throw Error(ex, name);
        }
    }, ct);

    Task<T> ReadOnly<T>(string path) =>
        Task.FromException<T>(new FileOperationException(new FileError.AccessDenied(Locations.NameOf(path))));

    Task ReadOnly(string path) => ReadOnly<bool>(path);

    public Task<Stream> OpenWriteAsync(string path, bool overwrite, CancellationToken ct = default) => ReadOnly<Stream>(path);
    public Task CreateDirectoryAsync(string path, CancellationToken ct = default) => ReadOnly(path);
    public Task DeleteAsync(string path, CancellationToken ct = default) => ReadOnly(path);
    public Task RenameAsync(string from, string to, CancellationToken ct = default) => ReadOnly(from);

    /// <summary>시각을 못 맞춰도 오류가 아니다 (인터페이스 약속) → 조용히 무시한다.</summary>
    public Task SetModifiedAsync(string path, DateTimeOffset modified, CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>읽기 스트림을 닫을 때 그것이 기대는 압축 파일 핸들도 함께 닫는다.</summary>
    sealed class OwnedStream(Stream inner, params IDisposable[] owners) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => inner.ReadAsync(buffer, ct);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => inner.ReadAsync(buffer, offset, count, ct);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                foreach (var owner in owners) owner.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
