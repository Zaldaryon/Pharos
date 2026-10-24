#!/usr/bin/env bash
# Generates the Pharos templates against tests/TemplateSample/ExampleMod and writes the result next
# to it: ExampleMod.Tests (xunit2, --client), ExampleMod.Tests.V3 (xunit3, --client) and
# .github/workflows/pharos.yml. CI runs those samples, so they stay what the templates write.
#
# Usage: scripts/update-template-samples.sh [--check]
#   --check  Write nothing; fail when the samples differ from what the templates generate.
set -euo pipefail

root=$(cd "$(dirname "$0")/.." && pwd)
samples="$root/tests/TemplateSample"
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

export DOTNET_CLI_UI_LANGUAGE=en-US DOTNET_NOLOGO=true DOTNET_CLI_TELEMETRY_OPTOUT=true

dotnet pack "$root/src/Zaldaryon.Pharos.Templates/Zaldaryon.Pharos.Templates.csproj" -c Release \
  -o "$work/nupkg" -p:Version=0.0.0-samples > "$work/pack.log" || { cat "$work/pack.log"; exit 1; }
hive=(--debug:custom-hive "$work/hive")
dotnet new install "$work/nupkg/Zaldaryon.Pharos.Templates.0.0.0-samples.nupkg" "${hive[@]}" > /dev/null

out="$work/out"
mkdir -p "$out"
cd "$out"
dotnet new pharos-mod-tests -n ExampleMod.Tests --mod-project ../ExampleMod/ExampleMod.csproj --client "${hive[@]}" > /dev/null
dotnet new pharos-mod-tests -n ExampleMod.Tests.V3 --mod-project ../ExampleMod/ExampleMod.csproj --framework xunit3 --client "${hive[@]}" > /dev/null
dotnet new pharos-ci --test-project tests/TemplateSample/ExampleMod.Tests/ExampleMod.Tests.csproj "${hive[@]}" > /dev/null

# Check escaping through the actual template engine, not just the manifest's declared transforms.
for framework in xunit2 xunit3; do
  dotnet new pharos-mod-tests -n "Escaped.$framework" --framework "$framework" \
    --mod-project '../R&D Tools/ExampleMod.csproj' "${hive[@]}" > /dev/null
  actual=$(dotnet msbuild "Escaped.$framework/Escaped.$framework.csproj" -getProperty:PharosModProject)
  [ "$actual" = '../R&D Tools/ExampleMod.csproj' ] || { echo "Path did not round-trip: $actual"; exit 1; }
done
dotnet new pharos-ci -o escaped-ci --test-project "it's here/Tests.csproj" "${hive[@]}" > /dev/null
grep -Fq "test-project: 'it''s here/Tests.csproj'" escaped-ci/.github/workflows/pharos.yml

# Exercise the generated JSON helper without booting the game or pulling test packages.
mkdir "$work/metadata"
cp "$out/ExampleMod.Tests/ModUnderTest.cs" "$work/metadata/"
cat > "$work/metadata/Check.csproj" <<'XML'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
</Project>
XML
cat > "$work/metadata/Program.cs" <<'CS'
using System.Text;

string manifest = System.IO.Path.Combine(ModUnderTest.Path, "modinfo.json");
Directory.CreateDirectory(ModUnderTest.Path);
foreach (string json in new[] {
    "{\"metadata\":{\"modid\":\"wrong\"},\"ModID\":\"correct\"}",
    "{/* \"modid\":\"wrong\" */\"MODID\":\"correct\",}",
    "{\"modid\":\"wrong\",\"ModId\":\"correct\"}" })
{
    File.WriteAllText(manifest, json, new UTF8Encoding(true));
    if (ModUnderTest.Id != "correct") throw new Exception("Wrong top-level mod ID.");
}
foreach (string json in new[] { "{}", "{\"metadata\":{\"modid\":\"wrong\"}}", "{\"modid\":\"\"}" })
{
    File.WriteAllText(manifest, json);
    ExpectMissing();
}
File.Delete(manifest);
ExpectMissing();
Console.WriteLine("Template metadata and escaped paths passed.");

static void ExpectMissing()
{
    try { _ = ModUnderTest.Id; }
    catch (InvalidOperationException ex) when (ex.Message.Contains("set PharosModId")) { return; }
    throw new Exception("Missing top-level modid was accepted.");
}
internal static partial class ModUnderTest
{
    public static readonly string Path = System.IO.Path.Combine(AppContext.BaseDirectory, "mod");
    private const string IdOverride = "";
}
CS
dotnet run --project "$work/metadata/Check.csproj" -c Release

generated=(ExampleMod.Tests ExampleMod.Tests.V3 .github)
if [ "${1:-}" = "--check" ]; then
  status=0
  for dir in "${generated[@]}"; do
    diff -r --exclude=bin --exclude=obj "$out/$dir" "$samples/$dir" || status=1
  done
  if [ "$status" != 0 ]; then
    echo "::error::The template samples differ from what the templates generate. Run scripts/update-template-samples.sh."
  fi
  exit "$status"
fi

for dir in "${generated[@]}"; do
  rm -rf "${samples:?}/$dir"
  cp -r "$out/$dir" "$samples/$dir"
done
echo "Wrote ${generated[*]} to $samples"
