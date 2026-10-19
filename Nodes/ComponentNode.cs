using FFXIVClientStructs.FFXIV.Client.System.Memory;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Classes;
using KamiToolKit.System;

namespace KamiToolKit.Nodes;

public abstract unsafe class ComponentNode(NodeType nodeType) : NodeBase<AtkComponentNode>(nodeType) {
    public abstract AtkComponentBase* ComponentBase { get; }
    public abstract AtkUldComponentDataBase* DataBase { get; }
    public abstract AtkComponentNode* InternalComponentNode { get; }
}

public abstract unsafe class ComponentNode<T, TU> : ComponentNode where T : unmanaged, ICreatable where TU : unmanaged {

    public readonly CollisionNode CollisionNode;

    protected ComponentNode() : base((NodeType)1001) {
        Component = NativeMemoryHelper.Create<T>();
        var componentBase = (AtkComponentBase*)Component;

        Data = NativeMemoryHelper.UiAlloc<TU>();

        componentBase->Initialize();

        CollisionNode = new CollisionNode {
            NodeId = 1, LinkedComponent = componentBase, NodeFlags = NodeFlags.Visible | NodeFlags.Enabled | NodeFlags.HasCollision | NodeFlags.RespondToMouse | NodeFlags.Focusable | NodeFlags.EmitsEvents,
        };

        CollisionNode.InternalResNode->ParentNode = InternalResNode;

        componentBase->OwnerNode = InternalNode;
        componentBase->AtkResNode = CollisionNode.InternalResNode;
        componentBase->ComponentFlags = 1;

        ref var uldManager = ref componentBase->UldManager;

        uldManager.Objects = (AtkUldObjectInfo*)NativeMemoryHelper.UiAlloc<AtkUldComponentInfo>();
        ref var objects = ref uldManager.Objects;
        uldManager.ObjectCount = 1;

        objects->NodeList = (AtkResNode**)NativeMemoryHelper.Malloc(8);
        objects->NodeList[0] = CollisionNode.InternalResNode;
        objects->NodeCount = 1;
        objects->Id = 1001;

        uldManager.InitializeResourceRendererManager();
        uldManager.RootNode = CollisionNode.InternalResNode;

        uldManager.UpdateDrawNodeList();
        uldManager.ResourceFlags = AtkUldManagerResourceFlag.Initialized | AtkUldManagerResourceFlag.ArraysAllocated;
        uldManager.LoadedState = AtkLoadState.Loaded;
    }

    public override AtkComponentBase* ComponentBase => (AtkComponentBase*)Component;
    public override AtkUldComponentDataBase* DataBase => (AtkUldComponentDataBase*)Data;
    public override AtkComponentNode* InternalComponentNode => (AtkComponentNode*)InternalResNode;

    /// <summary>
    ///     取得／設定元件指標。
    ///     ⚠️ NodeBase&lt;T&gt;.Dispose 會把 InternalNode 設為 null，此後再讀寫這個屬性
    ///     不可以裸解參考：節點已釋放時 getter 回傳 null、
    ///     setter 靜默略過寫入（與下面 Data 的 setter 同一套語意）。
    /// </summary>
    internal T* Component {
        get {
            var node = InternalNode;
            if (node is null) return null;

            return (T*)node->Component;
        }
        set {
            // 目前唯一的呼叫點是本類別的建構式（此時 InternalNode 必定有效），
            // 所以這層判空在現況下不會被觸發；它讓「節點已釋放後再寫入」
            // 這條路徑結構上不可能崩，而不是靠呼叫點的時序保證。
            var node = InternalNode;
            if (node is null) return;

            node->Component = (AtkComponentBase*)value;
        }
    }

    /// <summary>
    ///     取得／設定元件資料指標。
    ///     ⚠️ 這裡有兩層要判空，缺一層都會裸解參考：
    ///     ① NodeBase&lt;T&gt;.Dispose 會把 InternalNode 設為 null；
    ///     ② 節點還在但元件尚未建立／已被 Dtor 掉時 InternalNode-&gt;Component 為 null。
    ///     任一層取不到就回傳 null（getter）或靜默略過寫入（setter）。
    /// </summary>
    internal TU* Data {
        get {
            var node = InternalNode;
            if (node is null) return null;

            var component = node->Component;
            if (component is null) return null;

            return (TU*)component->UldManager.ComponentData;
        }
        set {
            var node = InternalNode;
            if (node is null) return;

            var component = node->Component;
            if (component is null) return;

            component->UldManager.ComponentData = (AtkUldComponentDataBase*)value;
        }
    }

    /// <summary>
    ///     子節點數量。
    ///     ⚠️ ComponentBase 是 (AtkComponentBase*)Component，而 Component 在節點釋放後為 null，
    ///     直接讀 UldManager.NodeListCount 等於從位址 0 附近取值。取不到就回 0。
    /// </summary>
    public override int ChildCount {
        get {
            var componentBase = ComponentBase;
            if (componentBase is null) return 0;

            return componentBase->UldManager.NodeListCount;
        }
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            // 🔴 Data 可能是 null（節點已釋放又再次
            //    走到 Dispose 時）。NativeMemoryHelper.UiFree → IMemorySpace.Free
            //    是**沒有判空的原生 [MemberFunction]**，把 null 丟進遊戲的配置器
            //    無法離線證明安全，而 AccessViolationException 是 try/catch 攔不到的。
            //    取不到就不要釋放——本來就沒東西可釋放。
            var data = Data;
            if (data is not null) {
                NativeMemoryHelper.UiFree(data);
            }

            Data = null;

            // ⚠️ 元件取不到時只跳過 Dtor 這一行，不改變其餘釋放順序，
            //    尤其 base.Dispose(disposing) 一定要照樣呼叫——它負責 Destroy 節點本體
            //    並把 InternalNode 設為 null，因判空而 early-return 會直接漏掉節點釋放。
            var componentBase = ComponentBase;
            if (componentBase is not null) {
                componentBase->Dtor(1);
            }

            base.Dispose(disposing);
        }
    }

    protected void SetInternalComponentType(ComponentType type) {
        var componentInfo = (AtkUldComponentInfo*)ComponentBase->UldManager.Objects;

        componentInfo->ComponentType = type;
    }

    protected void InitializeComponentEvents() {
        ComponentBase->InitializeFromComponentData(DataBase);

        ComponentBase->Setup();
        ComponentBase->SetEnabledState(true);
    }

    protected override void OnSizeChanged() {
        base.OnSizeChanged();

        CollisionNode.Size = Size;
        ComponentBase->UldManager.RootNodeHeight = (ushort)Height;
        ComponentBase->UldManager.RootNodeWidth = (ushort)Width;
    }
}
