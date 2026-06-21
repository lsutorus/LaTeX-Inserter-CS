using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LaTeXInserter.Models;

[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(HotkeyChord))]
[JsonSerializable(typeof(ModifierMask))]
internal partial class JsonContext : JsonSerializerContext;
