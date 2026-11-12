using System.Text.Json.Nodes;

namespace DaylightCompare.Domain.Policy;

/// <summary>
/// 내장 기본 정책. 처음 실행할 때 사용자 정책 파일(<c>policy.json</c>)로 그대로 쓰이고, 겹치기의 맨 아래 층(<see cref="Defaults"/>)이 된다.
/// 사용자가 파일에서 형식을 지우거나 고쳐도 내장 값이 되살아나지 않는다 — 맨 아래 층은 <c>defaults</c>의 빠진 키만 채운다.
/// </summary>
public static class BuiltinPolicy
{
    // 정규식은 JSON 문자열이라 \ 를 두 번 쓴다.
    const string Text = """
    {
      "schema": 1,
      "defaults": {
        "folder": {
          "criteria": [
            { "by": "size" },
            { "by": "timestamp", "toleranceSec": 2, "ignoreHourOffsets": true },
            { "by": "content" }
          ],
          "filters": {
            "excludeFolders": [".git", ".svn", ".hg", ".vs", ".idea", "node_modules", "bin", "obj", "__pycache__", ".dart_tool", "build"],
            "exclude": ["*.bak", "~*", "*.tmp", "Thumbs.db", ".DS_Store", "desktop.ini"],
            "showHidden": false
          }
        },
        "text": {
          "alignment": "histogram",
          "normalize": { "lineBreaks": true, "trailingSpace": true, "encoding": true, "case": false, "allWhitespace": false },
          "tabWidth": 4
        },
        "binary": { "align": "fixed", "fill": 255 },
        "image": { "tolerance": 0, "ignoreAlpha": false, "align": "topLeft", "maxDiffPercent": 0 },
        "table": { "delimiter": ",", "header": true, "numericTolerance": 0 }
      },
      "formats": [
        { "id": "c-family", "view": "text", "grammar": "c-like", "importance": { "unimportant": ["comment", "whitespace"] },
          "masks": ["*.c", "*.h", "*.cc", "*.cpp", "*.cxx", "*.hpp", "*.hh", "*.cs", "*.java", "*.kt", "*.kts", "*.js", "*.mjs", "*.ts", "*.tsx", "*.jsx",
                    "*.dart", "*.go", "*.rs", "*.swift", "*.m", "*.mm", "*.scala", "*.groovy", "*.gradle", "*.proto", "*.css", "*.scss", "*.less"] },
        { "id": "hash-comment", "view": "text", "grammar": "hash", "importance": { "unimportant": ["comment", "whitespace"] },
          "masks": ["*.py", "*.sh", "*.bash", "*.zsh", "*.ps1", "*.rb", "*.pl", "*.r", "*.yaml", "*.yml", "*.toml", "*.cmake", "CMakeLists.txt",
                    "Makefile", "*.mk", "Dockerfile", ".gitignore", ".gitattributes", "*.conf", "*.properties"] },
        { "id": "ini", "view": "text", "grammar": "ini", "importance": { "unimportant": ["comment", "whitespace"] }, "masks": ["*.ini", "*.cfg", "*.inf", "*.reg"] },
        { "id": "xml", "view": "text", "grammar": "xml", "importance": { "unimportant": ["comment", "whitespace"] },
          "masks": ["*.xml", "*.xaml", "*.axaml", "*.csproj", "*.props", "*.targets", "*.slnx", "*.plist", "*.svg", "*.html", "*.htm", "*.xhtml", "*.resx", "*.config"] },
        { "id": "json", "view": "text", "grammar": "json", "importance": { "unimportant": ["whitespace"] }, "masks": ["*.json", "*.jsonc", "*.arb", "*.json5"] },
        { "id": "sql", "view": "text", "grammar": "sql", "importance": { "unimportant": ["comment", "whitespace"] }, "masks": ["*.sql"] },
        { "id": "markdown", "view": "text", "masks": ["*.md", "*.markdown", "*.txt", "*.log", "*.rst", "*.adoc"] },
        { "id": "table", "view": "table", "masks": ["*.csv"], "table": { "delimiter": "," } },
        { "id": "table-tab", "view": "table", "masks": ["*.tsv", "*.tab"], "table": { "delimiter": "\t" } },
        { "id": "images", "view": "image", "masks": ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp", "*.ico"] },
        { "id": "firmware-ihex", "view": "binary", "converter": "builtin:ihex-to-bin", "masks": ["*.hex", "*.ihex", "*.ihx"] },
        { "id": "firmware-srec", "view": "binary", "converter": "builtin:srec-to-bin", "masks": ["*.s19", "*.s28", "*.s37", "*.srec", "*.mot"] },
        { "id": "binary", "view": "binary",
          "masks": ["*.bin", "*.img", "*.exe", "*.dll", "*.so", "*.dylib", "*.o", "*.obj", "*.a", "*.lib", "*.elf", "*.pdb", "*.class", "*.wasm",
                    "*.pdf", "*.zip", "*.7z", "*.gz", "*.tar", "*.jar", "*.apk", "*.iso", "*.dmg", "*.ttf", "*.otf", "*.woff", "*.woff2",
                    "*.mp3", "*.mp4", "*.wav", "*.avi", "*.mov", "*.docx", "*.xlsx", "*.pptx", "*.db", "*.sqlite"] }
      ],
      "grammars": {
        "c-like": {
          "comment": ["//.*$", "/\\*[\\s\\S]*?\\*/"],
          "string": ["\"(?:\\\\.|[^\"\\\\])*\"", "'(?:\\\\.|[^'\\\\])*'", "`(?:\\\\.|[^`\\\\])*`"],
          "whitespace": ["[ \\t]+"]
        },
        "hash": {
          "comment": ["#.*$"],
          "string": ["\"(?:\\\\.|[^\"\\\\])*\"", "'(?:\\\\.|[^'\\\\])*'"],
          "whitespace": ["[ \\t]+"]
        },
        "ini": {
          "comment": ["^\\s*[;#].*$"],
          "whitespace": ["[ \\t]+"]
        },
        "xml": {
          "comment": ["<!--[\\s\\S]*?-->"],
          "string": ["\"[^\"]*\"", "'[^']*'"],
          "whitespace": ["[ \\t]+"]
        },
        "json": {
          "comment": ["//.*$", "/\\*[\\s\\S]*?\\*/"],
          "string": ["\"(?:\\\\.|[^\"\\\\])*\""],
          "whitespace": ["[ \\t]+"]
        },
        "sql": {
          "comment": ["--.*$", "/\\*[\\s\\S]*?\\*/"],
          "string": ["'(?:''|[^'])*'"],
          "whitespace": ["[ \\t]+"]
        }
      }
    }
    """;

    /// <summary>내장 정책 전체 (처음 만드는 사용자 정책 파일의 내용). 부를 때마다 새 객체다.</summary>
    public static JsonObject Json() => (JsonObject)JsonNode.Parse(Text)!;

    /// <summary>겹치기의 맨 아래 층: 내장 <c>defaults</c>.</summary>
    public static JsonObject Defaults() => (JsonObject)Json()["defaults"]!;

    static readonly HashSet<string> GrammarIds = [.. ((JsonObject)Json()["grammars"]!).Select(kv => kv.Key)];

    public static bool HasGrammar(string id) => GrammarIds.Contains(id);
}
