"""Diagnostics tripwires with narrow native-read and explicitly consented callback-observer boundaries."""
from pathlib import Path
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
errors = []
sources = [p for folder in ["DebugKit", "DebugKit.Core"] for p in (ROOT / folder).rglob("*.cs")
           if not any(part in {"bin", "obj"} for part in p.relative_to(ROOT).parts)]
for path in sources:
    text = path.read_text(encoding="utf-8")
    # Token checks intentionally conservative: new capabilities require updating the safety review.
    forbidden = [
        r"\bDllImport\b", r"\bLibraryImport\b",
        r"\bGetIpcProvider\b", r"\bFireCallback\w*\b", r"\bUseAction\w*\b",
        r"\bProcessCommand\b", r"\bSigScanner\b", r"\bGetObjectAddress\b", r"\bThread\.Sleep\b",
        r"\bGetProperties\s*\(", r"\bGetFields\s*\(", r"\bGetPluginInstance\b",
        r"\bGetRawSheet\s*\(", r"\b(?:VerifySheetChecksums|PanicOnSheetChecksumMismatch)\s*=\s*false\b",
    ]
    # Strip comments so explanatory safety documentation doesn't trip token checks.
    code = re.sub(r"//[^\n]*|/\*.*?\*/", "", text, flags=re.S)
    callback = path.relative_to(ROOT).as_posix() == "DebugKit/Readers/Native/AddonCallbackObserver.cs"
    # Only references to the reviewed generated signature/address are allowed, never direct callback dispatch.
    checked = code.replace("AtkUnitBase.Delegates.FireCallback", "ReviewedCallbackDelegate").replace("AtkUnitBase.MemberFunctionPointers.FireCallback", "ReviewedCallbackAddress") if callback else code
    if re.search(r"\bHookFrom\w+|\bHook\s*<", code) and not callback:
        errors.append(f"{path.relative_to(ROOT)}: hook outside explicit callback observer")
    if re.search(r"\bHttpClient\b|\bHttpRequestMessage\b|\bHttpClientHandler\b|\bWebClient\b|\bWebRequest\b", code) and path.relative_to(ROOT).as_posix() != "DebugKit.Core/NetworkCaptureClient.cs":
        errors.append(f"{path.relative_to(ROOT)}: HTTP outside reviewed fixed-host boundary")
    if re.search(r"\bGetIpcSubscriber\b|\bInvokeFunc\b", code) and path.relative_to(ROOT).as_posix() != "DebugKit/Readers/ProviderReader.cs":
        errors.append(f"{path.relative_to(ROOT)}: IPC outside reviewed provider boundary")
    if re.search(r"\bInvokeAction\b|\bSendMessage\b|\bSubscribe\b", code):
        errors.append(f"{path.relative_to(ROOT)}: IPC control/subscription forbidden")
    for pattern in forbidden:
        if re.search(pattern, checked):
            errors.append(f"{path.relative_to(ROOT)}: forbidden token {pattern}")
    native = path.relative_to(ROOT).as_posix() == "DebugKit/Readers/Native/NativeReader.cs"
    if not (native or callback) and re.search(r"\bunsafe\b|\.(?:Address)\b", code):
        errors.append(f"{path.relative_to(ROOT)}: native access outside reviewed reader")
    if re.search(r"\.(?:GameObjectId|EntityId|OwnerId|CharacterName|TargetObjectId|CastTargetObjectId|CrafterContentId|GetFateAddress|CreateFateReference)\b", code):
        errors.append(f"{path.relative_to(ROOT)}: identity/address property read")
    if native:
        if re.search(r"->\w+\s*=(?!=)|->\w+\[[^\]]+\]\s*=", code):
            errors.append("Native reader writes game memory")
        if re.search(r"\b(?:nint|nuint|IntPtr|UIntPtr)\b|\w+\s*\*", code.partition("public NativeReader(")[0]):
            errors.append("Native reader retains pointer state in fields")
        gate = code.partition("public ToolStep Capture(")[2].partition("var stamp")[0]
        if "!compatibility.Supported" not in gate or "!options.ExperimentalNative" not in gate:
            errors.append("Native reader missing compatibility or explicit-opt-in gate")
    if callback:
        if re.search(r"->\w+\s*=(?!=)|->\w+\[[^\]]+\]\s*=", code):
            errors.append("Callback observer writes native fields")
        fields = code.partition("public Task<ToolStep> Start(")[0]
        if re.search(r"\b(?:nint|nuint|IntPtr|UIntPtr)\b|\w+\s*\*", fields):
            errors.append("Callback observer retains raw pointers")
        gate = code.partition("public Task<ToolStep> Start(")[2].partition("var address")[0]
        for required in ("!compatibility.Supported", "!options.ExperimentalNative"):
            if required not in gate:
                errors.append(f"Callback observer missing gate: {required}")
        if code.count("hook!.Original(addon, count, values, flag)") != 1 or code.count("HookFromAddress<") != 1 or "return returned;" not in code:
            errors.append("Callback observer must have one fixed hook and one unchanged original invocation")
        if re.search(r"values\[i\]\.(?:Float|String|WideString|Pointer|Vector|AtkValues)\b|Marshal\.|NameString", code):
            errors.append("Callback observer reads excluded values or unbounded strings")
        if "RequestCallbackStop" not in code or "StopRequested" not in code:
            errors.append("Callback observer must stop only on explicit stop/cancellation")
        if "capture.Attempts < CallbackBuffer.MaximumEvents" in code or "WindowMilliseconds" in code:
            errors.append("Callback observer must not time out or stop at buffer capacity")
        if "capture.Buffer.Dropped" not in code or "CallbackBuffer Buffer = new();" not in code:
            errors.append("Callback observer must retain a rolling bounded buffer and report drops")
        for member in ("Int", "Int64", "UInt", "UInt64"):
            assignment = "signed" if member.startswith("Int") else "unsigned"
            if f"type == AtkValueType.{member}) {assignment} = values[i].{member};" not in code:
                errors.append(f"Callback integer tag/union member mismatch: {member}")
    if ".ContentId" in code and path.name != "DalamudGameSource.cs":
        errors.append(f"{path.relative_to(ROOT)}: Content ID outside lifetime read boundary")

window = (ROOT / "DebugKit/MainWindow.cs").read_text()
if re.search(r"\b(?:ReadContext|ReadObject|ObjectTable|LocalPlayer|IClientState|IPlayerState|ICondition)\b", window):
    errors.append("UI contains live game read symbols")
engine = (ROOT / "DebugKit.Core/DiagnosticEngine.cs").read_text()
if "StopCallbackCapture()" not in engine or "RequestCallbackStop()" not in engine:
    errors.append("Engine must route explicit callback stop without discarding result")
if 'ImGui.Button("Start capture")' not in window or 'ImGui.Button("Stop capture")' not in window:
    errors.append("Callback UI must expose manual start and stop controls")
if "ImGui.Checkbox" in window.partition("if (selected == Inspector.AddonCallbacks)")[2].partition("if (NetworkPolicy.IsNetwork(selected))")[0]:
    errors.append("Callback UI must not require per-capture consent checkboxes")
if "CallbackText.NumericWarning" not in window or "CallbackText.NumericWarning" not in (ROOT / "DebugKit.Core/Reports.cs").read_text():
    errors.append("Numeric callback capture must warn in UI and reports")

# Structural regression guard: left pane allows tool search/navigation, not tool controls.
navigation = window.partition("private void DrawNavigation()")[2].partition("private void DrawTool(")[0]
if not navigation or "ImGui.Selectable" not in navigation:
    errors.append("Missing navigation-only tool selector")
navigation_controls = navigation.replace('ImGui.InputTextWithHint("##find-tool", "Find tool", ref search, 80);', '')
navigation_controls = navigation_controls.replace('ImGui.CollapsingHeader("Debug Kit details")', '')
if re.search(r"ImGui\.(?:Input\w*|Button|SmallButton|Combo|Checkbox|CollapsingHeader)\s*\(|engine\.", navigation_controls):
    errors.append("Navigation contains non-navigation controls or diagnostic operations")
if 'ImGui.InputTextWithHint("##find-tool", "Find tool", ref search, 80);' not in navigation:
    errors.append("Tool search must remain in navigation")
if 'BeginChild("navigation-details-footer"' not in navigation or 'ImGui.CollapsingHeader("Debug Kit details")' not in navigation:
    errors.append("Plugin details must remain in the navigation footer")
tool = window.partition("private void DrawTool(")[2].partition("private void DrawInputs(")[0]
if '"Debug Kit details"' in tool:
    errors.append("Plugin details must not be repeated inside tools")
inputs = window.partition("private void DrawInputs(")[2].partition("private bool TryOptions(")[0]
if '"Capture both safe checks"' in window or "engine.Enqueue(" in inputs:
    errors.append("Tools must not expose multi-tool batch captures")
if "Queue(selected, options)" not in inputs or inputs.count("ImGui.SameLine();") < 2:
    errors.append("Selected-tool actions must use a single toolbar row")
if 'BeginChild("navigation"' not in window or 'BeginChild("selected-tool"' not in window:
    errors.append("Missing independent navigation/tool panes")

source = (ROOT / "DebugKit/DalamudGameSource.cs").read_text()
if re.search(r"\.(?:HomeWorld|CurrentWorld)\b", source):
    errors.append("Game source reads world-character identity")
if source.count("obj.Name.ToString()") != 1 or "ObjectNames.For(kind, obj.Name.ToString())" not in source:
    errors.append("Display names must enter through the bounded ObjectNames policy only")

world = (ROOT / "DebugKit/Readers/WorldReader.cs").read_text()
if "GetFile<LgbFile>" in world or "StaticLayoutParser.Parse(raw.Data)" not in world:
    errors.append("Live world inspection must use the bounded detached static parser, not the full LGB allocator")

network = (ROOT / "DebugKit.Core/NetworkCaptureClient.cs").read_text()
for required in ("AllowAutoRedirect = false", "UseCookies = false", "UseDefaultCredentials = false", "UseProxy = false", "HttpMethod.Get", "NetworkPolicy.RequestUri", "MaximumResponseBytes", "ResponseHeadersRead"):
    if required not in network:
        errors.append(f"Network boundary missing safeguard: {required}")
if re.search(r"HttpMethod\.(?:Post|Put|Delete|Patch)|DefaultRequestHeaders\.Authorization", network):
    errors.append("Network boundary contains upload/control/auth")
provider = (ROOT / "DebugKit/Readers/ProviderReader.cs").read_text()
if provider.count("GetIpcSubscriber<bool>(contract.Endpoint)") != 1 or "ProviderQueries.Capture" not in provider:
    errors.append("Provider IPC must use one typed boolean registry boundary")

for project in [ROOT / "DebugKit/DebugKit.csproj", ROOT / "DebugKit.Core/DebugKit.Core.csproj"]:
    tree = ET.parse(project)
    if tree.findall(".//PackageReference"):
        errors.append(f"{project.relative_to(ROOT)}: unexpected direct package dependency")
    for item in tree.findall(".//ProjectReference"):
        if (project.parent / item.attrib["Include"]).resolve() != (ROOT / "DebugKit.Core/DebugKit.Core.csproj").resolve():
            errors.append(f"{project.relative_to(ROOT)}: unexpected project dependency")

if errors:
    raise SystemExit("\n".join(errors))
print(f"PASS: source tripwires ({len(sources)} C# files), gated native boundary; no runtime safety guarantee.")
