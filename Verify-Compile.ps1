param([switch]$CheckSettings)
$ErrorActionPreference = 'Stop'
$game = 'E:\SteamLibrary\steamapps\common\worldbox'
$managed = Join-Path $game 'worldbox_Data\Managed'
$nml = Join-Path $game 'worldbox_Data\StreamingAssets\mods\NML'
$assemblies = Join-Path $nml 'Assemblies'
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Reflection;
public static class SakugaAssemblyResolver {
    public static string Folder;
    public static string Managed;
    public static void Register() { AppDomain.CurrentDomain.AssemblyResolve += Resolve; }
    private static Assembly Resolve(object sender, ResolveEventArgs e) {
        string path = Path.Combine(Folder, new AssemblyName(e.Name).Name + ".dll");
        if (!File.Exists(path)) path = Path.Combine(Managed, new AssemblyName(e.Name).Name + ".dll");
        return File.Exists(path) ? Assembly.LoadFrom(path) : null;
    }
}
'@
[SakugaAssemblyResolver]::Folder = $assemblies
[SakugaAssemblyResolver]::Managed = $managed
[SakugaAssemblyResolver]::Register()
foreach ($name in @('System.Collections.Immutable','System.Reflection.Metadata','System.Memory','System.Runtime.CompilerServices.Unsafe','Microsoft.CodeAnalysis','Microsoft.CodeAnalysis.CSharp')) {
    [System.Reflection.Assembly]::LoadFrom((Join-Path $assemblies ($name + '.dll'))) | Out-Null
}
$trees = New-Object 'System.Collections.Generic.List[Microsoft.CodeAnalysis.SyntaxTree]'
foreach ($file in Get-ChildItem (Join-Path $PSScriptRoot 'Code') -Recurse -Filter *.cs) {
    $trees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($file.FullName), [Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::Default, $file.FullName, [Text.Encoding]::UTF8, [Threading.CancellationToken]::None))
}
$refs = New-Object 'System.Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]'
$paths = @(Get-ChildItem $managed -Filter *.dll | Where-Object Name -ne 'Assembly-CSharp.dll' | Select-Object -ExpandProperty FullName)
$paths += (Join-Path $nml 'Assembly-CSharp-Publicized.dll')
$paths += (Join-Path $game 'worldbox_Data\StreamingAssets\mods\NeoModLoader.dll')
$paths += (Join-Path $assemblies '0Harmony.dll')
foreach ($path in $paths) {
    $refs.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($path, [Microsoft.CodeAnalysis.MetadataReferenceProperties]::Assembly, $null))
}
$options = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary)
$compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('Sakuga_Verify', $trees, $refs, $options)
$errors = @($compilation.GetDiagnostics() | Where-Object Severity -eq 'Error')
$errors | ForEach-Object { $_.ToString() }
if ($errors.Count -gt 0) { throw ('Compilation failed: ' + $errors.Count + ' errors') }
'Compilation passed against installed WorldBox/NML assemblies.'
if ($CheckSettings) {
     $modRoot=$PSScriptRoot
    $rules=[IO.File]::ReadAllText((Join-Path $modRoot 'Code/TribulationRules.cs'))
    $checks=@'
namespace Sakuga {
public static class TribulationRuleChecks {
    private static void Check(bool ok, string reason) { if(!ok)throw new System.Exception(reason); }
    public static void Run() {
        Check(!TribulationRules.NearDeath(5,1000), "Exactly 0.5 percent must not count");
        Check(TribulationRules.NearDeath(4,1000), "Living below threshold must count");
        Check(!TribulationRules.NearDeath(0,1000), "Lethal damage must not count");
        Check(!TribulationRules.NearDeath(-1,1000), "Negative health must not count");
        Check(!TribulationRules.NearDeath(1,0), "Invalid maximum health");
        Check(!TribulationRules.Survived(true,true,1,29.99f), "Proof granted too early");
        Check(TribulationRules.Survived(true,true,1,30), "Thirty seconds must qualify");
        Check(!TribulationRules.Survived(false,true,1,30), "Dead actor got proof");
        Check(!TribulationRules.Survived(true,false,1,30), "Transformation got proof");
        Check(!TribulationRules.Survived(true,true,0,30), "Pending death got proof");
        Check(TribulationRules.Qualified(50000,50000,50,50,10,10,true,true), "Exact gates rejected");
        Check(!TribulationRules.Qualified(49999,50000,50,50,10,10,true,true), "Insufficient XP");
        Check(!TribulationRules.Qualified(50000,50000,49,50,10,10,true,true), "Insufficient years");
        Check(!TribulationRules.Qualified(50000,50000,50,50,9,10,true,true), "Insufficient unique kills");
        Check(!TribulationRules.Qualified(50000,50000,50,50,10,10,false,true), "Missing near-death proof");
        Check(TribulationRules.Qualified(50000,50000,50,50,10,10,false,false), "Disabled proof still required");
        float previous=0,total=0;
        for(int i=1;i<=9;i++) {
            float damage=TribulationRules.Damage(i,4000,1);
            Check(damage>previous, "Strikes must strengthen"); previous=damage;total+=damage;
        }
        Check(System.Math.Abs(total-5480)<.1f, "Unexpected total damage at standard difficulty");
        Check(TribulationRules.Damage(9,1000000,1)==TribulationRules.Damage(9,8000,1), "HP scaling must have ceiling");
        Check(TribulationRules.Damage(1,1,1)==TribulationRules.Damage(1,3500,1), "HP scaling must have floor");
        Check(TribulationRules.Damage(0,4000,1)==0 && TribulationRules.Damage(10,4000,1)==0, "Extra strike damage");
        Check(TribulationRules.Damage(1,float.NaN,1)==0, "Nonfinite HP accepted");
        Check(!TribulationRules.Successful(8,true,1), "Early ascension");
        Check(!TribulationRules.Successful(9,true,0), "Lethal ninth strike granted ascension");
        Check(!TribulationRules.Successful(9,false,1), "Dead actor ascended");
        Check(TribulationRules.Successful(9,true,1), "Nine surviving strikes must ascend");
    }
}}
'@
    Add-Type -TypeDefinition ($rules + [Environment]::NewLine + $checks)
    [Sakuga.TribulationRuleChecks]::Run()
    [Reflection.Assembly]::LoadFrom((Join-Path $assemblies 'Mono.Cecil.dll')) | Out-Null
    $native=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $managed 'Assembly-CSharp.dll'))
    try {
        $actorType=$native.MainModule.Types | Where-Object Name -eq 'Actor'
        foreach($methodName in @('getHit','die','updateMovement','tryToAttack','u4_deadCheck')){
            if(@($actorType.Methods | Where-Object Name -eq $methodName).Count -ne 1){throw "Ambiguous or missing native patch target: $methodName"}
        }
        $fxType=$native.MainModule.Types | Where-Object Name -eq 'EffectsLibrary'
        $fxInit=$fxType.Methods | Where-Object Name -eq 'init'
        $fxIds=@($fxInit.Body.Instructions | Where-Object {$_.OpCode.Name -eq 'ldstr'} | ForEach-Object Operand)
        foreach($fxId in @('fx_lightning_small','fx_lightning_medium','fx_lightning_big')){
            if($fxIds -notcontains $fxId){throw "Missing native lightning effect: $fxId"}
        }
    } finally {$native.Dispose()}
    $config=Get-Content -LiteralPath (Join-Path $modRoot 'default_config.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $ids=@($config.sakuga_progress | ForEach-Object Id)
    if($ids -contains 'sakuga_true_god_bosses'){throw 'Obsolete boss condition remains'}
    foreach($id in @('sakuga_true_god_kills','sakuga_true_god_brink','sakuga_tribulation_power','sakuga_tribulation_effects')){
        if(@($ids | Where-Object {$_ -eq $id}).Count -ne 1){throw "Missing or repeated setting: $id"}
        foreach($lang in @('en','cz')){
            $locale=Get-Content -LiteralPath (Join-Path $modRoot "Locales/$lang.json") -Raw -Encoding UTF8 | ConvertFrom-Json
            if(!$locale.$id -or !$locale.($id+' Description')){throw "Missing translation: $lang/$id"}
        }
    }
    'Tribulation checks passed: strict near-death threshold, 30-second survival, all four gates, nine increasing strikes, bounded scaling, lethal finale, native patch targets and lightning IDs, menu and translations.'
}
