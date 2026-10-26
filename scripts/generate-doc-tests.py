import glob
import os
import re

docs = sorted(glob.glob('docs/*.md'))
output_dir = 'tests/Zaldaryon.Pharos.Docs.Tests/Generated'
os.makedirs(output_dir, exist_ok=True)

for f in glob.glob(f'{output_dir}/*.cs'):
    os.remove(f)

common_usings = """
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;
using Vintagestory.Server;
using Xunit;
using ProtoBuf;
using Zaldaryon.Pharos;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Bridge;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Events;
using Zaldaryon.Pharos.Fixtures;
using Zaldaryon.Pharos.Graphics;
using Zaldaryon.Pharos.Network;
using Zaldaryon.Pharos.Player;
using Zaldaryon.Pharos.Reporting;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.Translations;
using Zaldaryon.Pharos.UI;
using Zaldaryon.Pharos.Visual;
using Zaldaryon.Pharos.World;
using Zaldaryon.Pharos.Inspection;
using Zaldaryon.Pharos.Timing;
using Zaldaryon.Pharos.Audio;
using Zaldaryon.Pharos.Culling;
using Zaldaryon.Pharos.Input;
using Zaldaryon.Pharos.Memory;
using Zaldaryon.Pharos.Performance;
using Zaldaryon.Pharos.XUnit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Docs.Tests;
using WorldOptions = global::Atlas.Api.WorldOptions;
"""

container_members = """
        protected BlockPos nearClient => new(10, 64, 10);
        protected BlockPos nearAlice => new(10, 64, 10);
        protected BlockPos chestPos => new(10, 64, 10);
        protected BlockPos pos => new(10, 64, 10);
        protected BlockPos lamp => new(10, 64, 10);
        protected BlockPos lever => new(10, 64, 10);
        protected BlockPos doorPos => new(10, 64, 10);
        protected new string PlayerName => "TestPlayer";
        protected PlayerVitals vitals => null!;
        protected ClientVitals clientVitals => Player.Vitals;
        protected ServerTestPlayer player => null!;
        protected ClientServerLoopbackSession session => Session;
        protected EmbeddedServerHost serverHost => Server;
        protected BlockPos ground => new(10, 64, 10);
        protected long wolfId => 1;
        protected Entity wolf => null!;
        protected int SparkColor => 0;
        protected List<BlockPos> expectedBlocks => [];
        protected ICoreServerAPI sapi => Api;
        protected string playerUid => "playerUid";
        protected ServerTestPlayer alice => null!;
        protected ServerTestPlayer bob => null!;
        protected Entity entity => null!;
        protected BotResult used => new(true);
        protected List<string> myCache => [];
        protected DoorMock Door(BlockPos pos) => new();
        protected string DumpInventory() => string.Empty;
        protected string _lastFailure = string.Empty;
        protected List<BlockPos> marked => [];
        protected new int DataFilePort(string name) => 8080;
        protected EmbeddedServerHost host => ServerHost;
        protected EmbeddedServerHost Host => ServerHost;
        protected ICoreServerAPI api => Api;
        protected HeadlessClient client => Client;
"""

# --- C# token masking -------------------------------------------------------
# Whitespace inside verbatim/raw strings is data, and text that looks like
# attributes, type names or using directives inside a string literal is still
# string data. Every line transformation below runs on a token mask so literal
# and comment regions are never rewritten, blanked or extracted.
# ponytail: interpolation holes are parsed with a brace-counting heuristic
# (ternary colons at hole depth 0 switch to format text); upgrade path is a
# Roslyn-based generator if docs ever need pathological interpolations.

def _skip_string(code, i):
    m = re.match(r'(\$*)("{3,})', code[i:])
    if m:
        return _skip_raw(code, i + len(m.group(1)) + len(m.group(2)), len(m.group(2)), len(m.group(1)))
    tok = None
    for t in ('$@"', '@$"', '$"', '@"', '"', "'"):
        if code.startswith(t, i):
            tok = t
            break
    if tok is None:
        return i + 1
    i += len(tok)
    n = len(code)
    if tok == "'":
        while i < n:
            if code[i] == '\\':
                i += 2
                continue
            if code[i] == "'":
                return i + 1
            i += 1
        return n
    verbatim = '@' in tok
    interpolated = '$' in tok
    while i < n:
        c = code[i]
        if verbatim and c == '"' and code.startswith('"', i + 1):
            i += 2
            continue
        if not verbatim and c == '\\':
            i += 2
            continue
        if interpolated and c == '{':
            if code.startswith('{', i + 1):
                i += 2
                continue
            i = _skip_hole(code, i + 1)
            continue
        if interpolated and c == '}' and code.startswith('}', i + 1):
            i += 2
            continue
        if c == '"':
            return i + 1
        i += 1
    return n


def _skip_hole(code, i):
    n = len(code)
    depth = 0
    fmt = False
    while i < n:
        c = code[i]
        if fmt:
            if c == '}':
                return i + 1
            i += 1
            continue
        if code.startswith('//', i):
            j = code.find('\n', i)
            i = n if j < 0 else j
            continue
        if code.startswith('/*', i):
            j = code.find('*/', i + 2)
            i = n if j < 0 else j + 2
            continue
        if c in '"\'' or (c in '@$' and i + 1 < n and code[i + 1] in '"@$'):
            i = _skip_string(code, i)
            continue
        if c == '{':
            depth += 1
        elif c == '}':
            if depth == 0:
                return i + 1
            depth -= 1
        elif c == ':' and depth == 0:
            fmt = True
        i += 1
    return n


def _skip_raw(code, i, quote_count, dollar_count):
    n = len(code)
    close = '"' * quote_count
    hole = '{' * dollar_count if dollar_count else None
    while i < n:
        if code.startswith(close, i):
            return i + quote_count
        if hole and code.startswith(hole, i) and not code.startswith(hole + '{', i):
            i = _skip_raw_hole(code, i + dollar_count, dollar_count)
            continue
        i += 1
    return n


def _skip_raw_hole(code, i, braces):
    n = len(code)
    close = '}' * braces
    while i < n:
        if code.startswith(close, i):
            return i + braces
        c = code[i]
        if c in '"\'' or (c in '@$' and i + 1 < n and code[i + 1] in '"@$'):
            i = _skip_string(code, i)
            continue
        i += 1
    return n


def lex_csharp(code):
    """Per-char mask: 0 = code, 1 = string/char literal, 2 = comment."""
    n = len(code)
    mask = [0] * n
    i = 0
    while i < n:
        if code.startswith('//', i):
            j = code.find('\n', i)
            j = n if j < 0 else j
            for k in range(i, j):
                mask[k] = 2
            i = j
            continue
        if code.startswith('/*', i):
            j = code.find('*/', i + 2)
            j = n if j < 0 else j + 2
            for k in range(i, j):
                mask[k] = 2
            i = j
            continue
        if re.match(r'(\$*)"{3,}', code[i:]) or re.match(r'(\$?@"|@\$"|\$"|@"|"|\')', code[i:]):
            start = i
            i = _skip_string(code, i)
            for k in range(start, i):
                mask[k] = 1
            continue
        i += 1
    return mask


def line_offsets(code):
    offs = []
    pos = 0
    for line in code.split('\n'):
        offs.append((pos, line))
        pos += len(line) + 1
    return offs


def code_view(code, mask):
    return ''.join(ch if mask[k] == 0 else ' ' for k, ch in enumerate(code))


def sub_code(pattern, repl, code, mask):
    """re.sub over code regions only; matches touching literal/comment chars are skipped."""
    view = code_view(code, mask)
    out = []
    last = 0
    for m in re.finditer(pattern, view):
        if any(mask[k] != 0 for k in range(m.start(), m.end())):
            continue
        out.append(code[last:m.start()])
        out.append(repl(m))
        last = m.end()
    out.append(code[last:])
    return ''.join(out)


file_count = 0
snippet_count = 0

for doc_path in docs:
    file_slug = os.path.basename(doc_path).replace('.md', '').replace('-', '_')
    with open(doc_path, 'r', encoding='utf-8') as f:
        content = f.read()

    blocks = list(re.finditer(r'```csharp\s*([\s\S]*?)```', content))
    if not blocks:
        continue

    file_count += 1
    assembly_attributes = []
    generated_snippets = []

    for i, m in enumerate(blocks):
        snippet_count += 1
        raw_code = m.group(1).strip()
        line_num = content[:m.start()].count('\n') + 1

        lines = [l for l in raw_code.split('\n') if l.strip() and not l.strip().startswith('//')]
        if not lines:
            continue

        mask = lex_csharp(raw_code)
        offsets = line_offsets(raw_code)

        def line_mask(idx):
            start, line = offsets[idx]
            return mask[start:start + len(line)]

        # Check for // run marker in comment regions only
        is_live_run = any(
            re.match(r'^\s*//\s*run\b', ''.join(c for c, mk in zip(l, line_mask(k)) if mk == 2).strip())
            for k, (s, l) in enumerate(offsets)
        )

        # Extract genuine using directives and assembly attributes: only lines
        # whose non-whitespace chars are all code, never literal contents.
        usings_in_snippet = []
        clean_lines = []
        for k, (s, l) in enumerate(offsets):
            stripped = l.strip()
            lm = line_mask(k)
            pure_code = all(mk == 0 for ch, mk in zip(l, lm) if ch.strip())
            if pure_code and stripped.startswith('[assembly:'):
                assembly_attributes.append(stripped)
            elif pure_code and re.match(r'^using\s+(static\s+)?[A-Za-z0-9_.]+(\s*=\s*[A-Za-z0-9_.]+)?\s*;\s*$', stripped):
                usings_in_snippet.append(stripped)
            else:
                clean_lines.append(l)

        cleaned_code = '\n'.join(clean_lines).strip()
        mask = lex_csharp(cleaned_code)
        # Blank whitespace-only lines only when they contain no literal data.
        cleaned_code = '\n'.join(
            l if (l.strip() or any(mask[s + j] == 1 for j in range(len(l)))) else ''
            for s, l in line_offsets(cleaned_code)
        )
        if not is_live_run:
            def replace_bracket(match):
                inner = match.group(1)
                inner = re.sub(
                    r'\b(ClientScenario|ServerScenario|ClientServerScenario|AtlasScenario|ClientTheory|ServerTheory|AtlasTheory|Fact|Theory|InlineData|MemberData|ClassData|ClientSettingsMatrix|ServerSettingsMatrix|WorldConfig|ServerConfig|WorldConfigMatrix|ServerConfigMatrix|ModConfigMatrix)\b(\s*\([^)]*\))?',
                    'DocExample',
                    inner
                )
                return f'[{inner}]'

            cleaned_code = sub_code(r'\[([^\]]+)\]', replace_bracket, cleaned_code, lex_csharp(cleaned_code))
            cleaned_code = sub_code(r'\bModSmokeTest\b', lambda m: 'DocModSmokeTest', cleaned_code, lex_csharp(cleaned_code))
        mask = lex_csharp(cleaned_code)
        view_lines = code_view(cleaned_code, mask).split('\n')
        non_comment_lines = [l.strip() for l in view_lines]
        non_comment_lines = [l for l in non_comment_lines if l]
        if not non_comment_lines:
            continue

        ns = f"namespace Zaldaryon.Pharos.Docs.Generated.{file_slug}.Snippet_{i+1}"
        using_prefix = '\n'.join(usings_in_snippet)
        using_prefix = using_prefix.strip('\n')
        if 'migrating' in file_slug:
            using_prefix += '\nusing WorldOptions = global::Atlas.Api.WorldOptions;\nusing global::Atlas.Api;\nusing global::Atlas.XUnit;'

        is_interface_signatures = all(re.match(r'^(void|Task|Task<.*?>|Block|BlockEntity\??|Entity)\s+\w+\s*\(.*?\)\s*;\s*$', l) for l in non_comment_lines)
        is_class_declaration = re.match(r'^(public|internal|abstract|sealed|partial)?\s*class\b', non_comment_lines[0]) or (non_comment_lines[0].startswith('[') and any(re.match(r'^(public|internal|abstract|sealed|partial)?\s*class\b', l) for l in non_comment_lines))
        has_method = any(re.match(r'^(public|private|protected|internal)?\s*(async\s+)?(Task|void|Task<.*?>)\s+\w+\s*\(', l) for l in non_comment_lines)
        is_override = any(re.match(r'^(protected|public|private)\s+override\b', l) for l in non_comment_lines)
        is_attribute_only = non_comment_lines and non_comment_lines[0].startswith('[') and not any(';' in l or '{' in l for l in non_comment_lines)

        has_classes = any(re.match(r'^(public|internal)?\s*(\[ProtoContract\]\s*)?(public\s+)?class\b', l) for l in non_comment_lines)
        has_statements = any(';' in l and not re.match(r'^(public|protected|private|internal)?\s*(class|void|Task|override)', l) for l in non_comment_lines)

        run_trait = '        [Trait("Category", "Live")]' if is_live_run else ''

        if 'namespace ' in cleaned_code:
            snippet_body = f"""
namespace Zaldaryon.Pharos.Docs.Generated.{file_slug}.Snippet_{i+1}_Outer
{{
{using_prefix}
#line {line_num} "{doc_path}"
{cleaned_code}
#line default
}}
"""
        elif is_interface_signatures:
            snippet_body = f"""
{ns}
{{
{using_prefix}
    public interface IApiReference
    {{
#line {line_num} "{doc_path}"
{cleaned_code}
#line default
    }}
}}
"""
        elif is_attribute_only:
            snippet_body = f"""
{ns}
{{
{using_prefix}
#line {line_num} "{doc_path}"
{cleaned_code}
    public class ScenarioContainer : ClientServerScenarioBase {{ }}
#line default
}}
"""
        elif is_class_declaration:
            snippet_body = f"""
{ns}
{{
{using_prefix}
#line {line_num} "{doc_path}"
{cleaned_code}
#line default
}}
"""
        elif has_classes and has_statements and not has_method:
            class_lines = []
            stmt_lines = []
            offs = line_offsets(cleaned_code)
            emit_view = code_view(cleaned_code, mask).split('\n')
            for (start, l), vl in zip(offs, emit_view):
                entry = (start, l)
                if 'class ' in vl or 'ProtoMember' in vl or 'ProtoContract' in vl:
                    class_lines.append(entry)
                else:
                    stmt_lines.append(entry)
            # Strip boundary empty lines from stmt_lines
            while stmt_lines and not stmt_lines[0][1].strip():
                stmt_lines.pop(0)
            while stmt_lines and not stmt_lines[-1][1].strip():
                stmt_lines.pop()

            def render(lines, prefix):
                # Indent only when the line starts in code; a line starting inside
                # a multi-line literal must keep its exact characters.
                out = []
                for idx, l in lines:
                    first = len(l) - len(l.lstrip())
                    if not l.strip():
                        has_literal = any(mask[idx + j] == 1 for j in range(len(l)))
                        out.append(l if has_literal else '')
                    elif idx + first < len(mask) and mask[idx + first] == 0:
                        out.append(prefix + l)
                    else:
                        out.append(l)
                return chr(10).join(out)

            snippet_body = f"""
{ns}
{{
{using_prefix}
    public class ScenarioContainer : ClientServerScenarioBase
    {{
{container_members}
{render(class_lines, '        ')}

{run_trait}
        public async Task ExecuteSnippetAsync()
        {{
#line {line_num} "{doc_path}"
{render(stmt_lines, '            ')}
#line default
        }}
    }}
}}
"""
        elif has_method or is_override:
            snippet_body = f"""
{ns}
{{
{using_prefix}
    public class ScenarioContainer : ClientServerScenarioBase
    {{
{container_members}
#line {line_num} "{doc_path}"
{cleaned_code}
#line default
    }}
}}
"""
        else:
            # Statements
            base_spec = "" if 'migrating' in file_slug else " : ClientServerScenarioBase"
            members = "" if 'migrating' in file_slug else container_members
            snippet_body = f"""
{ns}
{{
{using_prefix}
    public class ScenarioContainer{base_spec}
    {{
{members}
{run_trait}
        public async Task ExecuteSnippetAsync()
        {{
#line {line_num} "{doc_path}"
{cleaned_code}
#line default
        }}
    }}
}}
"""
        generated_snippets.append(snippet_body)

    generated_code = [
        f"// Generated from {doc_path}",
        "#nullable enable",
        "#pragma warning disable CS0105, CS0168, CS0219, CS8600, CS8602, CS8604, CS8618, CS8669, CS0414, CS1998, CS0067, CS0649, CS0169, CS0618, CS4014, xUnit2029, xUnit1008",
        common_usings,
        '\n'.join(assembly_attributes),
    ] + generated_snippets

    out_file = os.path.join(output_dir, f"{file_slug}.g.cs")
    with open(out_file, 'w', encoding='utf-8') as f:
        f.write('\n'.join(generated_code))

print(f"Generated {file_count} files for {snippet_count} snippets.")
