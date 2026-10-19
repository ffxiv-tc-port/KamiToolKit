using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace KamiToolKit.Classes;
#if DEBUG
public unsafe class Experimental {
#else
internal unsafe class Experimental {
#endif

    private static Experimental? instance;
    public static Experimental Instance => instance ??= new Experimental();

    public void EnableHooks() { }

    public void DisposeHooks() { }

    // 明寫 Fallible：nint 欄位預設是 Infallible，對不上時 NativeController 建構子會擲，消費端插件整個載不起來。
    // 留 0 時唯一的讀取點（NodeBase.IsNodeValid 的比對）恆不相等，等於略過那一項判斷。
    [Signature("4C 8D 3D ?? ?? ?? ?? 4C 89 3F 41 F6 C4", Fallibility = Fallibility.Fallible)]
    public nint AtkEventListenerVirtualTable = nint.Zero;

    /// <summary>
    ///     取得 AtkStage 的檢視區事件管理器「指標」，AtkStage 尚未建好時回傳 null。
    ///     🔴 這裡必須回傳指標而不是傳值：AtkEventManager 是 0x8 的值型別，
    ///     以值回傳會把結構複製到呼叫端堆疊，接著呼叫 RegisterEvent/UnregisterEvent
    ///     （皆為 [MemberFunction]）就等於把「堆疊複本的位址」當 this 丟進原生端——
    ///     註冊寫進的是那份複本，靜默無效，且原生端可能留下指向已失效堆疊槽的指標。
    ///     ⚠️ 呼叫端必須自己判 null，否則就是裸解參考。
    /// </summary>
    public AtkEventManager* ViewportEventManager {
        get {
            // AtkStage.Instance() 是 [StaticAddress(..., isPointer: true)]：只有「特徵碼沒解析到」才擲例外；
            // 解析到但 AtkStage 尚未建好時回傳的是存在該位址的 null 指標。
            // 對 null 指標取欄位位址雖然不會當場崩，但那是無效位址，原生端一用就是
            // try/catch 攔不到的 AccessViolationException，所以在這裡就擋掉。
            var stage = AtkStage.Instance();
            if (stage is null) return null;

            // ViewportEventManager 是內嵌欄位而非二次指標，取址即真實位址。
            return &stage->ViewportEventManager;
        }
    }

    /// <summary>
    ///     AtkStage 是否已就緒，亦即 <see cref="ViewportEventManager" /> 拿得到非 null 指標。
    /// </summary>
    public bool ViewportEventManagerAvailable => AtkStage.Instance() is not null;

    public delegate void AtkComponentNumericInputSetValueDelegate(AtkComponentNumericInput* thisPtr, int value, bool triggerCallback, bool playSoundEffect);

    [Signature("E8 ?? ?? ?? ?? 40 80 FE ?? 74")]
    public AtkComponentNumericInputSetValueDelegate? AtkComponentNumericInputSetValueCallback = null;

#if DEBUG
    // WARNING: May result in undefined state or accidental network requests
    // Use at your own risk.
    public static void ForceOpenAddon(AgentId agentId)
        => DalamudInterface.Instance.Framework.RunOnFrameworkThread(() => {
            var agentModule = AgentModule.Instance();
            if (agentModule is null) return;

            var agent = agentModule->GetAgentByInternalId(agentId);
            if (agent is null) return;

            agent->Show();
        });

    // WARNING: May result in undefined state or accidental network requests
    // Use at your own risk.
    public static void ForceCloseAddon(AgentId agentId)
        => DalamudInterface.Instance.Framework.RunOnFrameworkThread(() => {
            var agentModule = AgentModule.Instance();
            if (agentModule is null) return;

            var agent = agentModule->GetAgentByInternalId(agentId);
            if (agent is null) return;

            agent->Hide();
        });
#endif
}
