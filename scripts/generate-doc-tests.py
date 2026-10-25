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

        # Check for // run marker
        is_live_run = bool(re.search(r'^\s*//\s*run\b', raw_code, re.MULTILINE))

        # Extract genuine using directives and assembly attributes
        usings_in_snippet = []
        clean_lines = []
        for l in raw_code.split('\n'):
            stripped = l.strip()
            if stripped.startswith('[assembly:'):
                assembly_attributes.append(stripped)
            elif re.match(r'^using\s+(static\s+)?[A-Za-z0-9_.]+(\s*=\s*[A-Za-z0-9_.]+)?\s*;\s*$', stripped):
                usings_in_snippet.append(stripped)
            else:
                clean_lines.append(l)

        cleaned_code = '\n'.join(clean_lines).strip()
        cleaned_code = '\n'.join(l if l.strip() else '' for l in cleaned_code.split('\n'))
        if not is_live_run:
            def replace_bracket(match):
                inner = match.group(1)
                inner = re.sub(
                    r'\b(ClientScenario|ServerScenario|ClientServerScenario|AtlasScenario|ClientTheory|ServerTheory|AtlasTheory|Fact|Theory|InlineData|MemberData|ClassData|ClientSettingsMatrix|ServerSettingsMatrix|WorldConfig|ServerConfig|WorldConfigMatrix|ServerConfigMatrix|ModConfigMatrix)\b(\s*\([^)]*\))?',
                    'DocExample',
                    inner
                )
                return f'[{inner}]'

            cleaned_code = re.sub(r'\[([^\]]+)\]', replace_bracket, cleaned_code)
            cleaned_code = re.sub(r'\bModSmokeTest\b', 'DocModSmokeTest', cleaned_code)
        non_comment_lines = [re.sub(r'//.*$', '', l).strip() for l in cleaned_code.split('\n')]
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
            for l in cleaned_code.split('\n'):
                if 'class ' in l or 'ProtoMember' in l or 'ProtoContract' in l:
                    class_lines.append(l)
                else:
                    stmt_lines.append(l)
            # Strip boundary empty lines from stmt_lines
            while stmt_lines and not stmt_lines[0].strip():
                stmt_lines.pop(0)
            while stmt_lines and not stmt_lines[-1].strip():
                stmt_lines.pop()

            snippet_body = f"""
{ns}
{{
{using_prefix}
    public class ScenarioContainer : ClientServerScenarioBase
    {{
{container_members}
{chr(10).join('        ' + l if l.strip() else '' for l in class_lines)}

{run_trait}
        public async Task ExecuteSnippetAsync()
        {{
#line {line_num} "{doc_path}"
{chr(10).join('            ' + l if l.strip() else '' for l in stmt_lines)}
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
